using DevBuddy.Application;
using DevBuddy.Application.Dispatch;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Application.UseCases.Handover;
using DevBuddy.Application.UseCases.Lifecycle;
using DevBuddy.Application.UseCases.Reading;
using DevBuddy.Application.UseCases.Safety;
using DevBuddy.Application.UseCases.Sources;
using Microsoft.Extensions.DependencyInjection;

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

        services.AddScoped<SearchKnowledgeUseCase>();
        services.AddScoped<GetRecordUseCase>();
        services.AddScoped<GetWorkItemUseCase>();
        services.AddScoped<ListProjectsUseCase>();
        services.AddScoped<ViewRecordHistoryUseCase>();
        services.AddScoped<CompareSnapshotsUseCase>();
        services.AddScoped<DownloadEvidenceUseCase>();

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
        services.AddScoped<RestoreSystemUseCase>();
        services.AddScoped<CheckSystemHealthUseCase>();

        services.AddScoped<GrantMembershipUseCase>();
        services.AddScoped<RevokeMembershipUseCase>();
        services.AddScoped<EnableProjectAiAccessUseCase>();
        services.AddScoped<DisableProjectAiAccessUseCase>();
        services.AddScoped<ReadAuditHistoryUseCase>();

        services.AddScoped(BuildDispatcher);
        return services;
    }

    private static OperationDispatcher BuildDispatcher(IServiceProvider services)
    {
        UseCaseExecutor executor = services.GetRequiredService<UseCaseExecutor>();

        return new OperationDispatcher(
        [
            Bind(services.GetRequiredService<SearchKnowledgeUseCase>(), executor),
            Bind(services.GetRequiredService<GetRecordUseCase>(), executor),
            Bind(services.GetRequiredService<GetWorkItemUseCase>(), executor),
            Bind(services.GetRequiredService<ListProjectsUseCase>(), executor),
            Bind(services.GetRequiredService<ViewRecordHistoryUseCase>(), executor),
            Bind(services.GetRequiredService<CompareSnapshotsUseCase>(), executor),

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
            Bind(services.GetRequiredService<RestoreSystemUseCase>(), executor),
            Bind(services.GetRequiredService<CheckSystemHealthUseCase>(), executor),

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
