using System.Net.Http.Json;
using System.Text.Json;

namespace DevBuddy.Api.Tests;

/// <summary>
/// The web client is generated from the server, and drift between them is a failing test.
/// <para>
/// info.md asks for a typed client so a contract change breaks the build rather than a browser.
/// This is that check: the generated file is committed, and this test regenerates it from a
/// running application and compares. An operation added, removed, renamed, or reshaped without
/// regenerating fails here, in CI, before anybody clicks anything.
/// </para>
/// <para>
/// Set <c>DEVBUDDY_WRITE_CLIENT=1</c> to write the file instead of comparing it. That is the
/// regeneration step, and it is deliberately not the default: a test that quietly rewrote the
/// thing it was checking would never fail.
/// </para>
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class GeneratedClientTests(ApiFixture fixture)
{
    private static readonly string ClientPath =
        Path.Combine(RepositoryRoot().FullName, "web", "admin", "src", "api", "operations.ts");

    [Fact]
    public async Task the_generated_client_matches_the_operations_the_server_serves()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        JsonElement manifest = await client.GetFromJsonAsync<JsonElement>("/operations");
        string generated = TypeScriptClient.Render(manifest);

        if (Environment.GetEnvironmentVariable("DEVBUDDY_WRITE_CLIENT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ClientPath)!);
            await File.WriteAllTextAsync(ClientPath, generated.ReplaceLineEndings("\n"));
            return;
        }

        Assert.True(File.Exists(ClientPath), $"{ClientPath} has never been generated.");

        string committed = await File.ReadAllTextAsync(ClientPath);

        // Line endings are normalised on both sides. The repository stores LF; a checkout on a
        // machine configured otherwise should fail this test for a real reason, not that one.
        Assert.Equal(
            committed.ReplaceLineEndings("\n"),
            generated.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// Every operation the manifest names has a schema for what it takes and what it returns.
    /// <para>
    /// Checked separately from the generated file because a schema that quietly became
    /// unexpressible would still generate: it would generate <c>unknown</c>, the client would
    /// still compile, and the type safety would be gone without anything saying so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task every_operation_describes_its_arguments_and_its_result()
    {
        using HttpClient client = await fixture.SignInAsAdministratorAsync();

        JsonElement manifest = await client.GetFromJsonAsync<JsonElement>("/operations");

        List<string> vague = [];

        foreach (JsonElement operation in manifest.EnumerateArray())
        {
            string name = operation.GetProperty("name").GetString()!;

            foreach (string part in new[] { "argumentsSchema", "resultSchema" })
            {
                JsonElement schema = operation.GetProperty(part);

                if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out _))
                {
                    vague.Add($"{name}.{part}");
                }
            }
        }

        Assert.Empty(vague);
    }

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !directory.EnumerateFiles("DevBuddy.slnx").Any())
        {
            directory = directory.Parent;
        }

        return directory ?? throw new InvalidOperationException("Could not find the repository root.");
    }
}
