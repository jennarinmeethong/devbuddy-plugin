using System.Net;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Path traversal and SSRF, the third and fourth halves of the Phase 6 exit criteria.
/// <para>
/// Both guards exist because everything an analyser reads is attacker-controlled. A repository can
/// contain a path that climbs out of the project, a symlink pointing at the host filesystem, and a
/// document naming a URL that resolves to the cloud metadata service.
/// </para>
/// </summary>
public sealed class PathGuardTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "devbuddy-path-" + Guid.NewGuid().ToString("N"));

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public PathGuardTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "docs", "readme.md"), "# inside");
    }

    public static TheoryData<string> Escapes() =>
    [
        "../outside.txt",
        "../../etc/passwd",
        "docs/../../outside.txt",
        "docs/../../../../../../etc/shadow",
        "./docs/./../../outside.txt",
    ];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void a_path_inside_the_root_resolves()
    {
        var guard = new PathGuard(_root);

        string resolved = guard.Resolve("docs/readme.md");

        Assert.StartsWith(guard.Root, resolved, StringComparison.Ordinal);
        Assert.True(File.Exists(resolved));
    }

    [Theory]
    [MemberData(nameof(Escapes))]
    public void a_path_that_climbs_out_is_refused(string escape)
    {
        var guard = new PathGuard(_root);

        // Refused by throwing rather than by returning null: a caller that ignored a null would
        // open the file anyway.
        Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve(escape));
        Assert.False(guard.IsInside(escape));
    }

    [Fact]
    public void an_absolute_path_outside_the_root_is_refused()
    {
        var guard = new PathGuard(_root);
        string outside = Path.Combine(Path.GetTempPath(), "devbuddy-elsewhere.txt");

        Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve(outside));
    }

    [Fact]
    public void a_path_containing_a_null_byte_is_refused()
    {
        var guard = new PathGuard(_root);

        Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve("docs/readme.md\0.png"));
    }

    [Fact]
    public void a_sibling_directory_with_the_same_prefix_is_not_inside()
    {
        // The classic off-by-one in a prefix check: /data/project and /data/project-other.
        string sibling = _root + "-other";
        Directory.CreateDirectory(sibling);

        try
        {
            var guard = new PathGuard(_root);
            Assert.False(guard.IsInside(Path.Combine(sibling, "file.txt")));
        }
        finally
        {
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Fact]
    public void a_file_reached_through_a_symlinked_parent_directory_is_refused()
    {
        // The case the leaf-only check missed, and the one an attacker actually gets: the file is
        // not a link, its parent is. Only Linux exercises this — Windows takes the fallback below
        // without developer mode, which is why it survived a green suite on the machine this was
        // written on and failed the first time CI ran it.
        string outsideDirectory =
            Path.Combine(Path.GetTempPath(), "devbuddy-outside-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "not yours");

        string link = Path.Combine(_root, "escape");

        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            _output.WriteLine(
                "Symbolic links cannot be created here, so the linked-parent variant was not "
                + "exercised. CI runs this on Linux, where it is.");

            Directory.Delete(outsideDirectory, recursive: true);
            return;
        }

        try
        {
            var guard = new PathGuard(_root);

            // Every component is resolved, so the link is followed even though the last segment
            // names an ordinary file sitting behind it.
            Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve("escape/secret.txt"));
            Assert.False(guard.IsInside(Path.Combine(link, "secret.txt")));

            // And a directory deeper still, so this is not a one-level-of-nesting fix.
            string nested = Path.Combine(outsideDirectory, "deeper");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "also-secret.txt"), "nor this");

            Assert.Throws<UnauthorizedAccessException>(
                () => guard.Resolve("escape/deeper/also-secret.txt"));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }

    [Fact]
    public void a_path_inside_the_root_still_resolves_when_every_component_is_checked()
    {
        // The other half of the fix: resolving each component must not start refusing ordinary
        // paths. A guard that denies everything would pass every test above.
        string directory = Path.Combine(_root, "src", "nested");
        Directory.CreateDirectory(directory);

        string file = Path.Combine(directory, "ok.txt");
        File.WriteAllText(file, "fine");

        var guard = new PathGuard(_root);

        Assert.True(guard.IsInside(file));
        Assert.Equal(file, guard.Resolve(Path.Combine("src", "nested", "ok.txt")));

        // Including one that does not exist yet, which is what a write path looks like.
        Assert.True(guard.IsInside(Path.Combine(directory, "not-created-yet.txt")));
    }

    [Fact]
    public void a_symlink_pointing_out_of_the_root_is_refused()
    {
        string outsideDirectory =
            Path.Combine(Path.GetTempPath(), "devbuddy-outside-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "secret.txt"), "not yours");

        string link = Path.Combine(_root, "escape");

        try
        {
            Directory.CreateSymbolicLink(link, outsideDirectory);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Windows needs developer mode or elevation to create a link. Rather than skipping
            // and reporting a pass that checked nothing, the test asserts the property it still
            // can and says out loud which variant it could not cover.
            _output.WriteLine(
                "Symbolic links cannot be created here, so the link variant was not exercised. "
                + "The direct escape is still checked below.");

            var fallbackGuard = new PathGuard(_root);
            Assert.False(fallbackGuard.IsInside(Path.Combine(outsideDirectory, "secret.txt")));
            Directory.Delete(outsideDirectory, recursive: true);
            return;
        }

        try
        {
            var guard = new PathGuard(_root);

            // The string looks like it is inside the root. Only resolving the link shows it is
            // not, which is why the guard resolves before comparing.
            Assert.False(guard.IsInside(link));
            Assert.Throws<UnauthorizedAccessException>(() => guard.Resolve("escape/secret.txt"));
        }
        finally
        {
            Directory.Delete(link);
            Directory.Delete(outsideDirectory, recursive: true);
        }
    }
}

