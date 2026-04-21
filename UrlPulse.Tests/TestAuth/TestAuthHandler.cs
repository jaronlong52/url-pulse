using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text.Encodings.Web;

namespace UrlPulse.Tests.TestAuth;

public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
  public const string SchemeName = "TestScheme";

  protected override Task<AuthenticateResult> HandleAuthenticateAsync()
  {
    // You can customize claims here (roles, name, etc.)
    var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "TestUser"),
            new(ClaimTypes.NameIdentifier, "test-user-id"),
            // Add roles if your app uses them later: new Claim(ClaimTypes.Role, "Admin")
        };

    var identity = new ClaimsIdentity(claims, SchemeName);
    var principal = new ClaimsPrincipal(identity);

    var ticket = new AuthenticationTicket(principal, SchemeName);

    return Task.FromResult(AuthenticateResult.Success(ticket));
  }
}