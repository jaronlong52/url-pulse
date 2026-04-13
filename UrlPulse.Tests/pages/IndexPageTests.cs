using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Authentication;
using Moq;
using System.Net;
using System.Text.RegularExpressions;
using FluentAssertions;
using UrlPulse.Core.Data;
using UrlPulse.Core.Models;
using UrlPulse.Core.Services;
using UrlPulse.Core.Interfaces;
using UrlPulse.Tests.TestAuth;

// 1. Point to the specific namespace we added to the Web project
using WebApp = UrlPulse.Web.Program;

namespace UrlPulse.Tests.Pages;

public class IndexPageIntegrationTests : IClassFixture<IndexPageIntegrationTests.TestWebAppFactory>
{
  public class TestWebAppFactory : WebApplicationFactory<WebApp>
  {
    public Mock<IUrlChecker> UrlCheckerMock { get; } = new(MockBehavior.Loose);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
      var dbName = Guid.NewGuid().ToString();

      builder.UseEnvironment("Testing");
      builder.ConfigureServices(services =>
            {
              // === Professional Auth Override ===
              services.RemoveAll<IAuthenticationService>(); // clean old registration

              // Replace with our test scheme (this overrides Microsoft Identity Web in tests)
              services.AddAuthentication(TestAuthHandler.SchemeName)
                  .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(
                      TestAuthHandler.SchemeName,
                      options => { });

              // Ensure the app uses our test scheme as default
              services.Configure<AuthenticationOptions>(options =>
              {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultScheme = TestAuthHandler.SchemeName;
              });

              // Your existing service overrides
              services.RemoveAll<IUrlChecker>();
              services.AddSingleton(UrlCheckerMock.Object);

              services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
              services.AddDbContext<ApplicationDbContext>(options =>
                  options.UseInMemoryDatabase(dbName));
            });
    }
  }

  private readonly TestWebAppFactory _factory;
  private readonly HttpClient _client;

  public IndexPageIntegrationTests(TestWebAppFactory factory)
  {
    _factory = factory;

    _client = factory.CreateClient(new WebApplicationFactoryClientOptions
    {
      AllowAutoRedirect = false
    });

    _factory.UrlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(new UrlCheckResult(true, 50, DateTime.UtcNow, 200));
  }

  private async Task<UrlMonitor> SeedMonitorAsync(string url = "https://example.com", bool isPaused = false)
  {
    using var scope = _factory.Server.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var monitor = new UrlMonitor
    {
      Url = url,
      CheckIntervalMinutes = 1,
      TimeoutMs = 5000,
      IsActive = true,
      IsPaused = isPaused,
      CreatedAt = DateTime.UtcNow,
      History = new List<LatencyHistory> {
                new() { CheckedAt = DateTime.UtcNow, LatencyMs = 100, StatusCode = 200 }
            }
    };
    db.UrlMonitors.Add(monitor);
    await db.SaveChangesAsync();
    return monitor;
  }

  private static string ExtractAntiForgeryToken(string html)
  {
    var match = Regex.Match(
        html,
        @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""",
        RegexOptions.IgnoreCase);

    match.Success.Should().BeTrue(because: "the page must render an antiforgery token");
    return match.Groups[1].Value;
  }

  private async Task<HttpResponseMessage> PostFormAsync(Dictionary<string, string> fields)
  {
    var getResponse = await _client.GetAsync("/");
    var html = await getResponse.Content.ReadAsStringAsync();
    var token = ExtractAntiForgeryToken(html);

    var formData = new Dictionary<string, string>(fields)
    {
      ["__RequestVerificationToken"] = token
    };

    return await _client.PostAsync("/", new FormUrlEncodedContent(formData));
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Structural & UI Logic
  // ══════════════════════════════════════════════════════════════════════════

  [Theory]
  [InlineData("URL Pulse")]
  [InlineData("System Active")]
  [InlineData("Add New Monitor")]
  [InlineData("Active Monitors")]
  [InlineData("Status")]
  [InlineData("Target Information")]
  public async Task Get_RendersExpectedContent(string content)
  {
    var html = await _client.GetStringAsync("/");
    html.Should().Contain(content);
  }

  [Theory]
  [InlineData("InputUrl")]
  [InlineData("InputInterval")]
  [InlineData("__RequestVerificationToken")]
  public async Task Get_RendersRequiredInputs(string name)
  {
    var html = await _client.GetStringAsync("/");
    html.Should().Contain($"name=\"{name}\"");
  }

  [Fact]
  public async Task Get_ReturnsHttpOkAndHtmlType()
  {
    var response = await _client.GetAsync("/");
    response.StatusCode.Should().Be(HttpStatusCode.OK);
    response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // Monitor Table & Persistence
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_WithMultipleMonitors_RendersAllUrls()
  {
    await SeedMonitorAsync("https://alpha.com");
    await SeedMonitorAsync("https://beta.com");
    var html = await _client.GetStringAsync("/");
    html.Should().Contain("alpha.com").And.Contain("beta.com");
  }

  [Fact]
  public async Task Post_WithValidUrl_RedirectsAndPersists()
  {
    var response = await PostFormAsync(new() { ["InputUrl"] = "https://valid.com" });
    response.StatusCode.Should().Be(HttpStatusCode.Redirect);

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.UrlMonitors.Should().Contain(m => m.Url == "https://valid.com");
  }

  [Theory]
  [InlineData("", "")]
  [InlineData("not-a-url", "Invalid URL")]
  [InlineData("ftp://invalid.com", "Only http and https are supported")]
  public async Task Post_WithInvalidUrl_ReturnsPageAndDoesNotPersist(string url, string error)
  {
    var response = await PostFormAsync(new() { ["InputUrl"] = url });
    response.StatusCode.Should().Be(HttpStatusCode.OK);

    var html = await response.Content.ReadAsStringAsync();
    if (!string.IsNullOrEmpty(error))
    {
      html.Should().Contain(error);
    }

    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.UrlMonitors.Should().NotContain(m => m.Url == url);
  }

  // ══════════════════════════════════════════════════════════════════════════
  // AJAX & JS Dependencies
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task Get_MonitorsPartialHandler_ReturnsSuccessAndValidHtml()
  {
    var response = await _client.GetAsync("/?handler=MonitorsPartial");
    var html = await response.Content.ReadAsStringAsync();

    response.StatusCode.Should().Be(HttpStatusCode.OK);
    html.Should().Contain("<tr id=\"monitor-").And.Contain("https://");
  }

  [Theory]
  [InlineData("SetPause")]
  [InlineData("Delete")]
  public async Task Post_Handlers_WithValidId_ReturnsOk(string handler)
  {
    var monitor = await SeedMonitorAsync();
    var getResponse = await _client.GetAsync("/");
    var token = ExtractAntiForgeryToken(await getResponse.Content.ReadAsStringAsync());

    var request = new HttpRequestMessage(HttpMethod.Post, $"/?handler={handler}&id={monitor.Id}");
    request.Headers.Add("RequestVerificationToken", token);

    var response = await _client.SendAsync(request);
    response.StatusCode.Should().Be(HttpStatusCode.OK);
  }

  [Theory]
  [InlineData("refreshTable")]
  [InlineData("getToken")]
  [InlineData("updateLocalTimes")]
  [InlineData("id=\"monitor-table\"")]
  public async Task Get_RendersRequiredJavaScript(string snippet)
  {
    var html = await _client.GetStringAsync("/");
    html.Should().Contain(snippet);
  }
}