using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using ModelContextProtocol.Protocol;

namespace DevBuddy.McpServer;

/// <summary>
/// The tools this server offers, built from the operation catalogue and nothing else.
/// <para>
/// Control SB-07. The list is derived from <c>AiExposure.Allowed</c> at runtime, so an operation
/// that is not marked exposed is <b>absent</b> from the surface rather than present and refused.
/// A model cannot call a tool it was never told about, and a tool list is the only thing it has to
/// go on.
/// </para>
/// <para>
/// The same surface is served over both transports. There is no per-transport list to drift, and
/// the tool-surface test runs against this type rather than against either transport, so neither
/// can widen it.
/// </para>
/// </summary>
public static class ToolSurface
{
    /// <summary>
    /// One line per tool, describing what it does for the model that has to choose between them.
    /// <para>
    /// Kept here rather than in the catalogue because a description is a property of the AI-facing
    /// surface, not of the operation: the API and the console have no use for it. A test asserts
    /// every exposed operation has one, so the map cannot fall behind the catalogue.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, string> Descriptions { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["search_knowledge"] =
                "Full-text search over the knowledge records of one project, with optional filters "
                + "by record kind and status. Returns titles and snippets, not whole records.",
            ["get_record"] =
                "Reads one knowledge record. Returns the published revision unless a specific "
                + "revision number is asked for, so unapproved drafts are not served by accident.",
            ["get_work_item"] =
                "Reads work identity: type, title, goal, what is in scope, what is deliberately "
                + "excluded, and the stakeholders.",
            ["list_projects"] =
                "Lists the projects the requesting person can see. Only projects whose owner has "
                + "enabled AI access appear here.",
            ["view_record_history"] =
                "Every revision of one record, with its content hash and the approval that covers "
                + "it. Use this to see what was approved and what was not.",
            ["compare_snapshots"] =
                "Compares two source-system snapshots of a repository and reports which references "
                + "moved. Reports differences; does not resolve them.",
            ["analyze_project"] =
                "Read-only survey of a project working copy: file types, sizes, and manifests.",
            ["analyze_code"] =
                "Read-only survey of the source files in a project working copy.",
            ["analyze_documents"] =
                "Read-only survey of the documents in a project working copy.",
            ["analyze_architecture"] =
                "Read-only survey of declared dependencies between projects and packages.",
            ["analyze_git_history"] =
                "Reads git references from the working copy metadata. Commit history beyond the "
                + "references needs the source system.",
            ["analyze_work_items"] =
                "Summarises the work items in a project and how much published knowledge each one "
                + "carries.",
            ["analyze_test_evidence"] =
                "Finds test result files in a project working copy.",
            ["analyze_change_impact"] =
                "Given a commit or a range, reports the paths it touched and what they affect.",
            ["generate_handover"] =
                "Assembles a handover for one work item from its published records, with the open "
                + "questions and missing evidence alongside.",
            ["find_open_questions"] =
                "Everything about one work item that nobody has answered yet, including work whose "
                + "exclusions were never recorded.",
            ["find_missing_evidence"] =
                "Claims with nothing behind them, and evidence that exists but has not been scanned.",
            ["create_draft"] =
                "Creates a draft knowledge record. A draft is not visible to readers of published "
                + "knowledge until a person approves it. Provenance is required.",
        };

    /// <summary>
    /// The tools, in catalogue order, each with a schema generated from the request type it binds
    /// to. Ordering is stable so a client that caches the list sees the same thing every time.
    /// </summary>
    public static IReadOnlyList<Tool> Describe(OperationDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        List<Tool> tools = [];

        foreach (UseCaseDescriptor descriptor in dispatcher.AiOperations)
        {
            OperationBinding binding = dispatcher.Find(descriptor.Name)
                ?? throw new InvalidOperationException($"No binding for {descriptor.Name}.");

            tools.Add(new Tool
            {
                Name = descriptor.Name,
                Description = Descriptions.TryGetValue(descriptor.Name, out string? text)
                    ? text
                    : "No description is recorded for this operation.",
                InputSchema = OperationSchemas.For(binding.RequestType),
            });
        }

        return tools;
    }
}
