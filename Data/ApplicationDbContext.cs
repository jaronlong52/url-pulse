using Microsoft.EntityFrameworkCore;

namespace UrlPulse.Data;

public class ApplicationDbContext : DbContext
{
  public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
      : base(options)
  {
  }

  public DbSet<Models.UrlMonitor> UrlMonitors { get; set; }
}