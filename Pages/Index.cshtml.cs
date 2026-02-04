using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace UrlPulse.Pages;

public class IndexModel : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Please enter a valid URL")]
    [Url(ErrorMessage = "Invalid URL format")]
    public string InputValue { get; set; }

    public void OnGet()
    {
        // This is where you will eventually load the list of URLs from the database
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // TODO: Logic to save the URL to the database

        return RedirectToPage(); // Refresh page to see new entry
    }
}