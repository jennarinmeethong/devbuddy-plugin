using DevBuddy.Application.Abstractions;
using DevBuddy.Application.Pipeline;
using DevBuddy.Application.UseCases.Administration;
using DevBuddy.Domain.Access;
using DevBuddy.Domain.Common;

namespace DevBuddy.Security.Tests;

/// <summary>
/// The real pipeline actually calls <see cref="IEmailSender"/> when an account is created — not a
/// fake standing in for it, the same <c>CreateUserAccountUseCase</c> wiring that ships, proven
/// against real PostgreSQL. What a specific transport does with the message is covered where
/// <c>SmtpEmailSender</c> and <c>LogEmailSender</c> live instead.
/// </summary>
[Collection(SecurityCollection.Name)]
public sealed class AccountProvisioningEmailTests(SecurityFixture fixture)
{
    private readonly SecurityFixture _fixture = fixture;

    [Fact]
    public async Task creating_an_account_sends_the_setup_token_to_its_new_owner()
    {
        World world = await _fixture.CreateWorldAsync();
        UserId administrator = await _fixture.CreateUserAsync($"email-admin-{Guid.NewGuid():N}@example.com");
        await _fixture.GrantAsync(world.Workspace, administrator, Role.Administrator);

        string newAddress = $"newcomer-{Guid.NewGuid():N}@example.com";

        using Session session = _fixture.OpenSession(world.Workspace);

        UseCaseResult<UserAccountCreatedResponse> result = await session.RunAsync(
            new CreateUserAccountUseCase(
                session.Resolve<ICredentialManager>(),
                session.Resolve<IAccessDirectory>(),
                session.Resolve<IClock>(),
                session.Resolve<IEmailSender>()),
            new CreateUserAccountRequest(world.Workspace, newAddress, "Newcomer", Role.Contributor),
            World.Human(administrator));

        Assert.True(result.IsSuccess, $"Expected success but got {result.Outcome}: {result.Reason}");
        UserAccountCreatedResponse response = result.Value!;

        EmailMessage sent = Assert.Single(_fixture.Emails.Sent, message => message.ToAddress == newAddress);
        Assert.Contains(response.SetupToken, sent.Body, StringComparison.Ordinal);
    }
}
