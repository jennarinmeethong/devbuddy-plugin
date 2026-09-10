using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Machine tokens, stored as hashes, bound to one workspace, and resolved on every call.
/// <para>
/// The same opaque-token treatment refresh and recovery tokens get: 256 bits of randomness, kept
/// only as a SHA-256 digest, looked up by exact hash. A database dump hands an attacker a list of
/// digests rather than a set of working credentials.
/// </para>
/// <para>
/// Resolution reads the database every time rather than caching. A token that was live a minute
/// ago is not the same promise as a token that is live now, and revocation that took effect on
/// the next process restart would not be revocation.
/// </para>
/// <para>
/// Every statement below is scoped by owner, and the ones a person drives are scoped by workspace
/// as well — in the statement itself rather than in a filter afterwards, so a row belonging
/// elsewhere is never loaded and cannot be leaked by a projection somebody changes later.
/// </para>
/// </summary>
internal sealed class MachineTokenService(DevBuddyDbContext db, IClock clock) : IMachineTokenService
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public async Task<MachineTokenIssued> IssueAsync(
        UserId userId,
        WorkspaceId workspaceId,
        string name,
        TimeSpan lifetime,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        string token = OpaqueToken.Create();
        var id = MachineTokenId.New();

        _db.MachineTokens.Add(new MachineTokenRow
        {
            Id = id.Value,
            UserId = userId.Value,
            WorkspaceId = workspaceId.Value,
            TokenHash = OpaqueToken.Hash(token),
            Name = name,
            IssuedAt = now,
            ExpiresAt = now + lifetime,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new MachineTokenIssued(id, workspaceId, token, now + lifetime);
    }

    public async Task<MachineTokenIdentity?> ResolveAsync(
        string token, CancellationToken cancellationToken)
    {
        string hash = OpaqueToken.Hash(token ?? string.Empty);
        DateTimeOffset now = _clock.UtcNow;

        MachineTokenRow? stored = await _db.MachineTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            // One answer for unknown, revoked, and expired. A caller holding a token that does not
            // work has no business learning which of the three it is.
            return null;
        }

        if (stored.WorkspaceId is not { } workspace)
        {
            // A token from before tokens were scoped. Refused, and deliberately not repaired:
            // the row says who owns it and nothing about where it was meant to work, so any
            // workspace this code picked would be a workspace nobody granted it. Its owner mints
            // a replacement, which takes a minute; the alternative silently widens a credential.
            return null;
        }

        // Recorded outside the change tracker and without failing the call if it does not land.
        // Knowing a token is still in use is worth having; it is not worth refusing a request over.
        await _db.MachineTokens
            .Where(candidate => candidate.Id == stored.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.LastUsedAt, now),
                cancellationToken);

        return new MachineTokenIdentity(
            new MachineTokenId(stored.Id),
            new UserId(stored.UserId),
            new WorkspaceId(workspace),
            stored.IssuedAt,
            stored.ExpiresAt);
    }

    public async Task<IReadOnlyList<MachineTokenSummary>> ListAsync(
        UserId userId, WorkspaceId workspaceId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        // This workspace's tokens, and the caller's own unscoped leftovers. A legacy row belongs
        // to no workspace, so showing it here reveals nothing about another one — and not showing
        // it would leave a person watching their plugin fail with no way to see why.
        List<MachineTokenRow> rows = await _db.MachineTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId.Value
                && (token.WorkspaceId == workspaceId.Value || token.WorkspaceId == null))
            .OrderByDescending(token => token.IssuedAt)
            .ToListAsync(cancellationToken);

        // Revoked and expired tokens stay listed. A credential that used to work is exactly what
        // somebody auditing their own access wants to see.
        return
        [
            .. rows.Select(row => new MachineTokenSummary(
                new MachineTokenId(row.Id),
                row.Name,
                row.IssuedAt,
                row.ExpiresAt,
                row.LastUsedAt,

                // An unscoped token is never active, whatever its dates say, because the
                // resolver refuses it. Reporting it as active would be reporting a lie.
                row.WorkspaceId is not null && row.RevokedAt is null && row.ExpiresAt > now,
                row.WorkspaceId is null))
        ];
    }

    public async Task<bool> RevokeAsync(
        UserId userId,
        WorkspaceId workspaceId,
        MachineTokenId id,
        CancellationToken cancellationToken)
    {
        // Scoped to the owner and the workspace in the statement itself, so a token belonging to
        // somebody else, or to another workspace, is not found rather than found and refused.
        // The two are indistinguishable from outside, which is the point.
        int revoked = await _db.MachineTokens
            .Where(token => token.Id == id.Value
                && token.UserId == userId.Value
                && (token.WorkspaceId == workspaceId.Value || token.WorkspaceId == null)
                && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, _clock.UtcNow),
                cancellationToken);

        return revoked == 1;
    }
}
