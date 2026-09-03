using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.UseCases.Administration;

// Credentials a person mints for their own processes.
//
// Every one of these acts on the caller and nobody else. There is no user identifier in any
// request, so there is nothing to point at somebody else's tokens — the same shape as GET /me,
// and for the same reason: an operation that took a subject would need a rule about who may name
// whom, and the rule that needs no enforcement is the one that cannot be expressed.
//
// They are workspace-scoped only because the pipeline authorises against a membership and needs a
// workspace to check one in. Holding any live grant in the workspace is enough, which is correct:
// a token carries the permissions its owner already has, so minting one grants nothing.

public sealed record IssueMachineTokenRequest(
    WorkspaceId WorkspaceId,
    string Name,
    int LifetimeDays = 90) : WorkspaceRequest(WorkspaceId), IScannableRequest
{
    public override string ResourceReference => Name;

    /// <summary>
    /// The name is retained, so it is scanned. A person labelling a token after the machine it
    /// lives on is normal; a person pasting a connection string into the box is the case this
    /// catches (SB-17).
    /// </summary>
    public IEnumerable<string> ContentForScanning
    {
        get { yield return Name; }
    }

    public override IReadOnlyList<string> Validate()
    {
        List<string> errors = [];

        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("A token needs a name, so it can be told apart from the others later.");
        }

        if (LifetimeDays is < 1 or > 365)
        {
            // A ceiling rather than none. A credential that never expires is one nobody ever
            // notices is still valid.
            errors.Add("A token lifetime must be between 1 and 365 days.");
        }

        return errors;
    }
}

/// <summary>
/// The token, once.
/// <para>
/// It is not audited and it is never readable again: the store keeps a hash. An audit row
/// carrying it would be a working credential sitting in the one table designed to be kept
/// forever (SB-19).
/// </para>
/// </summary>
public sealed record MachineTokenIssuedResponse(
    MachineTokenId TokenId,
    string Name,
    string Token,
    DateTimeOffset ExpiresAt) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tokenId"] = TokenId.Value.ToString(),
            ["expiresAt"] = ExpiresAt.ToString("O"),
        };
}

public sealed class IssueMachineTokenUseCase(IMachineTokenService tokens)
    : UseCase<IssueMachineTokenRequest, MachineTokenIssuedResponse>
{
    private readonly IMachineTokenService _tokens = Guard.NotNull(tokens, nameof(tokens));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.IssueMachineToken;

    protected internal override async Task<MachineTokenIssuedResponse> HandleAsync(
        IssueMachineTokenRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        MachineTokenIssued issued = await _tokens.IssueAsync(
            caller.UserId,
            request.Name,
            TimeSpan.FromDays(request.LifetimeDays),
            cancellationToken);

        return new MachineTokenIssuedResponse(
            issued.Id, request.Name, issued.Token, issued.ExpiresAt);
    }
}

public sealed record ListMachineTokensRequest(WorkspaceId WorkspaceId) : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => "machine-tokens";
}

public sealed record ListMachineTokensResponse(IReadOnlyList<MachineTokenSummary> Tokens);

public sealed class ListMachineTokensUseCase(IMachineTokenService tokens)
    : UseCase<ListMachineTokensRequest, ListMachineTokensResponse>
{
    private readonly IMachineTokenService _tokens = Guard.NotNull(tokens, nameof(tokens));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.ListMachineTokens;

    protected internal override async Task<ListMachineTokensResponse> HandleAsync(
        ListMachineTokensRequest request, CallerContext caller, CancellationToken cancellationToken) =>
        new(await _tokens.ListAsync(caller.UserId, cancellationToken));
}

public sealed record RevokeMachineTokenRequest(WorkspaceId WorkspaceId, MachineTokenId TokenId)
    : WorkspaceRequest(WorkspaceId)
{
    public override string ResourceReference => TokenId.ToString();
}

public sealed record MachineTokenRevokedResponse(MachineTokenId TokenId) : IAuditableResult
{
    public IReadOnlyDictionary<string, string> AuditDetails =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["tokenId"] = TokenId.Value.ToString(),
        };
}

public sealed class RevokeMachineTokenUseCase(IMachineTokenService tokens)
    : UseCase<RevokeMachineTokenRequest, MachineTokenRevokedResponse>
{
    private readonly IMachineTokenService _tokens = Guard.NotNull(tokens, nameof(tokens));

    public override UseCaseDescriptor Descriptor => UseCaseCatalog.RevokeMachineToken;

    protected internal override async Task<MachineTokenRevokedResponse> HandleAsync(
        RevokeMachineTokenRequest request, CallerContext caller, CancellationToken cancellationToken)
    {
        // Scoped to the caller in the store rather than checked here, so somebody else's token
        // and a token that never existed produce the same answer.
        bool revoked = await _tokens.RevokeAsync(caller.UserId, request.TokenId, cancellationToken);

        return revoked
            ? new MachineTokenRevokedResponse(request.TokenId)
            : throw new ResourceNotFoundException("No such token.");
    }
}
