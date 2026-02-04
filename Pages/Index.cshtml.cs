using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

public class IndexModel : PageModel
{
    [BindProperty]
    public string? InputValue { get; set; }

    public void OnGet()
    {
        // Runs when the page is loaded
    }

    public void OnPost()
    {
        // Runs when the form is submitted
        Console.WriteLine($"Form submitted with value: {InputValue}");
    }
}
