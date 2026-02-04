namespace UrlPulse.Models;

public class UrlMonitor
{
  public int Id { get; set; }
  public string Url { get; set; } = string.Empty;
  public DateTime CreatedAt { get; set; }
  public DateTime? LastChecked { get; set; }
  public int? LatencyMs { get; set; }
  public bool IsUp { get; set; } = true;
}