using System.Globalization;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Application.Workers.Jobs;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Embeddings;
using DevBuddy.Infrastructure.Hosting;
using DevBuddy.Infrastructure.Observability;
using DevBuddy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Cli;

/// <summary>
/// Builds the container and runs one command against it.
/// <para>
/// The console composes the system exactly as the servers do, through
/// <see cref="HostComposition.AddDevBuddy"/>. A console with its own composition would be a
/// console that could reach a different database, or the same one with a different evidence store
/// behind it, and neither is something a person running <c>backup</c> would expect.
/// </para>
/// </summary>
internal static class Runner
{
    /// <summary>
    /// Exit codes. Distinct so a script can tell a refusal from a crash rather than having to
    /// read the message.
    /// </summary>
    private const int Ok = 0;
    private const int Refused = 1;
    private const int Misconfigured = 2;

    /// <summary><c>scope-report</c> found rows, so a script can act on it without parsing text.</summary>
    private const int StrayRowsFound = 3;

    /// <summary>
    /// The same code, for a command that rejects its arguments before it builds anything —
    /// <c>retention --every</c> with an interval that is not one.
    /// </summary>
    public static int MisconfiguredExitCode => Misconfigured;

    /// <summary>Indented output, because a person is reading it.</summary>
    private static readonly JsonSerializerOptions Printing = new() { WriteIndented = true };

