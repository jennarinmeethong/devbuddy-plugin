using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Machine tokens, stored as hashes and resolved on every call.
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
/// </summary>
internal sealed class MachineTokenService(DevBuddyDbContext db, IClock clock) : IMachineTokenService
{
    private readonly DevBuddyDbContext _db = Guard.NotNull(db, nameof(db));
    private readonly IClock _clock = Guard.NotNull(clock, nameof(clock));

    public async Task<MachineTokenIssued> IssueAsync(
        UserId userId, string name, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        string token = OpaqueToken.Create();
        var id = MachineTokenId.New();

        _db.MachineTokens.Add(new MachineTokenRow
        {
            Id = id.Value,
            UserId = userId.Value,
            TokenHash = OpaqueToken.Hash(token),
            Name = name,
            IssuedAt = now,
            ExpiresAt = now + lifetime,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new MachineTokenIssued(id, token, now + lifetime);
    }

    public async Task<UserId?> ResolveAsync(string token, CancellationToken cancellationToken)
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

        // Recorded outside the change tracker and without failing the call if it does not land.
        // Knowing a token is still in use is worth having; it is not worth refusing a request over.
        await _db.MachineTokens
            .Where(candidate => candidate.Id == stored.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.LastUsedAt, now),
                cancellationToken);

        return new UserId(stored.UserId);
    }

    public async Task<IReadOnlyList<MachineTokenSummary>> ListAsync(
        UserId userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        List<MachineTokenRow> rows = await _db.MachineTokens
            .AsNoTracking()
            .Where(token => token.UserId == userId.Value)
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
                row.RevokedAt is null && row.ExpiresAt > now))
        ];
    }

    public async Task<bool> RevokeAsync(
        UserId userId, MachineTokenId id, CancellationToken cancellationToken)
    {
        // Scoped to the owner in the statement itself, so somebody else's token is not found
        // rather than found and refused.
        int revoked = await _db.MachineTokens
            .Where(token => token.Id == id.Value
                && token.UserId == userId.Value
                && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, _clock.UtcNow),
                cancellationToken);

        return revoked == 1;
    }
}
