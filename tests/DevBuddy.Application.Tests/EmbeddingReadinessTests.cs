using DevBuddy.Application.Workers;
using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The lines an installation's embedding approval is written from (Phase 13, B7). Each case is a
/// thing an approver must be told about, and whether it stands in the way.
/// </summary>
public sealed class EmbeddingReadinessTests
{
    private static readonly EmbeddingFacts Ready = new(
        "SelfHosted", "qwen3-embedding:0.6b", 1024, "OpenAiCompatible", LeavesTheBoundary: false,
        IndexAvailable: true, WorkerTokenPresent: true, WorkerTokenResolves: true, [Role.Viewer], Budget: 50);

    [Fact]
    public void a_ready_self_hosted_installation_has_no_problem()
    {
        IReadOnlyList<ReadinessLine> lines = EmbeddingReadiness.Evaluate(Ready);

        Assert.DoesNotContain(lines, line => line.Problem);
        Assert.DoesNotContain(lines, line => line.Check == "egress");
    }

    [Fact]
    public void embeddings_off_is_reported_and_is_not_a_problem()
    {
        IReadOnlyList<ReadinessLine> lines = EmbeddingReadiness.Evaluate(Ready with { Provider = "None" });

        ReadinessLine only = Assert.Single(lines);
        Assert.False(only.Problem);
    }

    [Fact]
    public void each_thing_that_stands_in_the_way_is_a_problem()
    {
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { IndexAvailable = false }), line => line.Check == "vector index" && line.Problem);
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { WorkerTokenPresent = false }), line => line.Check == "worker token" && line.Problem);
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { WorkerTokenResolves = false }), line => line.Check == "worker token" && line.Problem);
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { WorkerRoles = [] }), line => line.Check == "worker token" && line.Problem);
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { Dimensions = 0 }), line => line.Check == "provider" && line.Problem);
        Assert.Contains(EmbeddingReadiness.Evaluate(Ready with { Budget = -1 }), line => line.Check == "budget" && line.Problem);
    }

    [Fact]
    public void a_worker_token_that_reaches_further_than_reading_is_a_problem()
    {
        Assert.Contains(
            EmbeddingReadiness.Evaluate(Ready with { WorkerRoles = [Role.Administrator] }),
            line => line.Check == "worker token" && line.Problem);
        Assert.Contains(
            EmbeddingReadiness.Evaluate(Ready with { WorkerRoles = [Role.Viewer, Role.Contributor] }),
            line => line.Check == "worker token" && line.Problem);
    }

    [Fact]
    public void the_hosted_mode_is_named_as_egress_for_the_approver()
    {
        ReadinessLine egress = Assert.Single(
            EmbeddingReadiness.Evaluate(Ready with { Provider = "HostedApi", LeavesTheBoundary = true }),
            line => line.Check == "egress");

        Assert.False(egress.Problem);
        Assert.Contains("acceptance", egress.Finding, StringComparison.Ordinal);
    }
}
