using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Models;

namespace QuotesApi.Authorization;

public class CollectionOwnershipAuthorizationHandler
    : AuthorizationHandler<CollectionOwnershipRequirement, Collection>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        CollectionOwnershipRequirement requirement,
        Collection resource)
    {
        var userId = context.User.FindFirstValue(
            ClaimTypes.NameIdentifier);

        if (userId is not null &&
            int.TryParse(userId, out var parsedUserId) &&
            parsedUserId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
