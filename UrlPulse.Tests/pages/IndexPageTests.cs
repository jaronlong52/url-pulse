using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using System.Net;
using System.Text.RegularExpressions;
using UrlPulse.Data;
using UrlPulse.Models;
using UrlPulse.Services;

namespace UrlPulse.Tests.Pages;

/// <summary>
/// Integration tests for the Index Razor page (Index.cshtml).
///
/// These tests spin up the full ASP.NET Core pipeline in-process using
/// <see cref="WebApplicationFactory{TEntryPoint}"/>. EF is replaced with an
/// in-memory database and <see cref="IUrlChecker"/> is replaced with a mock so
/// no real network requests are made. The goal is to verify that the rendered
/// HTML honours the view contract — things that unit tests on the PageModel
/// cannot catch (missing form fields, broken bindings, client-side scaffolding,
/// AJAX handler routes, etc.).
/// </summary>
public class IndexPageIntegrationTests : IClassFixture<IndexPageIntegrationTests.TestWebAppFactory>
{
  // ── Custom WebApplicationFactory ─────────────────────────────────────────

  /// <summary>
  /// Replaces production infrastructure with test doubles so the full
  /// Razor pipeline can run without a real database or outbound HTTP.
  /// </summary>
  public class TestWebAppFactory : WebApplicationFactory<Program>
  {
    // Shared mock — tests can configure behaviour via this property.
    public Mock<IUrlChecker> UrlCheckerMock { get; } = new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      builder.ConfigureServices(services =>
      {
        // Replace the Npgsql-backed context with a fast in-memory one.
        // RemoveAll removes every descriptor for the type, which is
        // necessary because Npgsql registers provider-specific services
        // that conflict with InMemory if any descriptor is left behind.
        services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
        services.RemoveAll<ApplicationDbContext>();

        services.AddDbContext<ApplicationDbContext>(opts =>
                  opts.UseInMemoryDatabase("TestDb"));

        // Replace the real checker so no outbound HTTP occurs.
        services.RemoveAll<IUrlChecker>();
        services.AddSingleton(UrlCheckerMock.Object);
      });
    }
  }

  // ── Fixtures ──────────────────────────────────────────────────────────────

  private readonly TestWebAppFactory _factory;
  private readonly HttpClient _client;

  public IndexPageIntegrationTests(TestWebAppFactory factory)
  {
    _factory = factory;

    // Disable automatic redirect following so we can assert on 302s explicitly.
    _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      AllowAutoRedirect = false
    });

    // Default stub — most tests do not care about checker internals.
    _factory.UrlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(new UrlCheckResult(IsUp: true, LatencyMs: 50, CheckedAt: DateTime.UtcNow, StatusCode: 200));
  }

  // ── Helpers ───────────────────────────────────────────────────────────────

  /// <summary>
  /// Seeds a monitor directly into the shared in-memory database.
  /// Uses a fresh scope to avoid DbContext concurrency issues.
  /// </summary>
  private async Task<UrlMonitor> SeedMonitorAsync(
      string url = "https://example.com",
      bool isPaused = false)
  {
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var monitor = new UrlMonitor
    {
      Url = url,
      CheckIntervalMinutes = 1,
      TimeoutMs = 5000,
      IsActive = true,
      IsPaused = isPaused,
      CreatedAt = DateTime.UtcNow,
      History = new List<LatencyHistory>
            {
                new() { CheckedAt = DateTime.UtcNow, LatencyMs = 100, StatusCode = 200 }
            }
    };

    db.UrlMonitors.Add(monitor);
    await db.SaveChangesAsync();
    return monitor;
  }

  /// <summary>
  /// Extracts the antiforgery token from a rendered page's HTML.
  /// Required for any POST request to pass ASP.NET Core's CSRF middleware.
  /// </summary>
  private static string ExtractAntiForgeryToken(string html)
  {
    var match = Regex.Match(
        html,
        @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.IgnoreCase);

    match.Success.Should().BeTrue(because: "the page must render an antiforgery token");
    return match.Groups[1].Value;
  }

  /// <summary>
  /// Performs a full GET → extract token → POST round-trip and returns the response.
  /// </summary>
  private async Task<HttpResponseMessage> PostFormAsync(Dictionary<string, string> fields)
  {
    // 1. GET the page to obtain a valid antiforgery token.
    var getResponse = await _client.GetAsync("/");
    var html = await getResponse.Content.ReadAsStringAsync();
    var token = ExtractAntiForgeryToken(html);

    // 2. Build the form body including the token.
    var formData = new Dictionary<string, string>(fields)
    {
      ["__RequestVerificationToken"] = token
    };

    return await _client.PostAsync("/", new FormUrlEncodedContent(formData));
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Page load & core structure
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_ReturnsHttpOk()
  {
    var response = await _client.GetAsync("/");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task Get_ReturnsHtmlContentType()
  {
    var response = await _client.GetAsync("/");

    response.Content.Headers.ContentType?.MediaType
        .Should().Be("text/html");
  }

  [Fact]
  public async Task Get_RendersAppTitle()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("URL Pulse");
  }

  [Fact]
  public async Task Get_RendersSystemActiveBadge()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("System Active");
  }

  [Fact]
  public async Task Get_RendersAddMonitorFormHeading()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("Add New Monitor");
  }

  [Fact]
  public async Task Get_RendersActiveMonitorsSectionHeading()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("Active Monitors");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Form rendering — input fields & bindings
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_RendersUrlInputField()
  {
    var html = await _client.GetStringAsync("/");

    // asp-for="InputUrl" must generate an <input> with name="InputUrl"
    html.Should().MatchRegex(@"<input[^>]*name=""InputUrl""");
  }

  [Fact]
  public async Task Get_RendersUrlInputAsTypeUrl()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().MatchRegex(@"<input[^>]*type=""url""[^>]*name=""InputUrl""");
  }

  [Fact]
  public async Task Get_RendersIntervalInputField()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().MatchRegex(@"<input[^>]*name=""InputInterval""");
  }

  [Fact]
  public async Task Get_RendersTimeoutInputField()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().MatchRegex(@"<input[^>]*name=""InputTimeout""");
  }

  [Fact]
  public async Task Get_RendersSubmitButton()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("Add Target");
  }

  [Fact]
  public async Task Get_RendersAntiForgeryToken()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().MatchRegex(@"<input[^>]*name=""__RequestVerificationToken""");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Monitor table rendering
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_RendersMonitorTableHeaders()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("Status")
        .And.Contain("Target Information")
        .And.Contain("Last Check")
        .And.Contain("Response")
        .And.Contain("Actions");
  }

  [Fact]
  public async Task Get_WithSeededMonitor_RendersMonitorUrl()
  {
    await SeedMonitorAsync("https://seeded-monitor.com");

    var html = await _client.GetStringAsync("/");

    html.Should().Contain("seeded-monitor.com");
  }

  [Fact]
  public async Task Get_WithMultipleMonitors_RendersAllUrls()
  {
    await SeedMonitorAsync("https://alpha.com");
    await SeedMonitorAsync("https://beta.com");

    var html = await _client.GetStringAsync("/");

    html.Should().Contain("alpha.com")
        .And.Contain("beta.com");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // POST — valid submission
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Post_WithValidUrl_RedirectsToIndexPage()
  {
    var response = await PostFormAsync(new()
    {
      ["InputUrl"] = "https://valid-url.com"
    });

    response.StatusCode.Should().Be(HttpStatusCode.Redirect);
    response.Headers.Location?.OriginalString.Should().Be("/");
  }

  [Fact]
  public async Task Post_WithValidUrl_PersistsMonitorInDatabase()
  {
    await PostFormAsync(new()
    {
      ["InputUrl"] = "https://persisted-monitor.com"
    });

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.UrlMonitors.Should().Contain(m => m.Url == "https://persisted-monitor.com");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // POST — invalid submission (validation)
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Post_WithMissingUrl_ReturnsPageWithErrors()
  {
    // Submitting an empty URL string should fail [Url] validation and
    // re-render the page (HTTP 200) rather than redirecting.
    var response = await PostFormAsync(new()
    {
      ["InputUrl"] = string.Empty
    });

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task Post_WithMalformedUrl_ReturnsPageWithValidationMessage()
  {
    var response = await PostFormAsync(new()
    {
      ["InputUrl"] = "not-a-url"
    });

    var html = await response.Content.ReadAsStringAsync();
    html.Should().Contain("Invalid URL format");
  }

  [Fact]
  public async Task Post_WithMalformedUrl_DoesNotPersistMonitor()
  {
    await PostFormAsync(new()
    {
      ["InputUrl"] = "not-a-url"
    });

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.UrlMonitors.Should().NotContain(m => m.Url == "not-a-url");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // AJAX handler routes — JavaScript depends on these URLs being reachable
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_MonitorsPartialHandler_ReturnsOk()
  {
    var response = await _client.GetAsync("/?handler=MonitorsPartial");

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Fact]
  public async Task Get_MonitorsPartialHandler_ReturnsHtmlFragment()
  {
    var response = await _client.GetAsync("/?handler=MonitorsPartial");
    var html = await response.Content.ReadAsStringAsync();

    // The partial renders table rows; an empty database returns no <tr>.
    // Either way the response must be a valid fragment (not an error page).
    response.IsSuccessStatusCode.Should().BeTrue();
    html.Should().NotContain("500")
        .And.NotContain("An unhandled exception");
  }

  [Fact]
  public async Task Post_SetPauseHandler_WithValidId_ReturnsJsonWithIsPaused()
  {
    var monitor = await SeedMonitorAsync(isPaused: false);

    var getResponse = await _client.GetAsync("/");
    var html = await getResponse.Content.ReadAsStringAsync();
    var token = ExtractAntiForgeryToken(html);

    var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/?handler=SetPause&id={monitor.Id}");

    request.Headers.Add("RequestVerificationToken", token);

    var response = await _client.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    var body = await response.Content.ReadAsStringAsync();
    body.Should().Contain("isPaused");
  }

  [Fact]
  public async Task Post_DeleteHandler_WithValidId_ReturnsOk()
  {
    var monitor = await SeedMonitorAsync();

    var getResponse = await _client.GetAsync("/");
    var html = await getResponse.Content.ReadAsStringAsync();
    var token = ExtractAntiForgeryToken(html);

    var request = new HttpRequestMessage(
        HttpMethod.Post,
        $"/?handler=Delete&id={monitor.Id}");

    request.Headers.Add("RequestVerificationToken", token);

    var response = await _client.SendAsync(request);

    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  // ══════════════════════════════════════════════════════════════════════════
  // JavaScript scaffolding — client-side AJAX depends on these being present
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_RendersRefreshTableJavaScript()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("refreshTable");
  }

  [Fact]
  public async Task Get_RendersGetTokenJavaScript()
  {
    var html = await _client.GetStringAsync("/");

    // The stable token accessor function must be present for AJAX POSTs to work.
    html.Should().Contain("getToken");
  }

  [Fact]
  public async Task Get_RendersUpdateLocalTimesJavaScript()
  {
    var html = await _client.GetStringAsync("/");

    html.Should().Contain("updateLocalTimes");
  }

  [Fact]
  public async Task Get_RendersMonitorTableBodyWithCorrectId()
  {
    var html = await _client.GetStringAsync("/");

    // JavaScript targets this ID for partial refresh — must not be renamed.
    html.Should().Contain(@"id=""monitor-table""");
  }
}