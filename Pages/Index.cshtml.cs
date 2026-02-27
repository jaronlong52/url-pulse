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

    // Constructor creates the DbContext instance via dependency injection
    public IndexModel(ApplicationDbContext context, IUrlChecker urlChecker)
    {
        _context = context;
        _urlChecker = urlChecker;
    }

    [BindProperty]
    [Url(ErrorMessage = "Invalid URL format")]
    public string InputValue { get; set; } = string.Empty; // Initialized to empty because string type is non-nullable

    public List<UrlMonitor> UrlMonitors { get; set; } = new(); // Holds data loaded from the database

    public async Task OnGetAsync()
    {
        // Load monitors and include the latest history entry to check for staleness
        UrlMonitors = await _context.UrlMonitors
            .Include(m => m.History.OrderByDescending(h => h.CheckedAt).Take(1))
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var tasks = new List<Task>();

        foreach (var monitor in UrlMonitors)
        {
            var lastCheck = monitor.History.FirstOrDefault();

            // Check if it's time for a new pulse
            if (lastCheck == null || (now - lastCheck.CheckedAt).TotalSeconds > monitor.CheckIntervalSeconds)
            {
                // We launch the tasks in parallel
                tasks.Add(PerformUrlCheck(monitor));
            }
        }

        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
            await _context.SaveChangesAsync();
        }
    }

    private async Task PerformUrlCheck(UrlMonitor monitor)
    {
        var result = await _urlChecker.CheckUrlAsync(monitor.Url, monitor.TimeoutMs);

        var history = new LatencyHistory
        {
            UrlMonitorId = monitor.Id,
            CheckedAt = result.CheckedAt,
            LatencyMs = result.LatencyMs ?? 0,
            StatusCode = result.StatusCode,
            ErrorMessage = result.IsUp ? string.Empty : "Service Unavailable"
        };

        // Add to the context; EF handles the foreign key via the list or ID
        _context.LatencyHistories.Add(history);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            // Reload list for the UI if validation fails
            UrlMonitors = await _context.UrlMonitors
                .Include(m => m.History.OrderByDescending(h => h.CheckedAt).Take(1))
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
            return Page();
        }
        // TODO - Add the necessary inputs to the frontend so user can input timout and check intervals

        // 1. Perform the initial check immediately
        var result = await _urlChecker.CheckUrlAsync(InputValue, 5000);

        // 2. Create the Monitor with its first History record
        var urlMonitor = new UrlMonitor
        {
            Url = InputValue,
            CreatedAt = DateTime.UtcNow,
            CheckIntervalSeconds = 60, // Default values from your model
            TimeoutMs = 5000,
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

        return RedirectToPage();
    }
}