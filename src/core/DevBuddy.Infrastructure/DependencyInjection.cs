using Amazon.Runtime;
using Amazon.S3;
using DevBuddy.Application.Abstractions;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Analysis;
using DevBuddy.Infrastructure.Email;
using DevBuddy.Infrastructure.Evidence;
using DevBuddy.Infrastructure.Identity;
using DevBuddy.Infrastructure.Observability;
using DevBuddy.Infrastructure.Persistence;
using DevBuddy.Infrastructure.Persistence.Repositories;
using DevBuddy.Infrastructure.Persistence.Search;
using DevBuddy.Infrastructure.Scanning;
using DevBuddy.Infrastructure.Security;
using DevBuddy.Infrastructure.Time;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        Action<EvidenceStoreOptions>? configureEvidence = null,
        Action<AnalysisOptions>? configureAnalysis = null,
        Action<OutboundAccessOptions>? configureOutbound = null,
        Action<BackupOptions>? configureBackup = null,
        Action<RetentionOptions>? configureRetention = null,
        Action<ExportOptions>? configureExport = null,
        Action<EmailOptions>? configureEmail = null,
        Action<GitHubOptions>? configureGitHub = null,
        Action<LogFileOptions>? configureLogFile = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureGitHub is not null)
        {
            services.Configure(configureGitHub);
        }

        if (configureBackup is not null)
        {
            services.Configure(configureBackup);
        }

        if (configureRetention is not null)
        {
            services.Configure(configureRetention);
        }

        if (configureExport is not null)
        {
            services.Configure(configureExport);
        }

        if (configureEmail is not null)
        {
            services.Configure(configureEmail);
        }

        // Registered whether or not file logging is switched on, because the retention sweep needs
        // to know the window and the path even in the host that is not the one writing the files.
        // An unconfigured path simply means it finds nothing to sweep.
        LogFileOptions logFile = new();
        configureLogFile?.Invoke(logFile);
        services.AddSingleton(logFile);

        if (configureAnalysis is not null)
        {
            services.Configure(configureAnalysis);
        }

        // Nothing is reachable by default. An empty allow-list is the right starting point for a
        // system whose analysers read attacker-controlled content (SB-03, SB-06).
        var outbound = new OutboundAccessOptions();
        configureOutbound?.Invoke(outbound);
        services.AddSingleton(outbound);

        services.AddDbContext<DevBuddyDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<MutableTenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<MutableTenantContext>());

        services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        services.AddScoped<IProjectDirectory, ProjectDirectory>();
        services.AddScoped<ITeamDirectory, TeamDirectory>();
        services.AddScoped<IAccessDirectory, AccessDirectory>();
        services.AddScoped<IWorkspaceProvisioner, WorkspaceProvisioner>();
        services.AddScoped<ISearchIndex, PostgresSearchIndex>();
        services.AddScoped<EvidenceMetadataStore>();

        services.AddScoped<AuditStore>();
        services.AddScoped<IAuditSink>(provider => provider.GetRequiredService<AuditStore>());
        services.AddScoped<IAuditReader>(provider => provider.GetRequiredService<AuditStore>());

        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<BackupService>();
        services.AddScoped<ExportService>();
        services.AddScoped<IAdministrativeOperations, AdministrativeOperations>();
        services.AddScoped<IRetentionEnforcer, RetentionService>();

        // The real scanner and redactor share one rule set, so nothing can be reported as
        // sensitive and released anyway (SB-17).
        services.AddSingleton<IRedactor, SecretRedactor>();
        services.AddSingleton<ISecretScanner, SecretScanner>();

        // A second, narrower scanner for customer data, production data, and personal
        // information (SB-18). Denied only on the AI channel, and only until a project owner
        // approves a bounded scope — the pipeline applies that exception, not this pair.
        services.AddSingleton<IPersonalDataRedactor, PersonalDataRedactor>();
        services.AddSingleton<IPersonalDataScanner, PersonalDataScanner>();

        // Wrapped rather than checked inside, so the concurrency ceiling cannot be forgotten by a
        // second implementation of the port (SB-21).
        services.AddScoped<FileSystemCodeAnalyzer>();
        services.AddSingleton<AnalysisSlots>();
        services.AddScoped<ICodeAnalyzer>(provider => new ConcurrentAnalysisLimit(
            provider.GetRequiredService<FileSystemCodeAnalyzer>(),
            provider.GetRequiredService<AnalysisSlots>()));
        services.AddScoped<IKnowledgeQualityChecks, KnowledgeQualityChecks>();

        // Reads the mounted working copy by default: git metadata and loose objects, as files, no
        // network and no token (SB-04). The GitHub API adapter (ADR-0010) is the same port, opt-in
        // via GitHubOptions.Mode — off leaves this line's behaviour exactly as it always was.
        services.AddScoped<WorkingCopySourceSystemClient>();
        services.AddHttpClient<GitHubApiSourceSystemClient>();

        services.AddScoped<ISourceSystemClient>(provider =>
        {
            IOptions<GitHubOptions> options = provider.GetRequiredService<IOptions<GitHubOptions>>();

            return options.Value.Mode == SourceSystemMode.GitHubApi
                ? provider.GetRequiredService<GitHubApiSourceSystemClient>()
                : provider.GetRequiredService<WorkingCopySourceSystemClient>();
        });

        services.AddSingleton<IHostResolver, DnsHostResolver>();
        services.AddSingleton<UrlGuard>();

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
        services.AddScoped<ISignedInUserDirectory, SignedInUserDirectory>();
        services.AddScoped<IMachineTokenService, MachineTokenService>();

        // The one path that creates an identity without one. Registered with identity rather
        // than with persistence because it needs the credential manager to set a password.
        services.AddScoped<IInstallationBootstrapper, InstallationBootstrapper>();

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

        services.AddScoped<IEmailSender>(provider =>
        {
            IOptions<EmailOptions> options = provider.GetRequiredService<IOptions<EmailOptions>>();

            return options.Value.Provider == EmailProvider.Smtp
                ? new SmtpEmailSender(options)
                : new LogEmailSender(provider.GetRequiredService<ILogger<LogEmailSender>>());
        });

        return services;
    }
}