    public static async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        return await WithScopeAsync(async scope =>
        {
            var db = scope.ServiceProvider.GetRequiredService<DevBuddyDbContext>();

            IEnumerable<string> pending = await db.Database.GetPendingMigrationsAsync(cancellationToken);
            string[] names = [.. pending];

            if (names.Length == 0)
            {
                Console.WriteLine("The database is already up to date.");
                return Ok;
            }

            Console.WriteLine($"Applying {names.Length} migration(s): {string.Join(", ", names)}");
            await db.Database.MigrateAsync(cancellationToken);
            Console.WriteLine("Done.");
            return Ok;
        });
    }

    public static async Task<int> BootstrapAsync(
        string workspaceName,
        string email,
        string password,
        string? projectName,
        CancellationToken cancellationToken)
    {
        return await WithScopeAsync(async scope =>
        {
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IInstallationBootstrapper>();

            BootstrapResult result = await bootstrapper.BootstrapAsync(
                new BootstrapRequest(workspaceName, email, password, projectName), cancellationToken);

            if (!result.Created)
            {
                Console.Error.WriteLine(result.Reason);
                return Refused;
            }

            Console.WriteLine(result.Reason);
            Console.WriteLine($"workspace  {result.WorkspaceId!.Value.Value}");

            if (result.ProjectId is { } project)
            {
                Console.WriteLine($"project    {project.Value}");
            }

            // Printed because the identifier is what every later command needs as --actor, and
            // because it is not a secret: the password is, and that is not echoed anywhere.
            Console.WriteLine($"actor      {result.AdministratorId!.Value.Value}");
            return Ok;
        });
    }

    /// <summary>
    /// Restores a backup, outside the pipeline.
    /// <para>
    /// No actor, because there is nobody to be: a restore from total loss runs against a database
    /// with no accounts in it. The safety here is not authorisation but the restore's own refusal
    /// to write into an installation that already has data.
    /// </para>
    /// </summary>
    public static async Task<int> RestoreAsync(string reference, CancellationToken cancellationToken)
    {
        return await WithScopeAsync(async scope =>
        {
            RestoreOutcome outcome = await scope.ServiceProvider
                .GetRequiredService<IAdministrativeOperations>()
                .RestoreAsync(reference, cancellationToken);

            if (!outcome.Succeeded)
            {
                Console.Error.WriteLine(outcome.Detail);
                return Refused;
            }

            Console.WriteLine(outcome.Detail);
            return Ok;
        });
    }

    /// <summary>
    /// Lists rows stored against a project that is not a live project of their workspace, and
    /// changes nothing. See <see cref="IScopeIntegrityReport"/> for why they can exist and why this
    /// only reports them.
    /// </summary>
    public static async Task<int> ScopeReportAsync(CancellationToken cancellationToken)
    {
        return await WithScopeAsync(async scope =>
        {
            IReadOnlyList<StrayScopeRows> found = await scope.ServiceProvider
                .GetRequiredService<IScopeIntegrityReport>()
                .FindAsync(cancellationToken);

            if (found.Count == 0)
            {
                Console.WriteLine("No row names a project that is not a live project of its workspace.");
                return Ok;
            }

            Console.WriteLine(
                $"{found.Sum(entry => entry.Rows)} row(s) name a project that is not a live project of their workspace. Nothing was changed.");

            foreach (StrayScopeRows entry in found)
            {
                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{entry.Table,-28} workspace {entry.WorkspaceId}  project {entry.ProjectId}  rows {entry.Rows}"));
            }

            return StrayRowsFound;
        });
    }

    /// <summary>
    /// Deletes what <see cref="ScopeReportAsync"/> reports, when the operator confirms the count it
    /// showed and names an actor who administers every workspace involved. Refused with nothing
    /// changed otherwise; see <see cref="IScopeIntegrityReport.PurgeAsync"/>.
    /// </summary>
    public static async Task<int> ScopePurgeAsync(int? confirm, string? actor, CancellationToken cancellationToken)
    {
        if (confirm is not { } expected || !Guid.TryParse(actor, out Guid actorId))
        {
            Console.Error.WriteLine(
                "Deleting needs --confirm <the row count scope-report showed> and --actor <your user id>. Nothing was deleted.");
            return Misconfigured;
        }

        return await WithScopeAsync(async scope =>
        {
            StrayScopePurge purge = await scope.ServiceProvider
                .GetRequiredService<IScopeIntegrityReport>()
                .PurgeAsync(new UserId(actorId), expected, cancellationToken);

            switch (purge.Outcome)
            {
                case StrayScopePurgeOutcome.Purged:
                    Console.WriteLine(purge.Message);
                    foreach (StrayScopeRows entry in purge.Removed)
                    {
                        Console.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{entry.Table,-28} workspace {entry.WorkspaceId}  project {entry.ProjectId}  rows {entry.Rows}"));
                    }

                    return Ok;

                case StrayScopePurgeOutcome.NothingToPurge:
                    Console.WriteLine(purge.Message);
                    return Ok;

                default:
                    Console.Error.WriteLine(purge.Message);
                    return Refused;
            }
        });
    }

    /// <summary>
    /// Applies the retention schedule, outside the pipeline for the same reason as
    /// <see cref="RestoreAsync"/>: a sweep spans every workspace and project, so there is no
    /// single caller to authorise it against.
    /// <para>
    /// With <paramref name="every"/> set it stays running and repeats, which is how the shipped
    /// stack schedules the sweep without a shell to put <c>cron</c> in. Either way the pass
    /// itself is <see cref="RetentionSchedule.SweepOnceAsync"/> against the same
    /// <see cref="IRetentionEnforcer"/>; the interval adds a loop and nothing else.
    /// </para>
    /// </summary>
    public static async Task<int> RetentionAsync(TimeSpan? every, CancellationToken cancellationToken)
    {
        return await WithProviderAsync(async provider =>
        {
            // A scope per pass, not one for the process. The unit of work behind this is a
            // DbContext, and a scheduler that held one open for months would hold its change
            // tracker, its connection, and every entity it had ever seen for just as long.
            async Task<RetentionReport> SweepAsync(CancellationToken token)
            {
                using IServiceScope scope = provider.CreateScope();

                return await scope.ServiceProvider
                    .GetRequiredService<IRetentionEnforcer>()
                    .ApplyAsync(token);
            }

            if (every is not { } interval)
            {
                await RetentionSchedule.SweepOnceAsync(SweepAsync, Console.Out, cancellationToken);
                return Ok;
            }

            using CancellationTokenSource stopping =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            using IDisposable signals = RetentionSchedule.StopOnSignal(stopping);

            await RetentionSchedule.SweepEveryAsync(
                SweepAsync,
                interval,
                Task.Delay,
                Console.Out,
                Console.Error,
                stopping.Token);

            return Ok;
        });
    }

    /// <summary>
    /// Runs one worker job as the owner of <see cref="WorkerSchedule.TokenVariable"/>, once or on
    /// a schedule.
    /// <para>
    /// Unlike <see cref="RetentionAsync"/> this runs <b>inside</b> the pipeline, because both jobs
    /// read project content and ADR-0013 leaves such a job only one shape: a caller bound to a real
    /// token. Every call the job makes is dispatched, authorised against the membership behind that
    /// token, redacted on the channel the job declared, and audited under the token owner's name.
    /// </para>
    /// <para>
    /// A scope per pass, and within it the token resolved again, the job built again and the
    /// workspace entered again. Nothing about a caller survives from one pass to the next, so a
    /// revoked token stops the very next pass and a narrowed membership narrows it.
    /// </para>
    /// </summary>
    public static async Task<int> WorkerAsync(
        string jobName,
        TimeSpan? every,
        int? budget,
        TimeSpan? staleAfter,
        CancellationToken cancellationToken)
    {
        string? token = Environment.GetEnvironmentVariable(WorkerSchedule.TokenVariable);

        if (!WorkerSchedule.TryValidate(jobName, budget, staleAfter, token, out string? problem))
        {
            Console.Error.WriteLine(problem);
            return Misconfigured;
        }

        return await WithProviderAsync(async provider =>
        {
            async Task<WorkerRunReport> PassAsync(CancellationToken passToken)
            {
                using IServiceScope scope = provider.CreateScope();
                IServiceProvider services = scope.ServiceProvider;

                OperationDispatcher dispatcher = services.GetRequiredService<OperationDispatcher>();

                CallerBoundWorkerJob job = jobName == WorkerSchedule.RecordEmbeddingSweep
                    ? new RecordEmbeddingSweepJob(
                        dispatcher,
                        services.GetRequiredService<EmbeddingGateway>(),
                        services.GetRequiredService<IEmbeddingIndex>(),
                        services.GetRequiredService<IPersonalDataRedactor>().RuleSetFingerprint,
                        services.GetService<EmbeddingOptions>()?.ChunkCharacters
                            ?? RecordEmbeddingSweepJob.DefaultChunkCharacters)
                    : new StaleRecordSweepJob(dispatcher, staleAfter!.Value);

                WorkerRunReport report = await WorkerSchedule.PassAsync(
                    job,
                    resolving => services.GetRequiredService<IMachineTokenService>()
                        .ResolveAsync(token!, resolving),
                    workspace => services.GetRequiredService<MutableTenantContext>()
                        .EnterWorkspace(workspace),
                    budget ?? 0,
                    passToken);

                WorkerSchedule.Report(job, report, Console.Out);
                return report;
            }

            if (every is not { } interval)
            {
                WorkerRunReport once = await PassAsync(cancellationToken);
                return once.Refused ? Refused : Ok;
            }

            using CancellationTokenSource stopping =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            using IDisposable signals = RetentionSchedule.StopOnSignal(stopping);

            await WorkerSchedule.RunEveryAsync(
                jobName,
                PassAsync,
                interval,
                Task.Delay,
                Console.Out,
                Console.Error,
                stopping.Token);

            return Ok;
        });
    }

    public static int ListOperations(bool aiOnly)
    {
        IReadOnlyList<UseCaseDescriptor> descriptors =
            aiOnly ? UseCaseCatalog.AiExposed : UseCaseCatalog.All;

        foreach (UseCaseDescriptor descriptor in descriptors)
        {
            string exposure = descriptor.AiExposure == AiExposure.Allowed ? "ai" : "human-only";
            Console.WriteLine($"{descriptor.Name,-28} {descriptor.Permission,-18} {exposure}");
        }

        return Ok;
    }

    /// <summary>
    /// Runs one operation through the dispatcher, on the Human channel.
    /// <para>
    /// Human rather than InternalSystem, and that is a real distinction: a person at a terminal
    /// is a person, and their permissions are the ones that should apply. Nothing about running
    /// on a server console makes an operator more privileged than they are in the web UI.
    /// </para>
    /// </summary>
    public static async Task<int> InvokeAsync(
        string name, string body, string? actor, CancellationToken cancellationToken)
    {
        string? subject = actor ?? Environment.GetEnvironmentVariable("DEVBUDDY_ACTOR");

        if (!Guid.TryParse(subject, out Guid actorId))
        {
            Console.Error.WriteLine(
                "No actor. Pass --actor <id> or set DEVBUDDY_ACTOR. Operations run as somebody, "
                + "and the console will not run them as nobody.");

            return Misconfigured;
        }

        JsonElement arguments;

        try
        {
            arguments = JsonSerializer.Deserialize<JsonElement>(body);
        }
        catch (JsonException failure)
        {
            Console.Error.WriteLine($"The arguments are not valid JSON: {failure.Message}");
            return Misconfigured;
        }

        return await WithScopeAsync(async scope =>
        {
            IServiceProvider services = scope.ServiceProvider;

            await WorkspaceEntry.EnterAsync(
                arguments,
                services.GetRequiredService<MutableTenantContext>(),
                services.GetRequiredService<IWorkspaceResolver>(),
                cancellationToken);

            DispatchResult result = await services.GetRequiredService<OperationDispatcher>()
                .InvokeAsync(
                    name,
                    arguments,
                    new CallerContext(new UserId(actorId), AccessChannel.Human, "console"),
                    cancellationToken);

            if (result.IsSuccess)
            {
                Console.WriteLine(Pretty(result.Payload));
                return Ok;
            }

            Console.Error.WriteLine(result.Reason);

            foreach (string detail in result.Details)
            {
                Console.Error.WriteLine($"  {detail}");
            }

            return Refused;
        });
    }

    /// <summary>Builds the container and runs the work in one scope, which is what every command
    /// but the scheduled retention mode wants.</summary>
    private static Task<int> WithScopeAsync(Func<IServiceScope, Task<int>> work) =>
        WithProviderAsync(async provider =>
        {
            using IServiceScope scope = provider.CreateScope();
            return await work(scope);
        });

    /// <summary>
    /// Builds the container from configuration and hands over the provider rather than a scope.
    /// <para>
    /// A missing connection string is reported as a configuration problem rather than thrown at
    /// the operator as a stack trace, because it is the single most likely thing to be wrong on a
    /// first run and it is not a bug.
    /// </para>
    /// <para>
    /// The provider rather than a scope, because the scheduled retention mode runs for as long as
    /// the container does and creates a scope per pass: one scope held for the life of a scheduler
    /// is one <c>DbContext</c> held for the life of a scheduler.
    /// </para>
    /// </summary>
    private static async Task<int> WithProviderAsync(Func<IServiceProvider, Task<int>> work)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables("DEVBUDDY_")
            .Build();

        ServiceCollection services = new();

        try
        {
            services.AddDevBuddy(configuration, "The console");

            ConsoleLogging.Add(services, configuration);
        }
        catch (InvalidOperationException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return Misconfigured;
        }

        await using ServiceProvider provider = services.BuildServiceProvider();

        return await work(provider);
    }

    private static string Pretty(JsonElement? payload) =>
        payload is { } value
            ? JsonSerializer.Serialize(value, Printing)
            : string.Create(CultureInfo.InvariantCulture, $"{{}}");
}
