using System.Diagnostics;

namespace UrlPulse.Services;

public class UrlChecker : IUrlChecker
{
  private readonly HttpClient _httpClient;

  public UrlChecker(HttpClient httpClient)
  {
    _httpClient = httpClient;
  }

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
