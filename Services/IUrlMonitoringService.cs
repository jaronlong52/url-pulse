namespace UrlPulse.Services;

public interface IUrlMonitoringService
{
  Task RunChecksAsync(CancellationToken cancellationToken);
}