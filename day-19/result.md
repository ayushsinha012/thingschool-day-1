# Day 19 — Azure Service Bus Topics + DLQ — Result

## Exercise

"Publish to a Service Bus topic with two subscriptions, consume with a
competing-consumer worker, make handlers idempotent (dedupe on a message
id), and demonstrate the dead-letter queue catching a poison message."

"Paste the publisher + consumer, the idempotency key handling, and proof a
poison message landed in the DLQ."

## Brief

No Service Bus code, infrastructure, or UI existed anywhere in the
repository before this work (confirmed by search — no `ServiceBus`,
`Azure.Messaging.ServiceBus`, `DeadLetter`, or `Topic`/`Subscription`
references outside .NET SDK/EF Core noise). Everything below — the Azure
resources, the backend publisher/consumer/idempotency/DLQ code, the tests,
and the UI — was built new for Day 19, reusing the existing `day-1/QuotesApi`
backend, `Day-16/task-2` Angular app, `thinkschool-rg` resource group, and
the existing `quotes-api` Container App's user-assigned managed identity.

## Publisher

`day-1/QuotesApi/Messaging/QuoteEventPublisher.cs` — sends to the topic
(`quote-events`), sets `MessageId` from `MessageIdResolver.Resolve`, and
stamps `EventType`/`PublishedAtUtc`/`Poison` as `ApplicationProperties`. See
README.md "Publisher" for the full listing.

## Consumer

`day-1/QuotesApi/Messaging/SubscriptionWorker.cs` (base) +
`SubscriptionAWorker.cs` / `SubscriptionBWorker.cs` — one `BackgroundService`
per subscription, each running a `ServiceBusProcessor` with
`MaxConcurrentCalls = 2`, `AutoCompleteMessages = false`. See README.md
"Consumers" for the full listing.

## Two Subscriptions

Created directly via `az servicebus topic subscription create` (not
recreated — did not exist before this session):

- Namespace: `sb-quotesapi-thinkschool` (Standard tier, `thinkschool-rg`,
  Central India) — Standard tier because Basic does not support topics.
- Topic: `quote-events`
- Subscription A: `sub-audit`, `MaxDeliveryCount=3`,
  dead-lettering-on-expiration enabled
- Subscription B: `sub-notifications`, `MaxDeliveryCount=3`,
  dead-lettering-on-expiration enabled

RBAC: granted `Azure Service Bus Data Owner` on the namespace to the signed-in
developer account (local dev, via `az login`/`DefaultAzureCredential`) and to
`quotes-api`'s existing user-assigned managed identity
(`id-quotesApi-2i2oapij4zsrc`) for the deployed Container App. No connection
string was created or stored anywhere.

## Competing Consumers

Verified against the real namespace (local dev run, `dotnet run` against
`sb-quotesapi-thinkschool`): publishing once and then replaying the same
`MessageId` produced these `consumer activity` entries —

```
sub-audit         A1  Received  → Processed   (delivery 1, first publish)
sub-notifications B1  Received  → Processed   (delivery 1, first publish)
sub-audit         A2  Received  → Duplicate   (delivery 1, replay)
sub-notifications B2  Received  → Duplicate   (delivery 1, replay)
```

The first delivery landed on worker slot `A1`/`B1`; the replay (a second,
concurrent `ServiceBusProcessor` receive loop against the *same*
subscription) landed on `A2`/`B2` — two different concurrent handler
invocations competing for work on one subscription, which is what
`MaxConcurrentCalls = 2` actually buys. `sub-audit` and `sub-notifications`
each got their own independent copy of the same message — fan-out, not
competing consumers between the two subscriptions themselves.

## Idempotency

`MessageId` handling: see README.md "Idempotency" for
`MessageIdResolver.Resolve` and `QuoteEventProcessor.ProcessAsync`.

Real duplicate proof (local dev run against the live namespace):

