# Day 19 — Azure Service Bus Topics + DLQ

## Task

"Publish to a Service Bus topic with two subscriptions, consume with a competing-consumer worker, make handlers idempotent (dedupe on a message id), and demonstrate the dead-letter queue catching a poison message."

## Exercise

"Paste the publisher + consumer, the idempotency key handling, and proof a poison message landed in the DLQ."

## Architecture

```
Topic (quote-events)
 ├── Subscription A (sub-audit)         — worker slots A1, A2 (MaxConcurrentCalls=2)
 └── Subscription B (sub-notifications) — worker slots B1, B2 (MaxConcurrentCalls=2)
```

Every publish sends one message to the topic. Both subscriptions each get their
own independent copy (fan-out). Within a subscription, `ServiceBusProcessor`
runs up to two concurrent handler invocations against that one subscription
(competing consumers) — a small worker-slot pool (`A1`/`A2`, `B1`/`B2`) records
which concurrent invocation actually handled a given delivery, so the
consumer-activity log shows real competition, not a simulation.

Live resources (`thinkschool-rg`, Central India, subscription `708f56eb-d40f-4658-adde-d6f5866dad34`):

| Resource | Value |
|---|---|
| Service Bus namespace | `sb-quotesapi-thinkschool.servicebus.windows.net` (Standard tier) |
| Topic | `quote-events` |
| Subscription A | `sub-audit` (MaxDeliveryCount 3) |
| Subscription B | `sub-notifications` (MaxDeliveryCount 3) |
| Backend — Container App `quotes-api` | https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io |
| Frontend — Static Web App `thinkschool-ayush-swa` | https://polite-mushroom-04dd5ce00.7.azurestaticapps.net/messaging |

