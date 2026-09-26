using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DevBuddy.Infrastructure.Email;

/// <summary>A real mail server, reached over SMTP with MailKit.</summary>
/// <remarks>
/// <para>
/// A message that cannot be delivered is logged and never thrown to the caller. Until 2026-09-26 a
/// failure escaped as an unhandled exception, which did two kinds of harm. Account recovery answered
/// 500 for an address that has an account and 202 for one that does not, so a broken mail server
/// told anybody who asked which addresses had accounts: the one thing that endpoint exists not to
/// say. And <c>create_user_account</c> answered 500 after the account and its membership had been
/// written, losing the setup token its response exists to return.
/// </para>
/// <para>
/// Delivery is best effort for both callers, and neither needs to know it failed. Recovery answers
/// the same either way by design, and a new account's setup token is in the response whatever
/// happens to the email. The log line says what failed and where, so an operator can fix the
/// settings and issue the message again. It never carries the body, which holds the token.
/// </para>
/// </remarks>
internal sealed partial class SmtpEmailSender(IOptions<EmailOptions> options, ILogger<SmtpEmailSender> logger)
    : IEmailSender
{
    private readonly EmailOptions _options = Guard.NotNull(options, nameof(options)).Value;
    private readonly ILogger<SmtpEmailSender> _logger = Guard.NotNull(logger, nameof(logger));

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Guard.NotNull(message, nameof(message));

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
            mime.To.Add(MailboxAddress.Parse(message.ToAddress));
            mime.Subject = message.Subject;
            mime.Body = new TextPart("plain") { Text = message.Body };

            using var client = new SmtpClient();

            SecureSocketOptions security = _options.UseStartTls
                ? SecureSocketOptions.StartTls
                : SecureSocketOptions.None;

            await client.ConnectAsync(_options.Host, _options.Port, security, cancellationToken);

            if (!string.IsNullOrEmpty(_options.Username))
            {
                await client.AuthenticateAsync(_options.Username, _options.Password, cancellationToken);
            }

            await client.SendAsync(mime, cancellationToken);
            await client.DisconnectAsync(quit: true, cancellationToken);
        }
        catch (Exception failure) when (!cancellationToken.IsCancellationRequested)
        {
            // A caller that gave up still sees its cancellation. Anything else is the mail server's
            // or the settings' fault, and is reported here rather than to whoever asked.
            MessageNotDelivered(_logger, failure, message.ToAddress, message.Subject, _options.Host, _options.Port);
        }
    }

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "A message to {ToAddress} ({Subject}) could not be delivered through SMTP server "
            + "{Host}:{Port}. Its contents were not written to this log. The request that sent it "
            + "was answered as if it had been delivered, so the answer says nothing about whether "
            + "an account exists. Check Email:Host, Email:Port, Email:Username and Email:Password, "
            + "then issue the message again.")]
    private static partial void MessageNotDelivered(
        ILogger logger, Exception failure, string toAddress, string subject, string host, int port);
}
