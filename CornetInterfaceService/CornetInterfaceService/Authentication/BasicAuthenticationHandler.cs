using System;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CornetInterfaceService.Authentication;

/// <summary>
/// Handles Basic Authentication for incoming requests.
/// Credentials are configured in User Secrets (development) or environment variables (production).
/// </summary>
public class BasicAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IConfiguration _configuration;

    public BasicAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Skip authentication for health check endpoint
        if (Request.Path.StartsWithSegments("/hc"))
        {
            return AuthenticateResult.NoResult();
        }

        if (!Request.Headers.ContainsKey("Authorization"))
        {
            return AuthenticateResult.Fail("Missing Authorization Header");
        }

        try
        {
            var authHeader = AuthenticationHeaderValue.Parse(Request.Headers["Authorization"]);

            if (authHeader.Scheme != "Basic")
            {
                return AuthenticateResult.Fail("Invalid Authorization Scheme. Expected Basic.");
            }

            var credentialBytes = Convert.FromBase64String(authHeader.Parameter ?? string.Empty);
            var credentials = Encoding.UTF8.GetString(credentialBytes).Split(':', 2);

            if (credentials.Length != 2)
            {
                return AuthenticateResult.Fail("Invalid Authorization Header format");
            }

            var username = credentials[0];
            var password = credentials[1];

            // Validate credentials against configuration
            var configuredUsername = _configuration["BASIC_AUTH_USERNAME"];
            var configuredPassword = _configuration["BASIC_AUTH_PASSWORD"];

            if (string.IsNullOrEmpty(configuredUsername) || string.IsNullOrEmpty(configuredPassword))
            {
                Logger.LogError("BasicAuth credentials not configured. Set BasicAuth:Username and BasicAuth:Password in User Secrets or environment variables.");
                return AuthenticateResult.Fail("Server authentication configuration error");
            }

            if (username == configuredUsername && password == configuredPassword)
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, username),
                    new Claim(ClaimTypes.Name, username),
                };
                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                Logger.LogInformation("Basic authentication successful for user: {Username}", username);
                return AuthenticateResult.Success(ticket);
            }

            Logger.LogWarning("Basic authentication failed for user: {Username}", username);
            return AuthenticateResult.Fail("Invalid Username or Password");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error processing Basic Authentication");
            return AuthenticateResult.Fail($"Error processing authentication: {ex.Message}");
        }
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers["WWW-Authenticate"] = "Basic realm=\"Cornet Interface Service\"";
        return base.HandleChallengeAsync(properties);
    }
}
