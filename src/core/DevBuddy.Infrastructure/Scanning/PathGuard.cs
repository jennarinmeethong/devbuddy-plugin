using DevBuddy.Domain.Common;

namespace DevBuddy.Infrastructure.Scanning;

/// <summary>
/// Keeps file access inside one authorised root.
/// <para>
/// Control SB-05. Everything an analyser reads is untrusted content, and a repository under study
/// can contain a path that climbs out, a symlink pointing at the host filesystem, or a name that
/// looks harmless until the operating system normalises it.
/// </para>
/// <para>
/// The check is on the fully resolved path, after symlinks. Checking the string a caller supplied
/// would pass for <c>project/../../etc/passwd</c> and for a symlink named <c>docs</c>, which are
/// the two cases that matter.
/// </para>
/// </summary>
public sealed class PathGuard
{
    private readonly string _root;

    public PathGuard(string root)
    {
        Guard.NotBlank(root, nameof(root));

        // Resolved once, including symlinks, so a root that is itself a link cannot be used to
        // make every later comparison meaningless.
        _root = Normalise(Path.GetFullPath(root));
    }

    public string Root => _root;

    /// <summary>
    /// Resolves a path relative to the root and refuses anything that lands outside it.
    /// Throws rather than returning null: a caller that ignored a null would read the file anyway.
    /// </summary>
    public string Resolve(string relativeOrAbsolutePath)
    {
        Guard.NotBlank(relativeOrAbsolutePath, nameof(relativeOrAbsolutePath));

        if (relativeOrAbsolutePath.Contains('\0', StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("A path containing a null byte is not a path.");
        }

        string candidate = Path.IsPathRooted(relativeOrAbsolutePath)
            ? Path.GetFullPath(relativeOrAbsolutePath)
            : Path.GetFullPath(Path.Combine(_root, relativeOrAbsolutePath));

        string resolved = Normalise(candidate);

        if (!IsInsideRoot(resolved))
        {
            throw new UnauthorizedAccessException(
                $"The path {relativeOrAbsolutePath} resolves outside the authorised project root.");
        }

        return resolved;
    }

    /// <summary>The non-throwing form, for filtering a directory listing.</summary>
    public bool IsInside(string path)
    {
        try
        {
            Resolve(path);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (DomainValidationException)
        {
            return false;
        }
    }

    private bool IsInsideRoot(string resolved) =>
        string.Equals(resolved, _root, PathComparison)
        || resolved.StartsWith(_root + Path.DirectorySeparatorChar, PathComparison);

    /// <summary>
    /// Follows symlinks and trims a trailing separator. A link inside the root that points out of
    /// it is the case a string comparison alone would miss.
    /// </summary>
    private static string Normalise(string path)
    {
        string resolved = path;

        try
        {
            FileSystemInfo? target = File.Exists(path) || Directory.Exists(path)
                ? File.ResolveLinkTarget(path, returnFinalTarget: true)
                    ?? Directory.ResolveLinkTarget(path, returnFinalTarget: true)
                : null;

            if (target is not null)
            {
                resolved = Path.GetFullPath(target.FullName);
            }
        }
        catch (IOException)
        {
            // A broken or circular link resolves to nothing useful. The unresolved path is then
            // compared as-is, which can only be more restrictive, never less.
        }

        return resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Windows paths are case-insensitive and Linux paths are not. Comparing case-insensitively
    /// everywhere would be wrong on Linux in the permissive direction.
    /// </summary>
    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
