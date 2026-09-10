using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Email;

/// <summary>
/// The delivery channel for an installation with no SMTP server configured — not a stub, the
/// documented behaviour `info.md` describes for a setup or recovery token before this port
/// existed: an operator retrieves it from here and carries it the rest of the way by hand.
/// <para>
/// <b>The body is withheld unless the operator asked for it.</b> Through v1 this sender wrote the
/// whole message, token included, because that was the only way the token reached anybody — and
/// `docs/security/release-readiness.md` has carried "a secret in a log, by design" as an accepted
/// risk ever since. Phase 12B closed that the way the owner chose: a token is written to a log
/// only when <see cref="EmailOptions.AllowTokensInLog"/> is set on purpose. Left alone, this logs
/// that a message was generated, for whom, and how to make it reachable, and the token itself is
/// never written down.
/// </para>
/// <para>
/// Withholding it does not strand an administrator. <c>create_user_account</c> also returns the
/// setup token in its own response, so the invite path has a delivery route that was never a log
/// line. Self-service recovery does not: with no SMTP and no opt-in, a recovery token is
/// deliberately unreachable, which is the whole point of making this a decision rather than a
/// default.
/// </para>
/// </summary>
internal sealed partial class LogEmailSender(
    ILogger<LogEmailSender> logger, IOptions<EmailOptions> options) : IEmailSender
{
    private readonly ILogger<LogEmailSender> _logger = Guard.NotNull(logger, nameof(logger));
    private readonly IOptions<EmailOptions> _options = Guard.NotNull(options, nameof(options));

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_options.Value.AllowTokensInLog)
        {
            LogMessage(_logger, message.ToAddress, message.Subject, message.Body);
        }
        else
        {
            LogWithheld(_logger, message.ToAddress, message.Subject);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "No SMTP server is configured and Email:AllowTokensInLog is set, so this "
            + "message to {ToAddress} was written here instead of delivered. Treat this log line "
            + "as sensitive: anything that can read this file can use the token in it.\n"
            + "Subject: {Subject}\n{Body}")]
    private static partial void LogMessage(ILogger logger, string toAddress, string subject, string body);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "A message to {ToAddress} ({Subject}) could not be delivered and its contents "
            + "were not written to this log. Configure Email:Provider=Smtp to deliver it, or set "
            + "Email:AllowTokensInLog=true to accept a one-time token being written to the "
            + "application log and issue it again. A setup token is also returned in the "
            + "create_user_account response; a recovery token has no second route.")]
    private static partial void LogWithheld(ILogger logger, string toAddress, string subject);
}
