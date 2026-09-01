using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;
using DevBuddy.Domain.Evidence;
using DevBuddy.Domain.Tenancy;

namespace DevBuddy.Domain.Tests;

/// <summary>
/// Scoping and access invariants. These are the domain half of controls SB-08, SB-11 and SB-17:
/// the authorization decision itself lives in the Application pipeline, but the domain refuses
/// to represent an unscoped record or a releasable-but-unscanned artefact at all.
/// </summary>
public sealed class TenancyAndAccessTests
{
    [Fact]
    public void a_scope_cannot_be_built_without_both_a_workspace_and_a_project()
    {
        Assert.Throws<DomainValidationException>(() => new ProjectScope(default, Fixtures.ProjectAlpha));
        Assert.Throws<DomainValidationException>(() => new ProjectScope(Fixtures.Workspace, default));
    }

    [Fact]
    public void two_projects_in_one_workspace_do_not_contain_each_other()
    {
        Assert.True(Fixtures.AlphaScope.Contains(Fixtures.AlphaScope));
        Assert.False(Fixtures.AlphaScope.Contains(Fixtures.BetaScope));
        Assert.False(Fixtures.BetaScope.Contains(Fixtures.AlphaScope));
    }

    [Fact]
    public void ai_access_is_denied_for_a_project_that_has_never_been_configured()
    {
        var policy = new ProjectAiAccessPolicy(Fixtures.AlphaScope);

        // Control SB-08. The default is structural: an untouched policy and a missing policy
        // behave the same way, so forgetting to configure a project cannot open it up.
        Assert.False(policy.IsEnabled);
        Assert.Null(policy.EnabledBy);
        Assert.Null(policy.EnabledAt);
        Assert.Null(policy.BoundedDataScope);
    }

    [Fact]
    public void enabling_ai_access_records_who_enabled_it_and_when()
    {
        var policy = new ProjectAiAccessPolicy(Fixtures.AlphaScope);

        policy.Enable(Fixtures.Author, Fixtures.Now);

        Assert.True(policy.IsEnabled);
        Assert.Equal(Fixtures.Author, policy.EnabledBy);
        Assert.Equal(Fixtures.Now, policy.EnabledAt);

        policy.Disable();

        Assert.False(policy.IsEnabled);
        Assert.Null(policy.EnabledBy);
    }

    [Fact]
    public void a_project_membership_does_not_cover_a_sibling_project()
    {
        Membership membership = Membership.ForProject(
            MembershipId.New(), Fixtures.Author, Fixtures.AlphaScope, Role.Contributor, Fixtures.Now, Fixtures.Reviewer);

        Assert.True(membership.Covers(Fixtures.AlphaScope));
        Assert.False(membership.Covers(Fixtures.BetaScope));
    }

    [Fact]
    public void a_workspace_membership_covers_every_project_in_that_workspace()
    {
        Membership membership = Membership.ForWorkspace(
            MembershipId.New(), Fixtures.Author, Fixtures.Workspace, Role.Reviewer, Fixtures.Now, Fixtures.Reviewer);

        Assert.True(membership.Covers(Fixtures.AlphaScope));
        Assert.True(membership.Covers(Fixtures.BetaScope));

        var otherWorkspace = new ProjectScope(WorkspaceId.New(), Fixtures.ProjectAlpha);
        Assert.False(membership.Covers(otherWorkspace));
    }

    [Fact]
    public void a_revoked_membership_covers_nothing()
    {
        Membership membership = Membership.ForWorkspace(
            MembershipId.New(), Fixtures.Author, Fixtures.Workspace, Role.Administrator, Fixtures.Now, Fixtures.Reviewer);

        membership.Revoke(Fixtures.Now.AddDays(1));

        // Revocation stops subsequent access. It does not recall what was already taken:
        // that is accepted limitation AL-3, stated rather than implied.
        Assert.False(membership.IsActive);
        Assert.False(membership.Covers(Fixtures.AlphaScope));
        Assert.Throws<InvalidTransitionException>(() => membership.Revoke(Fixtures.Now.AddDays(2)));
    }

    [Fact]
    public void evidence_is_not_releasable_until_it_has_been_scanned()
    {
        var evidence = new EvidenceObject(
            EvidenceObjectId.New(),
            Fixtures.AlphaScope,
            ContentHash.FromContent("log contents"),
            mediaType: "text/plain",
            sizeBytes: 2048,
            storageKey: "alpha/evidence/2026/09/01/abc",
            capturedAt: Fixtures.Now,
            capturedBy: Fixtures.Author);

        // Control SB-17: unscanned material is not safe by default.
        Assert.Equal(RedactionState.NotScanned, evidence.RedactionState);
        Assert.False(evidence.IsReleasable);

        evidence.RecordScanResult(RedactionState.Redacted, Fixtures.Now.AddMinutes(1));
        Assert.True(evidence.IsReleasable);

        evidence.RecordScanResult(RedactionState.Blocked, Fixtures.Now.AddMinutes(2));
        Assert.False(evidence.IsReleasable);

        Assert.Throws<DomainValidationException>(
            () => evidence.RecordScanResult(RedactionState.NotScanned, Fixtures.Now.AddMinutes(3)));
    }

    [Fact]
    public void a_production_environment_is_flagged_as_holding_production_data()
    {
        var production = new DeploymentEnvironment(
            DeploymentEnvironmentId.New(), Fixtures.AlphaScope, "prod-eu", EnvironmentKind.Production);

        var staging = new DeploymentEnvironment(
            DeploymentEnvironmentId.New(), Fixtures.AlphaScope, "staging", EnvironmentKind.Staging);

        Assert.True(production.HoldsProductionData);
        Assert.False(staging.HoldsProductionData);
    }

    [Fact]
    public void timestamps_must_be_utc()
    {
        var localTime = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.FromHours(7));

        Assert.Throws<DomainValidationException>(
            () => new Workspace(WorkspaceId.New(), "Acme", Fixtures.Author, localTime));
    }
}
