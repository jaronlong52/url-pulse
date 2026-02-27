namespace UrlPulse.Models;

public class UrlMonitor
{
  public int Id { get; set; }
  public string Url { get; set; } = string.Empty;
  public int CheckIntervalSeconds { get; set; } = 60; // How often to check the URL
  public int TimeoutMs { get; set; } = 5000; // How long to wait before considering the check a failure
  public bool IsActive { get; set; } = true;
  public DateTime CreatedAt { get; set; } // UTC time
  public List<LatencyHistory> History { get; set; } = new(); // Navigation Property: One Monitor has many History records
}