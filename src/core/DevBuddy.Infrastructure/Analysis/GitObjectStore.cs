using System.Globalization;
using System.IO.Compression;
using System.Text;
using DevBuddy.Domain.Common;
using DevBuddy.Infrastructure.Scanning;

namespace DevBuddy.Infrastructure.Analysis;

/// <summary>
/// Reads a git repository by opening its files.
/// <para>
/// Not by running git. Control SB-04 forbids executing anything while a repository is under study,
/// and "it is only git" is exactly the exception that makes a rule stop meaning anything. The
/// object format is stable, documented, and a zlib stream away.
/// </para>
/// <para>
/// Loose objects only. Packing is a compression format with delta chains, and a partial
/// implementation of it would fail in ways that look like missing history rather than like a
/// missing feature. When an object is packed this says so, by name, rather than returning less.
/// </para>
/// </summary>
internal sealed class GitObjectStore
{
    private readonly PathGuard _guard;
    private readonly string _gitDirectory;
    private readonly int _maxEntries;

    public GitObjectStore(PathGuard guard, int maxEntries)
    {
        _guard = Guard.NotNull(guard, nameof(guard));
        _gitDirectory = Path.Combine(_guard.Root, ".git");
        _maxEntries = maxEntries;
    }

    public bool Exists => Directory.Exists(_gitDirectory);

    /// <summary>
    /// What HEAD points at: the symbolic reference where there is one, and the commit it resolves
    /// to. A detached HEAD reports the commit as its own reference.
    /// </summary>
    public GitHead ReadHead()
    {
        string content = ReadTextFile(Path.Combine(_gitDirectory, "HEAD")).Trim();

        if (!content.StartsWith("ref:", StringComparison.Ordinal))
        {
            return new GitHead("HEAD", content);
        }

        string reference = content["ref:".Length..].Trim();
        return new GitHead(reference, ResolveReference(reference));
    }

    /// <summary>Every reference the repository has on disk, loose and packed alike.</summary>
    public IReadOnlyDictionary<string, string> EnumerateReferences()
    {
        var references = new Dictionary<string, string>(StringComparer.Ordinal);

        // packed-refs first, so a loose ref of the same name wins: that is what git does, because
        // a loose ref is the newer write.
        string packed = Path.Combine(_gitDirectory, "packed-refs");

        if (File.Exists(packed) && _guard.IsInside(packed))
        {
            foreach (string line in ReadTextFile(packed).Split('\n'))
            {
                string trimmed = line.Trim();

                if (trimmed.Length == 0 || trimmed[0] is '#' or '^')
                {
                    continue;
                }

                string[] parts = trimmed.Split(' ', 2);

                if (parts.Length == 2)
                {
                    references[parts[1].Trim()] = parts[0].Trim();
                }
            }
        }

        string refsRoot = Path.Combine(_gitDirectory, "refs");

        if (Directory.Exists(refsRoot))
        {
            foreach (string file in Directory.EnumerateFiles(refsRoot, "*", SearchOption.AllDirectories))
            {
                if (!_guard.IsInside(file))
                {
                    continue;
                }

                string name = "refs/"
                    + Path.GetRelativePath(refsRoot, file).Replace('\\', '/');

                references[name] = ReadTextFile(file).Trim();
            }
        }

        return references;
    }

    public string ResolveReference(string reference)
    {
        string direct = Path.Combine(_gitDirectory, reference.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(direct) && _guard.IsInside(direct))
        {
            return ReadTextFile(direct).Trim();
        }

        return EnumerateReferences().TryGetValue(reference, out string? packed)
            ? packed
            : throw new InvalidOperationException($"The reference {reference} does not exist in this working copy.");
    }

    /// <summary>
    /// A commit, parsed from its object. The header is text up to the first blank line; the rest
    /// is the message.
    /// </summary>
    public GitCommit ReadCommit(string sha)
    {
        (string type, byte[] payload) = ReadObject(sha);

        if (type != "commit")
        {
            throw new InvalidOperationException($"Object {sha} is a {type}, not a commit.");
        }

        string text = Encoding.UTF8.GetString(payload);
        string tree = string.Empty;
        List<string> parents = [];
        string author = "unknown";
        DateTimeOffset when = DateTimeOffset.UnixEpoch;

        foreach (string line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("tree ", StringComparison.Ordinal))
            {
                tree = line[5..].Trim();
            }
            else if (line.StartsWith("parent ", StringComparison.Ordinal))
            {
                parents.Add(line[7..].Trim());
            }
            else if (line.StartsWith("author ", StringComparison.Ordinal))
            {
                (author, when) = ParseIdentity(line[7..]);
            }
        }

