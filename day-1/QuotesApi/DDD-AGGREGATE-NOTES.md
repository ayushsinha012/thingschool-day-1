# Aggregate boundary quiz

**Scenario:** An `Order` has many `OrderLine` items. `Customer` is referenced by ID inside
`Order`. What's the aggregate root?

**Answer:** Order is the root; OrderLines are inside the aggregate; Customer is a separate
aggregate referenced by ID.

**Why:** `OrderLine` has no identity or lifecycle independent of the `Order` it belongs to
- it can't be created, mutated, or queried on its own in a way that matters to the
business, so it lives inside the `Order` aggregate and is only ever reached through it.
`Customer`, by contrast, has its own independent lifecycle (it exists, changes, and is
queried regardless of any single order), so it's a separate aggregate root that `Order`
references by ID rather than owns. Only one repository exists per aggregate root -
`IOrderRepository`, not `IOrderLineRepository`.

This is the same shape already built in this repo: `Collection` is the aggregate root,
`CollectionItem` is a value object that only exists inside a `Collection` and is never
addressed on its own (no `ICollectionItemRepository`), while `Quote`, referenced by ID
from inside a `Collection`, is its own separate aggregate with its own repository
(`IQuoteRepository`).
