using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace DevBuddy.Client;

/// <summary>
/// Where a machine token is kept between sessions: the operating system's credential store,
/// never a configuration file (ADR-0015).
/// </summary>
internal interface ICredentialStore
{
    /// <summary>Said to the person, so they know where to look for the token.</summary>
    string Description { get; }

    string? Read(string key);

    void Write(string key, string secret);

    bool Delete(string key);
}

internal static class CredentialStores
{
    /// <summary>One token per server and workspace, which is what a token is scoped to.</summary>
    public static string KeyFor(string origin, Guid workspace) => $"devbuddy:{origin}/{workspace:D}";

    /// <summary>
    /// The store for this machine. <c>DEVBUDDY_CREDENTIAL_STORE=file</c> chooses the file store
    /// anywhere, for tests and for a machine whose keyring cannot be reached.
    /// <para>
    /// Linux uses the file store. Secret Service would mean running <c>secret-tool</c>, and
    /// product code starts no process (SB-04); libsecret's own calls take variable arguments,
    /// which interop does not marshal portably. The file is mode 600 in a mode-700 folder.
    /// </para>
    /// </summary>
    public static ICredentialStore ForThisMachine(ClientHome home, Func<string, string?> environment)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(environment);

        if (string.Equals(environment("DEVBUDDY_CREDENTIAL_STORE"), "file", StringComparison.OrdinalIgnoreCase))
        {
            return new FileCredentialStore(home);
        }

        if (OperatingSystem.IsWindows())
        {
            return new WindowsCredentialStore();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new KeychainCredentialStore();
        }

        return new FileCredentialStore(home);
    }
}

/// <summary>
/// A mode-600 file per token under the client's home: the store on Linux, and anywhere
/// <c>DEVBUDDY_CREDENTIAL_STORE=file</c> asks for it.
/// </summary>
internal sealed class FileCredentialStore(ClientHome home) : ICredentialStore
{
    public string Description => $"a file readable only by you, in {home.TokenDirectory}";

    public string? Read(string key)
    {
        string file = PathFor(key);
        return File.Exists(file) ? File.ReadAllText(file).Trim() : null;
    }

    public void Write(string key, string secret)
    {
        ClientHome.EnsureOwnerOnlyDirectory(home.Directory);
        ClientHome.EnsureOwnerOnlyDirectory(home.TokenDirectory);

        string file = PathFor(key);
        string temporary = file + ".tmp";

        // Created empty and narrowed before the secret is written, so it is never readable by
        // anybody else, not even for a moment.
        File.WriteAllText(temporary, string.Empty);
        ClientHome.MakeOwnerOnly(temporary);
        File.WriteAllText(temporary, secret);
        File.Move(temporary, file, overwrite: true);
    }

    public bool Delete(string key)
    {
        string file = PathFor(key);

        if (!File.Exists(file))
        {
            return false;
        }

        File.Delete(file);
        return true;
    }

    /// <summary>Named by a hash of the key, so the file name gives away no server or workspace.</summary>
    private string PathFor(string key) =>
        Path.Combine(
            home.TokenDirectory,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
}

/// <summary>Windows Credential Manager, as a generic credential per key.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class WindowsCredentialStore : ICredentialStore
{
    private const uint GenericCredential = 1;
    private const uint PersistLocalMachine = 2;
    private const int NotFound = 1168;

    public string Description => "Windows Credential Manager";

    public string? Read(string key)
    {
        if (!CredRead(key, GenericCredential, 0, out nint pointer))
        {
            int error = Marshal.GetLastPInvokeError();
            return error == NotFound
                ? null
                : throw new InvalidOperationException($"Credential Manager could not read the token (error {error}).");
        }

        try
        {
            Credential credential = Marshal.PtrToStructure<Credential>(pointer);
            byte[] blob = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, blob, 0, blob.Length);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public void Write(string key, string secret)
    {
        byte[] blob = Encoding.UTF8.GetBytes(secret);
        nint target = Marshal.StringToCoTaskMemUni(key);
        nint user = Marshal.StringToCoTaskMemUni("devbuddy");
        nint secretBytes = Marshal.AllocCoTaskMem(blob.Length);

        try
        {
            Marshal.Copy(blob, 0, secretBytes, blob.Length);

            var credential = new Credential
            {
                Type = GenericCredential,
                TargetName = target,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = secretBytes,
                Persist = PersistLocalMachine,
                UserName = user,
            };

            if (!CredWrite(in credential, 0))
            {
                throw new InvalidOperationException(
                    $"Credential Manager could not store the token (error {Marshal.GetLastPInvokeError()}).");
            }
        }
        finally
        {
            // The copy of the secret this process made is cleared before it is freed.
            Marshal.Copy(new byte[blob.Length], 0, secretBytes, blob.Length);
            Marshal.FreeCoTaskMem(secretBytes);
            Marshal.FreeCoTaskMem(target);
            Marshal.FreeCoTaskMem(user);
        }
    }

    public bool Delete(string key)
    {
        if (CredDelete(key, GenericCredential, 0))
        {
            return true;
        }

        int error = Marshal.GetLastPInvokeError();
        return error == NotFound
            ? false
            : throw new InvalidOperationException($"Credential Manager could not remove the token (error {error}).");
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredWrite(in Credential credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredFree")]
    private static partial void CredFree(nint buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }
}

