using DevBuddy.Application;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Evidence;
using DevBuddy.Application.UseCases.Handover;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Application.UseCases.Safety;
using DevBuddy.Application.UseCases.Sources;
using DevBuddy.Application.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DevBuddy.Infrastructure.Hosting;

/// <summary>
/// Registers the pipeline and every operation, and builds the dispatcher a host talks to.
/// <para>
/// This is the one place that knows the full set of operations. A use case missing from here is
/// missing from all three hosts at once, which is why a test compares this list against the
/// catalogue rather than trusting it.
/// </para>
/// </summary>
public static class DevBuddyOperations
{
    /// <summary>
    /// <c>download_evidence</c> returns a stream of bytes, so it has no place in a dispatcher
    /// whose contract is JSON in and JSON out. The API serves it from a dedicated route that
    /// still goes through the pipeline; it is denied to AI, so the MCP surface never wanted it.
    /// </summary>
    public static string StreamingOperation => UseCaseCatalog.DownloadEvidence.Name;

    public static IServiceCollection AddDevBuddyOperations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<UseCaseExecutor>();

        // The one door to an embedding provider, registered with the operations rather than with
        // the infrastructure adapters, because it is where the SB-17 scan and the channel check
        // live and a use case must be able to resolve it whether or not a provider exists. With
        // none registered it answers every call with a refusal naming that, which is what an
        // installation that configured no provider should get.
        services.AddScoped(provider => new EmbeddingGateway(
            provider.GetRequiredService<ISecretScanner>(),
            provider.GetService<IEmbeddingProvider>()));

        // TryAdd, so the real PostgreSQL adapter wins wherever persistence is registered — which
        // happens first, being an infrastructure concern. What this leaves is an operations-only
        // container still able to build its use cases, which is how the tool-surface suite reads
        // the AI allow-list without a database.
        services.TryAddScoped<IEmbeddingIndex, AbsentEmbeddingIndex>();

        services.AddScoped<SearchKnowledgeUseCase>();
        services.AddScoped<SearchSimilarRecordsUseCase>();
        services.AddScoped<GetRecordUseCase>();
        services.AddScoped<GetWorkItemUseCase>();
        services.AddScoped<ListProjectsUseCase>();
        services.AddScoped<ViewRecordHistoryUseCase>();
        services.AddScoped<CompareSnapshotsUseCase>();
        services.AddScoped<DownloadEvidenceUseCase>();
        services.AddScoped<CaptureEvidenceUseCase>();
        services.AddScoped<ListEvidenceUseCase>();

        services.AddScoped<AnalyzeProjectUseCase>();
        services.AddScoped<AnalyzeCodeUseCase>();
        services.AddScoped<AnalyzeDocumentsUseCase>();
        services.AddScoped<AnalyzeArchitectureUseCase>();
        services.AddScoped<AnalyzeGitHistoryUseCase>();
        services.AddScoped<AnalyzeWorkItemsUseCase>();
        services.AddScoped<AnalyzeTestEvidenceUseCase>();
        services.AddScoped<AnalyzeChangeImpactUseCase>();

        services.AddScoped<GenerateHandoverUseCase>();
        services.AddScoped<FindOpenQuestionsUseCase>();
        services.AddScoped<FindMissingEvidenceUseCase>();

        services.AddScoped<CreateDraftUseCase>();
        services.AddScoped<ReviseDraftUseCase>();
        services.AddScoped<SubmitForApprovalUseCase>();
        services.AddScoped<ApproveRecordUseCase>();
        services.AddScoped<RequestCorrectionUseCase>();
        services.AddScoped<PublishRecordUseCase>();
        services.AddScoped<ArchiveRecordUseCase>();

        services.AddScoped<SyncSourcesUseCase>();
        services.AddScoped<ValidateProvenanceUseCase>();
        services.AddScoped<DetectDuplicatesUseCase>();
        services.AddScoped<DetectStalenessUseCase>();
        services.AddScoped<ReindexUseCase>();

        services.AddScoped<DetectSecretsUseCase>();
        services.AddScoped<RedactSensitiveDataUseCase>();

        services.AddScoped<ExportProjectUseCase>();
        services.AddScoped<BackupSystemUseCase>();
        services.AddScoped<CheckSystemHealthUseCase>();

        services.AddScoped<GrantMembershipUseCase>();
        services.AddScoped<RevokeMembershipUseCase>();
        services.AddScoped<EnableProjectAiAccessUseCase>();
        services.AddScoped<DisableProjectAiAccessUseCase>();
        services.AddScoped<ReadAuditHistoryUseCase>();
        services.AddScoped<ListMembershipsUseCase>();

        services.AddScoped<CreateWorkspaceUseCase>();
        services.AddScoped<CreateProjectUseCase>();
        services.AddScoped<DeleteProjectUseCase>();
        services.AddScoped<CreateTeamUseCase>();
        services.AddScoped<RenameTeamUseCase>();
        services.AddScoped<DeleteTeamUseCase>();
        services.AddScoped<ListTeamsUseCase>();
        services.AddScoped<ListTeamMembersUseCase>();
        services.AddScoped<AddTeamMemberUseCase>();
        services.AddScoped<RemoveTeamMemberUseCase>();
        services.AddScoped<CreateWorkItemUseCase>();
        services.AddScoped<CreateUserAccountUseCase>();
        services.AddScoped<ListWorkItemsUseCase>();
        services.AddScoped<ListRecordsUseCase>();
        services.AddScoped<IssueMachineTokenUseCase>();
        services.AddScoped<ListMachineTokensUseCase>();
        services.AddScoped<RevokeMachineTokenUseCase>();

