using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Email;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The fallback sender used when no SMTP server is configured, and the opt-in that now stands
/// between it and a token in a log file.
/// <para>
/// Two defects are pinned here, one from each direction. The original: the code before this port
/// existed logged a warning claiming the token was "written to this log" without actually
/// including it. And the one v1 shipped knowingly — with no SMTP configured it wrote every setup
/// and recovery token into the application log, which `docs/security/release-readiness.md` carried
/// as an accepted risk rather than a control. Phase 12B made it a decision:
/// <see cref="EmailOptions.AllowTokensInLog"/> is false, so the default installation writes no
/// token anywhere.
/// </para>
/// </summary>
public sealed class EmailSenderTests
{
    [Fact]
    public async Task the_log_sender_writes_the_address_subject_and_body_when_that_is_opted_into()
    {
        var logger = new CapturingLogger();
        var sender = new LogEmailSender(logger, Configured(allowTokensInLog: true));

        await sender.SendAsync(
            new EmailMessage("owner@example.com", "Reset your password", "Use this token: abc123"),
            CancellationToken.None);

        string logged = Assert.Single(logger.Messages);
        Assert.Contains("owner@example.com", logged, StringComparison.Ordinal);
        Assert.Contains("Reset your password", logged, StringComparison.Ordinal);
        Assert.Contains("abc123", logged, StringComparison.Ordinal);
    }

    /// <summary>
    /// The default, and the point of the whole change: nothing an installation was never asked
    /// about puts a usable credential in a file.
    /// </summary>
    [Fact]
    public async Task no_token_reaches_the_log_unless_the_operator_asked_for_that()
    {
        var logger = new CapturingLogger();
        var sender = new LogEmailSender(logger, Configured(allowTokensInLog: false));

        await sender.SendAsync(
            new EmailMessage("owner@example.com", "Reset your password", "Use this token: abc123"),
            CancellationToken.None);

        string logged = Assert.Single(logger.Messages);
        Assert.DoesNotContain("abc123", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("Use this token", logged, StringComparison.Ordinal);
    }

    /// <summary>
    /// Withholding it silently would be worse than writing it: an operator would see nothing and
    /// conclude the invite worked. The line has to name both ways out.
    /// </summary>
    [Fact]
    public async Task withholding_a_token_says_how_to_make_it_reachable()
    {
        var logger = new CapturingLogger();
        var sender = new LogEmailSender(logger, Configured(allowTokensInLog: false));

        await sender.SendAsync(
            new EmailMessage("owner@example.com", "Your DevBuddy account is ready", "token: xyz"),
            CancellationToken.None);

        string logged = Assert.Single(logger.Messages);
        Assert.Contains("owner@example.com", logged, StringComparison.Ordinal);
        Assert.Contains("Your DevBuddy account is ready", logged, StringComparison.Ordinal);
        Assert.Contains("Email:Provider=Smtp", logged, StringComparison.Ordinal);
        Assert.Contains("Email:AllowTokensInLog=true", logged, StringComparison.Ordinal);
    }

    /// <summary>The default is the safe one, checked on the type rather than on a comment.</summary>
    [Fact]
    public void writing_tokens_to_the_log_is_off_by_default()
    {
        Assert.False(new EmailOptions().AllowTokensInLog);
        Assert.Equal(EmailProvider.Log, new EmailOptions().Provider);
    }

    private static IOptions<EmailOptions> Configured(bool allowTokensInLog) =>
        Options.Create(new EmailOptions { AllowTokensInLog = allowTokensInLog });

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
