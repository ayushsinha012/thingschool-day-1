using FluentAssertions;
using QuotesApi.Messaging;

namespace Tests.Domain.Messaging;

public class MessageIdResolverTests
{
    [Fact]
    public void Resolve_WithIdempotencyKey_ReturnsTrimmedKey()
    {
        MessageIdResolver.Resolve("  order-42  ").Should().Be("order-42");
    }

    [Fact]
    public void Resolve_WithSameIdempotencyKey_IsDeterministicAcrossCalls()
    {
        var first = MessageIdResolver.Resolve("order-42");
        var second = MessageIdResolver.Resolve("order-42");

        first.Should().Be(second);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_WithNoIdempotencyKey_ReturnsDifferentValuePerCall(string? key)
    {
        var first = MessageIdResolver.Resolve(key);
        var second = MessageIdResolver.Resolve(key);

        first.Should().NotBe(second);
    }
}
