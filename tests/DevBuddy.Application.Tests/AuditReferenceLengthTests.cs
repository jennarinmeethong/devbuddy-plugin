using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Analysis;
using DevBuddy.Domain.Auditing;

namespace DevBuddy.Application.Tests;

/// <summary>
/// The audit entry holds the operation's name, a colon, and the request's reference, in at most
/// <see cref="AuditEvent.MaxResourceReferenceLength"/> characters. A reference that fit the
/// authorisation check but not the entry used to run the operation and then fail writing its row,
/// so the caller saw an error for something that had happened and the trail said nothing.
/// </summary>
public sealed class AuditReferenceLengthTests
{
    private static readonly int Limit =
        AuditEvent.MaxResourceReferenceLength - UseCaseCatalog.AnalyzeCode.Name.Length - 1;

    [Fact]
    public async Task a_reference_that_fits_the_entry_exactly_is_run_and_audited()
    {
        var harness = new Harness();

        await harness.SucceedAsync(
            new AnalyzeCodeUseCase(harness.Ports),
            new AnalysisRequest(TestData.Scope, Target: new string('a', Limit)),
            TestData.Human);

        AuditEvent entry = Assert.Single(harness.Audit.Entries);
        Assert.Equal(AuditEvent.MaxResourceReferenceLength, entry.ResourceReference.Length);
    }

    /// <summary>
    /// One character over the entry but still under the authorisation check's own 500, which is
    /// the case that used to reach the use case, and one over both.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public async Task a_reference_too_long_for_the_entry_is_refused_before_anything_runs(int over)
    {
        var harness = new Harness();

        UseCaseResult<AnalysisResponse> result = await harness.RunAsync(
            new AnalyzeCodeUseCase(harness.Ports),
            new AnalysisRequest(TestData.Scope, Target: new string('a', Limit + over)),
            TestData.Human);

        Assert.Equal(ExecutionOutcome.Invalid, result.Outcome);
        Assert.Contains($"at most {Limit}", Assert.Single(result.ValidationErrors));
        Assert.Empty(harness.Audit.Entries);
    }
}