        services.AddScoped(BuildDispatcher);
        return services;
    }

    private static OperationDispatcher BuildDispatcher(IServiceProvider services)
    {
        UseCaseExecutor executor = services.GetRequiredService<UseCaseExecutor>();

        return new OperationDispatcher(
        [
            Bind(services.GetRequiredService<SearchKnowledgeUseCase>(), executor),
            Bind(services.GetRequiredService<SearchSimilarRecordsUseCase>(), executor),
            Bind(services.GetRequiredService<GetRecordUseCase>(), executor),
            Bind(services.GetRequiredService<GetWorkItemUseCase>(), executor),
            Bind(services.GetRequiredService<ListProjectsUseCase>(), executor),
            Bind(services.GetRequiredService<ViewRecordHistoryUseCase>(), executor),
            Bind(services.GetRequiredService<CompareSnapshotsUseCase>(), executor),

            // list_evidence is bound; capture and download are not, and that is the same decision
            // in both directions. Bytes do not belong in a JSON envelope — base64 inflates a file
            // by a third and puts it through the serialiser — so those two have streaming
            // endpoints that call the executor directly. Listing what exists is ordinary JSON.
            Bind(services.GetRequiredService<ListEvidenceUseCase>(), executor),

            Bind(services.GetRequiredService<AnalyzeProjectUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeCodeUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeDocumentsUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeArchitectureUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeGitHistoryUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeWorkItemsUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeTestEvidenceUseCase>(), executor),
            Bind(services.GetRequiredService<AnalyzeChangeImpactUseCase>(), executor),

            Bind(services.GetRequiredService<GenerateHandoverUseCase>(), executor),
            Bind(services.GetRequiredService<FindOpenQuestionsUseCase>(), executor),
            Bind(services.GetRequiredService<FindMissingEvidenceUseCase>(), executor),

            Bind(services.GetRequiredService<CreateDraftUseCase>(), executor),
            Bind(services.GetRequiredService<ReviseDraftUseCase>(), executor),
            Bind(services.GetRequiredService<SubmitForApprovalUseCase>(), executor),
            Bind(services.GetRequiredService<ApproveRecordUseCase>(), executor),
            Bind(services.GetRequiredService<RequestCorrectionUseCase>(), executor),
            Bind(services.GetRequiredService<PublishRecordUseCase>(), executor),
            Bind(services.GetRequiredService<ArchiveRecordUseCase>(), executor),

            Bind(services.GetRequiredService<SyncSourcesUseCase>(), executor),
            Bind(services.GetRequiredService<ValidateProvenanceUseCase>(), executor),
            Bind(services.GetRequiredService<DetectDuplicatesUseCase>(), executor),
            Bind(services.GetRequiredService<DetectStalenessUseCase>(), executor),
            Bind(services.GetRequiredService<ReindexUseCase>(), executor),

            Bind(services.GetRequiredService<DetectSecretsUseCase>(), executor),
            Bind(services.GetRequiredService<RedactSensitiveDataUseCase>(), executor),

            Bind(services.GetRequiredService<ExportProjectUseCase>(), executor),
            Bind(services.GetRequiredService<BackupSystemUseCase>(), executor),
            Bind(services.GetRequiredService<CheckSystemHealthUseCase>(), executor),

            Bind(services.GetRequiredService<ListMembershipsUseCase>(), executor),
            Bind(services.GetRequiredService<CreateWorkspaceUseCase>(), executor),
            Bind(services.GetRequiredService<CreateProjectUseCase>(), executor),
            Bind(services.GetRequiredService<DeleteProjectUseCase>(), executor),
            Bind(services.GetRequiredService<CreateTeamUseCase>(), executor),
            Bind(services.GetRequiredService<RenameTeamUseCase>(), executor),
            Bind(services.GetRequiredService<DeleteTeamUseCase>(), executor),
            Bind(services.GetRequiredService<ListTeamsUseCase>(), executor),
            Bind(services.GetRequiredService<ListTeamMembersUseCase>(), executor),
            Bind(services.GetRequiredService<AddTeamMemberUseCase>(), executor),
            Bind(services.GetRequiredService<RemoveTeamMemberUseCase>(), executor),
            Bind(services.GetRequiredService<CreateWorkItemUseCase>(), executor),
            Bind(services.GetRequiredService<CreateUserAccountUseCase>(), executor),
            Bind(services.GetRequiredService<ListWorkItemsUseCase>(), executor),
            Bind(services.GetRequiredService<ListRecordsUseCase>(), executor),
            Bind(services.GetRequiredService<IssueMachineTokenUseCase>(), executor),
            Bind(services.GetRequiredService<ListMachineTokensUseCase>(), executor),
            Bind(services.GetRequiredService<RevokeMachineTokenUseCase>(), executor),

            Bind(services.GetRequiredService<GrantMembershipUseCase>(), executor),
            Bind(services.GetRequiredService<RevokeMembershipUseCase>(), executor),
            Bind(services.GetRequiredService<EnableProjectAiAccessUseCase>(), executor),
            Bind(services.GetRequiredService<DisableProjectAiAccessUseCase>(), executor),
            Bind(services.GetRequiredService<ReadAuditHistoryUseCase>(), executor),
        ]);
    }

    private static OperationBinding Bind<TRequest, TResponse>(
        UseCase<TRequest, TResponse> useCase, UseCaseExecutor executor)
        where TRequest : IUseCaseRequest =>
        OperationBinding.For(useCase, executor);
}
