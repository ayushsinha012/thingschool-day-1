# Day 12 - Task 1: CQRS-lite on Quotes

## What this task is about

The Quotes API used one repository (`IQuoteRepository`) for both writing a quote and reading a quote back. This task splits that into a command side and a query side using MediatR, so creating a quote and reading a quote no longer go through the same code path.

## What I implemented

- A command (`CreateQuoteCommand`) for creating a quote, handled by `CreateQuoteCommandHandler`, which still goes through the existing `IQuoteRepository` to persist the quote.
- A query (`GetQuoteByIdQuery`) for reading a quote by id, handled by `GetQuoteByIdQueryHandler`, which queries `AppDbContext` directly instead of going through the repository, and returns a separate read model (`QuoteReadModel`) instead of the `Quote` entity.
- MediatR wired into DI in `InfrastructureExtensions.cs`.
- The `POST /` and `GET /{id}` endpoints in `QuoteEndpoints.cs` updated to send the command/query through `IMediator` instead of calling the repository directly.
- Unit tests for both handlers.

## Files in this folder

```
QuotesApi/
  Application/Quotes/
    CreateQuoteCommand.cs           command + result
    CreateQuoteCommandHandler.cs    command handler (write side)
    GetQuoteByIdQuery.cs            query + read model
    GetQuoteByIdQueryHandler.cs     query handler (read side)
  Repositories/
    IQuoteRepository.cs             repository interface used by the write side
    QuoteRepository.cs              repository implementation used by the write side
  Models/
    Quote.cs                        write model / entity
  Data/
    AppDbContext.cs                 EF context, queried directly by the read side
  Extensions/
    InfrastructureExtensions.cs     MediatR registration
  Endpoints/
    QuoteEndpoints.cs                endpoints, now dispatching through IMediator
  Tests.Domain/
    CreateQuoteCommandHandlerTests.cs
    GetQuoteByIdQueryHandlerTests.cs
    TestDoubles/InMemoryQuoteRepository.cs
```

## How the command path works

`POST /` binds the request to `CreateQuoteRequest`, validates it, then builds a `CreateQuoteCommand(Author, Text)` and sends it through `IMediator`. `CreateQuoteCommandHandler` picks it up, calls `Quote.Create(...)` to build the entity, saves it through `IQuoteRepository.AddAsync`, and returns a `CreateQuoteResult` with the saved quote's fields. The endpoint maps that result to the HTTP response.

## How the query path works

`GET /{id}` builds a `GetQuoteByIdQuery(id)` and sends it through `IMediator`. `GetQuoteByIdQueryHandler` does not use the repository at all - it queries `AppDbContext.Quotes` directly with `AsNoTracking()`, filters out soft-deleted quotes, and projects straight into a `QuoteReadModel` (id, author, text, a formatted `Display` string, and `CharacterCount`). If nothing matches, it returns null and the endpoint responds with 404.

## Tests

`CreateQuoteCommandHandlerTests.cs` uses the in-memory repository test double and checks that a valid command persists the quote and that a blank author throws without persisting anything.

`GetQuoteByIdQueryHandlerTests.cs` uses a real `AppDbContext` backed by an in-memory SQLite connection and checks that an existing quote comes back shaped as a read model, a soft-deleted quote returns null, and a non-existent id returns null.
