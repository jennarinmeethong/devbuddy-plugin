using DevBuddy.Infrastructure.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DevBuddy.Cli;

/// <summary>
/// Where the console's own logs go.
/// </summary>
public static class ConsoleLogging
{
    /// <summary>
    /// The file sink when one is configured, and standard error for the console half of it.
    /// <para>
    /// Standard output is the console's answer: the JSON a <c>run</c> prints, the names
    /// <c>operations</c> lists. With a log path set, every statement EF executed was written there
    /// first, so nothing reading that output could parse it — the installation's own scripts had
    /// to cut the JSON out with <c>sed</c>. The MCP server under stdio has always sent its logs to
    /// standard error for the same reason.
    /// </para>
    /// <para>
    /// Off unless Logging:File:Path is set. This is the host that runs the retention sweep, so it
    /// needs the options even when it is not the one writing the files.
    /// </para>
    /// </summary>
    public static void Add(IServiceCollection services, IConfiguration configuration) =>
        services.AddDevBuddyFileLogging(configuration, logToStandardError: true);
}
