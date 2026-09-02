using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Sign-in against the product own account system (ADR-0005).
/// <para>
/// Authentication deliberately does not go through <c>UseCaseExecutor</c>. The pipeline resolves
/// an identity and then authorises it; a caller who is trying to obtain an identity has none yet,
/// so running sign-in through a stage that requires one would be circular. Hosts call this
/// directly, and it is the one place in the system where that is correct.
/// </para>
/// </summary>
public interface IUserAuthenticator
{
    Task<AuthenticationResult> AuthenticateAsync(
        string email, string password, CancellationToken cancellationToken);
}

/// <summary>Why a sign-in did or did not succeed.</summary>
public enum AuthenticationOutcome
{
    Succeeded = 1,

    /// <summary>
    /// Wrong password, unknown account, or an account with no credential. One value for all
    /// three on purpose: distinguishing them tells an attacker which emails are registered.
    /// </summary>
    InvalidCredentials = 2,

    /// <summary>Too many failed attempts. Temporary, and the response says when it lifts.</summary>
    LockedOut = 3,

    /// <summary>The account exists but has been disabled by an administrator.</summary>
    Disabled = 4,
}

/// <summary>
/// The result of a sign-in attempt. Carries the user only on success, so a caller cannot
/// accidentally act on an identity that was not established.
/// </summary>
public sealed record AuthenticationResult
{
    private AuthenticationResult(AuthenticationOutcome outcome, UserId? userId, DateTimeOffset? lockoutEndsAt)
    {
        Outcome = outcome;
        UserId = userId;
        LockoutEndsAt = lockoutEndsAt;
    }

    public AuthenticationOutcome Outcome { get; }

    public UserId? UserId { get; }

    public DateTimeOffset? LockoutEndsAt { get; }

    public bool IsAuthenticated => Outcome == AuthenticationOutcome.Succeeded;

    public static AuthenticationResult Success(UserId userId) =>
        new(AuthenticationOutcome.Succeeded, userId, null);

    public static AuthenticationResult InvalidCredentials() =>
        new(AuthenticationOutcome.InvalidCredentials, null, null);

    public static AuthenticationResult LockedOut(DateTimeOffset until) =>
        new(AuthenticationOutcome.LockedOut, null, until);

    public static AuthenticationResult Disabled() =>
        new(AuthenticationOutcome.Disabled, null, null);
}

/// <summary>
/// Issues and rotates the tokens a signed-in caller uses.
/// <para>
/// Access tokens are short-lived and signed, so they are not stored and cannot be revoked
/// individually; that is the trade-off short lifetimes buy. Refresh tokens are opaque, stored
/// only as a hash, single-use, and revocable — which is what makes "revoked access takes effect"
/// something the system can actually promise (SB-14).
/// </para>
/// </summary>
public interface ITokenService
{
    Task<TokenPair> IssueAsync(UserId userId, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a refresh token for a new pair and retires the old one. Presenting a token that
    /// was already used is treated as theft, not as a retry: the whole family is revoked.
    /// </summary>
    Task<TokenRefreshResult> RefreshAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeAsync(string refreshToken, CancellationToken cancellationToken);

    /// <summary>Revokes every refresh token a user holds, on sign-out everywhere or on suspicion.</summary>
    Task RevokeAllAsync(UserId userId, CancellationToken cancellationToken);
}

/// <summary>An access token and the refresh token that will replace it.</summary>
public sealed record TokenPair(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

/// <summary>Why a refresh did or did not succeed.</summary>
public enum TokenRefreshOutcome
{
    Succeeded = 1,

    /// <summary>Unknown, expired, or already revoked.</summary>
    Rejected = 2,

    /// <summary>
    /// A token that had already been exchanged was presented again. The family is revoked, and
    /// the caller has to sign in.
    /// </summary>
    ReuseDetected = 3,
}

public sealed record TokenRefreshResult(TokenRefreshOutcome Outcome, TokenPair? Tokens)
{
    public static TokenRefreshResult Success(TokenPair tokens) =>
        new(TokenRefreshOutcome.Succeeded, tokens);

    public static TokenRefreshResult Rejected() => new(TokenRefreshOutcome.Rejected, null);

    public static TokenRefreshResult ReuseDetected() => new(TokenRefreshOutcome.ReuseDetected, null);
}

/// <summary>
/// Account recovery. Single-use, time-boxed, and deliberately silent about whether the address
/// belongs to an account: an endpoint that says "no such user" is an account enumeration oracle.
/// </summary>
public interface IAccountRecoveryService
{
    /// <summary>
    /// Starts recovery. Returns the token to deliver out of band when the address matches an
    /// account, and null when it does not — the caller responds identically either way.
    /// </summary>
    Task<string?> BeginAsync(string email, CancellationToken cancellationToken);

    Task<RecoveryOutcome> CompleteAsync(
        string token, string newPassword, CancellationToken cancellationToken);
}

public enum RecoveryOutcome
{
    Succeeded = 1,

    /// <summary>Unknown, expired, or already used.</summary>
    Rejected = 2,

    /// <summary>The new password did not meet the minimum length.</summary>
    PasswordTooWeak = 3,
}

/// <summary>
/// Creates and updates credentials. Separate from <see cref="IUserAuthenticator"/> because
/// registering an account and proving you own one are different operations with different callers.
/// </summary>
public interface ICredentialManager
{
    Task SetPasswordAsync(UserId userId, string password, CancellationToken cancellationToken);

    Task<bool> HasCredentialAsync(UserId userId, CancellationToken cancellationToken);
}
