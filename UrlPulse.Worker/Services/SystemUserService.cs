using UrlPulse.Core.Interfaces;

namespace UrlPulse.Worker.Services;

// This replaces the web-based CurrentUserService for the background worker
public class SystemUserService : ICurrentUserService
{
  // Returns a recognizable string, or null, since the background worker 
  // shouldn't be creating new Monitors anyway, only LatencyHistories.
  public string? UserId => "SYSTEM_WORKER";
}