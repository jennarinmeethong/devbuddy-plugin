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
/// Two of them, COOP and COEP, only mean something on HTTPS, and are sent only then
/// (<see cref="SecureOriginOnly"/>).
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

    internal static readonly IReadOnlyList<KeyValuePair<string, string>> Always =
    [
        new("Content-Security-Policy", ContentSecurityPolicy),
        new("X-Frame-Options", "DENY"),
        new("X-Content-Type-Options", "nosniff"),
        new("Referrer-Policy", "no-referrer"),
        new("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=(), usb=()"),
        new("Cross-Origin-Resource-Policy", "same-origin"),
    ];

    /// <summary>
    /// Policies a browser honours only on a secure origin, sent only when the page arrived on one.
    /// <para>
    /// Over plain HTTP, Chromium ignores <c>Cross-Origin-Opener-Policy</c> and says so as a console
    /// error on every page load; the e2e suite found it, because the LAN installation and the test
    /// stack both serve plain HTTP. <c>Cross-Origin-Embedder-Policy</c> is ignored the same way,
    /// silently. Behind a reverse proxy that terminates TLS, both are honoured, and the proxy says so
    /// in <c>X-Forwarded-Proto</c>, which Caddy sends by default and the documented nginx
    /// configuration sets. Trusting that header costs nothing here: a caller who forges it only
    /// earns a header their browser then ignores.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlyList<KeyValuePair<string, string>> SecureOriginOnly =
    [
        new("Cross-Origin-Opener-Policy", "same-origin"),
        new("Cross-Origin-Embedder-Policy", "require-corp"),
    ];

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.Use((context, next) =>
        {
            HttpResponse response = context.Response;
            IEnumerable<KeyValuePair<string, string>> headers = ArrivedOverHttps(context.Request)
                ? Always.Concat(SecureOriginOnly)
                : Always;

            response.OnStarting(() =>
            {
                foreach ((string name, string value) in headers)
                {
                    response.Headers[name] = value;
                }

                return Task.CompletedTask;
            });

            return next(context);
        });
    }

    /// <summary>
    /// Whether the browser reached this page over HTTPS: directly, or through a proxy that said so.
    /// The first value is the client's own hop when proxies are chained.
    /// </summary>
    internal static bool ArrivedOverHttps(HttpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsHttps)
        {
            return true;
        }

        string forwarded = request.Headers["X-Forwarded-Proto"].ToString();
        int comma = forwarded.IndexOf(',', StringComparison.Ordinal);
        string first = (comma < 0 ? forwarded : forwarded[..comma]).Trim();

        return string.Equals(first, "https", StringComparison.OrdinalIgnoreCase);
    }
}
