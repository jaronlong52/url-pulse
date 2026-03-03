using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using UrlPulse.Data;
using UrlPulse.Models;
using UrlPulse.Services;

public class UrlMonitoringServiceTests
{
  // Shared method name constant — prevents silent breakage if the method is renamed.
  private const string RunChecksMethod = "RunChecksAsync";
  private const string ExecuteAsyncMethod = "ExecuteAsync";

  // -------------------------------------------------------------------------
  // Test infrastructure
  // -------------------------------------------------------------------------

  // Builds a fully wired DI container with an isolated in-memory database.
  // Optional seed action, checker mock, and logger mock can be injected.
  private static ServiceProvider BuildServiceProvider(
      Action<ApplicationDbContext>? seed = null,
      Mock<IUrlChecker>? checkerMock = null,
      Mock<ILogger<UrlMonitoringService>>? loggerMock = null)
  {
    var services = new ServiceCollection();

    // Capture the DB name here so every scope in this test shares the same database.
    var dbName = Guid.NewGuid().ToString();
    services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase(dbName));

    services.AddScoped<IUrlChecker>(_ =>
        checkerMock?.Object ?? Mock.Of<IUrlChecker>());

    services.AddSingleton(loggerMock?.Object ??
        Mock.Of<ILogger<UrlMonitoringService>>());

    var provider = services.BuildServiceProvider();

    if (seed != null)
    {
      using var scope = provider.CreateScope();
      var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
      seed(context);
      context.SaveChanges();
    }

