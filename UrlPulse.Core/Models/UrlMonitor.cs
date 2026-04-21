namespace UrlPulse.Core.Models;

public class UrlMonitor
{
  public int Id { get; set; }
  public string OwnerId { get; set; } = string.Empty;
  public string Url { get; set; } = string.Empty;
  public int CheckIntervalMinutes { get; set; } = 1; // How often to check the URL
  public bool IsPaused { get; set; } = false; // Whether the monitor is currently paused
  public int TimeoutMs { get; set; } = 5000; // How long to wait before considering the check a failure
  public bool IsActive { get; set; } = true; // Whether site is currently active
  public DateTime CreatedAt { get; set; } // UTC time
  public List<LatencyHistory> History { get; set; } = new(); // Navigation Property: One Monitor has many History records
}