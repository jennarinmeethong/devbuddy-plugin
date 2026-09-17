using DevBuddy.Cli;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The console's standard output is its answer, and a log line there made it unreadable: with a
/// log path set, every statement EF ran was printed ahead of the JSON a <c>run</c> returns.
/// <para>
/// In a collection of its own because it swaps the process's console writers, which every other
/// test in the assembly would otherwise share.
/// </para>
/// </summary>
[Collection(ConsoleWriters.Name)]
public sealed class ConsoleLoggingTests
{
    private static readonly Action<ILogger, string, Exception?> Executed =
        LoggerMessage.Define<string>(LogLevel.Information, new EventId(1), "Executed DbCommand {Sql}");

    [Fact]
    public void with_a_log_file_configured_nothing_is_logged_to_standard_output()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"devbuddy-console-log-{Guid.NewGuid():N}");
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        StringWriter output = new();
        StringWriter error = new();

        try
        {
            Console.SetOut(output);
            Console.SetError(error);

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Logging:File:Path"] = Path.Combine(directory, "devbuddy.log"),
                })
                .Build();

            ServiceCollection services = new();
            ConsoleLogging.Add(services, configuration);

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                Executed(
                    provider.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("Microsoft.EntityFrameworkCore.Database.Command"),
                    "select 1",
                    null);
            }

            Assert.Equal(string.Empty, output.ToString());
            Assert.Contains("Executed DbCommand", error.ToString(), StringComparison.Ordinal);

            // The file still gets it: this moves the console half, it does not turn logging off.
            string written = File.ReadAllText(Assert.Single(Directory.GetFiles(directory)));
            Assert.Contains("Executed DbCommand", written, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConsoleWriters
{
    public const string Name = "Console writers";
}
