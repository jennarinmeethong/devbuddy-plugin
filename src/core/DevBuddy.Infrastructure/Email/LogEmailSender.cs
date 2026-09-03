using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using Microsoft.Extensions.Logging;

namespace DevBuddy.Infrastructure.Email;

/// <summary>
/// The delivery channel for an installation with no SMTP server configured — not a stub, the
/// documented behaviour `info.md` describes for a setup or recovery token before this port
/// existed: an operator retrieves it from here and carries it the rest of the way by hand.
/// <para>
/// Logs the whole message, address and body included. That is deliberate rather than an
/// oversight: the entire reason this sender exists is so the token in the body reaches the one
/// person who can read this log, and a redacted version would defeat the one job it has.
/// </para>
/// </summary>
internal sealed partial class LogEmailSender(ILogger<LogEmailSender> logger) : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger = Guard.NotNull(logger, nameof(logger));

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogMessage(_logger, message.ToAddress, message.Subject, message.Body);
        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "No SMTP server is configured, so this message to {ToAddress} was written here "
            + "instead of delivered. Treat this log line as sensitive.\nSubject: {Subject}\n{Body}")]
    private static partial void LogMessage(ILogger logger, string toAddress, string subject, string body);
}
