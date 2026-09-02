using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Messaging;
using QuotesApi.Outbox;
using Tests.Domain.TestDoubles;

namespace Tests.Domain.Outbox;

public class OutboxRelayProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;

    public OutboxRelayProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
    }

    private static OutboxMessage Seed(AppDbContext db, string messageId, string payload = "{}")
    {
        var message = new OutboxMessage
        {
            MessageId = messageId,
            MessageType = "quote.created",
            Payload = payload,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.OutboxMessages.Add(message);
        db.SaveChanges();

        return message;
    }

    [Fact]
    public async Task ProcessBatchAsync_PublishesPendingMessage_AndMarksSentAt()
    {
        var seeded = Seed(_db, "quote-created-1");
        var publisher = new FakeQuoteEventPublisher();
        var processor = new OutboxRelayProcessor(_db, publisher, NullLogger<OutboxRelayProcessor>.Instance);

        var publishedCount = await processor.ProcessBatchAsync(10, CancellationToken.None);

        publishedCount.Should().Be(1);

        var stored = await _db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == seeded.Id);
        stored.SentAt.Should().NotBeNull();
        stored.AttemptCount.Should().Be(1);
        stored.LastError.Should().BeNull();

        publisher.PublishedMessages.Should().ContainSingle(m => m.MessageId == "quote-created-1");
    }

    [Fact]
    public async Task ProcessBatchAsync_WhenPublishFails_LeavesSentAtNull_RecordsError_AndRetrySucceeds()
    {
        var seeded = Seed(_db, "quote-created-2");
        var publisher = new FakeQuoteEventPublisher(failuresBeforeSuccess: 1);
        var processor = new OutboxRelayProcessor(_db, publisher, NullLogger<OutboxRelayProcessor>.Instance);

        var firstAttempt = await processor.ProcessBatchAsync(10, CancellationToken.None);

        firstAttempt.Should().Be(0);

        var afterFailure = await _db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == seeded.Id);
        afterFailure.SentAt.Should().BeNull();
        afterFailure.AttemptCount.Should().Be(1);
        afterFailure.LastError.Should().NotBeNullOrEmpty();

        var secondAttempt = await processor.ProcessBatchAsync(10, CancellationToken.None);

        secondAttempt.Should().Be(1);

        var afterRetry = await _db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == seeded.Id);
        afterRetry.SentAt.Should().NotBeNull();
        afterRetry.AttemptCount.Should().Be(2);
        afterRetry.LastError.Should().BeNull();
    }

    [Fact]
    public async Task ProcessBatchAsync_WithMultiplePendingMessages_PublishesEachIndependently()
    {
        Seed(_db, "quote-created-3");
        Seed(_db, "quote-created-4");
        Seed(_db, "quote-created-5");

        var publisher = new FakeQuoteEventPublisher();
        var processor = new OutboxRelayProcessor(_db, publisher, NullLogger<OutboxRelayProcessor>.Instance);

        var publishedCount = await processor.ProcessBatchAsync(10, CancellationToken.None);

        publishedCount.Should().Be(3);
        publisher.PublishedMessages.Select(m => m.MessageId).Should()
            .BeEquivalentTo(new[] { "quote-created-3", "quote-created-4", "quote-created-5" });
        _db.OutboxMessages.AsNoTracking().Count(m => m.SentAt != null).Should().Be(3);
    }

    [Fact]
    public async Task CrashAfterPublishBeforeSentAt_MessageIsRepublishedOnRestart_AndConsumerDeduplicatesTheDuplicate()
    {
        var databaseName = $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

        await using var keepAliveConnection = new SqliteConnection($"DataSource={databaseName}");
        await keepAliveConnection.OpenAsync();

        var seedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(keepAliveConnection).Options;

        await using (var seedDb = new AppDbContext(seedOptions))
        {
            await seedDb.Database.EnsureCreatedAsync();
            Seed(seedDb, "quote-created-crash");
        }

        var publisher = new FakeQuoteEventPublisher();

        await using (var connectionBeforeCrash = new SqliteConnection($"DataSource={databaseName}"))
        {
            await connectionBeforeCrash.OpenAsync();

            var beforeCrashOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionBeforeCrash).Options;
            await using var dbBeforeCrash = new AppDbContext(beforeCrashOptions);

            var message = await dbBeforeCrash.OutboxMessages.SingleAsync(m => m.MessageId == "quote-created-crash");

            await publisher.PublishAsync(
                message.MessageType,
                message.Payload,
                message.MessageId,
                poison: false,
                CancellationToken.None);
        }

        await using (var connectionAfterCrash = new SqliteConnection($"DataSource={databaseName}"))
        {
            await connectionAfterCrash.OpenAsync();

            var afterCrashOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionAfterCrash).Options;
            await using var dbAfterCrash = new AppDbContext(afterCrashOptions);

            var stillUnsent = await dbAfterCrash.OutboxMessages.SingleAsync(m => m.MessageId == "quote-created-crash");
            stillUnsent.SentAt.Should().BeNull();
        }

        await using (var connectionAfterRestart = new SqliteConnection($"DataSource={databaseName}"))
        {
            await connectionAfterRestart.OpenAsync();

            var restartOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionAfterRestart).Options;
            await using var dbAfterRestart = new AppDbContext(restartOptions);

            var relay = new OutboxRelayProcessor(dbAfterRestart, publisher, NullLogger<OutboxRelayProcessor>.Instance);
            var publishedCount = await relay.ProcessBatchAsync(10, CancellationToken.None);

            publishedCount.Should().Be(1);

            var nowSent = await dbAfterRestart.OutboxMessages.SingleAsync(m => m.MessageId == "quote-created-crash");
            nowSent.SentAt.Should().NotBeNull();
        }

        publisher.PublishedMessages.Should().HaveCount(2);
        publisher.PublishedMessages.Select(m => m.MessageId).Distinct().Should().ContainSingle().Which.Should().Be("quote-created-crash");

        var activityLog = new MessagingActivityLog();
        var consumer = new QuoteEventProcessor(_db, activityLog, NullLogger<QuoteEventProcessor>.Instance);

        var command = new ProcessQuoteEventCommand("sub-audit", "quote-created-crash", "quote.created", "{}", 1, false);

        var firstDelivery = await consumer.ProcessAsync(command, "A1", CancellationToken.None);
        var duplicateDelivery = await consumer.ProcessAsync(command, "A2", CancellationToken.None);

        firstDelivery.Should().Be(MessageProcessingOutcome.Processed);
        duplicateDelivery.Should().Be(MessageProcessingOutcome.Duplicate);
        _db.ProcessedMessages.Count(m => m.MessageId == "quote-created-crash").Should().Be(1);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
