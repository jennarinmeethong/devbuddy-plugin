using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DevBuddy.Infrastructure.Tests;

/// <summary>
/// Builds a real git object store on disk, by writing the objects rather than by running git.
/// <para>
/// The tests have to be honest about what they exercise. Shelling out to git would mean the
/// fixture needs git installed, would make the test depend on whichever version is on the machine,
/// and would sit oddly beside a control that forbids the product from doing exactly that. Writing
/// the objects means the reader is tested against the format it claims to read.
/// </para>
/// </summary>
[SuppressMessage(
    "Security",
    "CA5350:Do Not Use Weak Cryptographic Algorithms",
    Justification = "SHA-1 is how git names objects. This is a content address in a file format, "
        + "not a security decision, and using anything else would produce a store git cannot read.")]
internal sealed class GitFixture
{
    private readonly string _gitDirectory;

    public GitFixture(string workingCopyRoot)
    {
        Root = workingCopyRoot;
        _gitDirectory = Path.Combine(workingCopyRoot, ".git");

        Directory.CreateDirectory(Path.Combine(_gitDirectory, "objects"));
        Directory.CreateDirectory(Path.Combine(_gitDirectory, "refs", "heads"));
        File.WriteAllText(Path.Combine(_gitDirectory, "HEAD"), "ref: refs/heads/main\n");
    }

    public string Root { get; }

    /// <summary>Writes a commit whose tree contains exactly these paths, and returns its id.</summary>
    public string Commit(
        IReadOnlyDictionary<string, string> files,
        string? parent = null,
        string author = "Jennarin",
        string message = "a commit")
    {
        string tree = WriteTree(BuildTree(files));

        var payload = new StringBuilder();
        payload.Append("tree ").Append(tree).Append('\n');

        if (parent is not null)
        {
            payload.Append("parent ").Append(parent).Append('\n');
        }

        string stamp = string.Create(
            CultureInfo.InvariantCulture,
            $"{author} <{author.ToLowerInvariant()}@example.com> 1780000000 +0000");

        payload.Append("author ").Append(stamp).Append('\n');
        payload.Append("committer ").Append(stamp).Append('\n');
        payload.Append('\n').Append(message).Append('\n');

        return WriteObject("commit", Encoding.UTF8.GetBytes(payload.ToString()));
    }

    /// <summary>Points a branch at a commit.</summary>
    public void SetBranch(string name, string commitId) =>
        File.WriteAllText(Path.Combine(_gitDirectory, "refs", "heads", name), commitId + "\n");

    /// <summary>Writes a packed-refs file, the form a repository takes after `git gc`.</summary>
    public void SetPackedRefs(IReadOnlyDictionary<string, string> references)
    {
        var content = new StringBuilder("# pack-refs with: peeled fully-peeled sorted \n");

        foreach ((string name, string commit) in references)
        {
            content.Append(commit).Append(' ').Append(name).Append('\n');
        }

        File.WriteAllText(Path.Combine(_gitDirectory, "packed-refs"), content.ToString());
    }

    /// <summary>
    /// Removes an object from loose storage, the way `git gc` does when it packs one. The reader
    /// has to notice and say so rather than reporting an empty result.
    /// </summary>
    public void PackAway(string objectId)
    {
        string path = Path.Combine(_gitDirectory, "objects", objectId[..2], objectId[2..]);

        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Nested dictionaries mirroring the directory structure of the paths given.</summary>
    private static Dictionary<string, object> BuildTree(IReadOnlyDictionary<string, string> files)
    {
        var root = new Dictionary<string, object>(StringComparer.Ordinal);

        foreach ((string path, string content) in files)
        {
            string[] segments = path.Split('/');
            Dictionary<string, object> current = root;

            for (int index = 0; index < segments.Length - 1; index++)
            {
                if (!current.TryGetValue(segments[index], out object? child)
                    || child is not Dictionary<string, object> directory)
                {
                    directory = new Dictionary<string, object>(StringComparer.Ordinal);
                    current[segments[index]] = directory;
                }

                current = directory;
            }

            current[segments[^1]] = content;
        }

        return root;
    }

    private string WriteTree(Dictionary<string, object> node)
    {
        var payload = new List<byte>();

        foreach ((string name, object child) in node.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            (string mode, string sha) = child switch
            {
                Dictionary<string, object> directory => ("40000", WriteTree(directory)),
                string content => ("100644", WriteObject("blob", Encoding.UTF8.GetBytes(content))),
                _ => throw new InvalidOperationException("Unexpected tree node."),
            };

            payload.AddRange(Encoding.ASCII.GetBytes(mode));
            payload.Add((byte)' ');
            payload.AddRange(Encoding.UTF8.GetBytes(name));
            payload.Add(0);
            payload.AddRange(Convert.FromHexString(sha));
        }

        return WriteObject("tree", [.. payload]);
    }

    /// <summary>
    /// Writes one object in git format: `&lt;type&gt; &lt;length&gt;\0&lt;payload&gt;`, named by
    /// the SHA-1 of that whole byte sequence, zlib-compressed on disk.
    /// </summary>
    private string WriteObject(string type, byte[] payload)
    {
        byte[] header = Encoding.ASCII.GetBytes(
            string.Create(CultureInfo.InvariantCulture, $"{type} {payload.Length}\0"));

        byte[] full = [.. header, .. payload];
        string sha = Convert.ToHexString(SHA1.HashData(full)).ToLowerInvariant();

        string directory = Path.Combine(_gitDirectory, "objects", sha[..2]);
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, sha[2..]);

        if (!File.Exists(path))
        {
            using FileStream file = File.Create(path);
            using var zlib = new ZLibStream(file, CompressionLevel.Optimal);
            zlib.Write(full);
        }

        return sha;
    }
}
