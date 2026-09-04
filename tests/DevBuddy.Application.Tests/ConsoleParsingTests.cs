using System.CommandLine;
using DevBuddy.Cli;

namespace DevBuddy.Application.Tests;

/// <summary>
/// What the console does with the arguments it is given, for the values that are easy to get wrong.
/// <para>
/// This exists because of one that was: System.CommandLine expands any token beginning with
/// <c>@</c> as a response file, so <c>--password @Aa123456</c> was read as "take further arguments
/// from a file named Aa123456" and failed with "Response file not found", naming neither the
/// option nor the reason. A password beginning with a punctuation character is ordinary, and the
/// console is where an administrator's first password is set.
/// </para>
/// </summary>
public sealed class ConsoleParsingTests
{
    [Theory]
    [InlineData("@Aa123456")]
    [InlineData("@")]
    [InlineData("@@doubled")]
    [InlineData("@/etc/passwd")]
    public void a_password_beginning_with_an_at_sign_is_a_password(string password)
    {
        ParseResult parsed = Parse(
            "bootstrap",
            "--workspace-name", "Drill",
            "--email", "someone@example.com",
            "--password", password,
            "--project-name", "Drill project");

        Assert.Empty(parsed.Errors.Select(error => error.Message));
        Assert.Equal(password, ValueOf(parsed, "--password"));
    }

    [Fact]
    public void the_other_values_on_that_line_survive_too()
    {
        // The original failure did not stop at the password: the parser consumed the following
        // option looking for a value and then reported the *project name* as an unrecognised
        // argument, which is why the message pointed at the wrong end of the command.
        ParseResult parsed = Parse(
            "bootstrap",
            "--workspace-name", "Drill",
            "--email", "someone@example.com",
            "--password", "@Aa123456",
            "--project-name", "Drill project");

        Assert.Equal("Drill", ValueOf(parsed, "--workspace-name"));
        Assert.Equal("someone@example.com", ValueOf(parsed, "--email"));
        Assert.Equal("Drill project", ValueOf(parsed, "--project-name"));
    }

    [Fact]
    public void a_missing_required_option_is_still_an_error()
    {
        // Turning off response files must not turn off validation with it.
        ParseResult parsed = Parse("bootstrap", "--workspace-name", "Drill");

        Assert.NotEmpty(parsed.Errors);
    }

    private static ParseResult Parse(params string[] args) =>
        CommandSurface.Build().Parse(args, CommandSurface.Parsing);

    private static string? ValueOf(ParseResult parsed, string optionName)
    {
        Option<string>? option = parsed.CommandResult.Command.Options
            .OfType<Option<string>>()
            .FirstOrDefault(candidate => candidate.Name == optionName);

        return option is null ? null : parsed.GetValue(option);
    }
}
