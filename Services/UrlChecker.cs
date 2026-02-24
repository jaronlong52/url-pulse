using System.Diagnostics;

namespace UrlPulse.Services;

public class UrlChecker : IUrlChecker
{
  private readonly HttpClient _httpClient;

  public UrlChecker(HttpClient httpClient)
  {
    _httpClient = httpClient;
    _httpClient.Timeout = TimeSpan.FromSeconds(10);
  }

  public async Task<UrlCheckResult> CheckUrlAsync(string url)
  {
    var checkedAt = DateTime.UtcNow;

    try
    {
      var stopwatch = Stopwatch.StartNew();
      using var response = await _httpClient.GetAsync(url);
      stopwatch.Stop();

      return new UrlCheckResult(
          response.IsSuccessStatusCode,
          (int)stopwatch.ElapsedMilliseconds,
          checkedAt
      );
    }
    catch
    {
      return new UrlCheckResult(
          false,
          null,
          checkedAt
      );
    }
  }
}
