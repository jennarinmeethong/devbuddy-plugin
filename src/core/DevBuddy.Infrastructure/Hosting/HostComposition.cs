using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Scanning;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Infrastructure.Hosting;

/// <summary>
/// Composes the whole system from configuration, for a host that has an
/// <see cref="IConfiguration"/> to read.
/// <para>
/// Written once rather than three times. The API, the MCP server, and the console all need the
/// same five sections wired the same way, and three copies of that wiring would eventually differ
/// — most likely in which section a host forgot, which is how a deployment ends up with an
/// evidence store or an outbound allow-list nobody meant to leave at its default.
/// </para>
/// </summary>
public static class HostComposition
{
    /// <summary>
    /// Registers persistence, evidence, analysis, outbound access, identity, and the operation
    /// set from configuration.
    /// <para>
    /// The connection string is required and never invented. A host that starts against a
    /// database it guessed at is worse than one that refuses to start.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevBuddy(
        this IServiceCollection services, IConfiguration configuration, string hostName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("DevBuddy")
            ?? throw new InvalidOperationException(
                $"No DevBuddy connection string is configured. {hostName} does not invent one.");

        services.AddDevBuddyInfrastructure(
            connectionString,
            evidence => configuration.GetSection(EvidenceStoreOptions.SectionName).Bind(evidence),
            analysis => configuration.GetSection(AnalysisOptions.SectionName).Bind(analysis),
            outbound => configuration.GetSection(OutboundAccessOptions.SectionName).Bind(outbound));

        services.AddDevBuddyIdentity(
            identity => configuration.GetSection(IdentitySettings.SectionName).Bind(identity));

        services.AddDevBuddyOperations();
        return services;
    }

    /// <summary>Reads the identity settings a host needs before the container is built.</summary>
    public static IdentitySettings ReadIdentitySettings(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        IdentitySettings settings = new();
        configuration.GetSection(IdentitySettings.SectionName).Bind(settings);
        return settings;
    }
}
