using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Models;
using UrlPulse.Core.Interfaces;

namespace UrlPulse.Core.Data;

public class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    ICurrentUserService currentUserService) : DbContext(options), DbContext
{
  private readonly ICurrentUserService _currentUserService = currentUserService;

  public DbSet<UrlMonitor> UrlMonitors { get; set; }
  public DbSet<LatencyHistory> LatencyHistories { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // Automatically filter out other users' data
    modelBuilder.Entity<UrlMonitor>().HasQueryFilter(m =>
        m.OwnerId == _currentUserService.UserId);
  }

  public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
  {
    // Automatically assign the current user ID to new monitors
    foreach (var entry in ChangeTracker.Entries<UrlMonitor>())
    {
      if (entry.State == EntityState.Added)
      {
        entry.Entity.OwnerId = _currentUserService.UserId ?? string.Empty;
      }
    }
    return base.SaveChangesAsync(cancellationToken);
  }
}