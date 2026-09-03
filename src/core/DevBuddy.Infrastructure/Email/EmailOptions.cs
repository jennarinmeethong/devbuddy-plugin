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
}
