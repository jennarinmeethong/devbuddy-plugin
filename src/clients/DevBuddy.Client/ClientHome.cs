namespace DevBuddy.Client;

/// <summary>
/// The client's own folder: <c>~/.devbuddy</c>, or <c>DEVBUDDY_HOME</c> when set.
/// </summary>
internal sealed record ClientHome(string Directory)
{
    public string RegistryFile => Path.Combine(Directory, "checkouts.json");

    /// <summary>Where the file credential store keeps tokens, when it is the one in use.</summary>
    public string TokenDirectory => Path.Combine(Directory, "tokens");

    public static ClientHome Resolve(Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment("DEVBUDDY_HOME") is { Length: > 0 } configured
            ? new ClientHome(Path.GetFullPath(configured))
            : new ClientHome(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".devbuddy"));
    }

    public void EnsureExists() => EnsureOwnerOnlyDirectory(Directory);

    public static void EnsureOwnerOnlyDirectory(string path)
    {
        System.IO.Directory.CreateDirectory(path);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    /// <summary>
    /// Mode 600 on Unix. On Windows a file under the profile already inherits an ACL that admits
    /// its owner, the system and administrators, which is what a profile is for.
    /// </summary>
    public static void MakeOwnerOnly(string file)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
