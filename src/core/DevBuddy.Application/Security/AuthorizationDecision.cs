using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Security;

/// <summary>
/// The answer. There is no third state: a use case runs only after an explicit allow, and the
/// pipeline treats anything else as a denial.
/// </summary>
public sealed record AuthorizationDecision
{
    private AuthorizationDecision(bool isAllowed, string reason)
    {
        IsAllowed = isAllowed;
        Reason = Guard.NotLongerThan(Guard.NotBlank(reason, nameof(reason)), 500, nameof(reason));
    }

    public bool IsAllowed { get; }

    /// <summary>
    /// Why. Written for an audit reader, and deliberately free of anything that would tell a
    /// caller about a resource they are not permitted to know exists.
    /// </summary>
    public string Reason { get; }

    public static AuthorizationDecision Allow(string reason = "Permitted.") => new(true, reason);

    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}
