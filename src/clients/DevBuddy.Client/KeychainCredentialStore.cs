using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DevBuddy.Client;

/// <summary>
/// The macOS login keychain, called directly through Security.framework.
/// <para>
/// Not through <c>/usr/bin/security</c>: product code starts no process (SB-04, held by
/// <c>NoExecutionTests</c>), and a secret passed to one would sit in its arguments or its input.
/// These are the generic-password calls Apple has marked deprecated in favour of <c>SecItem</c>,
/// which takes CoreFoundation dictionaries; they are still present, and each is a plain C call.
/// </para>
/// <para>
/// The keychain trusts the program that created an item. The client is ad-hoc signed, so after an
/// upgrade macOS may ask once whether the new build may read it.
/// </para>
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed partial class KeychainCredentialStore : ICredentialStore
{
    private const string Security = "/System/Library/Frameworks/Security.framework/Security";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const int Success = 0;
    private const int ItemNotFound = -25300;

    private static readonly byte[] Service = Encoding.UTF8.GetBytes("devbuddy");

    public string Description => "the macOS login keychain";

    public string? Read(string key)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);

        int status = SecKeychainFindGenericPassword(
            0, (uint)Service.Length, Service, (uint)account.Length, account,
            out uint length, out nint data, out nint item);

        if (status == ItemNotFound)
        {
            return null;
        }

        Check(status, "read");

        try
        {
            byte[] secret = new byte[length];
            Marshal.Copy(data, secret, 0, secret.Length);
            return Encoding.UTF8.GetString(secret);
        }
        finally
        {
            _ = SecKeychainItemFreeContent(0, data);
            CFRelease(item);
        }
    }

    public void Write(string key, string secret)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);
        byte[] password = Encoding.UTF8.GetBytes(secret);

        int status = SecKeychainFindGenericPassword(
            0, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, out nint data, out nint item);

        if (status == Success)
        {
            _ = SecKeychainItemFreeContent(0, data);

            try
            {
                Check(SecKeychainItemModifyAttributesAndData(item, 0, (uint)password.Length, password), "update");
            }
            finally
            {
                CFRelease(item);
            }

            return;
        }

        if (status != ItemNotFound)
        {
            Check(status, "look up");
        }

        Check(
            SecKeychainAddGenericPassword(
                0, (uint)Service.Length, Service, (uint)account.Length, account,
                (uint)password.Length, password, 0),
            "store");
    }

    public bool Delete(string key)
    {
        byte[] account = Encoding.UTF8.GetBytes(key);

        int status = SecKeychainFindGenericPassword(
            0, (uint)Service.Length, Service, (uint)account.Length, account,
            out _, out nint data, out nint item);

        if (status == ItemNotFound)
        {
            return false;
        }

        Check(status, "look up");
        _ = SecKeychainItemFreeContent(0, data);

        try
        {
            Check(SecKeychainItemDelete(item), "remove");
            return true;
        }
        finally
        {
            CFRelease(item);
        }
    }

    private static void Check(int status, string what)
    {
        if (status != Success)
        {
            throw new InvalidOperationException(
                $"The keychain could not {what} the token (OSStatus {status}). "
                + "If it is locked, unlock it and try again.");
        }
    }

    [LibraryImport(Security)]
    private static partial int SecKeychainFindGenericPassword(
        nint keychainOrArray,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        out uint passwordLength,
        out nint passwordData,
        out nint itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainAddGenericPassword(
        nint keychain,
        uint serviceNameLength,
        byte[] serviceName,
        uint accountNameLength,
        byte[] accountName,
        uint passwordLength,
        byte[] passwordData,
        nint itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemModifyAttributesAndData(
        nint itemRef, nint attributeList, uint length, byte[] data);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemDelete(nint itemRef);

    [LibraryImport(Security)]
    private static partial int SecKeychainItemFreeContent(nint attributeList, nint data);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(nint value);
}
