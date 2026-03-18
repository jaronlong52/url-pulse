using Microsoft.EntityFrameworkCore;
using UrlPulse.Data;
using UrlPulse.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    // Use Npgsql unless we are in a "Testing" environment
    if (builder.Environment.EnvironmentName != "Testing")
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
    else
    {
        options.UseInMemoryDatabase(Guid.NewGuid().ToString()); // Use a unique in-memory database for each test run
    }
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IUrlChecker, UrlChecker>();
builder.Services.AddHostedService<UrlMonitoringService>();

var app = builder.Build();

// Apply migrations at startup.
// IsRelational() returns false for InMemory (used in tests) and true for
// Npgsql, so this naturally skips the call in non-relational environments
// without any test-specific logic in production code.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();