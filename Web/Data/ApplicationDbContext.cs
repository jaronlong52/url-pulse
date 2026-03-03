using Microsoft.EntityFrameworkCore;
using UrlPulse.Models;

namespace UrlPulse.Data;

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
    // 1. Create a Composite Index for performance
    // This makes "Get last 10 results for Monitor X" lightning fast
    modelBuilder.Entity<LatencyHistory>()
        .HasIndex(h => new { h.UrlMonitorId, h.CheckedAt })
        .HasDatabaseName("IX_LatencyHistory_Monitor_Date");

    // 2. Optional: Index the CheckedAt column alone if you plan to 
    // run reports across ALL monitors for a specific time range
    modelBuilder.Entity<LatencyHistory>()
        .HasIndex(h => h.CheckedAt);
  }
}