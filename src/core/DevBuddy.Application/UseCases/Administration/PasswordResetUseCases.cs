using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Application.UseCases.Administration;

// A password reset an administrator hands over in person (Phase 13, D1).
//
// Self-service recovery needs a delivery channel: SMTP, or the opt-in that writes the token to a
// log. An installation with neither generates a recovery token nobody can read, and the person is
// locked out until it expires. This is the other way in, and it takes the same shape
// create_user_account already has: the token comes back in the response to an administrator who
// asked for it, and goes nowhere else.
//
// A password belongs to the installation, not to a workspace, and that is what makes this harder
// than it looks. An administrator of one workspace who could reset anybody who is a member there
// could reset a person who also belongs to another workspace, set the password, and sign in to
// that other workspace as them. So the reach checked here is everywhere the password works: the
// caller must be able to manage accounts in every workspace where the subject holds a live grant.

public sealed record IssuePasswordResetRequest(WorkspaceId WorkspaceId, UserId SubjectUserId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => SubjectUserId.ToString();

    public override IReadOnlyList<string> Validate() =>
        SubjectUserId.Value == Guid.Empty ? ["A password reset needs a subject user."] : [];
}

public sealed record PasswordResetIssuedResponse(
    UserId UserId,
    string ResetToken,
    DateTimeOffset ResetTokenExpiresAt) : IAuditableResult
{
    /// <summary>The token is never audited. An audit row that carried it would be a credential at rest.</summary>
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["userId"] = UserId.Value.ToString(),
            ["expiresAt"] = ResetTokenExpiresAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        };
}

public sealed class IssuePasswordResetUseCase(IAccessDirectory directory, IAccountRecoveryService recovery)
    : UseCase<IssuePasswordResetRequest, PasswordResetIssuedResponse>
{
    private readonly IAccessDirectory _directory = Guard.NotNull(directory, nameof(directory));
    private readonly IAccountRecoveryService _recovery = Guard.NotNull(recovery, nameof(recovery));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.IssuePasswordReset;

    protected internal override async Task<PasswordResetIssuedResponse> HandleAsync(
        IssuePasswordResetRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        if (request.SubjectUserId == caller.UserId)
        {
            // Somebody who is signed in can already change their own password; somebody holding
            // only a stolen session should not be able to turn it into the password.
            throw new InvalidTransitionException(
                "An administrator cannot issue a reset for their own account. Use password recovery instead.");
        }

        IReadOnlyList<Membership> subjectGrants =
            await _directory.ListLiveMembershipsEverywhereAsync(request.SubjectUserId, cancellationToken);

        if (!subjectGrants.Any(grant => grant.WorkspaceId == request.WorkspaceId))
        {
            // Worded the same as an unknown account, so this cannot be used to learn who exists.
            throw new ResourceNotFoundException("No member of this workspace has that identifier.");
        }

        IReadOnlyList<Membership> callerGrants =
            await _directory.ListLiveMembershipsEverywhereAsync(caller.UserId, cancellationToken);

        bool reachesEverywhere = subjectGrants
            .Select(grant => grant.WorkspaceId)
            .Distinct()
            .All(workspace => callerGrants.Any(grant =>
                grant.WorkspaceId == workspace
                && grant.ProjectId is null
                && RolePermissions.Grants(grant.Role, PermissionKind.ManageAccounts)));

        if (!reachesEverywhere)
        {
            // The subject can sign in somewhere this administrator does not administer. Resetting
            // the password would hand them that workspace too.
            throw new GuardRefusalException(
                "account-reach",
                "This person also belongs to a workspace you do not administer, so their password is not yours to reset. "
                + "An administrator of every workspace they belong to can, or they can use password recovery.");
        }

        PasswordReset reset = await _recovery.IssueForAsync(request.SubjectUserId, cancellationToken)
            ?? throw new InvalidTransitionException("That account is disabled, so no reset was issued.");

        return new PasswordResetIssuedResponse(request.SubjectUserId, reset.Token, reset.ExpiresAt);
    }
}
