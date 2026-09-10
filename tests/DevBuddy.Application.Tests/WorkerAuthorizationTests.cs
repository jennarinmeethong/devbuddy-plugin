using System.Reflection;
using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Security;
using DevBuddy.Application.Workers;
using DevBuddy.Domain.Common;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The authorization skeleton for autonomous background work (ADR-0013, confirmed in `info.md` on
/// 2026-09-10).
/// <para>
/// The design problem is the one that deleted <c>restore_system</c> as an operation and kept
/// <c>retention</c> outside the pipeline: a background job has no caller to authorise it against.
/// Two answers are sound — hold a real credential and be bounded by it, or hold none and touch
/// nothing a permission gates — and everything between them is the installation-wide superuser
/// this system has never had, running unattended.
/// </para>
/// <para>
/// These tests are what makes the second shape's boundary real. The first shape is enforced by
/// construction: <see cref="WorkerCaller"/> has a private constructor and one factory that takes
/// a resolved token, so a caller nobody granted cannot be written down.
/// </para>
/// </summary>
public sealed class WorkerAuthorizationTests
{
    /// <summary>
    /// Every dependency an <see cref="InstallationWorkerJob"/> is allowed to take.
    /// <para>
    /// An allow-list, not a deny-list, and for the same reason the AI tool surface is one: a new
    /// port added next year is refused by default rather than admitted by an omission nobody
    /// noticed. Adding a line here is a deliberate act with a diff, and the thing to ask of any
    /// candidate is whether a person's permissions gate what it reaches.
    /// </para>
    /// </summary>
    private static readonly string[] InstallationScopePorts =
    [
        // The retention sweep: installation-wide by nature, no caller, already outside the
        // pipeline for exactly this reason.
        "IRetentionEnforcer",

        // Time, and the budget the run was given. Neither reaches anything.
        "IClock",
        "WorkerBudget",
    ];

    [Fact]
    public void a_worker_caller_cannot_be_built_without_a_resolved_token()
    {
        // The only public way in. No constructor, and no factory taking a user or a workspace:
        // a background job whose identity came from a configuration file is the defect this
        // prevents, and it prevents it at compile time rather than in a review.
        ConstructorInfo[] publicConstructors = typeof(WorkerCaller)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        Assert.Empty(publicConstructors);

        MethodInfo[] factories = [.. typeof(WorkerCaller)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(WorkerCaller))];

        MethodInfo only = Assert.Single(factories);
        Assert.Equal(nameof(WorkerCaller.FromMachineToken), only.Name);
        Assert.Equal(typeof(MachineTokenIdentity), only.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void a_token_that_resolved_to_nobody_refuses_the_run_rather_than_running_as_nobody()
    {
        // Unknown, revoked, expired, or issued before tokens carried a workspace all arrive here
        // as null. The answer is no run — never an anonymous or elevated fallback.
        Assert.Null(WorkerCaller.FromMachineToken(null, "run-1", WorkerModelUse.None));
        Assert.Null(WorkerCaller.FromMachineToken(
            null, "run-1", WorkerModelUse.SendsContentToAModel));
    }

    [Fact]
    public void a_worker_caller_carries_the_credential_ceiling_from_its_token()
    {
        MachineTokenIdentity identity = Token();

        WorkerCaller caller = WorkerCaller.FromMachineToken(
            identity, "run-1", WorkerModelUse.SendsContentToAModel)!;

        Assert.Equal(identity.UserId, caller.Context.UserId);
        Assert.NotNull(caller.Context.Credential);
        Assert.Equal(identity.TokenId, caller.Context.Credential!.TokenId);
        Assert.Equal(identity.WorkspaceId, caller.Context.Credential.WorkspaceId);
        Assert.Equal(identity.WorkspaceId, caller.Workspace);
    }

    /// <summary>
    /// The channel follows whether the job touches a model, and this is the sharpest thing in the
    /// skeleton.
    /// <para>
    /// A job that sends content to a model runs on <see cref="AccessChannel.Ai"/>, because that is
    /// where the per-project AI access policy and the SB-18 redaction are attached — the executor
    /// applies both on the strength of the channel alone. A worker on
    /// <see cref="AccessChannel.InternalSystem"/> could otherwise read a project whose AI access
    /// nobody enabled and hand the contents to a model, through an authorization service answering
    /// every question correctly, because the membership behind it is real.
    /// </para>
    /// <para>
    /// The other direction matters just as much and is why this is not simply "workers are AI".
    /// The AI channel is an allow-list of eighteen operations, and every feature ADR-0013 proposed
    /// — stale-record detection, source analysis, reindexing — needs a permission that is
    /// <c>AiExposure.Denied</c>. A worker pinned to the AI channel could not do the work it exists
    /// for.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(WorkerModelUse.SendsContentToAModel, AccessChannel.Ai)]
    [InlineData(WorkerModelUse.None, AccessChannel.InternalSystem)]
    public void the_channel_follows_what_the_job_declared_about_models(
        WorkerModelUse modelUse, AccessChannel expected)
    {
        WorkerCaller caller = WorkerCaller.FromMachineToken(Token(), "run-1", modelUse)!;

        Assert.Equal(expected, caller.Context.Channel);
    }

    [Fact]
    public void a_worker_cannot_ask_for_a_channel_directly()
    {
        // The declaration is the only input. A channel parameter is the mistake this asserts the
        // absence of, and the declaration lives on the job type so it cannot vary per run.
        Assert.DoesNotContain(
            typeof(WorkerCaller)
                .GetMethod(nameof(WorkerCaller.FromMachineToken))!
                .GetParameters()
                .Select(parameter => parameter.ParameterType),
            type => type == typeof(AccessChannel));

        Assert.NotNull(typeof(CallerBoundWorkerJob).GetProperty(nameof(CallerBoundWorkerJob.ModelUse)));
    }

    /// <summary>
    /// A job that declared it touches no model, and then reaches for one. The declaration bought
    /// it the human-only operations its membership carries; it does not also buy it the model, and
    /// the gateway is where that is enforced rather than trusted.
    /// </summary>
    [Fact]
    public async Task a_job_that_declared_no_model_use_is_refused_the_embedding_provider()
    {
        WorkerCaller caller = WorkerCaller.FromMachineToken(Token(), "run-1", WorkerModelUse.None)!;
        EmbeddingGateway gateway = new(new CleanScanner(), new CountingProvider());

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            caller, ["anything at all"], new WorkerBudget(10), CancellationToken.None);

        Assert.True(outcome.Refused);
        Assert.Contains("AI channel", outcome.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// Shape two's reach, enforced by allow-list. This is the test that would fail if somebody
    /// gave an unattended job the knowledge repository.
    /// </summary>
    [Fact]
    public void an_installation_job_depends_on_nothing_a_permission_gates()
    {
        List<string> findings = [];

        foreach (Type job in InstallationJobs())
        {
            foreach (ConstructorInfo constructor in job.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    string name = parameter.ParameterType.Name;

                    if (!InstallationScopePorts.Contains(name, StringComparer.Ordinal))
                    {
                        findings.Add($"{job.Name} takes {name}");
                    }
                }
            }
        }

        Assert.True(
            findings.Count == 0,
            "An InstallationWorkerJob runs with no caller, so it may depend only on "
            + "installation-scope ports. Add a port to InstallationScopePorts on purpose, after "
            + "asking whether a person's permissions gate what it reaches. Found: "
            + string.Join("; ", findings));
    }

    /// <summary>
    /// The mutation check. A rule nobody has watched fail is a rule that might not work, so this
    /// hands the same inspection a job that breaks it and asserts it is caught.
    /// </summary>
    [Fact]
    public void the_allow_list_actually_catches_a_job_that_reaches_too_far()
    {
        ParameterInfo overreaching = typeof(SweepThatReadsKnowledge)
            .GetConstructors()[0]
            .GetParameters()[0];

        Assert.DoesNotContain(
            overreaching.ParameterType.Name, InstallationScopePorts, StringComparer.Ordinal);

        // And it is a real InstallationWorkerJob, so the rule above would have found it had it
        // been declared in the product rather than here.
        Assert.True(typeof(InstallationWorkerJob).IsAssignableFrom(typeof(SweepThatReadsKnowledge)));
    }

    /// <summary>
    /// There is no third shape to inherit from. A middle ground would not be a compromise; it
    /// would be the superuser, and the way to keep it out is to leave nowhere to write it.
    /// </summary>
    [Fact]
    public void there_are_exactly_two_shapes_of_worker_job()
    {
        Type[] shapes = [.. typeof(IWorkerJob).Assembly
            .GetTypes()
            .Where(type => type.IsAbstract
                && !type.IsInterface
                && typeof(IWorkerJob).IsAssignableFrom(type))];

        Assert.Equal(
            ["CallerBoundWorkerJob", "InstallationWorkerJob"],
            shapes.Select(type => type.Name).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A caller-bound job takes its caller per run rather than holding one. A token resolves per
    /// run and can be revoked between two of them; a job that captured one at construction would
    /// keep working for the life of the process after somebody revoked it.
    /// </summary>
    [Fact]
    public void a_caller_bound_job_is_handed_its_caller_per_run()
    {
        MethodInfo run = typeof(CallerBoundWorkerJob).GetMethod(nameof(CallerBoundWorkerJob.RunAsync))!;

        Assert.Contains(
            run.GetParameters().Select(parameter => parameter.ParameterType),
            type => type == typeof(WorkerCaller));

        Assert.DoesNotContain(
            typeof(CallerBoundWorkerJob)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.PropertyType),
            type => type == typeof(WorkerCaller));
    }

    [Fact]
    public void an_installation_job_is_handed_no_caller_at_all()
    {
        MethodInfo run = typeof(InstallationWorkerJob).GetMethod(nameof(InstallationWorkerJob.RunAsync))!;

        Assert.DoesNotContain(
            run.GetParameters().Select(parameter => parameter.ParameterType),
            type => type == typeof(WorkerCaller) || type == typeof(CallerContext));
    }

    [Theory]
    [InlineData(0, 1, false)]
    [InlineData(1, 1, true)]
    [InlineData(5, 6, false)]
    [InlineData(5, 5, true)]
    public void a_budget_refuses_what_it_cannot_cover(int maximum, int requested, bool expected)
    {
        Assert.Equal(expected, new WorkerBudget(maximum).TrySpend(requested));
    }

    [Fact]
    public void a_refused_spend_takes_nothing()
    {
        // All or nothing. A partial spend would leave a run believing it had paid for work it
        // cannot do, and the last call of a batch is exactly where that would be wrong.
        WorkerBudget budget = new(3);

        Assert.True(budget.TrySpend(2));
        Assert.False(budget.TrySpend(2));
        Assert.Equal(2, budget.Spent);
        Assert.Equal(1, budget.Remaining);
    }

    [Fact]
    public void an_exhausted_budget_stays_exhausted()
    {
        WorkerBudget budget = new(1);

        Assert.True(budget.TrySpend());
        Assert.True(budget.IsExhausted);
        Assert.False(budget.TrySpend());
        Assert.Equal(0, budget.Remaining);
    }

    /// <summary>
    /// Zero is how an operator switches a worker off without removing it, so it has to be a
    /// legal budget rather than a validation error.
    /// </summary>
    [Fact]
    public void a_budget_of_zero_is_a_switch_and_not_a_mistake()
    {
        WorkerBudget budget = new(0);

        Assert.True(budget.IsExhausted);
        Assert.False(budget.TrySpend());
    }

    [Fact]
    public void a_negative_budget_is_refused_rather_than_read_as_zero()
    {
        // A mistake that silently became zero would look like a working switch.
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkerBudget(-1));
    }

    [Fact]
    public void a_refused_run_is_an_outcome_and_not_a_failure()
    {
        WorkerRunReport report = WorkerRunReport.RefusedRun("stale-records", "the budget is spent");

        Assert.True(report.Refused);
        Assert.Equal(0, report.CallsSpent);
        Assert.Equal("the budget is spent", report.Reason);
    }

    /// <summary>
    /// The default, and the whole of what an installation that chose no provider gets: a refusal
    /// that says so, never a silent no-op that leaves a caller believing an index exists.
    /// </summary>
    [Fact]
    public async Task with_no_provider_configured_the_gateway_refuses_and_says_why()
    {
        EmbeddingGateway gateway = new(new CleanScanner());

        Assert.False(gateway.IsConfigured);
        Assert.Null(gateway.ProviderDescription);

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(), ["some text"], new WorkerBudget(1), CancellationToken.None);

        Assert.True(outcome.Refused);
        Assert.Contains("no embedding provider", outcome.Reason!, StringComparison.Ordinal);
        Assert.Contains("Full-text search is unaffected", outcome.Reason!, StringComparison.Ordinal);
    }

    /// <summary>
    /// ADR-0012's first point, as an assertion: embedding text is egress, so SB-17 applies
    /// <b>before</b> it leaves. A vector cannot be scanned after the fact, which is why scanning
    /// the response would be scanning the wrong copy.
    /// </summary>
    [Fact]
    public async Task a_secret_is_refused_before_anything_is_sent()
    {
        CountingProvider provider = new();
        EmbeddingGateway gateway = new(new ScannerThatFinds("connection-string"), provider);

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(),
            ["Host=db;Username=devbuddy;Password=hunter2"],
            new WorkerBudget(10),
            CancellationToken.None);

        Assert.True(outcome.BlockedBySecretScan);
        Assert.True(outcome.Refused);
        Assert.Equal(["connection-string"], outcome.BlockingRules);

        // The part that matters. Nothing left the boundary.
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task a_blocked_scan_names_its_rules_and_never_the_matched_text()
    {
        EmbeddingGateway gateway = new(
            new ScannerThatFinds("assigned-secret"), new CountingProvider());

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(),
            ["AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMIK7MDENG"],
            new WorkerBudget(2),
            CancellationToken.None);

        Assert.Contains("assigned-secret", outcome.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("wJalrXUtnFEMIK7MDENG", outcome.Reason!, StringComparison.Ordinal);
    }

    /// <summary>A refused scan costs nothing: the budget is spent after the scan, not before.</summary>
    [Fact]
    public async Task a_blocked_scan_does_not_spend_the_budget()
    {
        WorkerBudget budget = new(1);

        EmbeddingGateway gateway = new(
            new ScannerThatFinds("high-entropy-string"), new CountingProvider());

        await gateway.EmbedAsync(AiCaller(), ["nope"], budget, CancellationToken.None);

        Assert.Equal(0, budget.Spent);
        Assert.Equal(1, budget.Remaining);
    }

    [Fact]
    public async Task an_exhausted_budget_refuses_the_call_rather_than_making_it()
    {
        CountingProvider provider = new();
        EmbeddingGateway gateway = new(new CleanScanner(), provider);
        WorkerBudget budget = new(1);

        EmbeddingOutcome first = await gateway.EmbedAsync(
            AiCaller(), ["one"], budget, CancellationToken.None);

        EmbeddingOutcome second = await gateway.EmbedAsync(
            AiCaller(), ["two"], budget, CancellationToken.None);

        Assert.False(first.Refused);
        Assert.True(second.Refused);
        Assert.Contains("budget", second.Reason!, StringComparison.Ordinal);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>
    /// A provider that answered short dropped an input, and an index built from that is misaligned
    /// against the records it claims to describe: every row after the gap describes the wrong
    /// thing, and nothing about it looks wrong. Refused rather than trimmed.
    /// </summary>
    [Fact]
    public async Task a_provider_that_returns_too_few_vectors_is_refused()
    {
        EmbeddingGateway gateway = new(new CleanScanner(), new ShortProvider());

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(), ["one", "two", "three"], new WorkerBudget(5), CancellationToken.None);

        Assert.True(outcome.Refused);
        Assert.Contains("misaligned", outcome.Reason!, StringComparison.Ordinal);
        Assert.Empty(outcome.Vectors);
    }

    [Fact]
    public async Task an_empty_request_embeds_nothing_and_costs_nothing()
    {
        CountingProvider provider = new();
        WorkerBudget budget = new(3);
        EmbeddingGateway gateway = new(new CleanScanner(), provider);

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(), [], budget, CancellationToken.None);

        Assert.False(outcome.Refused);
        Assert.Empty(outcome.Vectors);
        Assert.Equal(0, provider.Calls);
        Assert.Equal(3, budget.Remaining);
    }

    [Fact]
    public async Task a_clean_batch_comes_back_one_vector_per_text()
    {
        EmbeddingGateway gateway = new(new CleanScanner(), new CountingProvider());

        EmbeddingOutcome outcome = await gateway.EmbedAsync(
            AiCaller(), ["a", "b", "c"], new WorkerBudget(5), CancellationToken.None);

        Assert.False(outcome.Refused);
        Assert.Equal(3, outcome.Vectors.Count);
    }

    /// <summary>
    /// The gateway is the only door. Anything else in the product holding
    /// <see cref="IEmbeddingProvider"/> would be a second place for the scan and the channel check
    /// to be missing, which is the same defect as a host reaching past the use-case pipeline.
    /// </summary>
    [Fact]
    public void nothing_but_the_gateway_reaches_the_embedding_provider()
    {
        string obj = string.Concat(SEPARATOR, "obj", SEPARATOR);
        string bin = string.Concat(SEPARATOR, "bin", SEPARATOR);

        string[] offenders =
        [
            .. Directory
                .EnumerateFiles(
                    Path.Combine(RepositoryLayout.Root.FullName, "src"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Where(file => !file.Contains(obj, StringComparison.Ordinal)
                    && !file.Contains(bin, StringComparison.Ordinal))
                .Where(file => File.ReadAllText(file)
                    .Contains("IEmbeddingProvider", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .Where(name => name is not "IEmbeddingProvider.cs"
                    and not "EmbeddingGateway.cs"
                    and not "HttpEmbeddingProvider.cs"
                    and not "DependencyInjection.cs")
                .Cast<string>()
        ];

        Assert.True(
            offenders.Length == 0,
            "Only EmbeddingGateway may reach IEmbeddingProvider: it is where the SB-17 scan runs "
            + "before text leaves and where the channel is checked. Found it in: "
            + string.Join(", ", offenders));
    }

    private static readonly string SEPARATOR = Path.DirectorySeparatorChar.ToString();

    private static WorkerCaller AiCaller() =>
        WorkerCaller.FromMachineToken(Token(), "run-1", WorkerModelUse.SendsContentToAModel)!;

    private sealed class CleanScanner : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
            Task.FromResult(SecretScanResult.Clean);
    }

    private sealed class ScannerThatFinds(string rule) : ISecretScanner
    {
        public Task<SecretScanResult> ScanAsync(string content, CancellationToken cancellationToken) =>
            Task.FromResult(new SecretScanResult([new SecretFinding(rule, 1, 8)]));
    }

    private sealed class CountingProvider : IEmbeddingProvider
    {
        public int Calls { get; private set; }

        public string Description => "a fake, in a test";

        public int Dimensions => 4;

        public bool LeavesTheBoundary => false;

        public Task<EmbeddingResult> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(new EmbeddingResult(
                [.. texts.Select(_ => new ReadOnlyMemory<float>([0f, 0f, 0f, 0f]))], 1));
        }
    }

    /// <summary>Answers with one vector fewer than it was asked for.</summary>
    private sealed class ShortProvider : IEmbeddingProvider
    {
        public string Description => "a fake that drops an input";

        public int Dimensions => 4;

        public bool LeavesTheBoundary => true;

        public Task<EmbeddingResult> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken cancellationToken) =>
            Task.FromResult(new EmbeddingResult(
                [.. texts.Skip(1).Select(_ => new ReadOnlyMemory<float>([0f, 0f, 0f, 0f]))], 1));
    }

    private static IEnumerable<Type> InstallationJobs() =>
        typeof(IWorkerJob).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(InstallationWorkerJob).IsAssignableFrom(type));

    private static MachineTokenIdentity Token() =>
        new(
            new MachineTokenId(Guid.NewGuid()),
            new UserId(Guid.NewGuid()),
            new WorkspaceId(Guid.NewGuid()),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(30));

    /// <summary>
    /// A job that breaks the rule, declared here and nowhere near the product, so
    /// <see cref="the_allow_list_actually_catches_a_job_that_reaches_too_far"/> has something real
    /// to catch. If this ever compiles as part of `DevBuddy.Application`, the allow-list test
    /// fails and that is the point.
    /// </summary>
    private sealed class SweepThatReadsKnowledge(IKnowledgeRepository repository) : InstallationWorkerJob
    {
        private readonly IKnowledgeRepository _repository = Guard.NotNull(repository, nameof(repository));

        public override string Name => "sweep-that-reads-knowledge";

        public override Task<WorkerRunReport> RunAsync(
            WorkerBudget budget, CancellationToken cancellationToken) =>
            throw new NotSupportedException(
                $"A test fixture, never run. It exists so the allow-list has a violation to "
                + $"catch, and it holds {_repository.GetType().Name} for exactly that reason.");
    }
}
