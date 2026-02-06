namespace UrlPulse.Services;

public interface IUrlChecker
{
  Task<UrlCheckResult> CheckUrlAsync(string url);
}
