using System.Diagnostics;
using OpenTelemetry;

namespace DevBuddy.Infrastructure.Observability;

/// <summary>
/// Takes the request path back off every span before it leaves the process.
/// <para>
/// The library instrumentation is generous by default: an HTTP server span carries
/// <c>url.path</c> and <c>url.query</c> as they arrived, and a client span carries the whole
/// <c>url.full</c>. For this application those are not innocuous. A path like
/// <c>/workspaces/{a}/projects/{b}/evidence/{c}</c> is three tenant identifiers, and a telemetry
/// backend is not scoped to a workspace or gated by a permission the way the audit store is —
/// anybody with a dashboard would be able to enumerate the installation.
/// </para>
/// <para>
/// What survives is what a dashboard actually needs: <c>http.route</c>, which is the template
/// rather than the filled path, the status code, the method, and the host a client span went to.
/// The rule this enforces is written down in <c>DevBuddyTelemetry</c>; this is the half of it that
/// applies to spans nobody in this codebase creates.
/// </para>
/// </summary>
internal sealed class ScrubIdentifiersProcessor : BaseProcessor<Activity>
{
    private static readonly string[] Removed =
    [
        "url.path",
        "url.query",
        "url.full",

        // The pre-1.0 semantic-convention names, still emitted when an instrumentation library is
        // configured for the old schema. Removing both is cheaper than depending on which.
        "http.url",
        "http.target",
    ];

    public override void OnEnd(Activity activity)
    {
        if (activity is null)
        {
            return;
        }

        foreach (string tag in Removed)
        {
            // SetTag(null) removes rather than blanks: a key present with an empty value would
            // read as "this request had no path", which is a different and false claim.
            if (activity.GetTagItem(tag) is not null)
            {
                activity.SetTag(tag, null);
            }
        }

        base.OnEnd(activity);
    }
}
