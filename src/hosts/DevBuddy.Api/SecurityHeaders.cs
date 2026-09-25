namespace DevBuddy.Api;

/// <summary>
/// The headers every answer from this host carries: the client's pages, the API's JSON, a refusal,
/// and the exception handler's 500 (Phase 14, C4).
/// <para>
/// ZAP's first scans found none of them. This host serves the administration UI as well as the
/// API, so the page a person signs in on had no content security policy and could be framed by
/// any site, and an evidence download could be sniffed into something other than what it was
/// stored as.
/// </para>
/// <para>
/// Set when the response starts rather than when the request arrives. The exception handler clears
/// the response before it writes its own, and headers set earlier would go with it; a callback
/// registered for the start of the response still runs.
/// </para>
/// <para>
/// No <c>Strict-Transport-Security</c>. This host speaks plain HTTP behind a reverse proxy that
/// holds the certificate, and HSTS is the proxy's to send: sent from here it would be sent over
/// plain HTTP, where a browser ignores it, or through a proxy that may be serving other names.
/// </para>
/// </summary>
internal static class SecurityHeaders
{
    /// <summary>
    /// The policy the built client needs and nothing more. Vite emits one module script and one
    /// stylesheet, both same-origin, with no inline code, no <c>url()</c> in the stylesheet and no
    /// <c>eval</c>, and the client talks only to this origin. An evidence download goes through a
    /// blob URL on an <c>a[download]</c> link, which is a download, not a fetch the policy governs.
    /// Nothing may frame the pages, which is also what <c>X-Frame-Options</c> says, for browsers
    /// that predate <c>frame-ancestors</c>.
    /// </summary>
    internal const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self'; "
        + "font-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'; "
        + "form-action 'self'; frame-ancestors 'none'";

    internal static readonly IReadOnlyList<KeyValuePair<string, string>> Values =
    [
        new("Content-Security-Policy", ContentSecurityPolicy),
        new("X-Frame-Options", "DENY"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()"),
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
        new("Cross-Origin-Embedder-Policy", "require-corp"),
    ];

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) =>
        {
            HttpResponse response = context.Response;

            response.OnStarting(() =>
            {
                foreach ((string name, string value) in Values)
                {
                    response.Headers[name] = value;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }
}
