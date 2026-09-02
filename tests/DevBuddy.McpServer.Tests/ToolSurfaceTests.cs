using System.Text.Json;
using System.Text.RegularExpressions;
using DevBuddy.Application;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace DevBuddy.McpServer.Tests;

/// <summary>
/// Control SB-07 and the Phase 7 exit criterion: the exported tool list equals the allow-list
/// exactly.
/// <para>
/// Not "contains" and not "does not contain the bad ones" — equals. A subset check would pass
/// while a tool went missing, and a superset check would pass while one crept in. The whole point
/// of the surface is that it is a closed list somebody decided on.
/// </para>
/// </summary>
public sealed partial class ToolSurfaceTests : IDisposable
{
    /// <summary>
    /// The eighteen operations info.md permits: search, get, analyse, create a draft, and generate
    /// a handover. Written out here as well as in the catalogue on purpose. Two independent
    /// statements of the same list mean a change has to be made twice, deliberately, and cannot
    /// happen as a side effect of adding a use case.
    /// </summary>
    private static readonly string[] Expected =
    [
        "search_knowledge",
        "get_record",
        "get_work_item",
        "list_projects",
        "view_record_history",
        "compare_snapshots",
        "analyze_project",
        "analyze_code",
        "analyze_documents",
        "analyze_architecture",
        "analyze_git_history",
        "analyze_work_items",
        "analyze_test_evidence",
        "analyze_change_impact",
        "generate_handover",
        "find_open_questions",
        "find_missing_evidence",
        "create_draft",
    ];

    private readonly ServiceProvider _host = NullPorts.BuildHost();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void the_exported_tool_list_equals_the_allow_list_exactly()
    {
        string[] exported = [.. Surface().Select(tool => tool.Name)];

        Assert.Equal(Expected, exported);
    }

    [Fact]
    public void the_exported_list_matches_the_catalogue_it_is_derived_from()
    {
        string[] fromCatalogue = [.. UseCaseCatalog.AiExposed.Select(descriptor => descriptor.Name)];
        string[] exported = [.. Surface().Select(tool => tool.Name)];

        Assert.Equal(fromCatalogue.Order(StringComparer.Ordinal), exported.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void no_human_gated_operation_appears_on_the_surface()
    {
        HashSet<string> exported = [.. Surface().Select(tool => tool.Name)];

        UseCaseDescriptor[] denied =
        [
            .. UseCaseCatalog.All.Where(descriptor => descriptor.AiExposure == AiExposure.Denied)
        ];

        Assert.NotEmpty(denied);

        // Absent, not merely refused. A model cannot call a tool it was never told about, and the
        // tool list is the only thing it has to go on.
        foreach (UseCaseDescriptor descriptor in denied)
        {
            Assert.DoesNotContain(descriptor.Name, exported);
        }
    }

    [Fact]
    public async Task calling_a_human_gated_operation_by_name_is_answered_as_unknown()
    {
        var handlers = new McpToolHandlers(Dispatcher(), AiCaller());

        CallToolResult result = await handlers.CallToolAsync(
            new CallToolRequestParams { Name = "publish_record" }, CancellationToken.None);

        // The same answer an operation that does not exist would get. Telling an AI caller that
        // publish_record is real but off limits is a disclosure that costs nothing to avoid.
        Assert.True(result.IsError);
        Assert.Contains("Not found", Rendered(result), StringComparison.Ordinal);
        Assert.DoesNotContain("permission", Rendered(result), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task an_operation_nobody_defined_is_answered_the_same_way()
    {
        var handlers = new McpToolHandlers(Dispatcher(), AiCaller());

        CallToolResult invented = await handlers.CallToolAsync(
            new CallToolRequestParams { Name = "exfiltrate_everything" }, CancellationToken.None);

        CallToolResult denied = await handlers.CallToolAsync(
            new CallToolRequestParams { Name = "backup_system" }, CancellationToken.None);

        Assert.True(invented.IsError);
        Assert.True(denied.IsError);
        Assert.Equal(Rendered(invented).Split(':')[0], Rendered(denied).Split(':')[0]);
    }

    [Fact]
    public void every_tool_carries_a_description_and_an_argument_schema()
    {
        foreach (Tool tool in Surface())
        {
            Assert.False(
                string.IsNullOrWhiteSpace(tool.Description),
                $"{tool.Name} has no description. A model choosing between tools has only this.");

            Assert.Equal(JsonValueKind.Object, tool.InputSchema.ValueKind);
            Assert.True(tool.InputSchema.TryGetProperty("type", out _), $"{tool.Name} has no schema type.");
        }
    }

    [Fact]
    public void the_description_map_covers_the_surface_and_nothing_else()
    {
        string[] described = [.. ToolSurface.Descriptions.Keys.Order(StringComparer.Ordinal)];

        // A description left behind for an operation that is no longer exposed is a small lie
        // about the surface, so the map has to match it in both directions.
        Assert.Equal(Expected.Order(StringComparer.Ordinal), described);
    }

    [Fact]
    public void the_tool_list_is_built_in_one_place_so_no_transport_can_grow_its_own()
    {
        var host = new DirectoryInfo(
            Path.Combine(RepositoryRoot().FullName, "src", "hosts", "DevBuddy.McpServer"));

        List<string> constructors = [];

        foreach (FileInfo file in host.EnumerateFiles("*.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            if (NewTool().IsMatch(File.ReadAllText(file.FullName)))
            {
                constructors.Add(file.Name);
            }
        }

        // The stdio and HTTP transports share one surface because there is only one place that can
        // make a Tool. A per-transport list is the exact drift SB-07 is about.
        Assert.Equal(["ToolSurface.cs"], constructors);
    }

    [Fact]
    public void the_surface_is_stable_across_calls()
    {
        string[] first = [.. Surface().Select(tool => tool.Name)];
        string[] second = [.. Surface().Select(tool => tool.Name)];

        Assert.Equal(first, second);
    }

    private static CallerContext AiCaller() =>
        new(new UserId(Guid.NewGuid()), AccessChannel.Ai, "tool-surface-test");

    private static string Rendered(CallToolResult result) =>
        string.Join(" ", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory
            ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    private OperationDispatcher Dispatcher() =>
        _host.CreateScope().ServiceProvider.GetRequiredService<OperationDispatcher>();

    private IReadOnlyList<Tool> Surface() => ToolSurface.Describe(Dispatcher());

    [GeneratedRegex(@"new\s+Tool\s*[({]")]
    private static partial Regex NewTool();
}