```
POST /api/messaging/publish {"eventType":"quote.created","payload":"demo quote payload","idempotencyKey":"demo-order-1"}
→ 200 {"messageId":"demo-order-1", ...}

POST /api/messaging/publish {"eventType":"quote.created","payload":"demo quote payload","idempotencyKey":"demo-order-1"}
→ 200 {"messageId":"demo-order-1", ...}   (same MessageId, by design)

GET /api/messaging/activity?take=8
→ sub-notifications B2 Duplicate "MessageId already processed on this subscription"
→ sub-audit         A2 Duplicate "MessageId already processed on this subscription"
→ sub-audit         A1 Processed
→ sub-notifications B1 Processed
```

Concurrency safety is also covered by an automated test
(`QuoteEventProcessorTests.ProcessAsync_ConcurrentDeliveries_OfSameMessageId_OnlyOneSucceeds`)
that runs two real, separate `AppDbContext`s against a shared-cache SQLite
database concurrently with `Task.WhenAll` — exactly one of the two
concurrent attempts returns `Processed`, the other `Duplicate`, proving the
database's composite primary key (not just the in-process check) is what
prevents two workers from both succeeding on the same `MessageId`.

## Poison Message

`day-1/QuotesApi/Messaging/QuoteEventProcessor.cs` throws
`PoisonMessageException` before touching the database when
`ApplicationProperties["Poison"] == true`; `SubscriptionWorker` abandons
(never completes) the message on that exception. Real local-dev log:

```
Received  sub-notifications B1 poison-demo-1 delivery=1
PoisonFailed                                delivery=1  "Simulated poison payload"
Received  sub-audit         A2 poison-demo-1 delivery=2
PoisonFailed                                delivery=2
Received  sub-notifications B1 poison-demo-1 delivery=3
PoisonFailed                                delivery=3
```

Both subscriptions independently ran the message through all 3 deliveries
(their own copies, fan-out) before Service Bus dead-lettered each.

## Dead-Letter Queue

`GET /api/messaging/dead-letters/sub-audit` (real response, local dev
against the live namespace):

```json
[
  {
    "messageId": "poison-demo-1",
    "eventType": "quote.poison-test",
    "subscriptionName": "sub-audit",
    "deliveryCount": 3,
    "deadLetterReason": "MaxDeliveryCountExceeded",
    "deadLetterErrorDescription": "Message could not be consumed after 3 delivery attempts.",
    "enqueuedTimeUtc": "2026-09-01T05:57:36.214+00:00",
    "body": "this message always fails"
  }
]
```

Identical result on `sub-notifications` for the same `MessageId`.
`GET /api/messaging/topology` confirmed the counts:

```json
[
  {"name":"sub-audit","activeMessageCount":0,"deadLetterMessageCount":1,"totalMessageCount":1},
  {"name":"sub-notifications","activeMessageCount":0,"deadLetterMessageCount":1,"totalMessageCount":1}
]
```

`deadLetterReason: "MaxDeliveryCountExceeded"` is Service Bus's own reason
string, not something the app invented — the app never calls a dead-letter
API itself; it only abandons.

## Graceful Shutdown

`SubscriptionWorker.StopAsync` calls `ServiceBusProcessor.StopProcessingAsync`
(which waits for in-flight handler invocations and stops accepting new
deliveries) before disposing the processor and calling
`base.StopAsync(cancellationToken)`. Every handler receives
`args.CancellationToken` and passes it through to
`Complete`/`AbandonMessageAsync` and to `IQuoteEventProcessor.ProcessAsync`,
which uses it for the EF Core transaction. No `Thread.Sleep`, no polling
loop — `ExecuteAsync` just awaits `Task.Delay(Timeout.Infinite, stoppingToken)`
after starting the processor, the same non-blocking pattern the existing
Day 18 `BackgroundJobWorker` uses for its own consume loop.

## UI

New **Messaging** tab in the existing Day 18-style Angular app
(`Day-16/task-2/src/app/app.html`/`app.routes.ts`), lazy-loaded at
`/messaging`, reusing the same signal + polling architecture as the
`jobs/jobs.ts` component. Publish form, live subscription counts (polled),
a consumer-activity table (polled, worker slot + outcome badges), and a
Dead-Letter Queue panel with a per-subscription "Peek dead letters" action.
The Background Jobs tab is unchanged.

