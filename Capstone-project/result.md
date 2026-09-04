# Day 22 — Capstone Kickoff: Design + Scaffold

## Task (as given)

> Pick a real product slice. Design it as a modular monolith (clean
> architecture by default — not microservices), scaffold the solution
> structure, and write the one-page design: bounded contexts, the core
> aggregate, and the async flows.

## Exercise (as given)

> Paste the repo URL + the one-page design (contexts, aggregate, async
> flows) and the scaffolded solution layout.

## Chosen product

**MaintainXpert — Smart Maintenance & Asset Management Platform.**
Initial slice: **Maintenance Work Orders** — register assets, raise a work
order, assign a technician, move it through its lifecycle, and publish
events other modules can react to asynchronously.

## One-page architecture summary

Full write-up: [`docs/architecture.md`](docs/architecture.md). Summary:

- **Bounded contexts:** `Maintenance` (core — work orders and their
  lifecycle), `AssetManagement` (assets/machines, referenced only by
  `AssetId`), `Notifications` (reacts to events, console sink for now),
  `SharedKernel` (event contracts + `AssetId`).
- **Core aggregate:** `WorkOrder` — `WorkOrderId`, `AssetId`, `Priority`,
  `Status`, `Description`, `AssignedTechnicianId`, `CreatedAt`. Lifecycle
  `Open → Assigned → InProgress → Completed`.
- **Invariants:** must reference an asset; cannot complete without a
  technician; a completed work order cannot be reassigned; invalid
  lifecycle transitions are rejected.
- **Async flows:**
  1. Create Work Order → `WorkOrderCreated` → `Notifications` consumes it.
  2. Complete Work Order → `WorkOrderCompleted` → `AssetManagement`
     updates the asset's maintenance state.
  Both are dispatched in-process today by `InProcessDomainEventDispatcher`
  (Api host), standing in for a future outbox/broker.
- **Why modular monolith:** one asset-facing slice does not justify
  independent deployability yet; module boundaries (project references +
  event contracts) already exist, so a future split doesn't require
  redesigning the domain.

## Scaffolded solution layout

```
Capstone-project/
├── MaintainXpert.slnx
├── .gitignore
├── src/
│   ├── MaintainXpert.Api/
│   │   ├── Endpoints/AssetEndpoints.cs
│   │   ├── Endpoints/WorkOrderEndpoints.cs
│   │   ├── Infrastructure/InProcessDomainEventDispatcher.cs
│   │   ├── Program.cs
│   │   └── MaintainXpert.Api.csproj
│   ├── MaintainXpert.Maintenance/
│   │   ├── Domain/ (WorkOrder, WorkOrderId, TechnicianId, WorkOrderStatus,
│   │   │            WorkOrderPriority, InvalidWorkOrderTransitionException,
│   │   │            Events/WorkOrderCreated.cs, Events/WorkOrderCompleted.cs)
│   │   ├── Application/ (IWorkOrderRepository, WorkOrderService,
│   │   │                 WorkOrderNotFoundException)
│   │   ├── Infrastructure/ (InMemoryWorkOrderRepository)
│   │   └── MaintainXpert.Maintenance.csproj
│   ├── MaintainXpert.Assets/
│   │   ├── Domain/ (Asset, AssetStatus)
│   │   ├── Application/ (IAssetRepository, WorkOrderCompletedHandler)
│   │   ├── Infrastructure/ (InMemoryAssetRepository)
│   │   └── MaintainXpert.Assets.csproj
│   ├── MaintainXpert.Notifications/
│   │   ├── Domain/ (NotificationMessage)
│   │   ├── Application/ (INotificationSink, WorkOrderCreatedNotificationHandler)
│   │   ├── Infrastructure/ (ConsoleNotificationSink)
│   │   └── MaintainXpert.Notifications.csproj
│   └── MaintainXpert.SharedKernel/
│       ├── IDomainEvent.cs
│       ├── IDomainEventHandler.cs
│       ├── IDomainEventDispatcher.cs
│       ├── AssetId.cs
│       └── MaintainXpert.SharedKernel.csproj
├── tests/
│   └── MaintainXpert.Maintenance.Tests/
│       ├── WorkOrderTests.cs
│       └── MaintainXpert.Maintenance.Tests.csproj
├── docs/
│   └── architecture.md
├── README.md
└── result.md
```

Project references (verified with `dotnet list reference`):

