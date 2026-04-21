using System.Security.Claims;

namespace UrlPulse.Services;

public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
  private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

  public string? UserId => _httpContextAccessor.HttpContext?.User?.FindFirstValue(
      "http://schemas.microsoft.com/identity/claims/objectidentifier");
}