using System.Globalization;
using System.Security.Claims;
using System.Text;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Short-lived signed access tokens and rotating, revocable refresh tokens (ADR-0005).
/// <para>
/// The asymmetry is the design. An access token is signed and not stored, so it cannot be revoked
/// individually — which is exactly why it lasts fifteen minutes. A refresh token is stored as a
/// hash, used once, and revocable, which is what lets the system promise that revoked access
/// stops working (SB-14).
/// </para>
/// </summary>
internal sealed class TokenService : ITokenService
{
    private readonly DevBuddyDbContext _db;
    private readonly IClock _clock;
    private readonly IdentitySettings _settings;

    public TokenService(DevBuddyDbContext db, IClock clock, IOptions<IdentitySettings> settings)
    {
        _db = Guard.NotNull(db, nameof(db));
        _clock = Guard.NotNull(clock, nameof(clock));
        _settings = Guard.NotNull(settings, nameof(settings)).Value;

        if (_settings.SigningKey.Length < 32)
        {
            // Refused rather than padded. A key short enough to brute force is worse than no
            // token at all, because it looks like protection.
            throw new InvalidOperationException(
                "The access-token signing key must be at least 32 characters. Supply one from a secret store.");
        }
    }

    public Task<TokenPair> IssueAsync(UserId userId, CancellationToken cancellationToken) =>
        IssueAsync(userId, Guid.NewGuid(), cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair.
    /// <para>
    /// Reads bypass the change tracker and the claim is staked with a conditional update. Both
    /// matter: a decision about whether a token is still live has to be made against what is in
    /// the database now, not against an entity this context happened to load earlier, and marking
    /// the token used in the same statement that checks it is what makes single-use hold when two
    /// requests arrive together.
    /// </para>
    /// </summary>
    public async Task<TokenRefreshResult> RefreshAsync(
        string refreshToken, CancellationToken cancellationToken)
    {
        string hash = OpaqueToken.Hash(refreshToken ?? string.Empty);

        RefreshTokenRow? stored = await _db.RefreshTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(token => token.TokenHash == hash, cancellationToken);

        if (stored is null)
        {
            return TokenRefreshResult.Rejected();
        }

        DateTimeOffset now = _clock.UtcNow;

        if (stored.UsedAt is not null)
        {
            // A token that was already exchanged is being presented again. The benign
            // explanations are rare and the malicious one is not, so the whole rotation family
            // goes, and the user signs in again.
            await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return TokenRefreshResult.ReuseDetected();
        }

        if (stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            return TokenRefreshResult.Rejected();
        }

        int claimed = await _db.RefreshTokens
            .Where(token => token.Id == stored.Id && token.UsedAt == null && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken);

        if (claimed != 1)
        {
            // Another request took it between the read and the update. That is the reuse case
            // arriving concurrently, and it is treated the same way.
            await RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);
            return TokenRefreshResult.ReuseDetected();
        }

        TokenPair pair = await IssueAsync(new UserId(stored.UserId), stored.FamilyId, cancellationToken);
        return TokenRefreshResult.Success(pair);
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken cancellationToken)
    {
        string hash = OpaqueToken.Hash(refreshToken ?? string.Empty);
        DateTimeOffset now = _clock.UtcNow;

        await _db.RefreshTokens
            .Where(token => token.TokenHash == hash && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
    }

    public async Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;

        await _db.RefreshTokens
            .Where(token => token.UserId == userId.Value && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);
    }

    private async Task<TokenPair> IssueAsync(
        UserId userId, Guid familyId, CancellationToken cancellationToken)
    {
        DateTimeOffset now = _clock.UtcNow;
        DateTimeOffset accessExpiry = now + _settings.AccessTokenLifetime;
        DateTimeOffset refreshExpiry = now + _settings.RefreshTokenLifetime;

        string refreshToken = OpaqueToken.Create();

        _db.RefreshTokens.Add(new RefreshTokenRow
        {
            Id = Guid.NewGuid(),
            UserId = userId.Value,
            TokenHash = OpaqueToken.Hash(refreshToken),
            FamilyId = familyId,
            IssuedAt = now,
            ExpiresAt = refreshExpiry,
        });

        await _db.SaveChangesAsync(cancellationToken);

        return new TokenPair(
            CreateAccessToken(userId, now, accessExpiry), accessExpiry, refreshToken, refreshExpiry);
    }

