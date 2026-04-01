using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Data;
using UrlPulse.Core.Services;
using UrlPulse.Core.Interfaces;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides; // Required for Azure fix

var builder = WebApplication.CreateBuilder(args);

// DATABASE CONFIGURATION
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

// HTTP CLIENTS
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IUrlChecker, UrlChecker>();

// AUTHENTICATION SERVICES
// This must match your "AzureAd" section in appsettings.json
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

// CONTROLLERS & AUTHORIZATION
builder.Services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});

builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

var app = builder.Build();

// DATABASE MIGRATIONS
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

// This stops the 401 loop caused by the app thinking it's on HTTP
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// AUTHENTICATION MIDDLEWARE
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();
app.MapControllers();

app.Run();

namespace UrlPulse.Web
{
    public partial class Program { }
}