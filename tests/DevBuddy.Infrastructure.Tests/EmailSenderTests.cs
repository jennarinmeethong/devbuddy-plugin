using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Email;
using Microsoft.Extensions.Logging;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The fallback sender used when no SMTP server is configured. Its whole job is to put the
/// message where a self-hosted operator can find it — the bug this fixes is that the equivalent
/// code before this port existed logged a warning claiming the token was "written to this log"
/// without actually including it.
/// </summary>
public sealed class EmailSenderTests
{
    [Fact]
    public async Task the_log_sender_writes_the_address_subject_and_body()
    {
        var logger = new CapturingLogger();
        var sender = new LogEmailSender(logger);

        await sender.SendAsync(
            new EmailMessage("owner@example.com", "Reset your password", "Use this token: abc123"),
            CancellationToken.None);

        string logged = Assert.Single(logger.Messages);
        Assert.Contains("owner@example.com", logged, StringComparison.Ordinal);
        Assert.Contains("Reset your password", logged, StringComparison.Ordinal);
        Assert.Contains("abc123", logged, StringComparison.Ordinal);
    }

    private sealed class CapturingLogger : ILogger<LogEmailSender>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