- `MaintainXpert.Api` → `SharedKernel`, `Maintenance`, `Assets`, `Notifications`
- `MaintainXpert.Maintenance` → `SharedKernel`
- `MaintainXpert.Assets` → `SharedKernel`, `Maintenance` (consumes `WorkOrderCompleted`)
- `MaintainXpert.Notifications` → `SharedKernel`, `Maintenance` (consumes `WorkOrderCreated`)
- `MaintainXpert.SharedKernel` → none
- `MaintainXpert.Maintenance.Tests` → `Maintenance`, `SharedKernel`

## Actual build result

```
$ dotnet build
  Restored /home/ayush/Desktop/thingschool/Capstone-project/src/MaintainXpert.Api/MaintainXpert.Api.csproj (in 375 ms).
  Restored /home/ayush/Desktop/thingschool/Capstone-project/src/MaintainXpert.Notifications/MaintainXpert.Notifications.csproj (in 372 ms).
  Restored /home/ayush/Desktop/thingschool/Capstone-project/src/MaintainXpert.Assets/MaintainXpert.Assets.csproj (in 374 ms).
  Restored /home/ayush/Desktop/thingschool/Capstone-project/src/MaintainXpert.SharedKernel/MaintainXpert.SharedKernel.csproj (in 3 ms).
  Restored /home/ayush/Desktop/thingschool/Capstone-project/src/MaintainXpert.Maintenance/MaintainXpert.Maintenance.csproj (in 4 ms).
  Restored /home/ayush/Desktop/thingschool/Capstone-project/tests/MaintainXpert.Maintenance.Tests/MaintainXpert.Maintenance.Tests.csproj (in 1.42 sec).
  MaintainXpert.SharedKernel -> .../bin/Debug/net10.0/MaintainXpert.SharedKernel.dll
  MaintainXpert.Maintenance -> .../bin/Debug/net10.0/MaintainXpert.Maintenance.dll
  MaintainXpert.Assets -> .../bin/Debug/net10.0/MaintainXpert.Assets.dll
  MaintainXpert.Notifications -> .../bin/Debug/net10.0/MaintainXpert.Notifications.dll
  MaintainXpert.Maintenance.Tests -> .../bin/Debug/net10.0/MaintainXpert.Maintenance.Tests.dll
  MaintainXpert.Api -> .../bin/Debug/net10.0/MaintainXpert.Api.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:11.70
```

(Run from a clean state — `src/*/bin`, `src/*/obj`, `tests/*/bin`,
`tests/*/obj` removed beforehand — using the .NET 10 SDK at
`~/.dotnet` (`10.0.302`), the same SDK targeted by the rest of the repo.)

## Actual test result

```
$ dotnet test
Test run for /home/ayush/Desktop/thingschool/Capstone-project/tests/MaintainXpert.Maintenance.Tests/bin/Debug/net10.0/MaintainXpert.Maintenance.Tests.dll (.NETCoreApp,Version=v10.0)
A total of 1 test files matched the specified pattern.

Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 1 s - MaintainXpert.Maintenance.Tests.dll (net10.0)
```

The 7 tests cover: creation raises `WorkOrderCreated`; creation without an
asset is rejected; a full valid lifecycle transition succeeds; completing
without a technician fails; a completed work order cannot be reassigned;
starting before assignment is rejected; completing before starting is
rejected.

## Design trade-offs worth mentioning

- **`AssetId` lives in `SharedKernel`, not `AssetManagement`.** Maintenance
  needs to reference an asset without depending on the AssetManagement
  module's implementation. Putting the identity type in the shared kernel
  keeps that reference lightweight; the actual `Asset` aggregate and its
  behavior stay owned by `AssetManagement` alone.
- **`Assets` and `Notifications` reference `Maintenance` directly**, for
  its event contracts (`WorkOrderCreated`, `WorkOrderCompleted`), not its
  internals. In a larger system these contracts would likely move to a
  separate `Contracts` project so consumers don't pull in the producer's
  whole implementation graph; skipped here to keep the module count small
  for a Day 22 scaffold.
- **The event dispatcher is in-process and synchronous** (`await`ed inline
  after each aggregate mutation), not backed by a queue or an outbox. That
  is intentional per the task scope ("do not build full messaging
  infrastructure yet") — the dispatcher's interface
  (`IDomainEventDispatcher`) is the seam where a real broker/outbox would
  be substituted later without touching the aggregate or the handlers.
- **All repositories are in-memory** (`ConcurrentDictionary`-backed). No
  database was introduced, per the task's explicit "avoid unnecessary
  database implementation at this stage" instruction.
- **No authentication, caching, or resilience wiring** was added — this
  slice's endpoints are unauthenticated and uncached, which is acceptable
  for a design/scaffold step but would need to be addressed before this
  becomes a real deployment target.
