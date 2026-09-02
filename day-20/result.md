# Day 20 — Transactional Outbox — Result

## Exercise

"A DB write and a queue publish must not diverge. Implement the transactional outbox: write the domain change + an outbox row in one EF transaction, then a relay publishes and marks sent. Prove no message is lost if the publish step crashes."

"Paste the outbox table + relay. Describe the crash scenario you tested and why no message is lost or duplicated (at-least-once + idempotent consumer)."

## Brief

Reused Day 18's `BackgroundService` + `IServiceScopeFactory` pattern and Day 19's Service Bus publisher and idempotent consumer (`QuoteEventProcessor` / `ProcessedMessage`). New work: an `OutboxMessage` table, an atomic quote+outbox write on `POST /api/quotes`, and an `OutboxRelayWorker`/`OutboxRelayProcessor` that publishes unsent rows and marks `SentAt` only after a successful publish. Canonical source: `day-1/QuotesApi/`. Frontend canonical source: `Day-16/task-2/` (the live Angular app also mirrored under `day-19/src/frontend/` for docs, same convention followed here).

## Outbox Table

`day-1/QuotesApi/Outbox/OutboxMessage.cs`:

```csharp
public class OutboxMessage
{
    public int Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string MessageType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int AttemptCount { get; set; }
    public string? LastError { get; set; }
}
```

`AppDbContext.OnModelCreating` adds a unique index on `MessageId` and a plain index on `SentAt`. Migrations: `Migrations/20260902050129_AddOutboxMessages.cs` (SQLite) and `Migrations.SqlServer/Migrations/20260902050152_AddOutboxMessages.cs` (SQL Server), generated with `dotnet ef migrations add AddOutboxMessages` against each context, alongside the existing `ProcessedMessage`/`AddQuoteAuthorIndex` history (nothing rewritten or deleted).

## Atomic EF Transaction

`day-1/QuotesApi/Repositories/QuoteRepository.cs`:

```csharp
public async Task<Quote> AddWithOutboxMessageAsync(
    Quote quote,
    string eventType,
    Func<Quote, string> buildPayload,
    CancellationToken cancellationToken)
{
    await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

    try
    {
        _db.Quotes.Add(quote);
        await _db.SaveChangesAsync(cancellationToken);

        _db.OutboxMessages.Add(new OutboxMessage
        {
            MessageId = $"quote-created-{quote.Id}",
            MessageType = eventType,
            Payload = buildPayload(quote),
            CreatedAt = DateTimeOffset.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
    catch
    {
        await transaction.RollbackAsync(cancellationToken);
        throw;
    }

    return quote;
}
```

`CreateQuoteCommandHandler` calls this instead of the plain `AddAsync`, building a `quote.created` payload from the just-created quote. The `MessageId` is derived from the quote's own generated `Id`, so it is deterministic for that domain row — not a random GUID per attempt.

## Relay

`day-1/QuotesApi/Outbox/OutboxRelayProcessor.cs` (the per-batch/per-row unit of work):

```csharp
public async Task<bool> PublishOneAsync(int outboxMessageId, CancellationToken cancellationToken)
{
    var claimed = await db.OutboxMessages
        .Where(message => message.Id == outboxMessageId && message.SentAt == null)
        .ExecuteUpdateAsync(
            setters => setters.SetProperty(m => m.AttemptCount, m => m.AttemptCount + 1),
            cancellationToken);

    if (claimed == 0) return false;

    var message = await db.OutboxMessages.AsNoTracking()
        .FirstOrDefaultAsync(m => m.Id == outboxMessageId, cancellationToken);
    if (message is null) return false;

    try
    {
        await publisher.PublishAsync(message.MessageType, message.Payload, message.MessageId, poison: false, cancellationToken);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        await db.OutboxMessages
            .Where(m => m.Id == outboxMessageId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.LastError, ex.Message), cancellationToken);
        return false;
    }

    await db.OutboxMessages
        .Where(m => m.Id == outboxMessageId && m.SentAt == null)
        .ExecuteUpdateAsync(
            setters => setters
                .SetProperty(m => m.SentAt, DateTimeOffset.UtcNow)
                .SetProperty(m => m.LastError, (string?)null),
            cancellationToken);

    return true;
}
```

