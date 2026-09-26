using System.Net;
using System.Net.Sockets;
using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The SMTP sender when the mail server cannot be reached.
/// <para>
/// Until 2026-09-26 the failure escaped to the caller. Account recovery then answered 500 for an
/// address with an account and 202 for one without, which told anybody who asked which addresses
/// had accounts, and <c>create_user_account</c> answered 500 after writing the account. The sender
/// now logs the failure and returns, and these tests hold both halves of that: the caller is not
/// told, and the log says what failed without the body, which carries a token.
/// </para>
/// </summary>
public sealed class SmtpEmailSenderTests
{
    private const string Token = "a-one-time-token-that-must-never-reach-a-log";

    [Fact]
    public async Task a_message_that_cannot_be_delivered_is_logged_and_not_thrown()
    {
        int port = ClosedPort();
        var logger = new CapturingLogger();
        var sender = new SmtpEmailSender(Unreachable(port), logger);

        await sender.SendAsync(
            new EmailMessage("someone@example.test", "Reset your DevBuddy password", $"Token: {Token}"),
            CancellationToken.None);

        (LogLevel level, string line) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("someone@example.test", line, StringComparison.Ordinal);
        Assert.Contains($"127.0.0.1:{port}", line, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_caller_that_cancelled_still_sees_its_cancellation()
    {
        var logger = new CapturingLogger();
        var sender = new SmtpEmailSender(Unreachable(ClosedPort()), logger);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sender.SendAsync(
            new EmailMessage("someone@example.test", "Subject", "Body"), cancelled.Token));

        Assert.Empty(logger.Entries);
    }

    private static IOptions<EmailOptions> Unreachable(int port) => Options.Create(new EmailOptions
    {
        Provider = EmailProvider.Smtp,
        Host = "127.0.0.1",
        Port = port,
        Username = "user",
        Password = "password",
    });

    // A port something listened on a moment ago and nothing does now, so the connection is refused
    // at once rather than timing out.
    private static int ClosedPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class CapturingLogger : ILogger<SmtpEmailSender>
    {
        public List<(LogLevel Level, string Line)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
