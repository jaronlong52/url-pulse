using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UrlPulse.Infrastructure.Data;
using UrlPulse.Core.Models;

namespace UrlPulse.Pages;

public class AnalyticsModel(ApplicationDbContext context) : PageModel
{
  private readonly ApplicationDbContext _context = context;

  // Populated on GET so the monitor selector dropdown has options
  public List<UrlMonitor> UrlMonitors { get; set; } = new();

  public async Task OnGetAsync()
  {
    UrlMonitors = await _context.UrlMonitors
        .OrderByDescending(u => u.CreatedAt)
        .Select(u => new UrlMonitor
        {
          Id = u.Id,
          Url = u.Url,
          IsActive = u.IsActive,
          IsPaused = u.IsPaused
        })
        .AsNoTracking()
        .ToListAsync();
  }

  // GET: ?handler=LatencyData&monitorId=1&range=24h
  // Returns multi-series data for the latency-over-time line chart.
  // Data is bucketed server-side so the chart stays responsive at any range.
  public async Task<IActionResult> OnGetLatencyDataAsync(int monitorId, string range = "24h")
  {
    var since = ParseRange(range);
    var bucket = GetBucketSize(range);

    // SelectMany via nav property so the global query filter on UrlMonitors
    // (which enforces OwnerId) is automatically applied before touching History.
    var raw = await _context.UrlMonitors
        .Where(u => u.Id == monitorId)
        .SelectMany(u => u.History)
        .Where(h => h.CheckedAt >= since)
        .OrderBy(h => h.CheckedAt)
        .Select(h => new { h.CheckedAt, h.LatencyMs, h.Region, h.StatusCode })
        .AsNoTracking()
        .ToListAsync();

    var series = raw
        .GroupBy(h => h.Region)
        .Select(g => new
        {
          name = string.IsNullOrWhiteSpace(g.Key) ? "Unknown" : g.Key,
          data = g
                .GroupBy(h => TruncateToBucket(h.CheckedAt, bucket))
                .OrderBy(b => b.Key)
                .Select(b => new
                {
                  // ISO-8601 string; JS will parse with new Date(...)
                  x = b.Key.ToString("o"),
                  y = (int)b.Average(h => h.LatencyMs),
                  // Error count lets the tooltip show a warning when checks fail
                  errors = b.Count(h => h.StatusCode == 0 || h.StatusCode >= 400)
                })
                .ToList()
        })
        .ToList();

    return new JsonResult(series);
  }

  // GET: ?handler=RegionSummary&monitorId=1&range=24h
  // Returns avg + P95 latency per region for the horizontal bar chart.
  public async Task<IActionResult> OnGetRegionSummaryAsync(int monitorId, string range = "24h")
  {
    var since = ParseRange(range);

    var raw = await _context.UrlMonitors
        .Where(u => u.Id == monitorId)
        .SelectMany(u => u.History)
        .Where(h => h.CheckedAt >= since)
        .Select(h => new { h.Region, h.LatencyMs, h.StatusCode })
        .AsNoTracking()
        .ToListAsync();

    var summary = raw
        .GroupBy(h => h.Region)
        .Select(g =>
        {
          var sorted = g.Select(h => h.LatencyMs).OrderBy(x => x).ToList();
          return new
          {
            region = string.IsNullOrWhiteSpace(g.Key) ? "Unknown" : g.Key,
            avgLatency = (int)sorted.Average(),
            p95Latency = Percentile(sorted, 0.95),
            successPct = Math.Round(
                      (double)g.Count(h => h.StatusCode >= 200 && h.StatusCode < 300)
                      / g.Count() * 100, 1),
            totalChecks = g.Count()
          };
        })
        .OrderBy(s => s.avgLatency)
        .ToList();

    return new JsonResult(summary);
  }

  // GET: ?handler=Stats&monitorId=1&range=24h
  // Returns the scalar values shown in the summary stat cards.
  public async Task<IActionResult> OnGetStatsAsync(int monitorId, string range = "24h")
  {
    var since = ParseRange(range);

    var records = await _context.UrlMonitors
        .Where(u => u.Id == monitorId)
        .SelectMany(u => u.History)
        .Where(h => h.CheckedAt >= since)
        .Select(h => new { h.LatencyMs, h.StatusCode })
        .AsNoTracking()
        .ToListAsync();

    if (records.Count == 0)
      return new JsonResult(null);

    var sorted = records.Select(h => h.LatencyMs).OrderBy(x => x).ToList();
    var successCount = records.Count(h => h.StatusCode >= 200 && h.StatusCode < 300);

    return new JsonResult(new
    {
      totalChecks = records.Count,
      uptimePercent = Math.Round((double)successCount / records.Count * 100, 2),
      avgLatency = (int)sorted.Average(),
      p50 = Percentile(sorted, 0.50),
      p95 = Percentile(sorted, 0.95),
      p99 = Percentile(sorted, 0.99),
      outages = records.Count(h => h.StatusCode == 0 || h.StatusCode >= 500)
    });
  }

  // GET: ?handler=StatusDistribution&monitorId=1&range=24h
  // Returns status-code family counts for the donut chart.
  public async Task<IActionResult> OnGetStatusDistributionAsync(int monitorId, string range = "24h")
  {
    var since = ParseRange(range);

    var codes = await _context.UrlMonitors
        .Where(u => u.Id == monitorId)
        .SelectMany(u => u.History)
        .Where(h => h.CheckedAt >= since)
        .Select(h => h.StatusCode)
        .ToListAsync();

    // Bucket into families: Timeout (0), 2xx, 3xx, 4xx, 5xx
    var distribution = codes
        .GroupBy(code => code == 0 ? "Timeout" : $"{code / 100}xx")
        .Select(g => new { label = g.Key, count = g.Count() })
        .OrderBy(g => g.label)
        .ToList();

    return new JsonResult(distribution);
  }

  // ─── Private helpers ──────────────────────────────────────────────────────

  private static DateTime ParseRange(string range) => range switch
  {
    "1h" => DateTime.UtcNow.AddHours(-1),
    "6h" => DateTime.UtcNow.AddHours(-6),
    "7d" => DateTime.UtcNow.AddDays(-7),
    "30d" => DateTime.UtcNow.AddDays(-30),
    _ => DateTime.UtcNow.AddHours(-24)  // default: 24h
  };

  // Coarser buckets for wider windows keep chart payloads small
  private static TimeSpan GetBucketSize(string range) => range switch
  {
    "1h" => TimeSpan.FromMinutes(1),
    "6h" => TimeSpan.FromMinutes(5),
    "24h" => TimeSpan.FromMinutes(15),
    "7d" => TimeSpan.FromHours(1),
    "30d" => TimeSpan.FromHours(6),
    _ => TimeSpan.FromMinutes(15)
  };

  private static DateTime TruncateToBucket(DateTime dt, TimeSpan bucket)
  {
    var ticks = dt.Ticks / bucket.Ticks * bucket.Ticks;
    return new DateTime(ticks, DateTimeKind.Utc);
  }

  // Nearest-rank percentile on a pre-sorted list
  private static int Percentile(List<int> sorted, double p)
  {
    if (sorted.Count == 0) return 0;
    var index = (int)Math.Ceiling(p * sorted.Count) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
  }
}
