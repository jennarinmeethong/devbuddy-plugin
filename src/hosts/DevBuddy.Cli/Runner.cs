using System.Globalization;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Administration;
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

            // Off unless Logging:File:Path is set. This is the host that runs the retention sweep,
            // so it needs the options even when it is not the one writing the files.
            services.AddDevBuddyFileLogging(configuration);
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
