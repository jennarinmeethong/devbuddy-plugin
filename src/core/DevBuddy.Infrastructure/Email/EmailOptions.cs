namespace DevBuddy.Infrastructure.Email;

/// <summary>Which sender to use. Log is the default: it needs no operator configuration to work.</summary>
public enum EmailProvider
{
    /// <summary>No SMTP server configured. See <see cref="LogEmailSender"/>.</summary>
    Log = 1,

    /// <summary>A real mail server. See <see cref="SmtpEmailSender"/>.</summary>
    Smtp = 2,
}

/// <summary>
/// How a setup or recovery token reaches its owner. Defaults to <see cref="EmailProvider.Log"/>,
/// the same "no delivery channel yet" fallback this system already used before this port existed
/// — an operator configures SMTP when they have a server to point at, and nothing else changes.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public EmailProvider Provider { get; set; } = EmailProvider.Log;

    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool UseStartTls { get; set; } = true;

    public string FromAddress { get; set; } = "devbuddy@localhost";

    public string FromName { get; set; } = "DevBuddy";

    /// <summary>
    /// Whether <see cref="LogEmailSender"/> may write a message body — and so a one-time setup or
    /// recovery token — into the application log. <b>False.</b>
    /// <para>
    /// v1 wrote them unconditionally, and said so: `docs/security/release-readiness.md` listed "a
    /// secret in a log, by design" among the risks the operator had to accept, and a retention
    /// window is not a control over who can read the file while it exists. Phase 12B closed it
    /// with an opt-in rather than by refusing to start without SMTP, because that second option
    /// breaks a plain <c>docker run</c> for a first-time operator and this one does not.
    /// </para>
    /// <para>
    /// Ignored unless <see cref="Provider"/> is <see cref="EmailProvider.Log"/>: with a real
    /// transport configured there is no log line for it to govern.
    /// </para>
    /// </summary>
    public bool AllowTokensInLog { get; set; }
}
