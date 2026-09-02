using Amazon.Runtime;
using Amazon.S3;
using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using DevBuddy.Infrastructure.Persistence.Search;
using DevBuddy.Infrastructure.Security;
using DevBuddy.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DevBuddy.Infrastructure;

/// <summary>
/// Composition for the infrastructure adapters. Hosts call this; nothing above the Application
/// layer knows which implementation is behind a port.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers PostgreSQL persistence, search, and evidence storage.
    /// <para>
    /// The caller supplies the connection string rather than this method reading configuration,
    /// so a host cannot accidentally inherit a connection from somewhere it did not intend.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevBuddyInfrastructure(
        this IServiceCollection services,
        string connectionString,
        Action<EvidenceStoreOptions>? configureEvidence = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContext<DevBuddyDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<MutableTenantContext>());

        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddScoped<IAccessDirectory, AccessDirectory>();
        services.AddScoped<ISearchIndex, PostgresSearchIndex>();
        services.AddScoped<EvidenceMetadataStore>();

        services.AddScoped<AuditStore>();
        services.AddScoped<IAuditSink>(provider => provider.GetRequiredService<AuditStore>());
        services.AddScoped<IAuditReader>(provider => provider.GetRequiredService<AuditStore>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAdministrativeOperations, AdministrativeOperations>();

        // Placeholders until Phase 6. They are registered so the pipeline runs end to end, and
        // they are named so nobody mistakes them for the control. See UnimplementedScanning.cs.
        services.AddSingleton<IRedactor, UnimplementedRedactor>();
        services.AddSingleton<ISecretScanner, UnimplementedSecretScanner>();

        services.AddEvidenceStorage(configureEvidence);
        return services;
    }

    /// <summary>
    /// Registers authorization, sign-in, tokens, and account recovery.
    /// <para>
    /// Separate from the persistence registration on purpose: a host that only reads knowledge
    /// (the console running a migration, say) has no reason to construct a token signer, and
    /// keeping them apart means a missing signing key fails at the host that needs one rather
    /// than at every host.
    /// </para>
    /// </summary>
    public static IServiceCollection AddDevBuddyIdentity(
        this IServiceCollection services, Action<IdentitySettings> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.Configure(configure);

        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddSingleton<IPasswordHasher<PasswordSubject>, PasswordHasher<PasswordSubject>>();
        services.AddScoped<IUserAuthenticator, UserAuthenticator>();
        services.AddScoped<ICredentialManager, CredentialManager>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IAccountRecoveryService, AccountRecoveryService>();
        services.AddScoped<IWorkspaceResolver, WorkspaceResolver>();

        return services;
    }

    private static IServiceCollection AddEvidenceStorage(
        this IServiceCollection services, Action<EvidenceStoreOptions>? configure)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IAmazonS3>(provider =>
        {
            EvidenceStoreOptions options = provider.GetRequiredService<IOptions<EvidenceStoreOptions>>().Value;

            var config = new AmazonS3Config
            {
                ServiceURL = options.ServiceUrl,

                // MinIO addresses buckets by path, not by subdomain.
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            };

            return new AmazonS3Client(
                new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
        });

        services.AddScoped<IEvidenceBlobStore>(provider =>
        {
            IOptions<EvidenceStoreOptions> options = provider.GetRequiredService<IOptions<EvidenceStoreOptions>>();

            return options.Value.Provider == EvidenceStoreProvider.FileSystem
                ? new FileSystemEvidenceBlobStore(options)
                : new ObjectStorageEvidenceBlobStore(provider.GetRequiredService<IAmazonS3>(), options);
        });

        services.AddScoped<IEvidenceStore, EvidenceStore>();
        return services;
    }
}
