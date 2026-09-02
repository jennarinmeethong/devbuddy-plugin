using System.Globalization;
using System.Text.Json;
using DevBuddy.Application;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Administration;
using DevBuddy.Infrastructure.Hosting;
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

    /// <summary>
    /// Builds the container from configuration and runs the work in one scope.
    /// <para>
    /// A missing connection string is reported as a configuration problem rather than thrown at
    /// the operator as a stack trace, because it is the single most likely thing to be wrong on a
    /// first run and it is not a bug.
    /// </para>
    /// </summary>
    private static async Task<int> WithScopeAsync(Func<IServiceScope, Task<int>> work)
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
        }
        catch (InvalidOperationException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return Misconfigured;
        }

        await using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();

        return await work(scope);
    }

    private static string Pretty(JsonElement? payload) =>
        payload is { } value
            ? JsonSerializer.Serialize(value, Printing)
            : string.Create(CultureInfo.InvariantCulture, $"{{}}");
}
