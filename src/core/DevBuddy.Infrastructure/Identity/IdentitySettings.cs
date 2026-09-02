namespace DevBuddy.Infrastructure.Identity;

/// <summary>
/// Lockout, token lifetime, and recovery settings.
/// <para>
/// The defaults are deliberate rather than arbitrary. A short access-token lifetime is what makes
/// an unrevokable signed token acceptable; a long one would make revocation a promise the system
/// cannot keep (SB-14). Lockout is a rate limit an attacker cannot route around, unlike a
/// per-IP one.
/// </para>
/// </summary>
public sealed class IdentitySettings
{
    public const string SectionName = "Identity";

    /// <summary>
    /// HMAC key for signing access tokens. Supplied by the host from a secret store; there is no
    /// default, and a short key is refused rather than padded.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "devbuddy";

    public string Audience { get; set; } = "devbuddy";

    /// <summary>
    /// Short enough that a stolen access token expires before it is worth much, long enough that
    /// a normal session is not refreshing constantly.
    /// </summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Failed attempts before the account locks.</summary>
    public int MaxFailedAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Recovery links are short-lived because they are sent through a channel this system does
    /// not control.
    /// </summary>
    public TimeSpan RecoveryTokenLifetime { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Minimum password length. Length is the only requirement: composition rules push people
    /// towards predictable substitutions without adding real entropy.
    /// </summary>
    public int MinimumPasswordLength { get; set; } = 12;

    /// <summary>
    /// Single-workspace mode for a local or single-team install. It resolves the workspace when
    /// exactly one exists so a caller does not have to name it; it changes nothing about project
    /// scoping, and it refuses to guess once a second workspace appears.
    /// </summary>
    public bool SingleWorkspaceMode { get; set; }
}
