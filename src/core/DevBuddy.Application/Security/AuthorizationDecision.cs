using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Security;

/// <summary>
/// The answer. There is no third state: a use case runs only after an explicit allow, and the
/// pipeline treats anything else as a denial.
/// </summary>
public sealed record AuthorizationDecision
{
    private AuthorizationDecision(bool isAllowed, string reason, string? boundedDataScope = null)
    {
        IsAllowed = isAllowed;
        Reason = Guard.NotLongerThan(Guard.NotBlank(reason, nameof(reason)), 500, nameof(reason));
        BoundedDataScope = boundedDataScope;
    }

    public bool IsAllowed { get; }

    /// <summary>
    /// Why. Written for an audit reader, and deliberately free of anything that would tell a
    /// caller about a resource they are not permitted to know exists.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// The project's separately approved bounded data-sharing scope, when this call is on the AI
    /// channel and one has been set. Null means customer, production, and personal data stay
    /// denied to AI for this project (SB-18, control B in the AI Data Policy).
    /// <para>
    /// Carried on the decision rather than looked up again later, because it is already read here
    /// while checking the project's AI access policy — a second read later could disagree with
    /// this one about a policy that changed in between.
    /// </para>
    /// </summary>
    public string? BoundedDataScope { get; }

    public static AuthorizationDecision Allow(string reason = "Permitted.", string? boundedDataScope = null) =>
        new(true, reason, boundedDataScope);

    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}
