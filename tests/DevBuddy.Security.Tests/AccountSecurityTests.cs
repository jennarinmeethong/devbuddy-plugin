using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace DevBuddy.Security.Tests;

/// <summary>
/// Control SB-13 (password storage, lockout), SB-14 (token lifetime, rotation, revocation), and
/// SB-15 (safe account recovery), against the real stores.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class AccountSecurityTests(SecurityFixture fixture)
{
    private const string GoodPassword = "correct-horse-battery";

    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task a_correct_password_signs_in_and_a_wrong_one_does_not()
    {
        string email = $"signin-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IUserAuthenticator authenticator = session.Resolve<IUserAuthenticator>();

        Assert.Equal(
            AuthenticationOutcome.Succeeded,
            (await authenticator.AuthenticateAsync(email, GoodPassword, CancellationToken.None)).Outcome);

        Assert.Equal(
            AuthenticationOutcome.InvalidCredentials,
            (await authenticator.AuthenticateAsync(email, "wrong-password-entirely", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task an_unknown_account_and_a_wrong_password_are_indistinguishable()
    {
        string email = $"enum-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IUserAuthenticator authenticator = session.Resolve<IUserAuthenticator>();

        AuthenticationResult wrongPassword =
            await authenticator.AuthenticateAsync(email, "not-the-password", CancellationToken.None);

        AuthenticationResult unknownAccount = await authenticator.AuthenticateAsync(
            $"nobody-{Guid.NewGuid():N}@example.com", "not-the-password", CancellationToken.None);

        // Identical answers. Anything else turns sign-in into a way to discover who has an
        // account here, which is a disclosure users never agreed to.
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, wrongPassword.Outcome);
        Assert.Equal(AuthenticationOutcome.InvalidCredentials, unknownAccount.Outcome);
    }

    [Fact]
    public async Task sign_in_is_case_insensitive_on_the_address()
    {
        string email = $"Mixed-Case-{Guid.NewGuid():N}@Example.COM";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();

        Assert.Equal(
            AuthenticationOutcome.Succeeded,
            (await session.Resolve<IUserAuthenticator>()
                .AuthenticateAsync(email.ToUpperInvariant(), GoodPassword, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task repeated_failures_lock_the_account_and_the_right_password_stops_working()
    {
        string email = $"lockout-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IUserAuthenticator authenticator = session.Resolve<IUserAuthenticator>();

        // Three failures is the configured threshold for these tests.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(
                AuthenticationOutcome.InvalidCredentials,
                (await authenticator.AuthenticateAsync(email, "guess", CancellationToken.None)).Outcome);
        }

        AuthenticationResult locking =
            await authenticator.AuthenticateAsync(email, "guess", CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.LockedOut, locking.Outcome);
        Assert.NotNull(locking.LockoutEndsAt);

        // The lockout is what makes guessing expensive, so it has to hold even for the correct
        // password. Otherwise an attacker learns they found it the moment they do.
        AuthenticationResult afterLock =
            await authenticator.AuthenticateAsync(email, GoodPassword, CancellationToken.None);

        Assert.Equal(AuthenticationOutcome.LockedOut, afterLock.Outcome);
    }

    [Fact]
    public async Task setting_a_new_password_clears_the_lockout()
    {
        string email = $"unlock-{Guid.NewGuid():N}@example.com";
        UserId userId = await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IUserAuthenticator authenticator = session.Resolve<IUserAuthenticator>();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await authenticator.AuthenticateAsync(email, "guess", CancellationToken.None);
        }

        Assert.Equal(
            AuthenticationOutcome.LockedOut,
            (await authenticator.AuthenticateAsync(email, GoodPassword, CancellationToken.None)).Outcome);

        await session.Resolve<ICredentialManager>()
            .SetPasswordAsync(userId, "a-brand-new-passphrase", CancellationToken.None);

        Assert.Equal(
            AuthenticationOutcome.Succeeded,
            (await authenticator.AuthenticateAsync(email, "a-brand-new-passphrase", CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task a_disabled_account_cannot_sign_in()
    {
        string email = $"off-{Guid.NewGuid():N}@example.com";
        UserId userId = await _fixture.CreateUserAsync(email);
        await _fixture.DisableAccountAsync(userId);

        using Session session = _fixture.OpenUnscopedSession();

        Assert.Equal(
            AuthenticationOutcome.Disabled,
            (await session.Resolve<IUserAuthenticator>()
                .AuthenticateAsync(email, GoodPassword, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task a_password_shorter_than_the_minimum_is_refused()
    {
        UserId userId = await _fixture.CreateUserAsync($"short-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();

        await Assert.ThrowsAsync<DomainValidationException>(
            () => session.Resolve<ICredentialManager>()
                .SetPasswordAsync(userId, "short", CancellationToken.None));
    }

    [Fact]
    public async Task the_stored_credential_is_a_hash_and_not_the_password()
    {
        string email = $"hash-{Guid.NewGuid():N}@example.com";
        UserId userId = await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();

        string stored = await session.Db.UserCredentials
            .Where(credential => credential.UserId == userId.Value)
            .Select(credential => credential.PasswordHash)
            .SingleAsync();

        Assert.DoesNotContain(GoodPassword, stored, StringComparison.Ordinal);
        Assert.True(stored.Length > 40);
    }

    [Fact]
    public async Task a_refresh_token_rotates_and_the_old_one_stops_working()
    {
        UserId userId = await _fixture.CreateUserAsync($"rotate-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();
        ITokenService tokens = session.Resolve<ITokenService>();

        TokenPair first = await tokens.IssueAsync(userId, CancellationToken.None);
        Assert.NotEmpty(first.AccessToken);
        Assert.True(first.AccessTokenExpiresAt < first.RefreshTokenExpiresAt);

        TokenRefreshResult rotated = await tokens.RefreshAsync(first.RefreshToken, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Succeeded, rotated.Outcome);
        Assert.NotEqual(first.RefreshToken, rotated.Tokens!.RefreshToken);

        // Presenting the retired token again is treated as theft rather than as a retry.
        TokenRefreshResult replayed = await tokens.RefreshAsync(first.RefreshToken, CancellationToken.None);
        Assert.Equal(TokenRefreshOutcome.ReuseDetected, replayed.Outcome);

        // And the replacement goes with it, because the attacker may be holding that one.
        TokenRefreshResult afterFamilyRevoke =
            await tokens.RefreshAsync(rotated.Tokens.RefreshToken, CancellationToken.None);

        Assert.Equal(TokenRefreshOutcome.Rejected, afterFamilyRevoke.Outcome);
    }

    [Fact]
    public async Task a_revoked_refresh_token_is_rejected()
    {
        UserId userId = await _fixture.CreateUserAsync($"revoke-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();
        ITokenService tokens = session.Resolve<ITokenService>();

        TokenPair pair = await tokens.IssueAsync(userId, CancellationToken.None);
        await tokens.RevokeAsync(pair.RefreshToken, CancellationToken.None);

        Assert.Equal(
            TokenRefreshOutcome.Rejected,
            (await tokens.RefreshAsync(pair.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task revoking_every_session_invalidates_all_outstanding_refresh_tokens()
    {
        UserId userId = await _fixture.CreateUserAsync($"revoke-all-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();
        ITokenService tokens = session.Resolve<ITokenService>();

        TokenPair laptop = await tokens.IssueAsync(userId, CancellationToken.None);
        TokenPair phone = await tokens.IssueAsync(userId, CancellationToken.None);

        await tokens.RevokeAllAsync(userId, CancellationToken.None);

        Assert.Equal(
            TokenRefreshOutcome.Rejected,
            (await tokens.RefreshAsync(laptop.RefreshToken, CancellationToken.None)).Outcome);

        Assert.Equal(
            TokenRefreshOutcome.Rejected,
            (await tokens.RefreshAsync(phone.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task an_expired_refresh_token_is_rejected()
    {
        UserId userId = await _fixture.CreateUserAsync($"expired-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();
        ITokenService tokens = session.Resolve<ITokenService>();

        TokenPair pair = await tokens.IssueAsync(userId, CancellationToken.None);

        // Ageing the row rather than waiting: the expiry check is what is under test, and the
        // real lifetime is two weeks.
        await session.Db.RefreshTokens
            .Where(token => token.UserId == userId.Value)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(token => token.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(
            TokenRefreshOutcome.Rejected,
            (await tokens.RefreshAsync(pair.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task the_refresh_token_is_never_stored_in_a_readable_form()
    {
        UserId userId = await _fixture.CreateUserAsync($"stored-{Guid.NewGuid():N}@example.com");

        using Session session = _fixture.OpenUnscopedSession();
        TokenPair pair = await session.Resolve<ITokenService>().IssueAsync(userId, CancellationToken.None);

        string storedHash = await session.Db.RefreshTokens
            .Where(token => token.UserId == userId.Value)
            .Select(token => token.TokenHash)
            .SingleAsync();

        // A database dump must not hand an attacker working credentials.
        Assert.DoesNotContain(pair.RefreshToken, storedHash, StringComparison.Ordinal);
        Assert.Equal(64, storedHash.Length);
    }

    [Fact]
    public async Task recovery_says_nothing_about_whether_an_address_is_registered()
    {
        string email = $"recover-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        string? known = await recovery.BeginAsync(email, CancellationToken.None);
        string? unknown = await recovery.BeginAsync(
            $"nobody-{Guid.NewGuid():N}@example.com", CancellationToken.None);

        // The caller responds identically either way; only one of these produces a token to send.
        Assert.NotNull(known);
        Assert.Null(unknown);
    }

    [Fact]
    public async Task a_recovery_token_works_once_and_then_never_again()
    {
        string email = $"recover-once-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();
        IUserAuthenticator authenticator = session.Resolve<IUserAuthenticator>();

        string token = (await recovery.BeginAsync(email, CancellationToken.None))!;

        Assert.Equal(
            RecoveryOutcome.Succeeded,
            await recovery.CompleteAsync(token, "a-recovered-passphrase", CancellationToken.None));

        Assert.Equal(
            AuthenticationOutcome.Succeeded,
            (await authenticator.AuthenticateAsync(email, "a-recovered-passphrase", CancellationToken.None)).Outcome);

        Assert.Equal(
            RecoveryOutcome.Rejected,
            await recovery.CompleteAsync(token, "another-passphrase-here", CancellationToken.None));
    }

    [Fact]
    public async Task a_weak_new_password_is_refused_without_burning_the_recovery_token()
    {
        string email = $"recover-weak-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        string token = (await recovery.BeginAsync(email, CancellationToken.None))!;

        Assert.Equal(
            RecoveryOutcome.PasswordTooWeak,
            await recovery.CompleteAsync(token, "short", CancellationToken.None));

        // Mistyping a password is the person, not an attack. Burning the token would strand them.
        Assert.Equal(
            RecoveryOutcome.Succeeded,
            await recovery.CompleteAsync(token, "a-long-enough-passphrase", CancellationToken.None));
    }

    [Fact]
    public async Task starting_recovery_again_retires_the_previous_token()
    {
        string email = $"recover-twice-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        string first = (await recovery.BeginAsync(email, CancellationToken.None))!;
        string second = (await recovery.BeginAsync(email, CancellationToken.None))!;

        // Two live recovery links is one more than anyone needs and one more chance to leak.
        Assert.Equal(
            RecoveryOutcome.Rejected,
            await recovery.CompleteAsync(first, "a-long-enough-passphrase", CancellationToken.None));

        Assert.Equal(
            RecoveryOutcome.Succeeded,
            await recovery.CompleteAsync(second, "a-long-enough-passphrase", CancellationToken.None));
    }

    [Fact]
    public async Task completing_recovery_signs_every_existing_session_out()
    {
        string email = $"recover-sessions-{Guid.NewGuid():N}@example.com";
        UserId userId = await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        ITokenService tokens = session.Resolve<ITokenService>();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        TokenPair existing = await tokens.IssueAsync(userId, CancellationToken.None);
        string token = (await recovery.BeginAsync(email, CancellationToken.None))!;

        await recovery.CompleteAsync(token, "a-long-enough-passphrase", CancellationToken.None);

        // Recovery means the old password may be in someone elses hands, and so may a session
        // opened with it.
        Assert.Equal(
            TokenRefreshOutcome.Rejected,
            (await tokens.RefreshAsync(existing.RefreshToken, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task an_expired_recovery_token_is_rejected()
    {
        string email = $"recover-expired-{Guid.NewGuid():N}@example.com";
        await _fixture.CreateUserAsync(email);

        using Session session = _fixture.OpenUnscopedSession();
        IAccountRecoveryService recovery = session.Resolve<IAccountRecoveryService>();

        string token = (await recovery.BeginAsync(email, CancellationToken.None))!;

        await session.Db.RecoveryTokens
            .Where(candidate => candidate.UsedAt == null)
            .ExecuteUpdateAsync(setters =>
                setters.SetProperty(candidate => candidate.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));

        Assert.Equal(
            RecoveryOutcome.Rejected,
            await recovery.CompleteAsync(token, "a-long-enough-passphrase", CancellationToken.None));
    }
}
