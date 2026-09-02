using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Models;
using QuotesApi.Outbox;
using QuotesApi.Repositories;

namespace Tests.Domain.Outbox;

public class OutboxAtomicWriteTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AppDbContext _db;
    private readonly QuoteRepository _repository;

    public OutboxAtomicWriteTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        _db.Database.EnsureCreated();
        _repository = new QuoteRepository(_db);
    }

    [Fact]
    public async Task AddWithOutboxMessageAsync_OnSuccess_PersistsQuoteAndOutboxMessageTogether()
    {
        var quote = Quote.Create("Seneca", "It is not that we have a short time to live, but that we waste a lot of it.");

        var created = await _repository.AddWithOutboxMessageAsync(
            quote,
            "quote.created",
            q => $"{{\"id\":{q.Id}}}",
            CancellationToken.None);

        _db.Quotes.Count().Should().Be(1);
        _db.OutboxMessages.Count().Should().Be(1);

        var outboxMessage = _db.OutboxMessages.Single();
        outboxMessage.MessageId.Should().Be($"quote-created-{created.Id}");
        outboxMessage.MessageType.Should().Be("quote.created");
        outboxMessage.SentAt.Should().BeNull();
        outboxMessage.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task AddWithOutboxMessageAsync_WhenOutboxInsertViolatesUniqueMessageId_RollsBackTheQuoteToo()
    {
        _db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = "quote-created-1",
            MessageType = "quote.created",
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync();

        var quote = Quote.Create("Rollback Author", "This quote must not survive the failed transaction.");

        var act = async () => await _repository.AddWithOutboxMessageAsync(
            quote,
            "quote.created",
            _ => "{}",
            CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>();

        _db.Quotes.Count().Should().Be(0);
        _db.OutboxMessages.Count().Should().Be(1);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
