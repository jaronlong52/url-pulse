using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UrlPulse.Data;
using UrlPulse.Models;
using UrlPulse.Services;

namespace UrlPulse.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;
    private readonly IUrlChecker _urlChecker;

    public IndexModel(ApplicationDbContext context, IUrlChecker urlChecker)
    {
        _context = context;
        _urlChecker = urlChecker;
    }

    [BindProperty]
    [Url(ErrorMessage = "Invalid URL format")]
    public string InputUrl { get; set; } = string.Empty;

    [BindProperty]
    public int? InputInterval { get; set; }

    [BindProperty]
    public int? InputTimeout { get; set; }

    public List<UrlMonitor> UrlMonitors { get; set; } = new();

    public async Task OnGetAsync()
    {
        UrlMonitors = await _context.UrlMonitors
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UrlMonitor
            {
                Id = u.Id,
                Url = u.Url,
                CheckIntervalMinutes = u.CheckIntervalMinutes,
                TimeoutMs = u.TimeoutMs,
                CreatedAt = u.CreatedAt,
                IsActive = u.IsActive,
                IsPaused = u.IsPaused,
                History = u.History
                    .OrderByDescending(h => h.CheckedAt)
                    .Take(1)
                    .ToList()
            })
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await OnGetAsync();
            return Page();
        }

        int finalTimeout = InputTimeout ?? 5000;
        int finalInterval = InputInterval ?? 1;

        var result = await _urlChecker.CheckUrlAsync(InputUrl, finalTimeout);

        var urlMonitor = new UrlMonitor
        {
            Url = InputUrl,
            CreatedAt = DateTime.UtcNow,
            CheckIntervalMinutes = finalInterval,
            TimeoutMs = finalTimeout,
            IsActive = true,
            History = new List<LatencyHistory>
            {
                new LatencyHistory
                {
                    CheckedAt = result.CheckedAt,
                    LatencyMs = result.LatencyMs ?? 0,
                    StatusCode = result.StatusCode,
                    ErrorMessage = result.IsUp ? string.Empty : "Initial check failed"
                }
            }
        };

        _context.UrlMonitors.Add(urlMonitor);
        await _context.SaveChangesAsync();

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var urlMonitor = await _context.UrlMonitors.FindAsync(id);

        if (urlMonitor != null)
        {
            _context.UrlMonitors.Remove(urlMonitor);
            await _context.SaveChangesAsync();
        }

        return new OkResult();
    }

    public async Task<PartialViewResult> OnGetMonitorsPartialAsync()
    {
        await OnGetAsync();
        return Partial("_MonitorTable", UrlMonitors);
    }

    public async Task<IActionResult> OnPostSetPauseAsync(int id)
    {
        var monitor = await _context.UrlMonitors.FindAsync(id);
        if (monitor == null)
            return NotFound();

        monitor.IsPaused = !monitor.IsPaused;

        await _context.SaveChangesAsync();

        return new JsonResult(new { isPaused = monitor.IsPaused });
    }
}