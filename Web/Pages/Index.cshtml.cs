using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UrlPulse.Infrastructure.Data;
using UrlPulse.Core.Models;
using UrlPulse.Core.Interfaces;

namespace UrlPulse.Pages;

public class IndexModel(ApplicationDbContext context, IUrlChecker urlChecker) : PageModel
{
    private readonly ApplicationDbContext _context = context;
    private readonly IUrlChecker _urlChecker = urlChecker;

    [BindProperty]
    [Required(ErrorMessage = "URL is required")]
    // This regex replaces the loose [Url] attribute
    [RegularExpression(@"^https?://.*", ErrorMessage = "Invalid URL. Only http and https are supported.")]
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
        // FirstOrDefaultAsync ensures the Global Query Filter is applied
        // If the monitor belongs to someone else, this returns null
        var urlMonitor = await _context.UrlMonitors.FirstOrDefaultAsync(m => m.Id == id);

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
        // FirstOrDefaultAsync ensures the Global Query Filter is applied
        // If the monitor belongs to someone else, this returns null
        var monitor = await _context.UrlMonitors.FirstOrDefaultAsync(m => m.Id == id);
        if (monitor == null)
            return NotFound();

        monitor.IsPaused = !monitor.IsPaused;
        await _context.SaveChangesAsync();

        return new JsonResult(new { isPaused = monitor.IsPaused });
    }
}