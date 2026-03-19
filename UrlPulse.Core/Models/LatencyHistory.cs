namespace UrlPulse.Core.Models;

public class LatencyHistory
{
  public int Id { get; set; }
  public int LatencyMs { get; set; }
  public int StatusCode { get; set; }
  public string ErrorMessage { get; set; } = string.Empty;
  public DateTime CheckedAt { get; set; } = DateTime.UtcNow; // UTC time
  public int UrlMonitorId { get; set; } // Foreign Key
  public UrlMonitor UrlMonitor { get; set; } = null!; // Navigation Property: Link back to the parent
}