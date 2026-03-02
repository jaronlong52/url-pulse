using Microsoft.EntityFrameworkCore;
using UrlPulse.Data;
using UrlPulse.Models;

namespace UrlPulse.Services;

// BackgroundService runs independently of HTTP requests.
// This keeps monitoring decoupled from the web/UI layer.
public class UrlMonitoringService : BackgroundService
{
  // Used to manually create DI scopes since BackgroundService is a singleton.
  // Required to safely resolve scoped services like DbContext.
  private readonly IServiceScopeFactory _scopeFactory;

  // Structured logging for observability and production diagnostics.
  private readonly ILogger<UrlMonitoringService> _logger;

  public UrlMonitoringService(
      IServiceScopeFactory scopeFactory,
      ILogger<UrlMonitoringService> logger)
  {
    _scopeFactory = scopeFactory;
    _logger = logger;
  }

  // Entry point automatically executed when the application starts.
  // Runs continuously until the application shuts down.
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("URL Monitoring Service started.");

    // Main monitoring loop — respects cancellation for graceful shutdown.
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        // Execute one monitoring cycle.
        await RunChecksAsync(stoppingToken);
      }
      catch (Exception ex)
      {
        // Prevent a single failure from terminating the entire service.
        _logger.LogError(ex, "Error during monitoring cycle.");
      }

      // Wake up frequently enough to serve the shortest allowed interval.
      // The per-monitor check inside RunChecksAsync decides if each one is due.
      await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
    }
  }

  // Executes a single monitoring cycle.
  // Determines which monitors are due and records new results.
  private async Task RunChecksAsync(CancellationToken token)
  {
    // Create a new DI scope for this cycle to properly manage scoped services.
    using var scope = _scopeFactory.CreateScope();

    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var checker = scope.ServiceProvider.GetRequiredService<IUrlChecker>();

    var now = DateTime.UtcNow;

    // Load only active monitors and only their most recent history entry.
    // This avoids loading entire history collections
    var monitors = await context.UrlMonitors
        .Where(m => m.IsActive && !m.IsPaused)
        .Include(m => m.History
            .OrderByDescending(h => h.CheckedAt)
            .Take(1))
        .ToListAsync(token);

    foreach (var monitor in monitors)
    {
      var lastCheck = monitor.History.FirstOrDefault();

      // Respect per-monitor interval.
      // Skip if the monitor is not yet due for another check.
      if (lastCheck != null &&
          (now - lastCheck.CheckedAt).TotalSeconds < monitor.CheckIntervalSeconds)
      {
        continue;
      }

      // Perform the actual HTTP check via abstraction.
      // Monitoring logic does not depend on HTTP implementation details.
      var result = await checker.CheckUrlAsync(monitor.Url, monitor.TimeoutMs);

      // Append new time-series record rather than overwriting state.
      // Enables trend analysis, uptime calculations, and historical reporting.
      context.LatencyHistories.Add(new LatencyHistory
      {
        UrlMonitorId = monitor.Id,
        CheckedAt = result.CheckedAt,
        LatencyMs = result.LatencyMs ?? 0,
        StatusCode = result.StatusCode,
        ErrorMessage = result.IsUp ? string.Empty : "Service Unavailable"
      });
    }

    // Persist all results in a single batch to reduce database round-trips.
    await context.SaveChangesAsync(token);
  }
}