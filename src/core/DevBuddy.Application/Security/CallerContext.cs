using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Security;

/// <summary>
/// Who is asking and how they arrived. Constructed by the host from an authenticated principal,
/// never from anything in the request body.
/// <para>
/// For an AI channel, <see cref="UserId"/> is the person on whose behalf the AI is acting. That
/// is what lets results be narrowed to the requesting user permissions rather than to whatever
/// the AI credential could reach (control SB-09).
/// </para>
/// </summary>
public sealed record CallerContext
{
    public CallerContext(
        UserId userId, AccessChannel channel, string requestId, CredentialScope? credential = null)
    {
        UserId = userId;
        Channel = Guard.Defined(channel, nameof(channel));
        RequestId = Guard.NotLongerThan(Guard.NotBlank(requestId, nameof(requestId)), 100, nameof(requestId));
        Credential = credential;
    }

    /// <summary>
    /// Nobody, on the AI channel. For the one question that has the same answer for everyone —
    /// what tools exist — so a host does not have to invent an identity to ask it.
    /// </summary>
    public static CallerContext Anonymous { get; } = new(default, AccessChannel.Ai, "anonymous");

    public UserId UserId { get; }

    public AccessChannel Channel { get; }

    /// <summary>Correlates the pipeline stages and the audit entry for one request.</summary>
    public string RequestId { get; }

    /// <summary>
    /// The limit the credential this caller presented puts on where they may act, or null when
    /// they presented none.
    /// <para>
    /// Null is the ordinary case and means unrestricted by credential: a person signed in through
    /// the web interface reaches every workspace their memberships cover, which is what a browser
    /// session is for. A machine token fills this in with the one workspace it was minted for,
    /// and the authorization service refuses any request naming another (SB-11).
    /// </para>
    /// </summary>
    public CredentialScope? Credential { get; }

    /// <summary>
    /// True when no identity was resolved. The pipeline stops here rather than falling through
    /// to a permission check that might accidentally pass.
    /// </summary>
    public bool IsAnonymous => UserId.Value == Guid.Empty;
}
