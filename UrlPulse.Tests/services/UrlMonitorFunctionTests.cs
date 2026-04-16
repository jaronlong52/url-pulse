using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using UrlPulse.Infrastructure.Data;
using UrlPulse.Core.Models;
using UrlPulse.Core.Services;
using UrlPulse.Core.Interfaces;
using UrlPulse.Worker.Functions;

public class UrlMonitorFunctionTests
{
  // -------------------------------------------------------------------------
  // Test infrastructure
  // -------------------------------------------------------------------------

  private static ServiceProvider BuildServiceProvider(
      Action<ApplicationDbContext>? seed = null,
      Mock<IUrlChecker>? checkerMock = null)
  {
    var services = new ServiceCollection();
    var dbName = Guid.NewGuid().ToString();

    services.AddDbContext<ApplicationDbContext>(options =>
        options.UseInMemoryDatabase(dbName));

    services.AddScoped<IUrlChecker>(_ =>
        checkerMock?.Object ?? Mock.Of<IUrlChecker>());

    // Standard logger for the Function
    services.AddSingleton(Mock.Of<ILogger<UrlMonitorFunction>>());

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

  private static UrlMonitorFunction CreateFunction(ServiceProvider provider)
  {
    return new UrlMonitorFunction(
        provider.GetRequiredService<ApplicationDbContext>(),
        provider.GetRequiredService<IUrlChecker>(),
        provider.GetRequiredService<ILogger<UrlMonitorFunction>>()
    );
  }

  private static Mock<IUrlChecker> BuildChecker(bool isUp, int? latencyMs, int statusCode)
  {
    var mock = new Mock<IUrlChecker>();
    mock.Setup(c => c.CheckUrlAsync(It.IsAny<string>(), It.IsAny<int>()))
        .ReturnsAsync(new UrlCheckResult(isUp, latencyMs, DateTime.UtcNow, statusCode));
    return mock;
  }

  // -------------------------------------------------------------------------
  // Tests
  // -------------------------------------------------------------------------

  [Fact]
  public async Task Run_Should_AddHistory_WhenMonitorIsDue()
  {
    // Arrange
    var checkerMock = BuildChecker(true, 150, 200);
    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        IsPaused = false,
        CheckIntervalMinutes = 1
      });
    }, checkerMock);

    var function = CreateFunction(provider);

    // Act - We pass null for TimerInfo as our logic doesn't use the timer's properties
    await function.Run(null!);

    // Assert
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var entry = await db.LatencyHistories.SingleAsync();

    entry.StatusCode.Should().Be(200);
    entry.LatencyMs.Should().Be(150);
  }

  [Fact]
  public async Task Run_Should_NotCheck_WhenMonitorIsNotDue()
  {
    // Arrange
    var now = DateTime.UtcNow;
    var provider = BuildServiceProvider(context =>
    {
      var monitor = new UrlMonitor
      {
        Url = "https://example.com",
        IsActive = true,
        CheckIntervalMinutes = 60
      };
      // Already checked 5 mins ago
      monitor.History.Add(new LatencyHistory { CheckedAt = now.AddMinutes(-5), StatusCode = 200, Region = "Unknown" });
      context.UrlMonitors.Add(monitor);
    });

    var function = CreateFunction(provider);

    // Act
    await function.Run(null!);

    // Assert
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.LatencyHistories.Should().HaveCount(1); // Only the seeded one exists
  }

  [Fact]
  public async Task Run_Should_Ignore_Paused_Monitors()
  {
    // Arrange
    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://paused.com",
        IsActive = true,
        IsPaused = true,
        CheckIntervalMinutes = 1
      });
    });

    var function = CreateFunction(provider);

    // Act
    await function.Run(null!);

    // Assert
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.LatencyHistories.Should().BeEmpty();
  }

  [Fact]
  public async Task Run_Should_RecordError_WhenUrlIsDown()
  {
    // Arrange
    var checkerMock = BuildChecker(false, null, 503);
    var provider = BuildServiceProvider(context =>
    {
      context.UrlMonitors.Add(new UrlMonitor
      {
        Url = "https://down.com",
        IsActive = true,
        CheckIntervalMinutes = 1
      });
    }, checkerMock);

    var function = CreateFunction(provider);

    // Act
    await function.Run(null!);

    // Assert
    using var scope = provider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var entry = await db.LatencyHistories.SingleAsync();
    entry.ErrorMessage.Should().Be("Service Unavailable");
  }
}