Authentication is `DefaultAzureCredential` throughout — no connection string
anywhere. Locally it resolves to the developer's own `az login` session
(`ManagedIdentityCredential` is explicitly excluded in Development so it
doesn't waste time probing IMDS on a non-Azure box); in the deployed
Container App it resolves to `quotes-api`'s existing user-assigned managed
identity, granted `Azure Service Bus Data Owner` on the namespace.

## Source locations

The backend lives inside the existing `day-1/QuotesApi` project (it is part
of that ASP.NET Core app, not a separate service):

- `day-1/QuotesApi/Messaging/` — publisher, processor/idempotency, activity
  log, subscription workers, options, DTOs
- `day-1/QuotesApi/Endpoints/MessagingEndpoints.cs` — `/api/messaging/*`
- `day-1/QuotesApi/Extensions/MessagingExtensions.cs` — DI wiring
- `day-1/QuotesApi/DTOs/MessagingRequests.cs`
- `day-1/QuotesApi/Data/AppDbContext.cs` — adds `ProcessedMessages`
- `day-1/QuotesApi/Migrations/2026...AddProcessedMessages.cs` (+ the
  `Migrations.SqlServer` equivalent, kept in sync)
- `day-1/QuotesApi/Tests.Domain/Messaging/` — focused unit tests

The frontend feature lives inside the existing Day-16/task-2 Angular
workspace, next to `jobs/`:

- `Day-16/task-2/src/app/messaging.ts` — models
- `Day-16/task-2/src/app/messaging.service.ts` — HTTP client
- `Day-16/task-2/src/app/messaging/` — `Messaging` component (ts/html/css/spec)
- `Day-16/task-2/src/app/app.routes.ts`, `app.html` — new `/messaging` route
  and nav tab

Only documentation, evidence, and screenshots live under `day-19/` itself —
putting the actual source there would have split the working ASP.NET Core
and Angular projects apart and broken compilation.

## Publisher

`QuoteEventPublisher` (`Messaging/QuoteEventPublisher.cs`) sends to the
**topic**, never to a subscription directly:

```csharp
public async Task<PublishedEvent> PublishAsync(
    string eventType, string payload, string? idempotencyKey, bool poison,
    CancellationToken cancellationToken)
{
    var messageId = MessageIdResolver.Resolve(idempotencyKey);
    var publishedAt = DateTimeOffset.UtcNow;

    var message = new ServiceBusMessage(payload)
    {
        MessageId = messageId,
        ContentType = "text/plain"
    };

    message.ApplicationProperties["EventType"] = eventType;
    message.ApplicationProperties["PublishedAtUtc"] = publishedAt.ToString("O");
    message.ApplicationProperties["Poison"] = poison;

    await _sender.SendMessageAsync(message, cancellationToken);

    return new PublishedEvent(messageId, eventType, _topicName, publishedAt, poison);
}
```

`MessageIdResolver.Resolve` is the deterministic idempotency key:

```csharp
public static string Resolve(string? idempotencyKey) =>
    string.IsNullOrWhiteSpace(idempotencyKey)
        ? Guid.NewGuid().ToString("N")
        : idempotencyKey.Trim();
```

An explicit idempotency key always produces the same `MessageId` — retrying
the same logical publish reuses it instead of minting a new one. A blank key
means "this is a new logical event", so it gets a fresh id.

## Consumers

`SubscriptionAWorker` / `SubscriptionBWorker` are thin `BackgroundService`
subclasses of `SubscriptionWorker`, each wrapping one `ServiceBusProcessor`
pinned to its own subscription with `MaxConcurrentCalls = 2`:

```csharp
_processor = client.CreateProcessor(
    _options.TopicName, subscriptionName,
    new ServiceBusProcessorOptions
    {
        AutoCompleteMessages = false,
        MaxConcurrentCalls = Math.Max(1, _options.MaxConcurrentCallsPerSubscription)
    });

_processor.ProcessMessageAsync += HandleMessageAsync;
_processor.ProcessErrorAsync += HandleErrorAsync;
await _processor.StartProcessingAsync(stoppingToken);
```

`AutoCompleteMessages = false` means the handler is fully in control of
settlement — it only completes a message after `IQuoteEventProcessor`
returns successfully, and abandons it (never completes it) on any failure,
poison or otherwise.

## Idempotency

`QuoteEventProcessor.ProcessAsync` dedupes on `(SubscriptionName, MessageId)`
against a persistent EF Core table (`ProcessedMessages`, SQLite in this demo,
same database the rest of QuotesApi already uses — not an in-memory
`HashSet`):

```csharp
var alreadyProcessed = await db.ProcessedMessages.AsNoTracking().AnyAsync(
    p => p.SubscriptionName == command.SubscriptionName && p.MessageId == command.MessageId,
    cancellationToken);

if (alreadyProcessed)
{
    return MessageProcessingOutcome.Duplicate; // skip business work, still completes the message
}

await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
db.ProcessedMessages.Add(new ProcessedMessage { SubscriptionName = ..., MessageId = ..., ... });

try
{
    await db.SaveChangesAsync(cancellationToken);
}
catch (DbUpdateException)
{
    // two workers raced the same MessageId - the DB's composite primary key
    // rejected the second insert. Still a Duplicate, not an error.
    await transaction.RollbackAsync(cancellationToken);
    return MessageProcessingOutcome.Duplicate;
}

await transaction.CommitAsync(cancellationToken);
return MessageProcessingOutcome.Processed;
```

The no-tracking pre-check is the fast path; the DB's composite primary key
on `(SubscriptionName, MessageId)` is the actual concurrency guarantee — it
is what stops two competing workers from both successfully processing the
same delivery, not the pre-check (see "Bug Found and Fixed" in `result.md`).
Same `MessageId` on two *different* subscriptions is fan-out, not a
duplicate — the composite key means both subscriptions process it once each.

## Poison Message

The publisher can tag a message `ApplicationProperties["Poison"] = true`.
The processor checks that flag before touching the database and throws
`PoisonMessageException` unconditionally — it never inserts a
`ProcessedMessages` row and never completes the message:

```csharp
if (command.Poison)
{
    throw new PoisonMessageException(command.MessageId);
}
```

The worker's handler catches that (and any other unhandled exception) and
explicitly abandons the message rather than completing it:

```csharp
catch (PoisonMessageException ex)
{
    logger.LogWarning(...);
    await args.AbandonMessageAsync(message, cancellationToken: args.CancellationToken);
}
```

Both subscriptions were created with `MaxDeliveryCount = 3`. Three abandoned
deliveries later, Service Bus moves the message to that subscription's DLQ
on its own — the app never calls dead-letter APIs itself.

## DLQ

`GET /api/messaging/dead-letters/{subscription}` peeks the subscription's
dead-letter sub-queue (`SubQueue.DeadLetter`) and returns the real
`DeadLetterReason`/`DeadLetterErrorDescription`. See "Dead-Letter Queue" in
`result.md` for the actual captured response
(`"deadLetterReason":"MaxDeliveryCountExceeded"`).

## Background Worker

`SubscriptionWorker.StopAsync` overrides the `BackgroundService` default to
stop the processor gracefully before the base class signals
`stoppingToken`:

```csharp
public override async Task StopAsync(CancellationToken cancellationToken)
{
    if (_processor is not null)
    {
        await _processor.StopProcessingAsync(cancellationToken);
        await _processor.DisposeAsync();
    }
    await base.StopAsync(cancellationToken);
}
```

`ExecuteAsync` itself just starts the processor and then awaits
`Task.Delay(Timeout.Infinite, stoppingToken)`, catching the resulting
`OperationCanceledException` on shutdown rather than letting it propagate —
all real work happens in the processor's own event handlers, each of which
receives and honors `args.CancellationToken`.

## UI

A new **Messaging** tab was added to the existing Day 18-style Angular
navigation (`Day-16/task-2/src/app/app.html`), next to Background Jobs — the
Background Jobs tab itself is untouched. The page follows the same
signal/polling pattern as the Jobs page: publish form, live subscription
counts, a polled consumer-activity table, and a Dead-Letter Queue panel with
a "Peek dead letters" action per subscription. Everything on the page calls
the real `/api/messaging/*` endpoints against the real Azure Service Bus
namespace — there is no simulated backend.

## Verification

See `result.md` "Verification Log" for the full sequence (local dev against
the real namespace, then re-verified against the deployed production
Container App + Static Web App) with real request/response evidence for
publish, fan-out, competing consumers, duplicate detection, the poison path,
and the DLQ.

## Screenshots

Captured against the **live deployed app**
(`https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`), not localhost:

- `docs/screenshots/01-service-bus-tab.png`
- `docs/screenshots/02-publish-message.png`
- `docs/screenshots/03-two-subscriptions.png`
- `docs/screenshots/04-competing-consumer.png`
- `docs/screenshots/05-idempotency-duplicate.png`
- `docs/screenshots/06-poison-message.png`
- `docs/screenshots/07-dead-letter-queue.png`

## What Would Break

- **Message schema changes** (renaming/removing an `ApplicationProperties`
  key like `EventType`/`Poison`) — both subscriptions' handlers read the
  same properties, so an unversioned change breaks both consumers at once,
  not just one.
- **MessageId semantics change** — if a caller ever generated a random
  `MessageId` per retry instead of reusing the idempotency key, every retry
  would look like a brand-new event and duplicate processing would go
  undetected. The dedupe entirely depends on the caller holding this
  contract.
- **Subscription changes** — renaming or deleting `sub-audit`/`sub-notifications`
  without updating `ServiceBus:SubscriptionA`/`SubscriptionB` in
  configuration leaves the corresponding worker unable to find its
  subscription at startup (`ServiceBusProcessor` throws), and any UI polling
  against a stale name gets 404s from `/dead-letters/{subscription}`.
- **MaxDeliveryCount changes** — raising it hides poison messages longer
  (more wasted redeliveries before DLQ); lowering it to 1 removes any
  tolerance for a transient failure, dead-lettering messages that would
  have succeeded on retry.
- **Deduplication store changes** — swapping `ProcessedMessages` for a
  different store (or losing the composite primary key) removes the actual
  concurrency guarantee; without a real unique constraint at the storage
  layer, two competing workers can both "succeed" on the same delivery.
- **Worker cancellation changes** — removing the `StopAsync` override would
  fall back to the base `BackgroundService.StopAsync`, which only signals
  `stoppingToken` and never calls `StopProcessingAsync` — in-flight message
  locks could be abandoned mid-shutdown instead of being released cleanly.
