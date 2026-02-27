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
    public string InputUrl { get; set; } = string.Empty;

    [BindProperty]
    public int? InputInterval { get; set; } // Nullable so it's not required

    [BindProperty]
    public int? InputTimeout { get; set; } // Nullable so it's not required

    public List<UrlMonitor> UrlMonitors { get; set; } = new(); // Holds data loaded from the database

    public async Task OnGetAsync()
    {
        UrlMonitors = await _context.UrlMonitors
            .Include(m => m.History
                .OrderByDescending(h => h.CheckedAt)
                .Take(1))
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            // Reload monitors for the table
            UrlMonitors = await _context.UrlMonitors
                .Include(m => m.History.OrderByDescending(h => h.CheckedAt).Take(1))
                .ToListAsync();
            return Page();
        }

        // Determine values: use user input OR defaults
        int finalTimeout = InputTimeout ?? 5000;
        int finalInterval = InputInterval ?? 60;

        var result = await _urlChecker.CheckUrlAsync(InputUrl, finalTimeout);

        var urlMonitor = new UrlMonitor
        {
            Url = InputUrl,
            CreatedAt = DateTime.UtcNow,
            CheckIntervalSeconds = finalInterval,
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

        return RedirectToPage();
    }
}