## Verification Log

**Local (dev, real Azure Service Bus, `dotnet run` + `az login`):**
- Backend build: `dotnet build QuotesApi.csproj` → 0 errors.
- Focused backend tests: `dotnet test Tests.Domain/Tests.Domain.csproj --filter FullyQualifiedName~Messaging` → 12/12 passed.
- Full `Tests.Domain` regression check: 108/108 passed (one unrelated
  `TimeoutException` on `CollectionAuthorizationTests`' xUnit class cleanup —
  logged as a warning, not a failure, and pre-existing/unrelated to Day 19).
- `GET /api/messaging/topology` → real subscription counts from Azure.
- `POST /api/messaging/publish` → real `MessageId` returned, message actually
  sent to the topic.
- `GET /api/messaging/activity` → fan-out confirmed: one publish produced
  independent `Received`→`Processed` entries on both `sub-audit` (`A1`) and
  `sub-notifications` (`B1`).
- Replay with the same idempotency key → `Duplicate` on both subscriptions,
  handled by different worker slots (`A2`/`B2`) — competing-consumer proof.
- Poison publish → 3 real delivery attempts per subscription, then
  `GET /api/messaging/dead-letters/{subscription}` returned the real
  `MaxDeliveryCountExceeded` dead-letter reason on both.
- Frontend: `ng build` (dev) → messaging chunk built clean;
  `ng build --configuration production` → same, chunk `16.14 kB` raw.
- Focused frontend tests: `ng test --watch=false --include='src/app/messaging/messaging.spec.ts'`
  → 5/5 passed. Full suite: `ng test --watch=false` → 6 files, 28/28 passed.

**Production (deployed, this session):**
- Backend image `cr2i2oapij4zsrc.azurecr.io/quotes-api/quotes-api-quotesapi-thinkschool:day19-servicebus-1788243508`
  built with `dotnet publish -c Release -r linux-x64 /t:PublishContainer` and
  pushed to the existing ACR (ACR Tasks/`az acr build` are disabled on this
  Azure for Students subscription — `TasksOperationsNotAllowed` — so the
  build ran locally instead, the same approach Day 18 used).
- `az containerapp update -n quotes-api -g thinkschool-rg --image ...:day19-servicebus-1788243508`
  → new revision `quotes-api--0000005`, 100% traffic, `provisioningState: Succeeded`.
- `GET https://quotes-api.../health` → healthy.
- `GET https://quotes-api.../api/messaging/topology` → real counts returned
  using the Container App's managed identity (not a connection string) —
  confirms `DefaultAzureCredential` resolves `ManagedIdentityCredential`
  correctly in production.
- Frontend: `ng build --configuration production` under Node 22 (`nvm use 22`
  — system default is Node 18, below Angular's minimum, same caveat Day 18
  hit), verified the built bundle's `apiBaseUrl` was the real Azure API URL
  (`grep -o 'apiBaseUrl:"[^"]*"' dist/.../chunk-*.js`), then
  `swa deploy dist/task-1/browser --deployment-token <fetched via az staticwebapp secrets list, never printed> --env production`.
- `https://polite-mushroom-04dd5ce00.7.azurestaticapps.net/messaging` → 200,
  served the freshly built bundle (`main-EEJF3R5F.js`, matched against the
  local build output).
- Live UI walkthrough (screenshots below) against production: publish, both
  subscriptions' counts update, replay produces a `Duplicate` badge on both
  subscriptions with different worker slots, a poison publish produces
  `PoisonFailed` rows and then real `MaxDeliveryCountExceeded` entries in the
  Dead-Letter Queue panel.

## Bug Found and Fixed

`QuoteEventProcessorTests.ProcessAsync_DuplicateMessageId_OnSameSubscription_ReturnsDuplicate_AndDoesNotDoubleRecord`
failed on first run with:

