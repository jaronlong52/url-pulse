namespace UrlPulse.Core.Interfaces;

public interface IUrlMonitoringService
{
  Task RunChecksAsync(CancellationToken cancellationToken);
}