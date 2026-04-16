using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Moq;
using UrlPulse.Pages;
using UrlPulse.Infrastructure.Data;
using UrlPulse.Core.Models;
using UrlPulse.Core.Services;
using UrlPulse.Core.Interfaces;

namespace UrlPulse.Tests.Pages;

/// <summary>
/// Unit tests for <see cref="IndexModel"/>.
/// Dependencies are isolated: EF uses an in-memory database and
/// IUrlChecker is mocked so no network I/O ever occurs.
/// </summary>
public class IndexModelTests : IDisposable
{
  // ── Fixtures ──────────────────────────────────────────────────────────────

  private readonly ApplicationDbContext _context;
  private readonly Mock<IUrlChecker> _urlCheckerMock;
  private readonly Mock<ICurrentUserService> _currentUserServiceMock;
  private readonly IndexModel _sut;

  public IndexModelTests()
  {
    var options = new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()) // isolated per test
        .Options;

    _currentUserServiceMock = new Mock<ICurrentUserService>();
    _currentUserServiceMock.Setup(c => c.UserId).Returns("test-user-123");
    _context = new ApplicationDbContext(options, _currentUserServiceMock.Object);
    _urlCheckerMock = new Mock<IUrlChecker>(MockBehavior.Strict);
    _sut = new IndexModel(_context, _urlCheckerMock.Object);

    // PageModel.Partial() resolves IModelMetadataProvider from
    // HttpContext.RequestServices — not from PageContext.ViewData.
    // A plain DefaultHttpContext has no RequestServices, which causes
    // ArgumentNullException deep inside the Partial() call. We must
    // supply a minimal service provider containing the provider.
    var metadataProvider = new EmptyModelMetadataProvider();
    var serviceCollection = new ServiceCollection();
    serviceCollection.AddSingleton<IModelMetadataProvider>(metadataProvider);

