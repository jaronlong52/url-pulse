using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Data;
using UrlPulse.Core.Interfaces;
using UrlPulse.Core.Models;

namespace UrlPulse.Worker.Functions;

public class UrlMonitorFunction
{
  private readonly ApplicationDbContext _context;
  private readonly IUrlChecker _checker;
  private readonly ILogger<UrlMonitorFunction> _logger;

  public UrlMonitorFunction(
      ApplicationDbContext context,
      IUrlChecker checker,
      ILogger<UrlMonitorFunction> logger)
  {
    _context = context;
    _checker = checker;
    _logger = logger;
  }

  // This runs every 1 minute. 
  // CRON format: [Seconds] [Minutes] [Hours] [Day] [Month] [Day_of_week]
  [Function("UrlPulseTimer")]
  public async Task Run([TimerTrigger("0 */1 * * * *")] TimerInfo myTimer)
  {
    _logger.LogInformation($"Pulse check started at: {DateTime.UtcNow}");

    var now = DateTime.UtcNow;
    const int toleranceSeconds = 5;

    // 1. Get active monitors
    var monitors = await _context.UrlMonitors
        .Where(m => m.IsActive && !m.IsPaused)
        .Include(m => m.History
            .OrderByDescending(h => h.CheckedAt)
            .Take(1))
        .ToListAsync();

    foreach (var monitor in monitors)
    {
      var lastCheck = monitor.History.FirstOrDefault();

      // 2. Check if it's time (Interval - 5s Tolerance)
      if (lastCheck != null)
      {
        var elapsed = now - lastCheck.CheckedAt;
        var thresholdSeconds = (monitor.CheckIntervalMinutes * 60) - toleranceSeconds;

        if (elapsed.TotalSeconds < thresholdSeconds)
        {
          continue;
        }
      }

      // 3. Perform the check
      var result = await _checker.CheckUrlAsync(monitor.Url, monitor.TimeoutMs);

      // 4. Log the result
      _context.LatencyHistories.Add(new LatencyHistory
      {
        UrlMonitorId = monitor.Id,
        CheckedAt = result.CheckedAt,
        LatencyMs = result.LatencyMs ?? 0,
        StatusCode = result.StatusCode,
        ErrorMessage = result.IsUp ? string.Empty : "Service Unavailable"
      });

      _logger.LogInformation($"Checked {monitor.Url}: {result.StatusCode}");
    }

    // 5. Save everything in one batch
    await _context.SaveChangesAsync();
  }
}