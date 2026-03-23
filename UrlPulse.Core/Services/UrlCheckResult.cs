namespace UrlPulse.Core.Services;

public record UrlCheckResult(
    bool IsUp,
    int? LatencyMs,
    DateTime CheckedAt,
    int StatusCode
);