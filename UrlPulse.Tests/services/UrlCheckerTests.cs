using System.Net;
using FluentAssertions;
using Moq;
using Moq.Protected;
using UrlPulse.Core.Services;

namespace UrlPulse.Tests.Services;

public class UrlCheckerTests
{
  private static HttpClient CreateHttpClient(HttpResponseMessage responseMessage)
  {
    var handlerMock = new Mock<HttpMessageHandler>();

    handlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ReturnsAsync(responseMessage);

    return new HttpClient(handlerMock.Object);
  }

  private static HttpClient CreateHttpClientThatThrows(Exception exception)
  {
    var handlerMock = new Mock<HttpMessageHandler>();

    handlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .ThrowsAsync(exception);

    return new HttpClient(handlerMock.Object);
  }

  [Fact]
  public async Task CheckUrlAsync_ShouldReturnSuccessResult_WhenStatusCodeIs2xx()
  {
    var response = new HttpResponseMessage(HttpStatusCode.OK);
    var httpClient = CreateHttpClient(response);
    var sut = new UrlChecker(httpClient);

    var result = await sut.CheckUrlAsync("https://example.com", 5000);

    result.IsUp.Should().BeTrue();
    result.StatusCode.Should().Be(200);
    result.LatencyMs.Should().NotBeNull();
    result.LatencyMs.Should().BeGreaterThanOrEqualTo(0);
    result.CheckedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
  }

  [Fact]
  public async Task CheckUrlAsync_ShouldReturnFailureResult_WhenStatusCodeIsNot2xx()
  {
    var response = new HttpResponseMessage(HttpStatusCode.InternalServerError);
    var httpClient = CreateHttpClient(response);
    var sut = new UrlChecker(httpClient);

    var result = await sut.CheckUrlAsync("https://example.com", 5000);

    result.IsUp.Should().BeFalse();
    result.StatusCode.Should().Be(500);
    result.LatencyMs.Should().NotBeNull(); // request completed, just failed
  }

  [Fact]
  public async Task CheckUrlAsync_ShouldReturnFailureResult_WhenHttpRequestThrows()
  {
    var httpClient = CreateHttpClientThatThrows(new HttpRequestException());
    var sut = new UrlChecker(httpClient);

    var result = await sut.CheckUrlAsync("https://example.com", 5000);

    result.IsUp.Should().BeFalse();
    result.StatusCode.Should().Be(0);
    result.LatencyMs.Should().BeNull();
    result.CheckedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
  }

  [Fact]
  public async Task CheckUrlAsync_ShouldReturnFailureResult_WhenTimeoutOccurs()
  {
    var handlerMock = new Mock<HttpMessageHandler>();

    handlerMock
        .Protected()
        .Setup<Task<HttpResponseMessage>>(
            "SendAsync",
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>())
        .Returns<HttpRequestMessage, CancellationToken>(async (_, token) =>
        {
          await Task.Delay(5000, token);
          return new HttpResponseMessage(HttpStatusCode.OK);
        });

    var httpClient = new HttpClient(handlerMock.Object);
    var sut = new UrlChecker(httpClient);

    var result = await sut.CheckUrlAsync("https://example.com", 10);

    result.IsUp.Should().BeFalse();
    result.StatusCode.Should().Be(0);
    result.LatencyMs.Should().BeNull();
  }

  [Fact]
  public void UrlCheckResult_Record_ShouldBeValueEqual()
  {
    var now = DateTime.UtcNow;

    var result1 = new UrlCheckResult(true, 123, now, 200);
    var result2 = new UrlCheckResult(true, 123, now, 200);

    result1.Should().Be(result2); // record value equality
  }
}