    return provider;
  }

  // Resolves the service using the same DI container used by the tests.
  private static UrlMonitoringService CreateService(ServiceProvider provider)
  {
    var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();
    var logger = provider.GetRequiredService<ILogger<UrlMonitoringService>>();
    return new UrlMonitoringService(scopeFactory, logger);
  }

  // Invokes the private RunChecksAsync method directly via reflection.
  private static Task InvokeRunChecksAsync(UrlMonitoringService service) =>
      (Task)typeof(UrlMonitoringService)
          .GetMethod(RunChecksMethod,
              System.Reflection.BindingFlags.NonPublic |
              System.Reflection.BindingFlags.Instance)!
          .Invoke(service, new object[] { CancellationToken.None })!;

  // Invokes ExecuteAsync via reflection and returns the running task.
  private static Task InvokeExecuteAsync(
      UrlMonitoringService service,
      CancellationToken token) =>
      (Task)typeof(UrlMonitoringService)
          .GetMethod(ExecuteAsyncMethod,
              System.Reflection.BindingFlags.NonPublic |
              System.Reflection.BindingFlags.Instance)!
          .Invoke(service, new object[] { token })!;

  // Builds a mock checker that returns a successful result with the given values.
  private static Mock<IUrlChecker> BuildSuccessfulChecker(
      int latencyMs = 150,
      int statusCode = 200) =>
      BuildChecker(isUp: true, latencyMs: latencyMs, statusCode: statusCode);

  // Builds a mock checker that returns a failed result.
  private static Mock<IUrlChecker> BuildFailedChecker(int statusCode = 503) =>
      BuildChecker(isUp: false, latencyMs: null, statusCode: statusCode);

  private static Mock<IUrlChecker> BuildChecker(
      bool isUp,
      int? latencyMs,
      int statusCode)
  {
    var mock = new Mock<IUrlChecker>();
    mock.Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(new UrlCheckResult(isUp, latencyMs, DateTime.UtcNow, statusCode));
    return mock;
  }

  // -------------------------------------------------------------------------
  // RunChecksAsync — history creation
  // -------------------------------------------------------------------------

  [Fact]
  public async Task RunChecks_Should_AddHistory_WhenMonitorIsDue()
  {
    var checkerMock = BuildSuccessfulChecker(latencyMs: 150, statusCode: 200);

    var provider = BuildServiceProvider(context =>
    {
      // No history means the monitor is always due.
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 1
      });
    }, checkerMock);

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // Verify the row was inserted with the values returned by the checker.
    var entry = await db.LatencyHistories.SingleAsync();
    entry.StatusCode.Should().Be(200);
    entry.LatencyMs.Should().Be(150);
    entry.ErrorMessage.Should().BeEmpty();
  }

  [Fact]
  public async Task RunChecks_Should_NotRun_WhenMonitorIsNotDue()
  {
    var now = DateTime.UtcNow;

    var provider = BuildServiceProvider(context =>
    {
      var monitor = new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 60
      };

      // Checked just now — nowhere near the 60-minute interval.
      var history = new LatencyHistory
      {
        UrlMonitor = monitor,
        CheckedAt = now,
        StatusCode = 200,
        LatencyMs = 100
      };

      monitor.History.Add(history);
      context.UrlMonitors.Add(monitor);
    });

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // The original seed entry is the only one; no new check was performed.
    db.LatencyHistories.Should().HaveCount(1);
  }

  [Fact]
  public async Task RunChecks_Should_NotRun_WhenCheckedExactlyAtIntervalBoundary()
  {
    var now = DateTime.UtcNow;

    var provider = BuildServiceProvider(context =>
    {
      var monitor = new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 60
      };

      // Checked 59.9 minutes ago against a 60-minute interval — just inside the
      // boundary. Using exactly -60 is a timing race: milliseconds elapse between
      // seeding and the service reading DateTime.UtcNow, which can push the elapsed
      // time fractionally past the interval and trigger an unexpected check.
      var history = new LatencyHistory
      {
        UrlMonitor = monitor,
        CheckedAt = now.AddMinutes(-59.9),
        StatusCode = 200,
        LatencyMs = 100
      };

      monitor.History.Add(history);
      context.UrlMonitors.Add(monitor);
    });

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    db.LatencyHistories.Should().HaveCount(1);
  }

  // -------------------------------------------------------------------------
  // RunChecksAsync — filtering
  // -------------------------------------------------------------------------

  [Fact]
  public async Task RunChecks_Should_Ignore_Inactive_Or_Paused_Monitors()
  {
    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.AddRange(
          new UrlMonitor
          {
            Url = "https://a.com",
            IsActive = false,   // inactive
            CheckIntervalMinutes = 5
          },
          new UrlMonitor
          {
            Url = "https://b.com",
            IsActive = true,
            IsPaused = true,    // paused
            CheckIntervalMinutes = 5
          }
      );
    });

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    db.LatencyHistories.Should().BeEmpty();
  }

  [Fact]
  public async Task RunChecks_Should_CheckAllDueMonitors_WhenMultipleAreActive()
  {
    var checkerMock = BuildSuccessfulChecker();

    var provider = BuildServiceProvider(context =>
    {
      // Both monitors have no history so both are due.
      context.UrlMonitors.AddRange(
          new UrlMonitor
          {
            Url = "https://a.com",
            IsActive = true,
            IsPaused = false,
            CheckIntervalMinutes = 1
          },
          new UrlMonitor
          {
            Url = "https://b.com",
            IsActive = true,
            IsPaused = false,
            CheckIntervalMinutes = 1
          }
      );
    }, checkerMock);

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // One history entry per monitor.
    db.LatencyHistories.Should().HaveCount(2);
  }

  [Fact]
  public async Task RunChecks_Should_OnlyCheckDueMonitors_WhenMixed()
  {
    var now = DateTime.UtcNow;
    var checkerMock = BuildSuccessfulChecker();

    var provider = BuildServiceProvider(context =>
    {
      // Monitor A: no history — due for a check.
      var monitorA = new UrlMonitor
      {
        Url = "https://due.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 60
      };

      // Monitor B: checked 5 minutes ago with a 60-minute interval — not due.
      var monitorB = new UrlMonitor
      {
        Url = "https://not-due.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 60
      };

      monitorB.History.Add(new LatencyHistory
      {
        UrlMonitor = monitorB,
        CheckedAt = now.AddMinutes(-5),
        StatusCode = 200,
        LatencyMs = 80
      });

      context.UrlMonitors.AddRange(monitorA, monitorB);
    }, checkerMock);

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    // The seed entry for monitorB plus one new entry for monitorA.
    db.LatencyHistories.Should().HaveCount(2);
  }

  // -------------------------------------------------------------------------
  // RunChecksAsync — result mapping
  // -------------------------------------------------------------------------

  [Fact]
  public async Task RunChecks_Should_RecordErrorMessage_WhenUrlIsDown()
  {
    var checkerMock = BuildFailedChecker(statusCode: 503);

    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://down.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 1
      });
    }, checkerMock);

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var entry = await db.LatencyHistories.SingleAsync();
    entry.StatusCode.Should().Be(503);
    entry.ErrorMessage.Should().Be("Service Unavailable");
  }

  [Fact]
  public async Task RunChecks_Should_StoreZeroLatency_WhenLatencyIsNull()
  {
    // A timed-out or unreachable URL will produce a null latency.
    var checkerMock = BuildFailedChecker(statusCode: 0);

    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://timeout.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 1
      });
    }, checkerMock);

    var service = CreateService(provider);
    await InvokeRunChecksAsync(service);

    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    var entry = await db.LatencyHistories.SingleAsync();
    entry.LatencyMs.Should().Be(0);
  }

  // -------------------------------------------------------------------------
  // ExecuteAsync — lifecycle and error handling
  // -------------------------------------------------------------------------

  [Fact]
  public async Task ExecuteAsync_Should_LogStartupMessage()
  {
    var loggerMock = new Mock<ILogger<UrlMonitoringService>>();

    var provider = BuildServiceProvider(loggerMock: loggerMock);
    var service = CreateService(provider);

    using var cts = new CancellationTokenSource();
    var task = InvokeExecuteAsync(service, cts.Token);

    // Cancel immediately — we only care that the startup log was emitted.
    await cts.CancelAsync();
    try { await task; } catch (OperationCanceledException) { }

    loggerMock.Verify(
        l => l.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("started")),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.Once);
  }

  [Fact]
  public async Task ExecuteAsync_Should_LogError_And_Continue_WhenCheckerThrows()
  {
    var loggerMock = new Mock<ILogger<UrlMonitoringService>>();

    // Use a TCS to signal the moment the checker is invoked — avoids a fixed delay.
    var cycleStarted = new TaskCompletionSource();

    var checkerMock = new Mock<IUrlChecker>();
    checkerMock
        .Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .Callback(() => cycleStarted.TrySetResult())
        .ThrowsAsync(new Exception("Network failure"));

    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 1
      });
    }, checkerMock, loggerMock);

    var service = CreateService(provider);

    using var cts = new CancellationTokenSource();
    var task = InvokeExecuteAsync(service, cts.Token);

    // Wait until the checker has been called before asserting.
    await cycleStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cts.CancelAsync();
    try { await task; } catch (OperationCanceledException) { }

    loggerMock.Verify(
        l => l.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
        Times.AtLeastOnce);
  }
}