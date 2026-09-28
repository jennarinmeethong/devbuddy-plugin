namespace DevBuddy.Client;

/// <summary>
/// Everything a command touches outside itself, so a test can hand it a temporary home, a file
/// credential store, a server of its own and a scripted token.
/// </summary>
internal sealed record ClientContext(
    ClientHome Home,
    ICredentialStore Store,
    HttpMessageHandler Http,
    TextWriter Out,
    TextWriter Error,
    Func<string, string?> Environment,
    string WorkingDirectory,
    Func<string, string?> ReadToken)
{
    public static ClientContext ForThisProcess()
    {
        static string? Read(string name) => System.Environment.GetEnvironmentVariable(name);

        ClientHome home = ClientHome.Resolve(Read);

        return new ClientContext(
            home,
            CredentialStores.ForThisMachine(home, Read),
            new SocketsHttpHandler(),
            Console.Out,
            Console.Error,
            Read,
            System.Environment.CurrentDirectory,
            TokenPrompt.Read);
    }
}

/// <summary>
/// Reads a token without it appearing on the screen, in the shell's history, or in the process
/// list: a hidden prompt at a terminal, one line of standard input otherwise.
/// </summary>
internal static class TokenPrompt
{
    public static string? Read(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            return Console.In.ReadLine()?.Trim();
        }

        Console.Error.Write(prompt);
        var typed = new System.Text.StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.Error.WriteLine();
                return typed.ToString().Trim();
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (typed.Length > 0)
                {
                    typed.Length--;
                }
            }
            else if (!char.IsControl(key.KeyChar))
            {
                typed.Append(key.KeyChar);
            }
        }
    }
}
