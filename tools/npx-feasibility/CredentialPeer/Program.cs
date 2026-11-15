using DevBuddy.Client;

if (args.Length != 2 || !Guid.TryParseExact(args[1], "D", out Guid id)
    || !string.Equals(id.ToString("D"), args[1], StringComparison.Ordinal))
{
    Console.Error.WriteLine("Use seed, verify or cleanup followed by a lowercase probe UUID.");
    return 1;
}

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
    Console.Error.WriteLine("The native credential peer requires Windows or macOS.");
    return 1;
}

string key = $"devbuddy:test/npx/{id:D}";
try
{
    switch (args[0])
    {
        case "seed":
            if (store.Read(key) is not null)
            {
                return 1;
            }

            store.Write(key, new string('D', 43));
            break;
        case "verify":
            if (!string.Equals(store.Read(key), new string('N', 43), StringComparison.Ordinal))
            {
                return 1;
            }

            store.Write(key, new string('V', 43));
            break;
        case "cleanup":
            store.Delete(key);
            if (store.Read(key) is not null)
            {
                return 1;
            }

            break;
        default:
            return 1;
    }
}
catch (InvalidOperationException)
{
    Console.Error.WriteLine("The native credential peer could not access its synthetic test item.");
    return 1;
}

Console.WriteLine("{\"passed\":true}");
return 0;
