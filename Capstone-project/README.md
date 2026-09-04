# MaintainXpert — Capstone Kickoff

MaintainXpert is a smart maintenance and asset management platform:
businesses register machines/assets, raise maintenance work orders, assign
technicians, and track work to completion, with other parts of the system
reacting asynchronously to what happens.

## Product slice (Day 22)

This kickoff scaffolds the **Maintenance Work Orders** slice: create a
work order against an asset, assign a technician, move it through its
lifecycle (`Open → Assigned → InProgress → Completed`), and publish events
that the AssetManagement and Notifications modules can consume
asynchronously. It is a design + scaffold step, not the full product.

## Architecture approach

One deployable application, built as a modular monolith with clean
(onion) architecture per module: each module has its own `Domain`,
`Application`, and `Infrastructure` folders, and modules talk to each
other only through public contracts — never through each other's
implementation types. See [`docs/architecture.md`](docs/architecture.md)
for the full one-page design: bounded contexts, the `WorkOrder` aggregate
and its invariants, and the async flows.

## Modules

- `MaintainXpert.Api` — the single host: DI composition root, minimal API
  endpoints, the in-process domain event dispatcher.
- `MaintainXpert.Maintenance` — the core bounded context: work orders and
  their lifecycle.
- `MaintainXpert.Assets` — assets/machines and their maintenance-related
  state.
- `MaintainXpert.Notifications` — reacts to maintenance events; currently
  a console sink standing in for a future notification channel.
- `MaintainXpert.SharedKernel` — the domain-event contracts and the
  `AssetId` identity type shared across contexts.

## How to build

```bash
cd Capstone-project
dotnet build
```

## How to run tests

```bash
cd Capstone-project
dotnet test
```

## How to run the API

```bash
cd Capstone-project/src/MaintainXpert.Api
dotnet run
```

See [`result.md`](result.md) for the actual build/test output captured for
this kickoff.
