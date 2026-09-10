using System.Text.RegularExpressions;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// The deployment configuration, checked as configuration rather than trusted as documentation.
/// <para>
/// Every claim in the hardening section of the plan is a line in a file somebody can delete while
/// tidying up, and none of them fails visibly when it goes: a published database port works
/// perfectly until somebody finds it. These read the files.
/// </para>
/// <para>
/// No Docker engine is needed. The subject is what the configuration says, and a test that
/// required a running daemon to check whether a port is published would run in fewer places than
/// the mistake it is looking for.
/// </para>
/// </summary>
public sealed partial class DeploymentTests
{
    /// <summary>The stateful services. Neither has any business being reachable from the host.</summary>
    private static readonly string[] Internal = ["database:", "evidence:"];

    [Fact]
    public void the_database_and_the_object_store_publish_no_host_ports()
    {
        string[] lines = ComposeLines();

        foreach (string service in Internal)
        {
            string[] block = BlockFor(lines, service);

            Assert.DoesNotContain(
                block,
                line => line.TrimStart().StartsWith("ports:", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Control SB-30, the other half: what *is* published is bound to loopback.
    /// <para>
    /// A mapping written as <c>"8080:8080"</c> listens on every interface the host has, which on a
    /// cloud instance means the internet. This stack expects a reverse proxy in front of it, so
    /// the only correct binding is the loopback one.
    /// </para>
    /// </summary>
    [Fact]
    public void every_published_port_is_bound_to_loopback()
    {
        List<string> exposed = [];

        foreach (string line in ComposeLines())
        {
            string trimmed = line.Trim();

            if (!trimmed.StartsWith("- \"", StringComparison.Ordinal) || !trimmed.Contains(':', StringComparison.Ordinal))
            {
                continue;
            }

            if (PortMapping().IsMatch(trimmed) && !trimmed.Contains("127.0.0.1:", StringComparison.Ordinal))
            {
                exposed.Add(trimmed);
            }
        }

        Assert.Empty(exposed);
    }

    /// <summary>
    /// Control SB-31. A container that can reach the Docker daemon is root on the host, whatever
    /// user it runs as inside.
    /// </summary>
    [Fact]
    public void nothing_mounts_the_docker_socket()
    {
        foreach (FileInfo file in DeploymentFiles())
        {
            Assert.DoesNotContain(
                "docker.sock",
                File.ReadAllText(file.FullName),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void every_application_image_runs_as_a_non_root_user()
    {
        FileInfo[] dockerfiles = [.. DockerDirectory().EnumerateFiles("Dockerfile.*")];

        Assert.NotEmpty(dockerfiles);

        foreach (FileInfo dockerfile in dockerfiles)
        {
            string[] lines = File.ReadAllLines(dockerfile.FullName);

            string? user = Array.FindLast(
                lines, line => line.TrimStart().StartsWith("USER ", StringComparison.Ordinal));

            Assert.True(user is not null, $"{dockerfile.Name} never switches away from root.");

            // The base image's own non-root account. Written out rather than inherited silently,
            // so a base image that changed its default would not change ours without a diff.
            Assert.Contains("APP_UID", user!, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Control SB-32, the repository half. A password in a committed file is a password in every
    /// clone, every fork, and the reflog of both.
    /// </summary>
    [Fact]
    public void no_deployment_file_carries_a_secret()
    {
        List<string> findings = [];

        foreach (FileInfo file in DeploymentFiles())
        {
            foreach (string line in File.ReadAllLines(file.FullName))
            {
                string trimmed = line.Trim();

                // A variable reference is the point; a value beside the name is the problem.
                if (AssignedSecret().IsMatch(trimmed)
                    && !trimmed.Contains("${", StringComparison.Ordinal))
                {
                    findings.Add($"{file.Name}: {trimmed}");
                }
            }
        }

        Assert.Empty(findings);
    }

    [Fact]
    public void the_object_store_has_a_key_to_encrypt_with()
    {
        // EvidenceStoreOptions.UseServerSideEncryption defaults to true, so the application asks
        // for AES256 on every object it stores, and MinIO refuses that write outright when it has
        // no key: "Server side encryption specified but KMS is not configured".
        //
        // The shipped stack ran without one. Nobody noticed for nine phases because there was no
        // way to upload evidence at all; the first attempt after capture_evidence shipped failed
        // with a 500. The unit tests could not have caught it — they turn encryption off for their
        // own container and say so — so the check belongs here, against the file that was wrong.
        string[] block = BlockFor(ComposeLines(), "evidence:");

        Assert.Contains(
            block,
            line => line.TrimStart().StartsWith("MINIO_KMS_SECRET_KEY:", StringComparison.Ordinal));
    }

    [Fact]
    public void the_example_environment_file_names_every_variable_the_stack_requires()
    {
        string compose = File.ReadAllText(Path.Combine(DockerDirectory().FullName, "compose.yaml"));
        string example = File.ReadAllText(Path.Combine(DockerDirectory().FullName, ".env.example"));

        string[] required =
        [
            .. RequiredVariable().Matches(compose)
                .Select(match => match.Groups["name"].Value)
                .Distinct(StringComparer.Ordinal)
        ];

        Assert.NotEmpty(required);

        // Every value the stack refuses to start without has to be in the example, or the first
        // thing a new operator meets is an error message naming a variable nothing told them about.
        foreach (string variable in required)
        {
            Assert.Contains(variable, example, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void the_real_environment_file_is_refused_by_git()
    {
        string ignored = File.ReadAllText(Path.Combine(RepositoryRoot().FullName, ".gitignore"));

        Assert.Contains("docker/.env", ignored, StringComparison.Ordinal);
    }

    /// <summary>
    /// The working copy analysis reads is mounted read-only, in the file as well as in the code.
    /// <para>
    /// Control SB-04 is enforced in the product — a source scan fails the build if a process API
    /// appears — and this is the belt beside those braces. A writable mount would not break the
    /// guarantee, but it would remove the reason to believe it.
    /// </para>
    /// </summary>
    [Fact]
    public void the_project_working_copies_are_mounted_read_only()
    {
        string[] mounts =
        [
            .. ComposeLines()
                .Select(line => line.Trim())
                .Where(line => line.Contains("/srv/projects", StringComparison.Ordinal))
        ];

        Assert.NotEmpty(mounts);

        foreach (string mount in mounts.Where(line => line.StartsWith("- ", StringComparison.Ordinal)))
        {
            Assert.EndsWith(":ro", mount, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Phase 12B's exit criterion, the half that lives in a file: the shipped stack schedules the
    /// sweep instead of leaving it to whatever an operator builds.
    /// <para>
    /// Through v1 <c>compose.yaml</c> had <c>migrate</c>, <c>api</c>, <c>mcp</c>, <c>database</c>
    /// and <c>evidence</c> and no scheduler, so the ten rows of the retention schedule were
    /// enforced in code and unscheduled in fact. A window nothing sweeps is a window in name only.
    /// </para>
    /// </summary>
    [Fact]
    public void the_stack_schedules_the_retention_sweep()
    {
        string[] block = BlockFor(ComposeLines(), "retention:");

        string command = Assert.Single(
            block, line => line.TrimStart().StartsWith("command:", StringComparison.Ordinal));

        Assert.Contains("\"retention\"", command, StringComparison.Ordinal);
        Assert.Contains("--every", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// The scheduler has no caller, and that is the design rather than an omission. A sweep spans
    /// every workspace and project, so a scheduler holding an actor would be the installation-wide
    /// superuser this system has deliberately never had — the same reason <c>restore_system</c>
    /// was deleted rather than fixed.
    /// </summary>
    [Fact]
    public void the_scheduler_runs_as_nobody()
    {
        Assert.DoesNotContain(
            "DEVBUDDY_ACTOR",
            ComposeText(),
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            BlockFor(ComposeLines(), "retention:"),
            line => line.Contains("Token", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A sweep that cannot see a store reports zero deletions for it and looks like it worked,
    /// which is the worst failure mode this has. All four copies it purges are volumes.
    /// </summary>
    [Fact]
    public void the_scheduler_can_reach_every_copy_it_purges()
    {
        string compose = ComposeText();

        Assert.Contains("volumes: *console-volumes", BlockFor(ComposeLines(), "retention:").Select(line => line.Trim()));

        foreach (string mount in (string[])["- backups:/srv/backups", "- exports:/srv/exports", "- logs:/srv/logs"])
        {
            Assert.Contains(mount, compose, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A mount point listed twice for one service. Compose rejects some duplicates outright and
    /// silently keeps one of the others, and the API service carried an identical <c>logs</c>
    /// mount twice — harmless as it happened, and exactly the kind of thing that is not harmless
    /// the day the two lines differ.
    /// </summary>
    [Fact]
    public void no_service_mounts_the_same_container_path_twice()
    {
        foreach (string service in (string[])["api:", "mcp:", "database:", "evidence:", "retention:", "migrate:"])
        {
            string[] block = BlockFor(ComposeLines(), service);

            string[] targets =
            [
                .. block
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("- ", StringComparison.Ordinal)
                        && line.Contains(":/", StringComparison.Ordinal))
                    .Select(line => line[2..].Split(':')[1])
            ];

            Assert.Equal(targets.Length, targets.Distinct(StringComparer.Ordinal).Count());
        }
    }

    /// <summary>
    /// The owner's Phase 12B decision on the <c>Log</c> email provider, as configuration. v1 wrote
    /// every setup and recovery token into the application log with nothing asked and nothing set,
    /// and release-readiness carried that as an accepted risk. The default is now off.
    /// </summary>
    [Fact]
    public void the_stack_does_not_write_tokens_to_a_log_unless_asked()
    {
        string[] block = BlockFor(ComposeLines(), "api:");

        string setting = Assert.Single(
            block,
            line => line.TrimStart().StartsWith("DEVBUDDY_Email__AllowTokensInLog:", StringComparison.Ordinal));

        // The default branch of the substitution is what an untouched deployment gets.
        Assert.Contains(":-false}", setting, StringComparison.Ordinal);
    }

    /// <summary>
    /// The images are multi-platform, and cross-compiled rather than emulated: the SDK stage runs
    /// on the builder's own architecture and <c>-a $TARGETARCH</c> decides what comes out. An
    /// arm64 image built by running the whole compiler under QEMU would work and would take long
    /// enough that somebody would eventually delete the platform instead.
    /// </summary>
    [Fact]
    public void every_image_builds_for_the_architecture_it_is_asked_for()
    {
        FileInfo[] dockerfiles = [.. DockerDirectory().EnumerateFiles("Dockerfile.*")];

        Assert.NotEmpty(dockerfiles);

        foreach (FileInfo dockerfile in dockerfiles)
        {
            string text = File.ReadAllText(dockerfile.FullName);

            Assert.Contains("--platform=$BUILDPLATFORM", text, StringComparison.Ordinal);
            Assert.Contains("ARG TARGETARCH", text, StringComparison.Ordinal);
            Assert.Contains("-a $TARGETARCH", text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// And the release actually pushes both. <c>linux/arm64</c> was in the original release-matrix
    /// decision, was never built for v1.0.0, and the decision was amended rather than the build
    /// added; this is the line that makes the amendment unnecessary.
    /// </summary>
    [Fact]
    public void the_release_publishes_both_architectures()
    {
        string workflow = File.ReadAllText(
            Path.Combine(RepositoryRoot().FullName, ".github", "workflows", "release.yml"));

        Assert.Contains("platforms: linux/amd64,linux/arm64", workflow, StringComparison.Ordinal);
    }

    private static string ComposeText() =>
        File.ReadAllText(Path.Combine(DockerDirectory().FullName, "compose.yaml"));

    private static string[] ComposeLines() =>
        File.ReadAllLines(Path.Combine(DockerDirectory().FullName, "compose.yaml"));

    /// <summary>
    /// The lines belonging to one service: from its name until the next thing at the same
    /// indentation. Crude, and enough — the file is small and the alternative is a YAML parser as
    /// a test dependency.
    /// </summary>
    private static string[] BlockFor(string[] lines, string service)
    {
        int start = Array.FindIndex(lines, line => line.Trim() == service);
        Assert.True(start >= 0, $"compose.yaml has no {service} service.");

        int indent = lines[start].Length - lines[start].TrimStart().Length;
        List<string> block = [];

        for (int index = start + 1; index < lines.Length; index++)
        {
            string line = lines[index];

            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (line.Length - line.TrimStart().Length <= indent)
            {
                break;
            }

            block.Add(line);
        }

        return [.. block];
    }

    private static IEnumerable<FileInfo> DeploymentFiles() =>
        DockerDirectory().EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(file => !string.Equals(file.Name, ".env", StringComparison.Ordinal));

    private static DirectoryInfo DockerDirectory() =>
        new(Path.Combine(RepositoryRoot().FullName, "docker"));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not find the repository root.");
    }

    /// <summary>A published port mapping, in the list form Compose uses.</summary>
    [GeneratedRegex(@"^-\s*""[^""]*\d+:\d+""")]
    private static partial Regex PortMapping();

    /// <summary>A secret-looking name with something other than a variable reference after it.</summary>
    [GeneratedRegex(@"(?i)(password|secret|signing_?key|access_?key|token)\s*[:=]\s*\S")]
    private static partial Regex AssignedSecret();

    /// <summary>A variable Compose refuses to start without: <c>${NAME:?message}</c>.</summary>
    [GeneratedRegex(@"\$\{(?<name>[A-Z0-9_]+):\?")]
    private static partial Regex RequiredVariable();
}
