namespace DevBuddy.Client.Tests;

/// <summary>
/// Opt-in peer for the Node feasibility runner. Uses the actual existing client adapters, with
/// a random test-only key. Ordinary test runs do not touch a person's credential store here.
/// </summary>
public sealed class NpxCredentialCompatibilityTests
{
    [Fact]
    public void the_existing_store_reads_and_writes_the_node_probe_credential()
    {
        string? id = Environment.GetEnvironmentVariable("DEVBUDDY_NPX_PROBE_ID");
        if (id is null)
        {
            return;
        }

        Assert.True(Guid.TryParseExact(id, "D", out Guid parsed));
        Assert.Equal(parsed.ToString("D"), id);
        string key = $"devbuddy:test/npx/{id}";
        ICredentialStore store;
        if (OperatingSystem.IsWindows())
        {
            store = new WindowsCredentialStore();
        }
        else if (OperatingSystem.IsMacOS())
        {
            store = new KeychainCredentialStore();
        }
        else
        {
            throw new InvalidOperationException("The native compatibility probe requires Windows or macOS.");
        }

        string? stage = Environment.GetEnvironmentVariable("DEVBUDDY_NPX_PROBE_STAGE");
        switch (stage)
        {
            case "seed":
                Assert.Null(store.Read(key));
                store.Write(key, new string('D', 43));
                break;
            case "verify":
                Assert.Equal(new string('N', 43), store.Read(key));
                store.Write(key, new string('V', 43));
                break;
            case "cleanup":
                store.Delete(key);
                Assert.Null(store.Read(key));
                break;
            default:
                throw new InvalidOperationException("Unknown native compatibility probe stage.");
        }
    }
}
