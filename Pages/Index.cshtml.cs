using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using UrlPulse.Data;
using UrlPulse.Models;

namespace UrlPulse.Pages;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _context;

    // Constructor creates the DbContext instance via dependency injection
    public IndexModel(ApplicationDbContext context)
    {
        _context = context;
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

        // Create new URL monitor
        var urlMonitor = new UrlMonitor
        {
            Url = InputValue,
            CreatedAt = DateTime.UtcNow,
            IsUp = true
        };

        // Add to database and save
        _context.UrlMonitors.Add(urlMonitor);
        await _context.SaveChangesAsync();

        return RedirectToPage(); // Refresh page to see new entry
    }
}