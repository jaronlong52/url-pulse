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
        // Load all URLs from the database, newest first
        UrlMonitors = await _context.UrlMonitors
            .OrderByDescending(u => u.CreatedAt)
            .ToListAsync();

        var now = DateTime.UtcNow;
        var staleThreshold = TimeSpan.FromSeconds(60);

        foreach (var monitor in UrlMonitors)
        {
            if (monitor.LastChecked == null ||
                now - monitor.LastChecked > staleThreshold)
            {
                var result = await _urlChecker.CheckUrlAsync(monitor.Url);

                monitor.LastChecked = result.CheckedAt;
                monitor.IsUp = result.IsUp;
                monitor.LatencyMs = result.LatencyMs;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            // Reload the list so it displays even when validation fails
            UrlMonitors = await _context.UrlMonitors
                .OrderByDescending(u => u.CreatedAt)
                .ToListAsync();
            return Page();
        }

        var result = await _urlChecker.CheckUrlAsync(InputValue);

        // Create new URL monitor
        var urlMonitor = new UrlMonitor
        {
            Url = InputValue,
            CreatedAt = result.CheckedAt,
            LastChecked = result.CheckedAt,
            LatencyMs = result.LatencyMs,
            IsUp = result.IsUp
        };

        // Adds the new entity to EF Core's change tracker
        _context.UrlMonitors.Add(urlMonitor);
        // Persists the new entity to the database
        await _context.SaveChangesAsync();

        return RedirectToPage(); // Refresh page to see new entry
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var urlMonitor = await _context.UrlMonitors.FindAsync(id);

        if (urlMonitor != null)
        {
            // Marks the entity for deletion in EF Core's change tracker
            _context.UrlMonitors.Remove(urlMonitor);
            // Executes the deletion in the database
            await _context.SaveChangesAsync();
        }

        return RedirectToPage();
    }
}