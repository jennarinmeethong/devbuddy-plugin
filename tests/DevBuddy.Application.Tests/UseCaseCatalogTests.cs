using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The catalogue is the answer to "what can AI reach". These tests pin that answer so it cannot
/// drift, and check that a use case cannot claim to redact its output without a response type
/// that actually can.
/// </summary>
public sealed class UseCaseCatalogTests
{
    /// <summary>
    /// Exactly the categories info.md permits: search, get, analyse, create a draft, and generate
    /// a handover. Written out rather than computed, so widening the AI surface means editing a
    /// test that says what it is doing.
    /// </summary>
    private static readonly string[] PermittedAiOperations =
    [
        "analyze_architecture",
        "analyze_change_impact",
        "analyze_code",
        "analyze_documents",
        "analyze_git_history",
        "analyze_project",
        "analyze_test_evidence",
        "analyze_work_items",
        "compare_snapshots",
        "create_draft",
        "find_missing_evidence",
        "find_open_questions",
        "generate_handover",
        "get_record",
        "get_work_item",
        "list_projects",
        "search_knowledge",

        // Added 2026-09-11 with the derived vector index (ADR-0012). Search is one of the five
        // categories info.md permits, and this is search: it needs the same ReadKnowledge
        // permission search_knowledge does, so the surface grows by a tool and not by a privilege.
        // What is new is that the query text reaches an embedding provider, which on a hosted mode
        // is outside the boundary — so the request declares itself scannable and SB-17 refuses a
        // secret before it leaves.
        "search_similar_records",

        "view_record_history",
    ];

    [Fact]
    public void the_ai_exposed_set_is_exactly_what_info_md_permits()
    {
        string[] exposed =
        [
            .. UseCaseCatalog.AiExposed.Select(descriptor => descriptor.Name).Order(StringComparer.Ordinal)
        ];

        Assert.Equal(PermittedAiOperations, exposed);
    }

    [Fact]
    public void every_human_gated_operation_is_absent_from_the_ai_surface()
    {
        string[] humanOnly =
        [
            "download_evidence",
            "approve_record", "publish_record", "request_correction", "archive_record",
            "sync_sources", "reindex", "detect_secrets", "redact_sensitive_data",
            "backup_system", "read_audit_history",
            "grant_membership", "revoke_membership",
            "enable_project_ai_access", "disable_project_ai_access",
        ];

        foreach (string name in humanOnly)
        {
            UseCaseDescriptor descriptor =
                Assert.Single(UseCaseCatalog.All, candidate => candidate.Name == name);

            Assert.Equal(AiExposure.Denied, descriptor.AiExposure);
            Assert.DoesNotContain(descriptor, UseCaseCatalog.AiExposed);
        }
    }

    [Fact]
    public void operation_names_are_unique_and_snake_case()
    {
        string[] names = [.. UseCaseCatalog.All.Select(descriptor => descriptor.Name)];

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
        Assert.All(names, name => Assert.Matches("^[a-z][a-z0-9_]*$", name));
    }

    [Fact]
    public void the_catalogue_lists_every_use_case_exactly_once()
    {
        var registry = new UseCaseRegistry(new FakePorts());

        string[] fromUseCases =
            [.. registry.Entries.Select(entry => entry.Descriptor.Name).Order(StringComparer.Ordinal)];
        string[] fromCatalogue =
            [.. UseCaseCatalog.All.Select(descriptor => descriptor.Name).Order(StringComparer.Ordinal)];

        Assert.Equal(fromCatalogue, fromUseCases);
    }

    [Fact]
    public void a_use_case_that_declares_redaction_returns_a_response_that_can_redact_itself()
    {
        var registry = new UseCaseRegistry(new FakePorts());
        List<string> mismatches = [];

        foreach (RegisteredUseCase entry in registry.Entries)
        {
            Type responseType = ResponseTypeOf(entry.UseCaseType);
            Type expected = typeof(IRedactableResponse<>).MakeGenericType(responseType);
            bool canRedact = expected.IsAssignableFrom(responseType);

            if (entry.Descriptor.RedactsOutput && !canRedact)
            {
                mismatches.Add($"{entry.Descriptor.Name} declares redaction but {responseType.Name} cannot redact.");
            }

            if (!entry.Descriptor.RedactsOutput && canRedact)
            {
                mismatches.Add($"{entry.Descriptor.Name} returns a redactable {responseType.Name} but does not declare it.");
            }
        }

        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void every_ai_exposed_read_operation_redacts_its_output()
    {
        // create_draft is the exception and it is deliberate: it writes, and what it writes is
        // scanned on the way in rather than redacted on the way out.
        UseCaseDescriptor[] readOperations =
        [
            .. UseCaseCatalog.AiExposed.Where(descriptor => descriptor.Name != "create_draft"
                && descriptor.Name != "list_projects")
        ];

        Assert.All(readOperations, descriptor => Assert.True(
            descriptor.RedactsOutput,
            $"{descriptor.Name} returns content to AI and must pass through the redactor."));
    }

    private static Type ResponseTypeOf(Type useCaseType)
    {
        for (Type? current = useCaseType.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(UseCase<,>))
            {
                return current.GetGenericArguments()[1];
            }
        }

        throw new InvalidOperationException($"{useCaseType.Name} does not derive from UseCase.");
    }
}
