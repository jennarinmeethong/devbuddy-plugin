using System.Text.Json;
using System.Text.Json.Serialization;

namespace DevBuddy.Client;

/// <summary>
/// One registered checkout: a folder, the server it belongs to, the workspace its token is for,
/// and optionally the project an assistant should work in by default.
/// </summary>
internal sealed record Checkout(string Path, string Server, Guid Workspace, Guid? Project);

/// <summary>
/// The per-user registry of checkouts, <c>checkouts.json</c> in the client's home (ADR-0015).
/// <para>
/// Outside every repository on purpose: which server a token is sent to must not be something a
/// pull request can change. It holds no secret, but it is still written owner-only, because it
/// says which servers and workspaces this person works in.
/// </para>
/// </summary>
internal sealed class Registry
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ClientHome _home;
    private readonly List<Checkout> _checkouts;

    private Registry(ClientHome home, List<Checkout> checkouts)
    {
        _home = home;
        _checkouts = checkouts;
    }

    public IReadOnlyList<Checkout> All => _checkouts;

    public static Registry Load(ClientHome home)
    {
        if (!File.Exists(home.RegistryFile))
        {
            return new Registry(home, []);
        }

        RegistryFile? file = JsonSerializer.Deserialize<RegistryFile>(
            File.ReadAllText(home.RegistryFile), Json);

        return new Registry(home, [.. file?.Checkouts ?? []]);
    }

    /// <summary>
    /// The registered checkout that contains this folder. The longest match wins, so a checkout
    /// registered inside another one's folder is still found for its own files.
    /// </summary>
    public Checkout? Find(string folder)
    {
        string wanted = Folders.Normalise(folder);

        return _checkouts
            .Where(checkout => Folders.Contains(checkout.Path, wanted))
            .MaxBy(checkout => checkout.Path.Length);
    }

    public Checkout? Exact(string path)
    {
        string wanted = Folders.Normalise(path);
        return _checkouts.FirstOrDefault(checkout => Folders.Same(checkout.Path, wanted));
    }

    public IReadOnlyList<Checkout> UsingToken(string server, Guid workspace) =>
        [.. _checkouts.Where(checkout =>
            string.Equals(checkout.Server, server, StringComparison.OrdinalIgnoreCase)
            && checkout.Workspace == workspace)];

    public void Put(Checkout checkout)
    {
        Remove(checkout.Path);
        _checkouts.Add(checkout with { Path = Folders.Normalise(checkout.Path) });
    }

    public bool Remove(string path)
    {
        string wanted = Folders.Normalise(path);
        return _checkouts.RemoveAll(checkout => Folders.Same(checkout.Path, wanted)) > 0;
    }

    public void Save()
    {
        _home.EnsureExists();

        string temporary = _home.RegistryFile + ".tmp";
        var file = new RegistryFile(1, [.. _checkouts.OrderBy(checkout => checkout.Path, StringComparer.Ordinal)]);

        File.WriteAllText(temporary, JsonSerializer.Serialize(file, Json));
        ClientHome.MakeOwnerOnly(temporary);
        File.Move(temporary, _home.RegistryFile, overwrite: true);
    }

    private sealed record RegistryFile(int Version, List<Checkout> Checkouts);
}

/// <summary>Folder paths as the registry compares them.</summary>
internal static class Folders
{
    /// <summary>
    /// Case-insensitive where the usual file system is: Windows and macOS. A registry that told
    /// <c>C:\Work</c> from <c>c:\work</c> would call a registered checkout unregistered.
    /// </summary>
    public static StringComparison Comparison { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    public static string Normalise(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        string root = System.IO.Path.GetPathRoot(full) ?? string.Empty;

        return full.Length > root.Length
            ? full.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar)
            : full;
    }

    public static bool Same(string left, string right) => string.Equals(left, right, Comparison);

    public static bool Contains(string checkout, string folder)
    {
        if (Same(checkout, folder))
        {
            return true;
        }

        string prefix = checkout.EndsWith(System.IO.Path.DirectorySeparatorChar)
            ? checkout
            : checkout + System.IO.Path.DirectorySeparatorChar;

        return folder.StartsWith(prefix, Comparison);
    }

    /// <summary>The nearest folder at or above this one holding a <c>.git</c> entry.</summary>
    public static string? GitRoot(string folder)
    {
        for (DirectoryInfo? directory = new(Normalise(folder)); directory is not null; directory = directory.Parent)
        {
            string git = System.IO.Path.Combine(directory.FullName, ".git");

            // A directory in an ordinary clone, a file in a worktree or a submodule.
            if (Directory.Exists(git) || File.Exists(git))
            {
                return Normalise(directory.FullName);
            }
        }

        return null;
    }
}
