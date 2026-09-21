using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The console and the workers carry no signing key: they compose every operation and sign nothing.
/// Found by the end-to-end suite in Phase 13 after issue_password_reset came to depend, through the
/// recovery service, on the token service, whose constructor refused a missing key: from then on
/// every console `run` and every worker pass failed before doing anything.
/// </summary>
public sealed class ConsoleCompositionTests
{
    [Fact]
    public void every_operation_composes_without_a_signing_key()
    {
        using ServiceProvider provider = Compose(signingKey: null);
        using IServiceScope scope = provider.CreateScope();

        OperationDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<OperationDispatcher>();

        Assert.NotNull(dispatcher);
    }

    [Fact]
    public async Task a_short_key_is_still_refused_when_a_token_is_signed()
    {
        using ServiceProvider provider = Compose(signingKey: "too-short");
        using IServiceScope scope = provider.CreateScope();

        ITokenService tokens = scope.ServiceProvider.GetRequiredService<ITokenService>();

        InvalidOperationException refusal = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tokens.IssueAsync(UserId.New(), CancellationToken.None));

        Assert.Contains("at least 32 characters", refusal.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider Compose(string? signingKey)
    {
        Dictionary<string, string?> settings = new(StringComparer.Ordinal)
        {
            ["ConnectionStrings:DevBuddy"] = "Host=unused.invalid;Database=unused;Username=unused;Password=unused",
        };

        if (signingKey is not null)
        {
            settings[$"{IdentitySettings.SectionName}:SigningKey"] = signingKey;
        }

        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        ServiceCollection services = new();
        services.AddLogging();
        services.AddDevBuddy(configuration, "The composition test");

        return services.BuildServiceProvider();
    }
}
