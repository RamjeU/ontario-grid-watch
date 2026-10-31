using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace GridWatch.Api.Security;

public sealed class AdminOptions
{
    public const string Section = "Admin";

    /// <summary>Key required for admin endpoints. Leave empty to disable them. Never commit a real key:
    /// set it with user secrets locally and as an App Service setting (or Key Vault reference) in Azure.</summary>
    public string? ApiKey { get; set; }
}

/// <summary>
/// Protects admin endpoints with a shared key in the X-Api-Key header. Keys are compared as
/// SHA-256 hashes with a constant-time check, so response timing does not leak how much of a
/// guessed key was right. A real multi-user system would use Microsoft Entra ID instead.
/// </summary>
public sealed class ApiKeyFilter(IOptions<AdminOptions> options) : IAuthorizationFilter
{
    public const string HeaderName = "X-Api-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var configured = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(configured))
        {
            context.Result = Problem(StatusCodes.Status403Forbidden, "Admin endpoints are disabled", "No admin API key is configured on the server.");
            return;
        }

        var supplied = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (string.IsNullOrEmpty(supplied) || !KeysMatch(supplied, configured))
        {
            context.Result = Problem(StatusCodes.Status401Unauthorized, "Invalid or missing API key", $"Send a valid key in the {HeaderName} header.");
        }
    }

    private static bool KeysMatch(string supplied, string configured) =>
        CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configured)));

    private static ObjectResult Problem(int status, string title, string detail) =>
        new(new ProblemDetails { Status = status, Title = title, Detail = detail }) { StatusCode = status };
}

public static class RateLimits
{
    public const string Read = "read";
    public const string Admin = "admin";
}