    _sut.PageContext = new PageContext
    {
      HttpContext = new DefaultHttpContext
      {
        RequestServices = serviceCollection.BuildServiceProvider()
      },
      RouteData = new RouteData(),
      ActionDescriptor = new CompiledPageActionDescriptor(),
      ViewData = new ViewDataDictionary(metadataProvider, new ModelStateDictionary())
    };
  }

  public void Dispose() => _context.Dispose();

  // ── Helpers ───────────────────────────────────────────────────────────────

  /// <summary>Seeds a UrlMonitor with an optional single history entry.</summary>
  private async Task<UrlMonitor> SeedMonitorAsync(
      string url = "https://example.com",
      bool isPaused = false,
      DateTime? createdAt = null)
  {
    var monitor = new UrlMonitor
    {
      Url = url,
      CheckIntervalMinutes = 1,
      TimeoutMs = 5000,
      IsActive = true,
      IsPaused = isPaused,
      CreatedAt = createdAt ?? DateTime.UtcNow,
      History = new List<LatencyHistory>
            {
                new() { CheckedAt = DateTime.UtcNow, LatencyMs = 120, StatusCode = 200 }
            }
    };

    _context.UrlMonitors.Add(monitor);
    await _context.SaveChangesAsync();
    return monitor;
  }

  /// <summary>Returns a successful UrlCheckResult stub.</summary>
  private static UrlCheckResult SuccessfulCheckResult(int statusCode = 200, int latencyMs = 85) =>
      new(IsUp: true, LatencyMs: latencyMs, CheckedAt: DateTime.UtcNow, StatusCode: statusCode);

  // ══════════════════════════════════════════════════════════════════════════
  // OnGetAsync
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task OnGetAsync_WithNoMonitors_LeavesUrlMonitorsEmpty()
  {
    await _sut.OnGetAsync();

    _sut.UrlMonitors.Should().BeEmpty();
  }

  [Fact]
  public async Task OnGetAsync_PopulatesUrlMonitors_WithAllSeededRecords()
  {
    await SeedMonitorAsync("https://alpha.com");
    await SeedMonitorAsync("https://beta.com");

    await _sut.OnGetAsync();

    _sut.UrlMonitors.Should().HaveCount(2);
  }

  [Fact]
  public async Task OnGetAsync_OrdersMonitors_ByCreatedAtDescending()
  {
    var older = await SeedMonitorAsync("https://older.com", createdAt: DateTime.UtcNow.AddDays(-2));
    var newer = await SeedMonitorAsync("https://newer.com", createdAt: DateTime.UtcNow.AddDays(-1));

    await _sut.OnGetAsync();

    _sut.UrlMonitors[0].Url.Should().Be(newer.Url);
    _sut.UrlMonitors[1].Url.Should().Be(older.Url);
  }

  [Fact]
  public async Task OnGetAsync_ProjectsOnlyLatestHistoryEntry_PerMonitor()
  {
    var monitor = new UrlMonitor
    {
      Url = "https://example.com",
      CreatedAt = DateTime.UtcNow,
      IsActive = true,
      History = new List<LatencyHistory>
            {
                new() { CheckedAt = DateTime.UtcNow.AddMinutes(-10), LatencyMs = 300, StatusCode = 500 },
                new() { CheckedAt = DateTime.UtcNow.AddMinutes(-1),  LatencyMs = 100, StatusCode = 200 },
                new() { CheckedAt = DateTime.UtcNow.AddMinutes(-5),  LatencyMs = 200, StatusCode = 200 }
            }
    };
    _context.UrlMonitors.Add(monitor);
    await _context.SaveChangesAsync();

    await _sut.OnGetAsync();

    var projected = _sut.UrlMonitors.Single();
    projected.History.Should().HaveCount(1);
    projected.History[0].LatencyMs.Should().Be(100, because: "only the most recent entry is projected");
  }

  // ══════════════════════════════════════════════════════════════════════════
  // OnPostAsync
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task OnPostAsync_WithInvalidModelState_ReturnsPageResult()
  {
    _sut.ModelState.AddModelError(nameof(IndexModel.InputUrl), "Invalid URL format");

    var result = await _sut.OnPostAsync();

    result.Should().BeOfType<PageResult>();
  }

  [Fact]
  public async Task OnPostAsync_WithInvalidModelState_DoesNotPersistAnyMonitor()
  {
    _sut.ModelState.AddModelError(nameof(IndexModel.InputUrl), "Invalid URL format");

    await _sut.OnPostAsync();

    _context.UrlMonitors.Should().BeEmpty();
  }

  [Fact]
  public async Task OnPostAsync_WithInvalidModelState_ReloadsUrlMonitors()
  {
    await SeedMonitorAsync();
    _sut.ModelState.AddModelError(nameof(IndexModel.InputUrl), "Invalid URL format");

    await _sut.OnPostAsync();

    // UrlMonitors must be populated so the view can re-render the table.
    _sut.UrlMonitors.Should().HaveCount(1);
  }

  [Fact]
  public async Task OnPostAsync_WithValidUrl_PersistsNewMonitor()
  {
    _sut.InputUrl = "https://example.com";
    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync("https://example.com", It.IsAny<int>()))
        .ReturnsAsync(SuccessfulCheckResult());

    await _sut.OnPostAsync();

    _context.UrlMonitors.Should().ContainSingle(m => m.Url == "https://example.com");
  }

  [Fact]
  public async Task OnPostAsync_WithValidUrl_RedirectsToPage()
  {
    _sut.InputUrl = "https://example.com";
    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(SuccessfulCheckResult());

    var result = await _sut.OnPostAsync();

    result.Should().BeOfType<RedirectToPageResult>();
  }

  [Fact]
  public async Task OnPostAsync_UsesDefaultTimeoutAndInterval_WhenNotSupplied()
  {
    _sut.InputUrl = "https://example.com";
    _sut.InputTimeout = null;
    _sut.InputInterval = null;

    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync("https://example.com", 5000))
        .ReturnsAsync(SuccessfulCheckResult())
        .Verifiable();

    await _sut.OnPostAsync();

    _urlCheckerMock.Verify();
    var saved = await _context.UrlMonitors.SingleAsync();
    saved.TimeoutMs.Should().Be(5000);
    saved.CheckIntervalMinutes.Should().Be(1);
  }

  [Fact]
  public async Task OnPostAsync_UsesSuppliedTimeoutAndInterval()
  {
    _sut.InputUrl = "https://example.com";
    _sut.InputTimeout = 3000;
    _sut.InputInterval = 5;

    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync("https://example.com", 3000))
        .ReturnsAsync(SuccessfulCheckResult())
        .Verifiable();

    await _sut.OnPostAsync();

    _urlCheckerMock.Verify();
    var saved = await _context.UrlMonitors.SingleAsync();
    saved.TimeoutMs.Should().Be(3000);
    saved.CheckIntervalMinutes.Should().Be(5);
  }

  [Fact]
  public async Task OnPostAsync_SeedsInitialHistoryEntry_FromCheckResult()
  {
    _sut.InputUrl = "https://example.com";
    var checkResult = new UrlCheckResult(
        IsUp: true,
        LatencyMs: 42,
        CheckedAt: new DateTime(2024, 6, 1, 12, 0, 0, DateTimeKind.Utc),
        StatusCode: 201);

    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(checkResult);

    await _sut.OnPostAsync();

    var saved = await _context.UrlMonitors
        .Include(m => m.History)
        .SingleAsync();

    saved.History.Should().ContainSingle();
    var history = saved.History[0];
    history.LatencyMs.Should().Be(42);
    history.StatusCode.Should().Be(201);
    history.CheckedAt.Should().Be(checkResult.CheckedAt);
    history.ErrorMessage.Should().BeEmpty(because: "the initial check succeeded");
  }

  [Fact]
  public async Task OnPostAsync_SetsNonEmptyErrorMessage_WhenInitialCheckFails()
  {
    _sut.InputUrl = "https://down.example.com";
    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(new UrlCheckResult(IsUp: false, LatencyMs: null, CheckedAt: DateTime.UtcNow, StatusCode: 503));

    await _sut.OnPostAsync();

    var saved = await _context.UrlMonitors
        .Include(m => m.History)
        .SingleAsync();

    saved.History[0].ErrorMessage.Should().NotBeNullOrEmpty();
  }

  [Fact]
  public async Task OnPostAsync_NewMonitor_IsActiveAndNotPaused()
  {
    _sut.InputUrl = "https://example.com";
    _urlCheckerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(SuccessfulCheckResult());

    await _sut.OnPostAsync();

    var saved = await _context.UrlMonitors.SingleAsync();
    saved.IsActive.Should().BeTrue();
    saved.IsPaused.Should().BeFalse();
  }

  // ══════════════════════════════════════════════════════════════════════════
  // OnPostDeleteAsync
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task OnPostDeleteAsync_WithExistingId_RemovesMonitorFromDatabase()
  {
    var monitor = await SeedMonitorAsync();

    await _sut.OnPostDeleteAsync(monitor.Id);

    _context.UrlMonitors.Should().BeEmpty();
  }

  [Fact]
  public async Task OnPostDeleteAsync_WithExistingId_ReturnsOkResult()
  {
    var monitor = await SeedMonitorAsync();

    var result = await _sut.OnPostDeleteAsync(monitor.Id);

    result.Should().BeOfType<OkResult>();
  }

  [Fact]
  public async Task OnPostDeleteAsync_WithNonExistentId_ReturnsOkResult()
  {
    // The handler is intentionally idempotent — a missing record is not an error.
    var result = await _sut.OnPostDeleteAsync(id: 9999);

    result.Should().BeOfType<OkResult>();
  }

  [Fact]
  public async Task OnPostDeleteAsync_WithNonExistentId_DoesNotThrow()
  {
    var act = async () => await _sut.OnPostDeleteAsync(id: 9999);

    await act.Should().NotThrowAsync();
  }

  [Fact]
  public async Task OnPostDeleteAsync_OnlyRemovesTargetedMonitor()
  {
    var target = await SeedMonitorAsync("https://target.com");
    var other = await SeedMonitorAsync("https://other.com");

    await _sut.OnPostDeleteAsync(target.Id);

    _context.UrlMonitors.Should().ContainSingle(m => m.Id == other.Id);
  }

  // ══════════════════════════════════════════════════════════════════════════
  // OnGetMonitorsPartialAsync
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task OnGetMonitorsPartialAsync_ReturnsPartialViewResult()
  {
    var result = await _sut.OnGetMonitorsPartialAsync();

    result.Should().BeOfType<PartialViewResult>();
  }

  [Fact]
  public async Task OnGetMonitorsPartialAsync_ReturnsCorrectViewName()
  {
    var result = await _sut.OnGetMonitorsPartialAsync();

    result.ViewName.Should().Be("_MonitorTable");
  }

  [Fact]
  public async Task OnGetMonitorsPartialAsync_PassesUrlMonitors_AsViewModel()
  {
    await SeedMonitorAsync("https://alpha.com");
    await SeedMonitorAsync("https://beta.com");

    var result = await _sut.OnGetMonitorsPartialAsync();

    result.Model.Should().BeAssignableTo<IEnumerable<UrlMonitor>>()
        .Which.Should().HaveCount(2);
  }

  // ══════════════════════════════════════════════════════════════════════════
  // OnPostSetPauseAsync
  // ══════════════════════════════════════════════════════════════════════════

  [Fact]
  public async Task OnPostSetPauseAsync_WithNonExistentId_ReturnsNotFound()
  {
    var result = await _sut.OnPostSetPauseAsync(id: 9999);

    result.Should().BeOfType<NotFoundResult>();
  }

  [Fact]
  public async Task OnPostSetPauseAsync_WhenMonitorIsActive_PausesIt()
  {
    var monitor = await SeedMonitorAsync(isPaused: false);

    await _sut.OnPostSetPauseAsync(monitor.Id);

    var updated = await _context.UrlMonitors.FindAsync(monitor.Id);
    updated!.IsPaused.Should().BeTrue();
  }

  [Fact]
  public async Task OnPostSetPauseAsync_WhenMonitorIsPaused_ResumesIt()
  {
    var monitor = await SeedMonitorAsync(isPaused: true);

    await _sut.OnPostSetPauseAsync(monitor.Id);

    var updated = await _context.UrlMonitors.FindAsync(monitor.Id);
    updated!.IsPaused.Should().BeFalse();
  }

  [Fact]
  public async Task OnPostSetPauseAsync_ReturnsJsonResult_WithNewPausedState()
  {
    var monitor = await SeedMonitorAsync(isPaused: false);

    var result = await _sut.OnPostSetPauseAsync(monitor.Id);

    var json = result.Should().BeOfType<JsonResult>().Subject;
    var value = json.Value!;

    // Inspect anonymous object via reflection — avoids a hard dependency on a DTO type.
    var isPaused = (bool)value.GetType().GetProperty("isPaused")!.GetValue(value)!;
    isPaused.Should().BeTrue();
  }

  [Fact]
  public async Task OnPostSetPauseAsync_Toggle_IsIdempotent_AfterTwoToggles()
  {
    var monitor = await SeedMonitorAsync(isPaused: false);

    await _sut.OnPostSetPauseAsync(monitor.Id);
    await _sut.OnPostSetPauseAsync(monitor.Id);

    var updated = await _context.UrlMonitors.FindAsync(monitor.Id);
    updated!.IsPaused.Should().BeFalse(because: "two toggles return to the original state");
  }
}