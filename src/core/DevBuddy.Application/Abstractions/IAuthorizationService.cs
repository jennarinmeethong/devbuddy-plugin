using DevBuddy.Application.Security;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Decides whether a caller may perform one operation on one resource.
/// <para>
/// The implementation is responsible for control SB-11 (verify identity, membership, and
/// resource permission server-side, and never trust a caller-supplied project identifier) and,
/// for the AI channel, for control SB-08 (per-project AI access, denied by default) and SB-09
/// (narrow to the requesting user, not to the AI credential).
/// </para>
/// </summary>
public interface IAuthorizationService
{
    Task<AuthorizationDecision> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken);
}
