using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Services;
using Tests.Domain.TestDoubles;

namespace Tests.Domain;

/// <summary>
/// Unit tests for <see cref="RefreshTokenService"/> using a real
/// <see cref="AppDbContext"/> backed by an in-memory SQLite connection
/// (no mocking framework), so rotation and reuse-detection are exercised
/// against real persisted rows rather than a stand-in for them.
/// </summary>
public class RefreshTokenServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly FakeClock _clock;
    private readonly RefreshTokenService _service;

    public RefreshTokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _clock = new FakeClock(DateTimeOffset.UtcNow);

        _service = new RefreshTokenService(
            _db,
            _clock,
            NullLogger<RefreshTokenService>.Instance);
    }

    [Fact]
    public async Task IssueAsync_ReturnsARawTokenThatCanLaterBeRotated()
    {
        // Act
        var rawToken = await _service.IssueAsync(
            userId: 1,
            familyId: Guid.NewGuid(),
            CancellationToken.None);

        // Assert
        rawToken.Should().NotBeNullOrWhiteSpace();

        var storedRow = await _db.RefreshTokens.SingleAsync();
        storedRow.TokenHash.Should().NotBe(rawToken);
        storedRow.RevokedAt.Should().BeNull();
    }

    [Fact]
    public async Task RotateAsync_WithValidToken_IssuesANewTokenAndRevokesTheOld()
    {
        // Arrange
        var familyId = Guid.NewGuid();
        var rawToken = await _service.IssueAsync(1, familyId, CancellationToken.None);

        // Act
        var result = await _service.RotateAsync(rawToken, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(RefreshTokenOutcome.Success);
        result.UserId.Should().Be(1);
        result.NewRawToken.Should().NotBeNullOrWhiteSpace();
        result.NewRawToken.Should().NotBe(rawToken);

        var rows = await _db.RefreshTokens.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Should().ContainSingle(row => row.RevokedAt != null);
    }

    [Fact]
    public async Task RotateAsync_WithUnknownToken_ReturnsNotFound()
    {
        // Act
        var result = await _service.RotateAsync("unknown-token", CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(RefreshTokenOutcome.NotFound);
    }

    [Fact]
    public async Task RotateAsync_AfterExpiry_ReturnsExpired()
    {
        // Arrange
        var rawToken = await _service.IssueAsync(1, Guid.NewGuid(), CancellationToken.None);
        _clock.UtcNow = _clock.UtcNow.AddDays(8);

        // Act
        var result = await _service.RotateAsync(rawToken, CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(RefreshTokenOutcome.Expired);
    }

    [Fact]
    public async Task RotateAsync_WithAlreadyRotatedToken_DetectsReuseAndKillsTheWholeChain()
    {
        // Arrange - simulate a leaked refresh token: rotate once (legitimate),
        // then present the original (now-stale) token again, as an attacker
        // replaying a stolen token would.
        var familyId = Guid.NewGuid();
        var firstToken = await _service.IssueAsync(1, familyId, CancellationToken.None);

        var firstRotation = await _service.RotateAsync(firstToken, CancellationToken.None);
        var secondToken = firstRotation.NewRawToken!;

        // Act - replay the already-used first token.
        var reuseResult = await _service.RotateAsync(firstToken, CancellationToken.None);

        // Assert
        reuseResult.Outcome.Should().Be(RefreshTokenOutcome.ReuseDetected);
        reuseResult.UserId.Should().Be(1);

        // The entire family is revoked, so even the legitimately-rotated
        // second token (never itself replayed) is now rejected too.
        var secondTokenRotation = await _service.RotateAsync(secondToken, CancellationToken.None);
        secondTokenRotation.Outcome.Should().Be(RefreshTokenOutcome.ReuseDetected);

        var allTokensInFamily = await _db.RefreshTokens
            .Where(t => t.FamilyId == familyId)
            .ToListAsync();

        allTokensInFamily.Should().OnlyContain(t => t.RevokedAt != null);
    }

    [Fact]
    public async Task RevokeAsync_MarksTheTokenRevokedSoItCanNoLongerBeRotated()
    {
        // Arrange
        var rawToken = await _service.IssueAsync(1, Guid.NewGuid(), CancellationToken.None);

        // Act
        await _service.RevokeAsync(rawToken, CancellationToken.None);

        // Assert
        var result = await _service.RotateAsync(rawToken, CancellationToken.None);
        result.Outcome.Should().Be(RefreshTokenOutcome.ReuseDetected);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
