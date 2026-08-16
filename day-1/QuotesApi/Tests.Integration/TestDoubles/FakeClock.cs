using QuotesApi.Services;

namespace Tests.Integration.TestDoubles;

/// <summary>
/// Test double for <see cref="IClock"/> that lets a test pin the
/// application's "now" to a fixed, known instant instead of relying on the
/// system clock. Kept local to Tests.Integration so this project does not
/// depend on Tests.Domain's copy.
/// </summary>
public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }
}
