using System.Diagnostics;
using UrlPulse.Core.Interfaces;
using UrlPulse.Core.Services;

namespace UrlPulse.Infrastructure.Services;

public class UrlChecker(HttpClient httpClient) : IUrlChecker
{
  private readonly HttpClient _httpClient = httpClient;

  public async Task<UrlCheckResult> CheckUrlAsync(string url, int timeoutMs)
  {
    var checkedAt = DateTime.UtcNow;

    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));

    try
    {
      var stopwatch = Stopwatch.StartNew();
      using var response = await _httpClient.GetAsync(url, cts.Token);
      stopwatch.Stop();

      return new UrlCheckResult(
          response.IsSuccessStatusCode,
          (int)stopwatch.ElapsedMilliseconds,
          checkedAt,
          (int)response.StatusCode
      );
    }
    catch
    {
      return new UrlCheckResult(
          false,
          null,
          checkedAt,
          0
      );
    }
  }
}
