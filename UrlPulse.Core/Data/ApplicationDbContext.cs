using Microsoft.EntityFrameworkCore;
using UrlPulse.Core.Models;

namespace UrlPulse.Core.Data;

public class ApplicationDbContext : DbContext
{
  public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
      : base(options)
  {
  }

  public DbSet<UrlMonitor> UrlMonitors { get; set; }
  public DbSet<LatencyHistory> LatencyHistories { get; set; }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    // Your existing indexing logic stays exactly the same!
    modelBuilder.Entity<LatencyHistory>()
        .HasIndex(h => new { h.UrlMonitorId, h.CheckedAt })
        .HasDatabaseName("IX_LatencyHistory_Monitor_Date");

    modelBuilder.Entity<LatencyHistory>()
        .HasIndex(h => h.CheckedAt);
  }
}