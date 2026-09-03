using DevBuddy.Application.Abstractions;

namespace DevBuddy.Api;

/// <summary>
/// Sign-in, token refresh, sign-out, and account recovery.
/// <para>
/// These are the only endpoints that do not go through the operation pipeline, and the reason is
/// structural rather than convenient: the pipeline resolves an identity and then authorises it,
/// and a caller trying to obtain an identity has none yet. Everything else in the API goes through
/// it (ADR-0005).
/// </para>
/// </summary>
internal static class AuthenticationEndpoints
{
    public static void MapAuthentication(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/sign-in", SignInAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("SignIn")
            .WithSummary("Exchanges an email and password for an access token and a refresh token.");

        routes.MapPost("/auth/refresh", RefreshAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("Refresh")
            .WithSummary("Exchanges a refresh token for a new pair. The old one is retired.");

        routes.MapPost("/auth/sign-out", SignOutAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("SignOut")
            .WithSummary("Revokes one refresh token.");

        routes.MapPost("/auth/recovery/begin", BeginRecoveryAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("BeginRecovery")
            .WithSummary("Starts account recovery. Answers the same way whether or not the address exists.");

        routes.MapPost("/auth/recovery/complete", CompleteRecoveryAsync)
            .AllowAnonymous()
            .RequireRateLimiting("auth")
            .WithName("CompleteRecovery")
            .WithSummary("Sets a new password using a recovery token, and signs every session out.");
    }

    private static async Task<IResult> SignInAsync(
        SignInRequest request,
        IUserAuthenticator authenticator,
        ITokenService tokens,
        CancellationToken cancellationToken)
    {
        AuthenticationResult attempt = await authenticator.AuthenticateAsync(
            request.Email, request.Password, cancellationToken);

        return attempt.Outcome switch
        {
            AuthenticationOutcome.Succeeded =>
                Results.Ok(await tokens.IssueAsync(attempt.UserId!.Value, cancellationToken)),

            // Locked out says when it lifts, because a person who has forgotten their password
            // needs to know whether to wait or to recover the account.
            AuthenticationOutcome.LockedOut => Results.Problem(
                title: "Too many attempts",
                detail: $"Locked until {attempt.LockoutEndsAt:O}.",
                statusCode: StatusCodes.Status423Locked),

            AuthenticationOutcome.Disabled => Results.Problem(
                title: "Account disabled",
                detail: "An administrator has disabled this account.",
                statusCode: StatusCodes.Status403Forbidden),

            // One answer for a wrong password and an address nobody has registered. Two answers
            // would turn this endpoint into a way to discover who has an account here.
            _ => Results.Problem(
                title: "Sign-in failed",
                detail: "The email or password is not correct.",
                statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request, ITokenService tokens, CancellationToken cancellationToken)
    {
        TokenRefreshResult result = await tokens.RefreshAsync(request.RefreshToken, cancellationToken);

        return result.Outcome switch
        {
            TokenRefreshOutcome.Succeeded => Results.Ok(result.Tokens),

            // A retired token presented again is treated as theft: the family is already revoked
            // by the time this returns, and the person has to sign in.
            TokenRefreshOutcome.ReuseDetected => Results.Problem(
                title: "Session ended",
                detail: "This token had already been used. Every session in its chain has been revoked.",
                statusCode: StatusCodes.Status401Unauthorized),

            _ => Results.Problem(
                title: "Refresh failed",
                detail: "The token is unknown, expired, or revoked.",
                statusCode: StatusCodes.Status401Unauthorized),
        };
    }

    private static async Task<IResult> SignOutAsync(
        RefreshRequest request, ITokenService tokens, CancellationToken cancellationToken)
    {
        await tokens.RevokeAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>
    /// Always accepted, whatever the address. The token, when there is one, goes out of band; the
    /// response says nothing about whether an account exists (SB-15).
    /// </summary>
    private static async Task<IResult> BeginRecoveryAsync(
        BeginRecoveryRequest request,
        IAccountRecoveryService recovery,
        IEmailSender email,
        CancellationToken cancellationToken)
    {
        string? token = await recovery.BeginAsync(request.Email, cancellationToken);

        if (token is not null)
        {
            // With no SMTP configured, IEmailSender's fallback logs this instead — the same "an
            // operator completes recovery by hand" behaviour this always had, just fixed: the
            // token used to be described as written to the log without actually being in it.
            await email.SendAsync(
                new EmailMessage(
                    request.Email,
                    "Reset your DevBuddy password",
                    $"Use this token to reset your password: {token}\n\n"
                        + "If you did not request this, no action is needed."),
                cancellationToken);
        }

        return Results.Accepted();
    }

    private static async Task<IResult> CompleteRecoveryAsync(
        CompleteRecoveryRequest request,
        IAccountRecoveryService recovery,
        CancellationToken cancellationToken)
    {
        RecoveryOutcome outcome = await recovery.CompleteAsync(
            request.Token, request.NewPassword, cancellationToken);

        return outcome switch
        {
            RecoveryOutcome.Succeeded => Results.NoContent(),

            RecoveryOutcome.PasswordTooWeak => Results.Problem(
                title: "Password too short",
                detail: "Choose a longer password. The recovery token is still usable.",
                statusCode: StatusCodes.Status400BadRequest),

            _ => Results.Problem(
                title: "Recovery failed",
                detail: "The token is unknown, expired, or already used.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }
}

internal sealed record SignInRequest(string Email, string Password);

internal sealed record RefreshRequest(string RefreshToken);

internal sealed record BeginRecoveryRequest(string Email);

internal sealed record CompleteRecoveryRequest(string Token, string NewPassword);
