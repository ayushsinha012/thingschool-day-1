using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using QuotesApi.Data;
using QuotesApi.Messaging;

namespace Tests.Domain.Messaging;

public class QuoteEventProcessorTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly MessagingActivityLog _activityLog = new();
    private readonly QuoteEventProcessor _processor;

    public QuoteEventProcessorTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();

        _processor = new QuoteEventProcessor(_db, _activityLog, NullLogger<QuoteEventProcessor>.Instance);
    }

    private static ProcessQuoteEventCommand Command(
        string messageId,
        string subscription = "sub-audit",
        bool poison = false,
        int deliveryCount = 1) =>
        new(subscription, messageId, "quote.created", "{}", deliveryCount, poison);

    [Fact]
    public async Task ProcessAsync_FirstDelivery_ReturnsProcessed_AndRecordsProcessedMessage()
    {
        var outcome = await _processor.ProcessAsync(Command("msg-1"), "A1", CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Processed);
        _db.ProcessedMessages.Count().Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_DuplicateMessageId_OnSameSubscription_ReturnsDuplicate_AndDoesNotDoubleRecord()
    {
        await _processor.ProcessAsync(Command("msg-2"), "A1", CancellationToken.None);

        var outcome = await _processor.ProcessAsync(Command("msg-2"), "A2", CancellationToken.None);

        outcome.Should().Be(MessageProcessingOutcome.Duplicate);
        _db.ProcessedMessages.Count(processed => processed.MessageId == "msg-2").Should().Be(1);
    }

    [Fact]
    public async Task ProcessAsync_DifferentMessageIds_AreBothProcessedIndependently()
    {
        var first = await _processor.ProcessAsync(Command("msg-3a"), "A1", CancellationToken.None);
        var second = await _processor.ProcessAsync(Command("msg-3b"), "A1", CancellationToken.None);

        first.Should().Be(MessageProcessingOutcome.Processed);
        second.Should().Be(MessageProcessingOutcome.Processed);
        _db.ProcessedMessages.Count().Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_SameMessageId_OnDifferentSubscriptions_AreBothProcessed_NotTreatedAsDuplicate()
    {
        var onSubscriptionA = await _processor.ProcessAsync(
            Command("msg-4", subscription: "sub-audit"), "A1", CancellationToken.None);

        var onSubscriptionB = await _processor.ProcessAsync(
            Command("msg-4", subscription: "sub-notifications"), "B1", CancellationToken.None);

        onSubscriptionA.Should().Be(MessageProcessingOutcome.Processed);
        onSubscriptionB.Should().Be(MessageProcessingOutcome.Processed);
        _db.ProcessedMessages.Count(processed => processed.MessageId == "msg-4").Should().Be(2);
    }

    [Fact]
    public async Task ProcessAsync_PoisonMessage_ThrowsPoisonMessageException_AndDoesNotRecordProcessedMessage()
    {
        var act = async () => await _processor.ProcessAsync(Command("msg-5", poison: true), "A1", CancellationToken.None);

        await act.Should().ThrowAsync<PoisonMessageException>();
        _db.ProcessedMessages.Any(processed => processed.MessageId == "msg-5").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_PoisonMessage_OnEveryRetry_NeverRecordsProcessedMessage()
    {
        for (var deliveryAttempt = 1; deliveryAttempt <= 3; deliveryAttempt++)
        {
            var act = async () => await _processor.ProcessAsync(
                Command("msg-6", poison: true, deliveryCount: deliveryAttempt), "A1", CancellationToken.None);

            await act.Should().ThrowAsync<PoisonMessageException>();
        }

        _db.ProcessedMessages.Any(processed => processed.MessageId == "msg-6").Should().BeFalse();
    }

    [Fact]
    public async Task ProcessAsync_ConcurrentDeliveries_OfSameMessageId_OnlyOneSucceeds()
    {
        var databaseName = $"file:{Guid.NewGuid():N}?mode=memory&cache=shared";

        await using var seedConnection = new SqliteConnection($"DataSource={databaseName}");
        await seedConnection.OpenAsync();

        var seedOptions = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(seedConnection).Options;

        await using (var seedDb = new AppDbContext(seedOptions))
        {
            await seedDb.Database.EnsureCreatedAsync();
        }

        await using var connectionOne = new SqliteConnection($"DataSource={databaseName}");
        await connectionOne.OpenAsync();
        await using var connectionTwo = new SqliteConnection($"DataSource={databaseName}");
        await connectionTwo.OpenAsync();

        var optionsOne = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionOne).Options;
        var optionsTwo = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connectionTwo).Options;

        await using var dbOne = new AppDbContext(optionsOne);
        await using var dbTwo = new AppDbContext(optionsTwo);

        var processorOne = new QuoteEventProcessor(dbOne, _activityLog, NullLogger<QuoteEventProcessor>.Instance);
        var processorTwo = new QuoteEventProcessor(dbTwo, _activityLog, NullLogger<QuoteEventProcessor>.Instance);

        var command = Command("msg-concurrent");

        var results = await Task.WhenAll(
            processorOne.ProcessAsync(command, "A1", CancellationToken.None),
            processorTwo.ProcessAsync(command, "A2", CancellationToken.None));

        results.Count(outcome => outcome == MessageProcessingOutcome.Processed).Should().Be(1);
        results.Count(outcome => outcome == MessageProcessingOutcome.Duplicate).Should().Be(1);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
