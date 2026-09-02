using System.Security.Cryptography;
using System.Text;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Opaque secrets and the way they are stored.
/// <para>
/// Tokens are random 256-bit values, handed out once and kept only as a SHA-256 digest. A plain
/// hash is right here and wrong for passwords: a token already has full entropy, so there is
/// nothing for a slow hash to protect against, and lookup has to be exact.
/// </para>
/// </summary>
internal static class OpaqueToken
{
    public static string Create() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Marker for the platform password hasher, which is generic over a user type.</summary>
internal sealed class PasswordSubject;

/// <summary>
/// Sign-in against the product own accounts.
/// <para>
/// Every failure path returns the same InvalidCredentials for an unknown email, a wrong password,
/// and an account with no credential set. Distinguishing them would turn the sign-in endpoint into
/// an account-enumeration oracle, and the cost of that is paid by users, not by us.
/// </para>
/// </summary>
internal sealed class UserAuthenticator : IUserAuthenticator
{
    private readonly DevBuddyDbContext _db;
    private readonly IPasswordHasher<PasswordSubject> _hasher;
    private readonly IClock _clock;
    private readonly IdentitySettings _settings;

    public UserAuthenticator(
        DevBuddyDbContext db,
        IPasswordHasher<PasswordSubject> hasher,
        IClock clock,
        IOptions<IdentitySettings> settings)
    {
        _db = Guard.NotNull(db, nameof(db));
        _hasher = Guard.NotNull(hasher, nameof(hasher));
        _clock = Guard.NotNull(clock, nameof(clock));
        _settings = Guard.NotNull(settings, nameof(settings)).Value;
    }

    public async Task<AuthenticationResult> AuthenticateAsync(
        string email, string password, CancellationToken cancellationToken)
    {
        string normalised = (email ?? string.Empty).Trim().ToUpperInvariant();

        UserRow? user = await _db.Users
            .FirstOrDefaultAsync(candidate => candidate.NormalizedEmail == normalised, cancellationToken);

        if (user is null)
        {
            return AuthenticationResult.InvalidCredentials();
        }

        if (user.IsDisabled)
        {
            return AuthenticationResult.Disabled();
        }

        UserCredentialRow? credential = await _db.UserCredentials
            .FirstOrDefaultAsync(candidate => candidate.UserId == user.Id, cancellationToken);

        if (credential is null)
        {
            return AuthenticationResult.InvalidCredentials();
        }

        DateTimeOffset now = _clock.UtcNow;

        if (credential.LockoutEndsAt is { } lockedUntil && lockedUntil > now)
        {
            // Checked before the password is verified, so a locked account cannot be used as an
            // oracle for whether a guess was correct.
            return AuthenticationResult.LockedOut(lockedUntil);
        }

        PasswordVerificationResult verification =
            _hasher.VerifyHashedPassword(new PasswordSubject(), credential.PasswordHash, password ?? string.Empty);

        if (verification == PasswordVerificationResult.Failed)
        {
            return await RecordFailureAsync(credential, now, cancellationToken);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            credential.PasswordHash = _hasher.HashPassword(new PasswordSubject(), password!);
        }

        credential.FailedAttempts = 0;
        credential.LockoutEndsAt = null;
        credential.UpdatedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        return AuthenticationResult.Success(new UserId(user.Id));
    }

    private async Task<AuthenticationResult> RecordFailureAsync(
        UserCredentialRow credential, DateTimeOffset now, CancellationToken cancellationToken)
    {
        credential.FailedAttempts++;
        credential.UpdatedAt = now;

        if (credential.FailedAttempts >= _settings.MaxFailedAttempts)
        {
            credential.LockoutEndsAt = now + _settings.LockoutDuration;
            credential.FailedAttempts = 0;
            await _db.SaveChangesAsync(cancellationToken);
            return AuthenticationResult.LockedOut(credential.LockoutEndsAt.Value);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return AuthenticationResult.InvalidCredentials();
    }
}

/// <summary>Creating and replacing passwords.</summary>
internal sealed class CredentialManager : ICredentialManager
{
    private readonly DevBuddyDbContext _db;
    private readonly IPasswordHasher<PasswordSubject> _hasher;
    private readonly IClock _clock;
    private readonly IdentitySettings _settings;

    public CredentialManager(
        DevBuddyDbContext db,
        IPasswordHasher<PasswordSubject> hasher,
        IClock clock,
        IOptions<IdentitySettings> settings)
    {
        _db = Guard.NotNull(db, nameof(db));
        _hasher = Guard.NotNull(hasher, nameof(hasher));
        _clock = Guard.NotNull(clock, nameof(clock));
        _settings = Guard.NotNull(settings, nameof(settings)).Value;
    }

    public async Task SetPasswordAsync(UserId userId, string password, CancellationToken cancellationToken)
    {
        if ((password ?? string.Empty).Length < _settings.MinimumPasswordLength)
        {
            throw new DomainValidationException(
                $"A password must be at least {_settings.MinimumPasswordLength} characters.");
        }

        UserCredentialRow? credential = await _db.UserCredentials
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId.Value, cancellationToken);

        string hash = _hasher.HashPassword(new PasswordSubject(), password!);

        if (credential is null)
        {
            _db.UserCredentials.Add(new UserCredentialRow
            {
                UserId = userId.Value,
                PasswordHash = hash,
                UpdatedAt = _clock.UtcNow,
            });
        }
        else
        {
            credential.PasswordHash = hash;

            // A password change clears a lockout: the person who set it has proved control.
            credential.FailedAttempts = 0;
            credential.LockoutEndsAt = null;
            credential.UpdatedAt = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> HasCredentialAsync(UserId userId, CancellationToken cancellationToken) =>
        _db.UserCredentials.AnyAsync(candidate => candidate.UserId == userId.Value, cancellationToken);
}
