namespace GridWatch.Api.Security;

public static class SecurityHeaders
{
    /// <summary>
    /// Adds standard response headers that limit what a browser will do with the app's pages:
    /// no MIME sniffing, no framing (clickjacking), a strict referrer policy and a Content Security
    /// Policy that only allows the app's own scripts and connections. The interactive API docs load
    /// their own assets, so the CSP is skipped there.
    /// </summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            var path = context.Request.Path;
            if (!path.StartsWithSegments("/docs") && !path.StartsWithSegments("/openapi"))
            {
                headers.ContentSecurityPolicy =
                    "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
                    "img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
            }

            await next();
        });
}