        return new GitCommit(sha, tree, parents, author, when);
    }

    /// <summary>
    /// Every file path in a tree, mapped to the object it points at. Recursive, and bounded: a
    /// repository under study can be shaped to make a naive walk run forever.
    /// </summary>
    public IReadOnlyDictionary<string, string> FlattenTree(string treeSha, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        var pending = new Stack<(string Sha, string Prefix)>();
        pending.Push((treeSha, string.Empty));

        while (pending.Count > 0 && files.Count < _maxEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (string sha, string prefix) = pending.Pop();

            foreach (GitTreeEntry entry in ReadTree(sha))
            {
                string path = prefix.Length == 0 ? entry.Name : $"{prefix}/{entry.Name}";

                if (entry.IsDirectory)
                {
                    pending.Push((entry.Sha, path));
                }
                else if (files.Count < _maxEntries)
                {
                    files[path] = entry.Sha;
                }
            }
        }

        return files;
    }

    private List<GitTreeEntry> ReadTree(string sha)
    {
        (string type, byte[] payload) = ReadObject(sha);

        if (type != "tree")
        {
            throw new InvalidOperationException($"Object {sha} is a {type}, not a tree.");
        }

        List<GitTreeEntry> entries = [];
        int position = 0;

        // Entries are `<mode> <name>\0<20 raw bytes>`, packed end to end with no separator.
        while (position < payload.Length)
        {
            int space = Array.IndexOf(payload, (byte)' ', position);
            int nul = Array.IndexOf(payload, (byte)0, space + 1);

            if (space < 0 || nul < 0 || nul + 20 >= payload.Length + 1)
            {
                break;
            }

            string mode = Encoding.ASCII.GetString(payload, position, space - position);
            string name = Encoding.UTF8.GetString(payload, space + 1, nul - space - 1);
            string entrySha = Convert.ToHexString(payload, nul + 1, 20).ToLowerInvariant();

            entries.Add(new GitTreeEntry(name, entrySha, mode.StartsWith("40", StringComparison.Ordinal)));
            position = nul + 21;
        }

        return entries;
    }

    /// <summary>
    /// A loose object, decompressed and split into its type and payload. Objects that have been
    /// packed are reported as such rather than silently treated as absent.
    /// </summary>
    private (string Type, byte[] Payload) ReadObject(string sha)
    {
        if (sha.Length != 40)
        {
            throw new InvalidOperationException($"{sha} is not a full object identifier.");
        }

        string path = Path.Combine(_gitDirectory, "objects", sha[..2], sha[2..]);

        if (!File.Exists(path) || !_guard.IsInside(path))
        {
            throw new NotSupportedException(
                $"Object {sha} is not stored loose in this working copy. Reading packed objects "
                + "means implementing delta chains, and a partial implementation would look like "
                + "missing history rather than a missing feature. Run `git unpack-objects`, or "
                + "wait for a source-system adapter that reads through the provider API.");
        }

        using FileStream file = File.OpenRead(path);
        using var zlib = new ZLibStream(file, CompressionMode.Decompress);
        using var buffer = new MemoryStream();
        zlib.CopyTo(buffer);

        byte[] raw = buffer.ToArray();
        int header = Array.IndexOf(raw, (byte)0);

        if (header < 0)
        {
            throw new InvalidOperationException($"Object {sha} has no header.");
        }

        string[] parts = Encoding.ASCII.GetString(raw, 0, header).Split(' ');
        return (parts[0], raw[(header + 1)..]);
    }

    /// <summary>
    /// An identity line: `Name &lt;email&gt; 1700000000 +0000`. The name is kept, the address is
    /// not: an audit trail and a knowledge record do not need everybody personal email address.
    /// </summary>
    private static (string Name, DateTimeOffset When) ParseIdentity(string line)
    {
        int emailStart = line.IndexOf('<', StringComparison.Ordinal);
        int emailEnd = line.IndexOf('>', StringComparison.Ordinal);

        string name = emailStart > 0 ? line[..emailStart].Trim() : line.Trim();
        DateTimeOffset when = DateTimeOffset.UnixEpoch;

        if (emailEnd > 0 && emailEnd + 1 < line.Length)
        {
            string[] rest = line[(emailEnd + 1)..].Trim().Split(' ');

            if (rest.Length > 0
                && long.TryParse(rest[0], CultureInfo.InvariantCulture, out long seconds))
            {
                when = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        return (name, when);
    }

    private string ReadTextFile(string path)
    {
        if (!_guard.IsInside(path))
        {
            throw new UnauthorizedAccessException($"{path} is outside the working copy.");
        }

        return File.ReadAllText(path);
    }
}

/// <summary>Where HEAD points, and the commit it resolves to.</summary>
internal sealed record GitHead(string Reference, string CommitId);

/// <summary>A commit, with its tree and first-parent chain.</summary>
internal sealed record GitCommit(
    string Sha, string TreeSha, IReadOnlyList<string> Parents, string Author, DateTimeOffset When);

/// <summary>One entry in a tree object.</summary>
internal sealed record GitTreeEntry(string Name, string Sha, bool IsDirectory);
