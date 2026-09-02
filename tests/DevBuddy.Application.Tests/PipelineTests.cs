using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Domain.Auditing;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Knowledge;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The stages themselves: what runs, in what order, and what each one does to the result.
/// </summary>
public sealed class PipelineTests
{
    [Fact]
    public async Task validation_runs_before_identity_and_before_authorization()
    {
        var harness = new Harness();
        var useCase = new SearchKnowledgeUseCase(harness.Ports);

        UseCaseResult<SearchKnowledgeResponse> result = await harness.RunAsync(
            useCase, new SearchKnowledgeRequest(TestData.Scope, QueryText: "   "), TestData.Anonymous);

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
        Assert.Contains("query text", string.Join(" ", result.ValidationErrors), StringComparison.OrdinalIgnoreCase);

        // Nothing was authorised and nothing was read, so nothing is audited either.
        Assert.Empty(harness.Authorization.Requests);
        Assert.Empty(harness.Audit.Entries);
        Assert.Equal(0, harness.Ports.Interactions);
    }

    [Fact]
    public async Task an_unbounded_result_count_is_refused()
    {
        var harness = new Harness();
        var useCase = new SearchKnowledgeUseCase(harness.Ports);

        UseCaseResult<SearchKnowledgeResponse> result = await harness.RunAsync(
            useCase, new SearchKnowledgeRequest(TestData.Scope, "importer", MaxResults: 5000));

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
    }

    [Fact]
    public async Task outbound_free_text_passes_through_the_redactor()
    {
        var harness = new Harness();
        var useCase = new SearchKnowledgeUseCase(harness.Ports);

        SearchKnowledgeResponse response = await harness.SucceedAsync(
            useCase, new SearchKnowledgeRequest(TestData.Scope, "importer"));

        KnowledgeSearchHit hit = Assert.Single(response.Hits);
        Assert.DoesNotContain("SECRET", hit.Title, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET", hit.Snippet, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", hit.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public async Task identifiers_survive_redaction_intact()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("Body with SECRET inside.");
        var useCase = new GetRecordUseCase(harness.Ports);

        KnowledgeRecordView view = await harness.SucceedAsync(
            useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        // Redaction touches prose, not identity. A pipeline that redacted every string would
        // make records unciteable.
        Assert.Equal(harness.Ports.Record.Id, view.RecordId);
        Assert.Equal(1, view.RevisionNumber);
        Assert.DoesNotContain("SECRET", view.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task a_successful_run_audits_the_action_its_descriptor_declares()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();
        var useCase = new GetRecordUseCase(harness.Ports);

        await harness.SucceedAsync(useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditAction.RecordViewed, entry.Action);
        Assert.Equal(AuditOutcome.Succeeded, entry.Outcome);
        Assert.Equal(TestData.Author, entry.ActorId);
        Assert.Equal(TestData.ProjectAlpha, entry.ProjectId);
        Assert.Equal(TestData.Workspace, entry.WorkspaceId);
    }

    [Fact]
    public async Task the_audit_entry_names_the_operation_and_resource_but_never_the_content()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft("A body containing SECRET material.");
        var useCase = new GetRecordUseCase(harness.Ports);

        await harness.SucceedAsync(useCase, new GetRecordRequest(TestData.Scope, harness.Ports.Record.Id));

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.StartsWith("get_record:", entry.ResourceReference, StringComparison.Ordinal);
        Assert.Contains(harness.Ports.Record.Id.ToString(), entry.ResourceReference, StringComparison.Ordinal);

        // Control SB-19: the audit store records that access happened, not what was accessed.
        Assert.DoesNotContain("SECRET", entry.ResourceReference, StringComparison.Ordinal);
        Assert.DoesNotContain("body", entry.ResourceReference, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task a_workspace_level_operation_audits_without_a_project()
    {
        var harness = new Harness();
        var useCase = new ListProjectsUseCase(harness.Ports, harness.Ports);

        await harness.SucceedAsync(useCase, new ListProjectsRequest(TestData.Workspace));

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(TestData.Workspace, entry.WorkspaceId);
        Assert.Null(entry.ProjectId);
    }

    [Fact]
    public async Task a_missing_resource_becomes_not_found_and_is_audited_as_failed()
    {
        var harness = new Harness();
        harness.Ports.Record = null;
        var useCase = new GetRecordUseCase(harness.Ports);

        UseCaseResult<KnowledgeRecordView> result = await harness.RunAsync(
            useCase, new GetRecordRequest(TestData.Scope, KnowledgeRecordId.New()));

        Assert.Equal(ExecutionOutcome.NotFound, result.Outcome);
        Assert.Null(result.Value);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(harness.Audit.Entries).Outcome);
    }

    [Fact]
    public async Task a_refused_domain_rule_becomes_rejected_rather_than_an_unhandled_error()
    {
        var harness = new Harness();
        harness.Ports.Record = TestData.NewDraft();
        var useCase = new PublishRecordUseCase(harness.Ports, harness.Ports);

        UseCaseResult<LifecycleResult> result = await harness.RunAsync(
            useCase, new RecordActionRequest(TestData.Scope, harness.Ports.Record.Id));

        // The aggregate refused: a draft cannot be published. The pipeline reports that as an
        // ordinary outcome the host can render, and audits the attempt.
        Assert.Equal(ExecutionOutcome.Rejected, result.Outcome);
        Assert.Contains("approved record", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(AuditOutcome.Failed, Assert.Single(harness.Audit.Entries).Outcome);
    }

    [Fact]
    public async Task the_authorization_request_carries_the_scope_the_caller_claimed()
    {
        var harness = new Harness();
        var useCase = new ExportProjectUseCase(harness.Ports);

        await harness.SucceedAsync(useCase, new ExportProjectRequest(TestData.Scope));

        // Claimed, not trusted. Verifying it is the job of the authorization implementation in
        // Phase 4; handing it over explicitly is what gives that implementation something to
        // check rather than a default it inherited.
        Security.AuthorizationRequest asked = Assert.Single(harness.Authorization.Requests);
        Assert.Equal(TestData.Workspace, asked.WorkspaceId);
        Assert.Equal(TestData.ProjectAlpha, asked.ProjectId);
    }

    [Fact]
    public async Task the_ai_channel_reaches_an_allowed_use_case_normally()
    {
        var harness = new Harness();
        var useCase = new SearchKnowledgeUseCase(harness.Ports);

        SearchKnowledgeResponse response = await harness.SucceedAsync(
            useCase, new SearchKnowledgeRequest(TestData.Scope, "importer"), TestData.Ai);

        // The structural check denies only what the catalogue denies. Search is permitted, and
        // the per-project AI policy is the authorization implementation concern (SB-08).
        Assert.NotEmpty(response.Hits);
        Assert.Single(harness.Authorization.Requests);
    }
}
