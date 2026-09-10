using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Workers;

/// <summary>
/// The identity a background job acts as, when it acts as somebody.
/// <para>
/// This type exists to make one thing impossible: a background job with a caller nobody granted.
/// It cannot be constructed from a <see cref="UserId"/>, a workspace, or anything a configuration
/// file could name. The only way to obtain one is
/// <see cref="FromMachineToken(MachineTokenIdentity?, string)"/>, which takes the result of
/// resolving a real token against the store — so the reach of a worker is a membership somebody
/// granted through the ordinary interface and can revoke there, and the audit row names its owner.
/// </para>
/// <para>
/// ADR-0013 permits exactly two shapes for autonomous work and nothing between them. This is the
/// first: <see cref="CallerBoundWorkerJob"/> is bounded by a credential exactly as a plugin
/// session is. The second, <see cref="InstallationWorkerJob"/>, has no caller at all and may
/// therefore touch nothing a person's permissions would gate. Anything between the two would be
/// the installation-wide superuser this system has deliberately never had, running unattended.
/// </para>
/// </summary>
public sealed class WorkerCaller
{
    private WorkerCaller(CallerContext context, MachineTokenIdentity identity)
    {
        Context = context;
        Identity = identity;
    }

    /// <summary>What the pipeline is handed. Carries the credential's workspace ceiling.</summary>
    public CallerContext Context { get; }

    /// <summary>The resolved token, for a job that needs to report whose authority it used.</summary>
    public MachineTokenIdentity Identity { get; }

    /// <summary>
    /// The workspace this worker may act in, and the only one. Taken from the credential rather
    /// than from anything the job was configured with.
    /// </summary>
    public WorkspaceId Workspace => Identity.WorkspaceId;

    /// <summary>
    /// Builds a caller from a resolved machine token, or answers null when the token resolved to
    /// nobody — unknown, revoked, expired, or issued before tokens carried a workspace.
    /// <para>
    /// Null is a refusal to run, never a fallback to an anonymous or elevated identity. A worker
    /// that could not resolve its credential has no business doing the work by another route.
    /// </para>
    /// <para>
    /// The channel is <see cref="AccessChannel.Ai"/> and is not a parameter. A worker exists to
    /// call an LLM or an embedding provider, and that is precisely the channel the AI controls are
    /// attached to: the eighteen-operation allow-list, the per-project AI access policy, and the
    /// SB-18 personal-data redaction. <see cref="AccessChannel.InternalSystem"/> would skip the
    /// second and third of those, so a worker running on it could read a project whose AI access
    /// nobody enabled and hand the contents to a model. That is not a channel a worker may pick.
    /// </para>
    /// </summary>
    /// <param name="identity">The result of <see cref="IMachineTokenService.ResolveAsync"/>.</param>
    /// <param name="requestId">Correlates the pipeline stages and the audit entry for this run.</param>
    public static WorkerCaller? FromMachineToken(MachineTokenIdentity? identity, string requestId)
    {
        if (identity is null)
        {
            return null;
        }

        return new WorkerCaller(
            new CallerContext(
                identity.UserId,
                AccessChannel.Ai,
                Guard.NotBlank(requestId, nameof(requestId)),
                new CredentialScope(identity.TokenId, identity.WorkspaceId)),
            identity);
    }
}
