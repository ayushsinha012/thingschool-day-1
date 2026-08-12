using QuotesApi.Services;

namespace Tests.Domain.TestDoubles;

/// <summary>
/// Hand-written test double for <see cref="IClock"/> that lets a test pin
/// "now" to a fixed, known instant instead of relying on the system clock.
/// </summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
