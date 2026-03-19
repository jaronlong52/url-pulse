using UrlPulse.Core.Services;

namespace UrlPulse.Core.Interfaces;

public interface IUrlChecker
{
  Task<UrlCheckResult> CheckUrlAsync(string url, int timeoutMs);
}
