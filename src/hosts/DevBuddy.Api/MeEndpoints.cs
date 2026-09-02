using System.Security.Claims;
using DevBuddy.Application.Abstractions;
using DevBuddy.Domain.Common;

namespace DevBuddy.Api;

/// <summary>
/// Who the caller is, and which workspaces they can open.
/// <para>
/// Outside the operation pipeline for a structural reason, like the credential endpoints: every
/// operation names a workspace and is authorised against membership of it, so the question "which
/// workspaces am I a member of" has no workspace to name. The signed-in web client asks this
/// first, and everything it does afterwards names a workspace and goes through the pipeline.
/// </para>
/// <para>
/// It answers for the caller and nobody else. There is no user identifier in the route or the
/// query, so there is nothing to point at somebody else's account.
/// </para>
/// </summary>
internal static class MeEndpoints
{
    public static void MapMe(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/me", DescribeAsync)
            .RequireAuthorization()
            .WithName("DescribeSignedInUser")
            .WithSummary("The signed-in account, its live workspace grants, and what each one carries.");
    }

    private static async Task<IResult> DescribeAsync(
        HttpContext context,
        ISignedInUserDirectory directory,
        CancellationToken cancellationToken)
    {
        string? subject = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(subject, out Guid parsed))
        {
            return Results.Unauthorized();
        }

        SignedInUser? user = await directory.DescribeAsync(new UserId(parsed), cancellationToken);

        // Null covers both an account that has been deleted and one that has been disabled since
        // the access token was issued. Either way the token is still cryptographically valid and
        // must stop working, which is why this is checked here rather than trusted from the token.
        return user is null ? Results.Unauthorized() : Results.Ok(user);
    }
}