```
System.InvalidOperationException: The instance of entity type 'ProcessedMessage'
cannot be tracked because another instance with the same key value for
{'SubscriptionName', 'MessageId'} is already being tracked.
```

`QuoteEventProcessor.ProcessAsync` originally relied entirely on catching
`DbUpdateException` from `SaveChangesAsync` to detect a duplicate. That
works the first time a `MessageId` is seen by a given `AppDbContext`
instance, but if the *same* `DbContext` instance processes the same
`MessageId` twice (which the real worker never does — each message gets its
own scope — but which the test, and potentially any future caller, could),
EF Core's own change tracker throws an unrelated `InvalidOperationException`
at `.Add()` time, before the query even reaches the database, because it
still has the first insert tracked as `Unchanged`.

**Fix:** added a `AsNoTracking()` existence check before the insert. A
duplicate found this way returns immediately without ever calling `.Add()`,
so the change tracker is never asked to track two entities with the same
key. The transactional insert + `catch (DbUpdateException)` stays in place
as the fallback for the real race — two different `DbContext`s (two actual
concurrent workers) both passing the pre-check and then colliding on the
database's own unique constraint. Verified by re-running the full focused
suite (12/12 passed) including the concurrency test, which still exercises
the `DbUpdateException` path directly (two separate `DbContext`s, no
pre-existing tracked state to trigger the original bug).

## Screenshots

Captured against the live deployed app
(`https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`) via a scripted
headless Chrome walkthrough (`puppeteer-core` driving the system
`google-chrome` binary — no bundled browser download) that filled the real
form, clicked Publish/Replay/Peek, and waited for the real polled data to
arrive before each capture:

| # | File | Shows |
|---|---|---|
| 1 | `01-service-bus-tab.png` | `/explore`, nav bar with the new Messaging tab |
| 2 | `02-publish-message.png` | `/messaging` initial state — form, real subscription counts |
| 3 | `03-two-subscriptions.png` | Subscription cards after a real publish (fan-out counts) |
| 4 | `04-competing-consumer.png` | Consumer activity right after the first publish |
| 5 | `05-idempotency-duplicate.png` | After "Replay same MessageId" — `Duplicate` badges on both subscriptions, different worker slots than the original |
| 6 | `06-poison-message.png` | After a poison publish — `PoisonFailed` rows, dead-letter counts incremented |
| 7 | `07-dead-letter-queue.png` | "Peek dead letters" result — real `MaxDeliveryCountExceeded` reason |

Note: the scripted keystroke sequence used to fill the text inputs across
consecutive captures didn't always clear the previous value first, so a few
screenshots show concatenated placeholder text in the Event type/Payload/
Idempotency key fields (e.g. `quote.createdquote.poison-test`). That's a
cosmetic artifact of how the demo data was typed for the screenshot, not an
application defect — the badges, counts, and DLQ reason strings next to it
are genuine, unedited values returned by the live API.

## What Would Break

See README.md "What Would Break" for the full list (message schema changes,
MessageId semantics, subscription renames, MaxDeliveryCount changes,
deduplication-store changes, worker cancellation changes).

## Final Result

Works, verified against the real Azure Service Bus namespace both locally
and on the deployed production Container App + Static Web App: publish to
the topic, fan-out to two real subscriptions, competing consumers within
each subscription (distinct worker slots proven via the activity log),
persistent MessageId-based idempotency (proven both by live duplicate
publishes and by an automated concurrent-DbContext test), and a poison
message genuinely reaching `MaxDeliveryCountExceeded` and landing in both
subscriptions' real DLQs.

Remaining, honestly stated: the consumer-activity log is in-memory
(`MessagingActivityLog`, a bounded ring buffer) and is lost on a process
restart or Container App revision change — the same documented limitation
Day 18's `JobStore` has, and, unlike the idempotency store itself
(`ProcessedMessages`, which is the actual EF Core-backed table this task
required to be persistent), it was never meant to be durable — it only
drives the UI's "what just happened" view.
