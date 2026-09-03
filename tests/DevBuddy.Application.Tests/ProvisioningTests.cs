using DevBuddy.Application.Abstractions;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;

namespace DevBuddy.Application.Tests;

/// <summary>
/// Creating an account hands the setup token to whoever is looking. Before this it was only the
/// API response, because there was no delivery channel; now it also goes out through
/// <see cref="IEmailSender"/>, which reaches the account's owner directly once an operator
/// configures SMTP, and still reaches an operator through the fallback log when they have not.
/// </summary>
public sealed class ProvisioningTests
{
    [Fact]
    public async Task creating_an_account_emails_the_setup_token_to_the_new_address()
    {
        var harness = new Harness();
        var useCase = new CreateUserAccountUseCase(harness.Ports, harness.Ports, harness.Ports, harness.Ports);

        UserAccountCreatedResponse response = await harness.SucceedAsync(
            useCase,
            new CreateUserAccountRequest(
                TestData.Workspace, "newcomer@example.test", "Newcomer", Role.Contributor));

        Assert.NotNull(harness.Ports.SentEmail);
        Assert.Equal("newcomer@example.test", harness.Ports.SentEmail.ToAddress);
        Assert.Contains(response.SetupToken, harness.Ports.SentEmail.Body, StringComparison.Ordinal);
    }
}
