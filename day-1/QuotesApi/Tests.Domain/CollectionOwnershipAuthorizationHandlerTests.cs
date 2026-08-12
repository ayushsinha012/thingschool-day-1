using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using QuotesApi.Authorization;
using QuotesApi.Models;

namespace Tests.Domain;

/// <summary>
/// Direct unit tests for <see cref="CollectionOwnershipAuthorizationHandler"/>,
/// invoked via <see cref="AuthorizationHandlerContext"/> without hosting the
/// real HTTP pipeline. These complement — and do not replace — the existing
/// end-to-end tests in <see cref="CollectionAuthorizationTests"/>.
/// </summary>
public class CollectionOwnershipAuthorizationHandlerTests
{
    private static AuthorizationHandlerContext BuildContext(
        Collection resource,
        int? nameIdentifier)
    {
        var identity = new ClaimsIdentity(authenticationType: "Test");

        if (nameIdentifier is not null)
        {
            identity.AddClaim(
                new Claim(ClaimTypes.NameIdentifier, nameIdentifier.Value.ToString()));
        }

        var user = new ClaimsPrincipal(identity);
        var requirements = new[] { new CollectionOwnershipRequirement() };

        return new AuthorizationHandlerContext(requirements, user, resource);
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsOwner_Succeeds()
    {
        // Arrange
        var handler = new CollectionOwnershipAuthorizationHandler();
        var collection = new Collection("My Quotes", ownerId: 301);
        var context = BuildContext(collection, nameIdentifier: 301);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenUserIsNotOwner_DoesNotSucceed()
    {
        // Arrange
        var handler = new CollectionOwnershipAuthorizationHandler();
        var collection = new Collection("My Quotes", ownerId: 301);
        var context = BuildContext(collection, nameIdentifier: 999);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenNameIdentifierClaimIsMissing_DoesNotSucceed()
    {
        // Arrange
        var handler = new CollectionOwnershipAuthorizationHandler();
        var collection = new Collection("My Quotes", ownerId: 301);
        var context = BuildContext(collection, nameIdentifier: null);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WhenNameIdentifierIsNotNumeric_DoesNotSucceed()
    {
        // Arrange
        var handler = new CollectionOwnershipAuthorizationHandler();
        var collection = new Collection("My Quotes", ownerId: 301);
        var identity = new ClaimsIdentity(authenticationType: "Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "not-a-number"));
        var user = new ClaimsPrincipal(identity);
        var requirements = new[] { new CollectionOwnershipRequirement() };
        var context = new AuthorizationHandlerContext(requirements, user, collection);

        // Act
        await handler.HandleAsync(context);

        // Assert
        context.HasSucceeded.Should().BeFalse();
    }
}