The "claim" is a single atomic `UPDATE ... WHERE SentAt IS NULL` that increments `AttemptCount`; a zero row count means another relay instance already claimed it, so this instance backs off instead of publishing. `SentAt` is set only after `PublishAsync` returns without throwing. `OutboxRelayWorker` (BackgroundService, `PeriodicTimer`, 2s poll by default) drives this per tick from its own DI scope, isolates exceptions per tick (a bad batch never kills the worker), and respects `CancellationToken` throughout — no `Thread.Sleep`.

Current deployment runs one relay instance in-process alongside the API (`scaleMinReplicas: 1`). The atomic claim above is what would make a second concurrent instance safe if the app were scaled out; that multi-instance path itself was not load-tested this session — documented honestly rather than assumed.

## Service Bus

Reused Day 19's `IQuoteEventPublisher` (`QuoteEventPublisher`, `Azure.Messaging.ServiceBus`, `DefaultAzureCredential`, no connection string) unchanged. The relay calls `PublishAsync(message.MessageType, message.Payload, message.MessageId, poison: false, ct)` against the existing `quote-events` topic (`sb-quotesapi-thinkschool` namespace, `sub-audit`/`sub-notifications` subscriptions) — no new topic, subscription, or publisher was created.

## Idempotency

Reused Day 19's `QuoteEventProcessor` and durable `ProcessedMessage` table (`(SubscriptionName, MessageId)` primary key) unchanged. The relay's `idempotencyKey` argument is always the outbox row's own `MessageId`, so a message redelivered by the relay (same `MessageId`, different Service Bus delivery) is recognized as a duplicate by the same mechanism Day 19 already proved handles duplicate *Service Bus* deliveries — no second idempotency system was built.

## Local Verification Log

`dotnet test Tests.Domain/Tests.Domain.csproj --filter "FullyQualifiedName~Outbox|FullyQualifiedName~CreateQuoteCommandHandlerTests"` → **10/10 passed**:

