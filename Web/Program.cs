using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Interfaces;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using UrlPulse.Infrastructure.Data;
using UrlPulse.Infrastructure.Services;
using UrlPulse.Services;

// Initialize application builder
var builder = WebApplication.CreateBuilder(args);

// Configure Authentication
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(builder.Configuration.GetSection("AzureAd"));

// Require users to be authenticated by default
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

// Add Razor Pages and Identity UI components
builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

// Configure Security & Infrastructure
// Allow app to read original client IPs/schemes when running behind a reverse proxy or load balancer
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                             | ForwardedHeaders.XForwardedProto
                             | ForwardedHeaders.XForwardedHost;

    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Enforce strict security policies for cookies
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.None;
    options.Secure = CookieSecurePolicy.Always;
    options.HttpOnly = Microsoft.AspNetCore.CookiePolicy.HttpOnlyPolicy.Always;
});

// Register Dependency Injection (DI) Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();

// Configure database context (PostgreSQL for regular use, In-Memory for testing)
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

// Register HTTP clients for making external API calls
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<IUrlChecker, UrlChecker>();

// Build Application
var app = builder.Build();

// Configure HTTP Request Pipeline (Middleware)
app.UseForwardedHeaders();
app.UseCookiePolicy();

// Automatically apply pending database migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    if (db.Database.IsRelational())
        db.Database.Migrate();
}

// Configure error handling for production environments
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Enable auth middleware
app.UseAuthentication();
app.UseAuthorization();

// Map routing endpoints
app.MapStaticAssets();
app.MapRazorPages().WithStaticAssets();
app.MapControllers();

// Start application
app.Run();

// Expose Program class so Integration Tests (WebApplicationFactory) can access it
namespace UrlPulse.Web
{
    public partial class Program { }
}