/// <summary>SSRF. Two independent gates, and a URL has to pass both.</summary>
public sealed class UrlGuardTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>The host as it appears in the URL, and the address it resolves to.</summary>
    public static TheoryData<string, string> PrivateTargets() => new()
    {
        { "127.0.0.1", "127.0.0.1" },
        { "localhost", "127.0.0.1" },
        { "10.0.0.5", "10.0.0.5" },
        { "172.16.31.9", "172.16.31.9" },
        { "192.168.1.1", "192.168.1.1" },

        // The cloud metadata address. The single most common SSRF target there is.
        { "169.254.169.254", "169.254.169.254" },
        { "100.64.0.1", "100.64.0.1" },
        { "0.0.0.0", "0.0.0.0" },
        { "[::1]", "::1" },
        { "[fe80::1]", "fe80::1" },
        { "[fd00::1]", "fd00::1" },
        { "internal.example.com", "10.4.5.6" },
        { "metadata.example.com", "169.254.169.254" },
    };

    [Fact]
    public async Task nothing_is_reachable_when_the_allow_list_is_empty()
    {
        UrlGuard guard = Guard([], new StubResolver());

        UrlDecision decision = await guard.InspectAsync("https://api.github.com/repos", Ct);

        // The default for a system whose analysers read attacker-controlled content.
        Assert.False(decision.IsAllowed);
        Assert.Contains("allow-list", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task an_allow_listed_host_resolving_to_a_public_address_is_permitted()
    {
        UrlGuard guard = Guard(
            ["api.github.com"], new StubResolver { ["api.github.com"] = [IPAddress.Parse("140.82.121.6")] });

        UrlDecision decision = await guard.InspectAsync("https://api.github.com/repos/x/y", Ct);

        Assert.True(decision.IsAllowed);
        Assert.Equal("api.github.com", decision.Target!.Host);
    }

    [Theory]
    [MemberData(nameof(PrivateTargets))]
    public async Task a_private_or_loopback_address_is_refused_even_when_the_host_is_allowed(
        string host, string resolvesTo)
    {
        string bare = host.Trim('[', ']');
        var resolver = new StubResolver { [bare] = [IPAddress.Parse(resolvesTo)] };

        UrlGuard guard = Guard([host, bare], resolver);

        UrlDecision decision = await guard.InspectAsync($"http://{host}/latest/meta-data", Ct);

        // The allow-list alone is not enough. Whoever controls a DNS record controls where an
        // allow-listed name points.
        Assert.False(decision.IsAllowed);
    }

    [Fact]
    public async Task a_host_that_resolves_to_both_a_public_and_a_private_address_is_refused()
    {
        UrlGuard guard = Guard(
            ["rebind.example.com"],
            new StubResolver
            {
                ["rebind.example.com"] = [IPAddress.Parse("93.184.216.34"), IPAddress.Loopback],
            });

        UrlDecision decision = await guard.InspectAsync("https://rebind.example.com/", Ct);

        // Every address is checked, not just the first. One public and one loopback answer is a
        // rebinding attack with the work already done.
        Assert.False(decision.IsAllowed);
        Assert.Contains("non-public", decision.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://evil.example.com/")]
    [InlineData("ftp://files.example.com/")]
    [InlineData("not a url at all")]
    [InlineData("/relative/path")]
    public async Task a_scheme_that_is_not_http_is_refused(string url)
    {
        UrlGuard guard = Guard(["evil.example.com", "files.example.com"], new StubResolver());

        // file: is how a URL fetcher becomes a file reader.
        Assert.False((await guard.InspectAsync(url, Ct)).IsAllowed);
    }

    [Fact]
    public async Task a_subdomain_is_allowed_only_when_the_suffix_form_is_used()
    {
        var resolver = new StubResolver
        {
            ["api.example.com"] = [IPAddress.Parse("93.184.216.34")],
        };

        Assert.False((await Guard(["example.com"], resolver)
            .InspectAsync("https://api.example.com/", Ct)).IsAllowed);

        Assert.True((await Guard([".example.com"], resolver)
            .InspectAsync("https://api.example.com/", Ct)).IsAllowed);
    }

    [Fact]
    public async Task a_suffix_entry_does_not_match_the_bare_domain_or_a_lookalike()
    {
        var resolver = new StubResolver
        {
            ["example.com"] = [IPAddress.Parse("93.184.216.34")],
            ["notexample.com"] = [IPAddress.Parse("93.184.216.35")],
        };

        Assert.False((await Guard([".example.com"], resolver)
            .InspectAsync("https://example.com/", Ct)).IsAllowed);

        Assert.False((await Guard([".example.com"], resolver)
            .InspectAsync("https://notexample.com/", Ct)).IsAllowed);
    }

    [Fact]
    public async Task an_ipv4_mapped_ipv6_loopback_is_still_loopback()
    {
        UrlGuard guard = Guard(
            ["sneaky.example.com"],
            new StubResolver
            {
                ["sneaky.example.com"] = [IPAddress.Parse("::ffff:127.0.0.1")],
            });

        Assert.False((await guard.InspectAsync("https://sneaky.example.com/", Ct)).IsAllowed);
    }

    [Fact]
    public async Task a_deployment_may_opt_into_private_addresses_and_it_is_visible_that_it_did()
    {
        var options = new OutboundAccessOptions { AllowPrivateAddresses = true };
        options.AllowedHosts.Add("gitea.internal");

        var guard = new UrlGuard(
            options, new StubResolver { ["gitea.internal"] = [IPAddress.Parse("10.1.2.3")] });

        // Off by default, and switching it on is one named setting that a reviewer can find.
        Assert.True((await guard.InspectAsync("https://gitea.internal/api", Ct)).IsAllowed);
    }

    private static UrlGuard Guard(string[] allowedHosts, IHostResolver resolver)
    {
        var options = new OutboundAccessOptions();

        foreach (string host in allowedHosts)
        {
            options.AllowedHosts.Add(host.Trim('[', ']'));
        }

        return new UrlGuard(options, resolver);
    }

    /// <summary>A resolver the test controls, so DNS is not part of what is under test.</summary>
    private sealed class StubResolver : IHostResolver
    {
        private readonly Dictionary<string, IPAddress[]> _answers =
            new(StringComparer.OrdinalIgnoreCase);

        public IPAddress[] this[string host]
        {
            set => _answers[host] = value;
        }

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(
            string host, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<IPAddress>>(
                _answers.TryGetValue(host, out IPAddress[]? addresses) ? addresses : []);
    }
}
