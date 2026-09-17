using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// How every host connects. Two settings that change no behaviour, and each of them removed a
/// line from every log that read like a fault and was not one.
/// </summary>
public sealed class DatabaseConnectionTests
{
    [Fact]
    public void gss_encryption_is_switched_off_when_nobody_chose()
    {
        NpgsqlConnectionStringBuilder result = new(
            DatabaseConnection.WithoutGssProbe("Host=database;Database=devbuddy;Username=devbuddy;Password='p@ss;word'"));

        Assert.Equal(GssEncryptionMode.Disable, result.GssEncryptionMode);

        // And nothing else moved, including a quoted password with the separator in it.
        Assert.Equal("database", result.Host);
        Assert.Equal("devbuddy", result.Database);
        Assert.Equal("p@ss;word", result.Password);
    }

    [Theory]
    [InlineData("Host=database;GSS Encryption Mode=Require", GssEncryptionMode.Require)]
    [InlineData("Host=database;GssEncryptionMode=Prefer", GssEncryptionMode.Prefer)]
    [InlineData("Host=database;gss encryption mode=require", GssEncryptionMode.Require)]
    public void an_operators_own_choice_is_kept(string connectionString, GssEncryptionMode expected)
    {
        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(
            DatabaseConnection.WithoutGssProbe(connectionString)).GssEncryptionMode);
    }

    [Fact]
    public void the_registered_context_uses_both_settings()
    {
        ServiceCollection services = new();
        services.AddDevBuddyInfrastructure("Host=database;Database=devbuddy");

        using ServiceProvider provider = services.BuildServiceProvider();
        DbContextOptions<DevBuddyDbContext> options =
            provider.GetRequiredService<DbContextOptions<DevBuddyDbContext>>();

        RelationalOptionsExtension relational = RelationalOptionsExtension.Extract(options);

        Assert.Equal(QuerySplittingBehavior.SingleQuery, relational.QuerySplittingBehavior);
        Assert.Equal(
            GssEncryptionMode.Disable,
            new NpgsqlConnectionStringBuilder(relational.ConnectionString).GssEncryptionMode);
    }
}
