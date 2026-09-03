using System.Globalization;

namespace DevBuddy.Api;

/// <summary>
/// The container health check, run as this same executable.
/// <para>
/// A chiseled image ships no shell and no curl, which is exactly why it is worth using: there is
/// nothing inside it to execute. That leaves the runtime itself as the only thing that can ask the
/// server whether it is up, so the health check is a mode of the application rather than a tool
/// beside it.
/// </para>
/// <para>
/// It asks over the loopback interface and reports by exit code. Nothing else: a health check that
/// printed a body would put whatever the server said into the container logs.
/// </para>
/// </summary>
internal static class HealthProbe
{
    private const int Healthy = 0;
    private const int Unhealthy = 1;

    public static async Task<int> RunAsync(string[] args)
    {
        Uri target = TargetFor(args);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        try
        {
            using HttpResponseMessage response = await client.GetAsync(target);
            return response.IsSuccessStatusCode ? Healthy : Unhealthy;
        }
        catch (HttpRequestException)
        {
            return Unhealthy;
        }
        catch (TaskCanceledException)
        {
            // A server that has not answered in five seconds is not healthy, whatever it is doing.
            return Unhealthy;
        }
    }

    /// <summary>
    /// Where to ask. Derived from the port the server was told to listen on, so a deployment that
    /// moves the port does not leave the health check asking the old one.
    /// </summary>
    private static Uri TargetFor(string[] args)
    {
        int index = Array.IndexOf(args, "--health-check");

        if (index >= 0 && index + 1 < args.Length && Uri.TryCreate(args[index + 1], UriKind.Absolute, out Uri? given))
        {
            return given;
        }

        string urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://+:8080";
        string first = urls.Split(';', StringSplitOptions.RemoveEmptyEntries)[0];

        int port = first.Split(':').LastOrDefault() is { } tail
            && int.TryParse(tail.TrimEnd('/'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                ? parsed
                : 8080;

        return new Uri($"http://127.0.0.1:{port}/health");
    }
}
