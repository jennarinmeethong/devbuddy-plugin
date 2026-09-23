using DevBuddy.Application.Security;
using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Workers;

/// <summary>
/// What an installation shows about its embedding setup, gathered by <c>embedding-check</c> without
/// changing anything or sending any text (Phase 13, B7).
/// </summary>
public sealed record EmbeddingFacts(
    string Provider,
    string? Model,
    int Dimensions,
    bool LeavesTheBoundary,
    bool IndexAvailable,
    bool WorkerTokenPresent,
    bool WorkerTokenResolves,
    IReadOnlyList<Role> WorkerRoles,
    int? Budget);

/// <summary>One line of the check: what was looked at, what was found, and whether it stands in the way.</summary>
public sealed record ReadinessLine(string Check, string Finding, bool Problem);

/// <summary>
/// Turns what an installation shows into the lines an approval is written from
/// (<c>docs/operations/embedding-approval.md</c>). Pure, so every case is tested without a stack.
/// </summary>
public static class EmbeddingReadiness
{
    public static IReadOnlyList<ReadinessLine> Evaluate(EmbeddingFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        List<ReadinessLine> lines = [];

        if (string.Equals(facts.Provider, "None", StringComparison.Ordinal))
        {
            lines.Add(new("provider", "None: embeddings are off, and full-text search is all there is.", false));
            return lines;
        }

        lines.Add(new(
            "provider",
            $"{facts.Provider}, model {facts.Model}, {facts.Dimensions} dimensions",
            string.IsNullOrWhiteSpace(facts.Model) || facts.Dimensions <= 0));

        if (facts.LeavesTheBoundary)
        {
            // Not a misconfiguration, and still the line an approver must read.
            lines.Add(new(
                "egress",
                "Hosted: project text leaves this installation for a model server outside it. It needs its own acceptance in info.md.",
                false));
        }

        lines.Add(facts.IndexAvailable
            ? new("vector index", "present (pgvector)", false)
            : new("vector index", "absent: the database has no pgvector, so the migration skipped the index", true));

        if (!facts.WorkerTokenPresent)
        {
            lines.Add(new("worker token", "not set, so no sweep can run", true));
        }
        else if (!facts.WorkerTokenResolves)
        {
            lines.Add(new("worker token", "set but not valid: revoked, expired, or from before workspace scoping", true));
        }
        else
        {
            // The sweep needs to read and nothing else. A role that carries more is reach the job
            // does not use, and a token is exactly what should not have it.
            bool wider = facts.WorkerRoles.Any(role =>
                RolePermissions.For(role).Any(permission =>
                    permission is not PermissionKind.ReadKnowledge and not PermissionKind.ManageOwnCredentials));

            lines.Add(new(
                "worker token",
                facts.WorkerRoles.Count == 0
                    ? "valid, but its owner holds no grant in its workspace"
                    : $"valid; its owner holds {string.Join(", ", facts.WorkerRoles)}"
                        + (wider ? ", which carries more than the sweep needs (Viewer)" : string.Empty),
                facts.WorkerRoles.Count == 0 || wider));
        }

        lines.Add(facts.Budget switch
        {
            null => new("budget", "not set; Compose defaults it to 0, which sends nothing", false),
            0 => new("budget", "0: the sweep runs and sends nothing", false),
            > 0 and var budget => new("budget", $"{budget} text(s) per pass", false),
            _ => new("budget", "negative, which the worker refuses", true),
        });

        return lines;
    }
}
