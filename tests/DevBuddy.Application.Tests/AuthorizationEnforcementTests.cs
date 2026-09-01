using DevBuddy.Application.Pipeline;
using DevBuddy.Application.Security;
using DevBuddy.Domain.Auditing;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The Phase 2 exit criterion: no use case can execute without an authorization decision, proved
/// by running every one of them rather than by reading the code.
/// <para>
/// The assertion is not only that the result says Denied. It is that the fake ports recorded zero
/// interactions, so the handler body never began. A use case that authorised nothing but returned
/// a denial after reading the database would fail this.
/// </para>
/// </summary>
public sealed class AuthorizationEnforcementTests
{
    [Fact]
    public void the_registry_covers_every_use_case_in_the_assembly()
    {
        var ports = new FakePorts();
        var registry = new UseCaseRegistry(ports);

        IReadOnlyList<Type> discovered = UseCaseRegistry.DiscoverUseCaseTypes();
        HashSet<Type> registered = [.. registry.Entries.Select(entry => entry.UseCaseType)];

        string[] missing =
        [
            .. discovered.Where(type => !registered.Contains(type)).Select(type => type.Name)
        ];

        // Without this, a new use case could be added and never driven by the tests below.
        Assert.True(
            missing.Length == 0,
            "Every use case must be registered in UseCaseRegistry so the pipeline tests drive it. "
            + "Missing: " + string.Join(", ", missing));

        Assert.Equal(discovered.Count, registry.Entries.Count);
    }

    [Fact]
    public async Task no_use_case_runs_when_authorization_denies()
    {
        var ports = new FakePorts();
        var authorization = new FakeAuthorizationService { Allow = false };
        var audit = new FakeAuditSink();
        var executor = new UseCaseExecutor(authorization, audit, ports, ports);
        var registry = new UseCaseRegistry(ports);

        foreach (RegisteredUseCase entry in registry.Entries)
        {
            ExecutionSummary summary = await entry.Run(executor, TestData.Human, TestFixture.None);

            Assert.Equal(ExecutionOutcome.Denied, summary.Outcome);
            Assert.Null(summary.Value);
        }

        Assert.Equal(0, ports.Interactions);
        Assert.Equal(registry.Entries.Count, authorization.Requests.Count);
        Assert.All(audit.Entries, entry => Assert.Equal(AuditOutcome.Denied, entry.Outcome));
        Assert.All(audit.Entries, entry => Assert.Equal(AuditAction.AccessDenied, entry.Action));
    }

    [Fact]
    public async Task every_use_case_asks_for_the_permission_its_descriptor_declares()
    {
        var ports = new FakePorts();
        var authorization = new FakeAuthorizationService { Allow = false };
        var executor = new UseCaseExecutor(authorization, new FakeAuditSink(), ports, ports);
        var registry = new UseCaseRegistry(ports);

        foreach (RegisteredUseCase entry in registry.Entries)
        {
            authorization.Requests.Clear();
            await entry.Run(executor, TestData.Human, TestFixture.None);

            AuthorizationRequest asked = Assert.Single(authorization.Requests);
            Assert.Equal(entry.Descriptor.Permission, asked.Permission);
            Assert.Equal(TestData.Author, asked.Caller.UserId);
        }
    }

    [Fact]
    public async Task an_anonymous_caller_reaches_neither_authorization_nor_any_use_case()
    {
        var ports = new FakePorts();
        var authorization = new FakeAuthorizationService { Allow = true };
        var executor = new UseCaseExecutor(authorization, new FakeAuditSink(), ports, ports);
        var registry = new UseCaseRegistry(ports);

        foreach (RegisteredUseCase entry in registry.Entries)
        {
            ExecutionSummary summary = await entry.Run(executor, TestData.Anonymous, TestFixture.None);
            Assert.Equal(ExecutionOutcome.Denied, summary.Outcome);
        }

        // Identity is resolved before the permission check, so a permissive authorization service
        // is never even consulted. That ordering is what stops an anonymous caller from reaching
        // a check that might accidentally pass.
        Assert.Empty(authorization.Requests);
        Assert.Equal(0, ports.Interactions);
    }

    [Fact]
    public async Task the_ai_channel_cannot_reach_a_use_case_the_catalogue_denies()
    {
        var ports = new FakePorts();
        var authorization = new FakeAuthorizationService { Allow = true };
        var executor = new UseCaseExecutor(authorization, new FakeAuditSink(), ports, ports);
        var registry = new UseCaseRegistry(ports);

        RegisteredUseCase[] denied =
        [
            .. registry.Entries.Where(entry => entry.Descriptor.AiExposure == AiExposure.Denied)
        ];

        Assert.NotEmpty(denied);

        foreach (RegisteredUseCase entry in denied)
        {
            authorization.Requests.Clear();
            ExecutionSummary summary = await entry.Run(executor, TestData.Ai, TestFixture.None);

            Assert.Equal(ExecutionOutcome.Denied, summary.Outcome);
            Assert.Contains(entry.Descriptor.Name, summary.Reason, StringComparison.Ordinal);

            // Refused before authorization, so an over-permissive policy cannot open it either.
            Assert.Empty(authorization.Requests);
        }

        Assert.Equal(0, ports.Interactions);
    }
}

internal static class TestFixture
{
    public static CancellationToken None => CancellationToken.None;
}