    private async Task RevokeFamilyAsync(
        Guid familyId, DateTimeOffset now, CancellationToken cancellationToken) =>
        await _db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.RevokedAt, now), cancellationToken);

    /// <summary>
    /// The access token carries the user identifier and nothing else. Not roles, not workspace
    /// membership: those are re-checked server-side on every request (SB-11), and a token that
    /// asserted them would still be asserting them after a grant was revoked.
    /// </summary>
    private string CreateAccessToken(UserId userId, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SigningKey)),
            SecurityAlgorithms.HmacSha256);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = credentials,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [ClaimTypes.NameIdentifier] = userId.Value.ToString(),
                [JwtRegisteredClaimNames.Sub] = userId.Value.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [JwtRegisteredClaimNames.Iat] =
                    issuedAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            },
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}

/// <summary>
/// Account recovery. Single-use, time-boxed, and silent about whether an address is registered.
/// </summary>
internal sealed class AccountRecoveryService : IAccountRecoveryService
{
    private readonly DevBuddyDbContext _db;
    private readonly ICredentialManager _credentials;
    private readonly ITokenService _tokens;
    private readonly IClock _clock;
    private readonly IdentitySettings _settings;

    public AccountRecoveryService(
        DevBuddyDbContext db,
        ICredentialManager credentials,
        ITokenService tokens,
        IClock clock,
        IOptions<IdentitySettings> settings)
    {
        _db = Guard.NotNull(db, nameof(db));
        _credentials = Guard.NotNull(credentials, nameof(credentials));
        _tokens = Guard.NotNull(tokens, nameof(tokens));
        _clock = Guard.NotNull(clock, nameof(clock));
        _settings = Guard.NotNull(settings, nameof(settings)).Value;
    }

    public async Task<string?> BeginAsync(string email, CancellationToken cancellationToken)
    {
        string normalised = (email ?? string.Empty).Trim().ToUpperInvariant();

        UserRow? user = await _db.Users
            .FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == normalised, cancellationToken);

        // Null for an unknown or disabled account. The caller responds identically either way,
        // so the endpoint cannot be used to discover who has an account here.
        if (user is null || user.IsDisabled)
        {
            return null;
        }

        DateTimeOffset now = _clock.UtcNow;

        // Any earlier outstanding token is retired. Two live recovery links is one more than
        // anyone needs and one more chance for the older one to leak.
        await _db.RecoveryTokens
            .Where(token => token.UserId == user.Id && token.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(token => token.UsedAt, now), cancellationToken);

        string recoveryToken = OpaqueToken.Create();

        _db.RecoveryTokens.Add(new RecoveryTokenRow
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = OpaqueToken.Hash(recoveryToken),
            IssuedAt = now,
            ExpiresAt = now + _settings.RecoveryTokenLifetime,
        });

        await _db.SaveChangesAsync(cancellationToken);
        return recoveryToken;
    }

    public async Task<RecoveryOutcome> CompleteAsync(
        string token, string newPassword, CancellationToken cancellationToken)
    {
        string hash = OpaqueToken.Hash(token ?? string.Empty);
        DateTimeOffset now = _clock.UtcNow;

        // Read outside the change tracker, for the same reason as token refresh: whether this
        // token is still live is a question about the database, not about what this context
        // loaded earlier.
        RecoveryTokenRow? stored = await _db.RecoveryTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (stored is null || stored.UsedAt is not null || stored.ExpiresAt <= now)
        {
            return RecoveryOutcome.Rejected;
        }

        if ((newPassword ?? string.Empty).Length < _settings.MinimumPasswordLength)
        {
            // The token stays usable: a password that was too short is the person mistyping, not
            // an attack, and burning the token would strand them.
            return RecoveryOutcome.PasswordTooWeak;
        }

        int claimed = await _db.RecoveryTokens
            .Where(candidate => candidate.Id == stored.Id && candidate.UsedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(candidate => candidate.UsedAt, now), cancellationToken);

        if (claimed != 1)
        {
            return RecoveryOutcome.Rejected;
        }

        var userId = new UserId(stored.UserId);
        await _credentials.SetPasswordAsync(userId, newPassword!, cancellationToken);

        // Recovery means the old password may be in someone elses hands. Every existing session
        // goes with it.
        await _tokens.RevokeAllAsync(userId, cancellationToken);

        return RecoveryOutcome.Succeeded;
    }
}
