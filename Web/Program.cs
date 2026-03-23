using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Data;
using UrlPulse.Core.Services;
using UrlPulse.Core.Interfaces;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (builder.Environment.EnvironmentName != "Testing")
    {
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
    }
    else
    {
        options.UseInMemoryDatabase(Guid.NewGuid().ToString());
    }
});

builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IUrlChecker, UrlChecker>();

var app = builder.Build();

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
app.MapRazorPages().WithStaticAssets();

app.Run();

namespace UrlPulse.Web
{
    public partial class Program { }
}