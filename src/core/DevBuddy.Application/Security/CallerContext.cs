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
    public CallerContext(UserId userId, AccessChannel channel, string requestId)
    {
        UserId = userId;
        Channel = Guard.Defined(channel, nameof(channel));
        RequestId = Guard.NotLongerThan(Guard.NotBlank(requestId, nameof(requestId)), 100, nameof(requestId));
    }

    public UserId UserId { get; }

    public AccessChannel Channel { get; }

    /// <summary>Correlates the pipeline stages and the audit entry for one request.</summary>
    public string RequestId { get; }

    /// <summary>
    /// True when no identity was resolved. The pipeline stops here rather than falling through
    /// to a permission check that might accidentally pass.
    /// </summary>
    public bool IsAnonymous => UserId.Value == Guid.Empty;
}
