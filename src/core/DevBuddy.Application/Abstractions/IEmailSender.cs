namespace DevBuddy.Application.Abstractions;

/// <summary>
/// Delivers a setup or recovery token to the person it belongs to.
/// <para>
/// Before this, both tokens were handed to whoever was already looking — an administrator in the
/// API response, or a log line for self-service recovery — "because no delivery channel exists
/// yet," per `info.md`. When no SMTP server is configured this port still fills that role: see
/// the implementation notes on the fallback sender. When one is configured, the token actually
/// reaches its owner instead.
/// </para>
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>A message with nowhere further to go than this port. Plain text; no attachments, no HTML.</summary>
public sealed record EmailMessage(string ToAddress, string Subject, string Body);
