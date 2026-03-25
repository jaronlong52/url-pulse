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
    base.OnModelCreating(modelBuilder);
  }
}