- `AddWithOutboxMessageAsync_OnSuccess_PersistsQuoteAndOutboxMessageTogether` — atomic write.
- `AddWithOutboxMessageAsync_WhenOutboxInsertViolatesUniqueMessageId_RollsBackTheQuoteToo` — real `DbUpdateException` (seeded a conflicting `MessageId` so the second insert hits the real unique index), quote count and outbox count both stay at their pre-transaction values afterward.
- `ProcessBatchAsync_PublishesPendingMessage_AndMarksSentAt` — relay success.
- `ProcessBatchAsync_WhenPublishFails_LeavesSentAtNull_RecordsError_AndRetrySucceeds` — publish failure leaves `SentAt` null, `AttemptCount` incremented, `LastError` recorded; a second call (retry) succeeds and clears `LastError`.
- `ProcessBatchAsync_WithMultiplePendingMessages_PublishesEachIndependently` — 3 independent rows, all published.
- `CrashAfterPublishBeforeSentAt_MessageIsRepublishedOnRestart_AndConsumerDeduplicatesTheDuplicate` — see "Crash Scenario" below.
- `Worker_StopAsync_CompletesPromptly_WhileWaitingOnTheNextPollTick` — cancellation.
- `Worker_WhenTheDatabaseIsUnreachable_RecordsTheFailure_AndKeepsPolling_WithoutCrashingTheHost` — worker exception isolation (this test is what caught bug #2 below).
- `Handle_WithValidAuthorAndText_PersistsQuoteAndReturnsResult` (updated) — the handler's fake-repository unit test now also asserts an outbox message was recorded.

Regression check: full `Tests.Domain` project (all days) → **116/116 passed** (one pre-existing, unrelated `TimeoutException` on `CollectionAuthorizationTests`' xUnit class cleanup, logged as a warning not a failure — present before this session's changes too).

Frontend: `ng build --configuration development` → outbox chunk built clean (21.75 kB raw). `ng test --watch=false` → **32/32 passed** (7 files), including the 4 new `outbox.spec.ts` tests (loading, empty, populated with a pending+sent row, and error state).

Live local run against the real Azure Service Bus namespace (not a fake): started `day-1/QuotesApi` with `dotnet run`, logged in as the seeded test user, `POST /api/quotes` twice. Both times: the quote row and its `OutboxMessage` row appeared together (confirmed via direct `sqlite3 quotes.db` query — one `INSERT INTO Quotes` and one `INSERT INTO OutboxMessages` in the same logged transaction), the relay picked each row up within its 2s poll and set `SentAt`, and `GET /api/outbox` reflected the correct pending → sent transition. `AttemptCount` and `LastError` behaved as expected on the earlier failure path too (covered by the automated test above, not re-run manually against the real broker).

## Azure Verification Log

- Reused: resource group `thinkschool-rg`, Service Bus namespace `sb-quotesapi-thinkschool` (topic `quote-events`, subscriptions `sub-audit`/`sub-notifications`), Container Apps environment `thinkschool-env`, Container App `quotes-api`, its user-assigned managed identity. None recreated.
- Deployed the Day-20 code: `dotnet publish -c Release -r linux-x64 /t:PublishContainer -p:ContainerRegistry=cr2i2oapij4zsrc.azurecr.io -p:ContainerRepository=quotes-api/quotes-api-quotesapi-thinkschool -p:ContainerImageTag=day20-outbox-<timestamp>` (same local-publish-to-ACR approach Day 18/19 used, since `az acr build`/ACR Tasks are disabled on this Azure for Students subscription; required `az acr login` first — a fresh `dotnet publish` push failed with `CONTAINER1016` until logged in), then `az containerapp update -n quotes-api -g thinkschool-rg --image ...` → new revision `quotes-api--0000007`, 100% traffic, `provisioningState: Succeeded`.
- Production smoke test against `https://quotes-api.politeocean-3efec37e.centralindia.azurecontainerapps.io`: `GET /health` → 200; logged in as the seeded test user; `POST /api/quotes` → quote created; `GET /api/outbox` (2s later) → the row present with `sentAt` populated (`attemptCount: 1`, `lastError: null`) — the atomic write and the relay's publish-then-mark-sent both confirmed working against the real deployed Container App, using its managed identity against the real Service Bus namespace (not a connection string).

**Durable storage attempt and rollback (a real finding, not a simulated one):**

- Attempted fix: created a new storage account (`stday20quotesapi`, Standard_LRS) and Azure Files share (`quotesapi-outbox-data`), registered it with the Container Apps environment, mounted it into `quotes-api` at `/data`, and pointed `ConnectionStrings__DefaultConnection` at `Data Source=/data/quotes.db`. Applied via `azd provision`; confirmed live via `az containerapp show` (`volumes`, `volumeMounts`, and the new connection string all present on the deployed template).
- Result: the container crash-looped. `az containerapp logs show` showed `Microsoft.Data.Sqlite.SqliteException: SQLite Error 5: 'database is locked'` thrown from EF Core's own migration-lock acquisition (`CREATE TABLE IF NOT EXISTS "__EFMigrationsLock"` inside `SqliteHistoryRepository.AcquireDatabaseLock`), an unhandled exception that terminated `Program.Main` before the app could start serving requests.
- Root cause: Azure Files exposed over SMB does not reliably support the byte-range/advisory file locking SQLite's locking model depends on. This is a documented SQLite limitation (SQLite's own guidance is not to put its database files on a network filesystem), not a misconfiguration of the mount — the volume, mount path, and connection string were all correct and confirmed present on the container.
- Recovery: reverted `ConnectionStrings__DefaultConnection` back to `Data Source=/tmp/quotes.db` via `az containerapp update --set-env-vars` (with explicit user confirmation, since this action was flagged as production-impacting by the environment's own permission classifier and correctly required approval before running). Confirmed `GET /health` → 200 again afterward, and re-ran the production smoke test above against the recovered revision.
- `infra/resources.bicep` was rolled back to match: the `outboxStorageAccount`/`outboxEnvironmentStorage` resources and the container's `volumes`/`volumeMounts` were removed, and `ConnectionStrings__DefaultConnection` reverted to `/tmp/quotes.db`, so a future `azd provision` reproduces the working state, not the crash-looping one. The storage account and file share themselves were left in place in Azure (harmless, minimal cost) rather than torn down under time pressure, but nothing references them anymore.
- **The correct fix, not implemented this session**: switch the durable store to SQL Server via the `Migrations.SqlServer` path that already exists in the repo (originally scaffolded for Testcontainers-based integration tests), pointed at a new database on the existing `thinkschool-day7-sql-0c0dda` server (not its Day-7 database) with Azure AD/managed-identity authentication instead of a password. That requires an `InfrastructureExtensions.AddInfrastructure` provider-selection branch, an AAD contained-user grant for the Container App's managed identity, and a firewall rule for Azure services — real code and infra changes, correctly out of scope to rush through after the SMB finding above.

**Limitation found this session:** publishing to the real `sb-quotesapi-thinkschool` Service Bus namespace from this session's environment consistently returns a clean AMQP send completion (`SendAsync done.`, no exception, confirmed via `Azure.Core.Diagnostics.AzureEventSourceListener` wire-level tracing) with valid `DefaultAzureCredential`/`AzureCliCredential` tokens and confirmed `Azure Service Bus Data Owner` RBAC on the namespace — but the message was not independently observable afterward via `PeekMessagesAsync`, `ReceiveMessageAsync`, or batch receive on `sub-audit` (tried PeekLock, ReceiveAndDelete, and both AMQP and AMQP-over-WebSockets transports, waited over 60s across multiple attempts), and neither the topic's `sizeInBytes` nor the subscriptions' `countDetails` changed after several confirmed sends. Checked and ruled out: RBAC, FQDN, subscription SQL filter (`1=1`, passes everything), duplicate detection (disabled), forwarding (none configured), transport. This looks like an environment/session-specific reachability issue between this sandbox and that namespace, not a defect in the outbox/relay/consumer code — the same publisher class and call shape that Day 19 proved working is used unchanged here, and the write side of the pattern (`SentAt` update after a non-throwing publish call) is exactly what the code is supposed to do with a "successful" send. Recorded honestly rather than claimed as fully verified; see "Final Result".

## UI

New "Outbox" tab in `Day-16/task-2` (the live Angular app), added next to Messaging in `app.routes.ts` and `app.html`, same visual language (`.page`/`.results`/`.status-badge` classes, `interval`+`switchMap`+`takeUntilDestroyed` polling pattern from `messaging.ts`/`jobs.ts`). Shows relay status (last run time, last batch published count, last error) from `GET /api/outbox/status`, and the outbox message list (MessageId, type, Pending/Sent badge, sent time, attempt count, last error) from `GET /api/outbox`, polling every 2s. Loading, empty ("No outbox messages yet"), and error states are all real, driven by the actual HTTP responses — not simulated.

## Bug Found and Fixed

1. **`DateTimeOffset` in `ORDER BY` against SQLite.** `OutboxRelayProcessor.ProcessBatchAsync` and `GET /api/outbox` both originally ordered by `CreatedAt` (a `DateTimeOffset` column). Running the real API locally threw `System.NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses` on the very first `GET /api/outbox` call — caught by the live local run, not by the unit tests (which happened to not exercise ordering with more than one row at the time). Fixed by ordering on `Id` instead, which is monotonic with insertion order on both SQLite and SQL Server and avoids the provider-specific translation gap entirely.
2. **Worker exception isolation gap.** `OutboxRelayWorker.RunOnceAsync` originally created its DI scope and resolved `OutboxRelayProcessor` *before* the try/catch block. The `Worker_WhenTheDatabaseIsUnreachable_...` test (written to prove exception isolation) initially failed with an unhandled `InvalidOperationException` from a deliberately-broken test DI registration — which showed the resolution call sat outside the safety net. In production this would mean any DI/scope-creation failure (not just a publish failure) could crash the whole worker instead of being logged and retried next tick. Fixed by moving scope creation and resolution inside the try block, matching the isolation guarantee Day 18's `BackgroundJobWorker` already has.

## UI Redeployment

The Outbox tab, route, and component described above were already fully
implemented and wired into `Day-16/task-2` (`app.routes.ts`, `app.html`)
before this session resumed — nothing in the UI itself needed to be built
or rewired. What was actually broken: the live Azure Static Web App
(`thinkschool-ayush-swa`, `https://polite-mushroom-04dd5ce00.7.azurestaticapps.net`)
was still serving a frontend build from before the Outbox tab existed.
Confirmed, not assumed: `curl`ing the deployed `index.html` showed
`Last-Modified: Tue, 01 Sep 2026`, its referenced `main-*.js` bundle
contained zero occurrences of the string `outbox`, and it did contain
`messaging` — i.e. it was the Day-19 build, one day older than the
Outbox tab's own files.

Fix: built `Day-16/task-2` with `ng build --configuration production`
(Node 18 in the default shell is below Angular 21's minimum of Node 20.19,
so this used `nvm use 22`, an already-installed Node version — no new
install), confirmed the `outbox` lazy chunk was present in the build
output and `ng test --watch=false` still passed 32/32, then deployed with
the same command Day 17 used for this exact Static Web App —
`swa deploy dist/task-1/browser --env production` — using a deployment
token fetched via `az staticwebapp secrets list` and passed only through
an environment variable, never printed. No Static Web App, Container App,
or Service Bus resource was created, recreated, or reconfigured; only the
static frontend files changed.

Verified live afterward: the deployed `index.html`'s `Last-Modified`
advanced to the deploy time, its `main-*.js` bundle now contains `outbox`,
the bundle hash matches the local production build exactly, and `GET
/outbox` on the live Static Web App returns `200` via the SPA fallback.

## Screenshots

`docs/screenshots/`, captured with a single headless Chrome (`pyppeteer`,
`google-chrome-stable --no-sandbox`) session against the live redeployed
site, immediately closed after 3 screenshots — no retries, no parallel
browser sessions:

![Outbox tab in the app navigation](docs/screenshots/01-outbox-tab.png)

The app nav with the Outbox tab visible next to Messaging, on the live
Static Web App.

![Outbox page showing relay status and the real outbox message list](docs/screenshots/02-outbox-page.png)

The Outbox page's relay-status panel and message table, both populated
from the real deployed backend (`GET /api/outbox/status`, `GET /api/outbox`).

![Outbox page with a message in the Sent state](docs/screenshots/04-outbox-sent.png)

The same real state, showing the one production outbox row
(`quote-created-1`) in the `Sent` state.

`03-outbox-pending.png` was intentionally **not captured**: at capture
time, production's only outbox row had already been marked `Sent` by the
relay (confirmed via `GET /api/outbox`), and no genuine `Pending` row
existed. Producing one would have meant writing a new quote to production
data outside this session's scope, which was avoided rather than staged
as a fake pending state. `05-crash-recovery.png` and
`06-idempotent-duplicate.png` were also not captured — that scenario is
proven by the automated test
(`CrashAfterPublishBeforeSentAt_MessageIsRepublishedOnRestart_AndConsumerDeduplicatesTheDuplicate`,
see above), not by anything the UI itself displays, so a screenshot of it
would not show anything real.

## What Would Break This

- **Non-atomic writes**: splitting the quote insert and the outbox insert into two transactions reopens the exact divergence window this exercise exists to close.
- **Non-deterministic `MessageId`**: a random GUID generated per relay attempt (instead of one derived from the quote's own `Id`) would mean a redelivered outbox row produces a *new* `MessageId` each time, defeating the consumer's dedupe key and turning safe at-least-once delivery into real duplicate processing.
- **Missing durable outbox storage**: the original `/tmp/quotes.db` is wiped on every container restart/replacement — any unsent (or even sent-but-not-yet-consumed) row would simply disappear, which is the data loss this whole task is meant to prevent. This is why the Azure Files mount was added rather than left as-is.
- **Incorrect claiming**: without the conditional `ExecuteUpdateAsync` (`WHERE SentAt IS NULL`), two relay instances scaled out could both read the same unsent row and both attempt to publish before either marks it sent — still safe today only because the consumer is idempotent, not because the claim prevents the race.
- **Non-idempotent consumer**: if `QuoteEventProcessor` didn't check `ProcessedMessages` (or if that table were in-memory instead of durable), every at-least-once redelivery would become a real duplicate business effect instead of a no-op.
- **Service Bus configuration drift**: renaming the topic/subscriptions or changing `ServiceBusOptions` without updating both the publisher and the consumer's config would silently stop delivery with no error surfaced on the publish side (the publish call itself doesn't validate that a subscription is listening).
- **Schema changes without a migration**: adding/removing an `OutboxMessage` column without a paired EF migration for both the SQLite and SQL Server contexts would break whichever provider wasn't updated, the same dual-migration discipline `ProcessedMessage` already established.

## Final Result

**LOCAL VERIFIED:**
- Atomic domain+outbox transaction (success and rollback), against a real SQLite `AppDbContext`.
- Relay claim/publish/mark-sent, publish failure leaving `SentAt` null with `LastError` and `AttemptCount` tracked, and successful retry.
- Crash-after-publish-before-`SentAt` scenario: the row is republished on "restart" with the same `MessageId`, and the real `QuoteEventProcessor` recognizes the second delivery as a duplicate — no second business effect.
- Worker cancellation and per-tick exception isolation.
- Live local run of the real API against the real Azure Service Bus namespace: quote creation, atomic outbox row creation, and the relay's non-throwing publish call all confirmed (via direct SQLite inspection and `GET /api/outbox`/`/status`).
- Angular Outbox tab: build clean, 32/32 tests passing, real loading/empty/error states.

**AZURE VERIFIED:**
- Existing Service Bus namespace/topic/subscriptions, Container App, and managed identity reused without recreation.
- Day-20 code built and pushed to the existing ACR, deployed to `quotes-api` via `az containerapp update --image` (new revision, 100% traffic, `provisioningState: Succeeded`).
- The atomic domain+outbox transaction and the relay's publish-then-mark-sent both confirmed working against the live, deployed Container App (`POST /api/quotes` → `GET /api/outbox` showed `sentAt` populated within 2 seconds), using the Container App's managed identity against the real Service Bus namespace.

**NOT VERIFIED / LIMITATION:**
- **Durable outbox storage in Azure.** The Azure Files (SMB) mount attempted for this is incompatible with SQLite's locking model (see "Azure Verification Log" for the exact crash and root cause) and was rolled back; production currently runs on the same ephemeral `/tmp/quotes.db` it used before this session. The outbox pattern's *correctness* (atomicity, at-least-once, idempotent consumer) is fully verified against a real durable SQLite database locally; what is not yet true in this Azure deployment is that the store surviving a container restart is that same database. The right fix — SQL Server via the already-scaffolded `Migrations.SqlServer` path — is identified but not implemented this session.
- End-to-end Service Bus subscriber-side delivery (a published outbox message actually landing on `sub-audit`/`sub-notifications`, visible via peek/receive) could not be independently confirmed from this session's environment, locally or in Azure — the publish call itself succeeds cleanly at the AMQP level with confirmed RBAC, but subsequent visibility on the subscription could not be observed within this session despite multiple independent checks (PeekLock, ReceiveAndDelete, AMQP and AMQP-over-WebSockets transports, over 60s of waiting). This does not implicate the outbox/relay/consumer logic itself (which is fully verified against a real database and a real, non-throwing publish call), but it means the very last hop — actual Service Bus fan-out to a subscription, in this environment, this session — is an honest gap, not a claimed success.
- Multi-instance relay scale-out (the atomic claim is designed for it, but it was not load-tested with more than one relay instance running concurrently).
- No exactly-once delivery is claimed anywhere in this design; the guarantee is at-least-once with an idempotent consumer.
