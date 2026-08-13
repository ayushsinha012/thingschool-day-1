# AI Reflection

Note on tooling: this environment has Claude Code available but not GitHub Copilot, so
both "rounds" below were done with Claude Code rather than a genuine Claude-vs-Copilot
comparison. The reflection below is written honestly against what actually happened,
not a hypothetical Copilot session.

**Round 1 - strategy pattern.** The messiest part of the original refactor was
`OrderService.CreateOrderAsync`: premium-customer and bulk-order discounts were applied
inline as `if` blocks mixed in with validation and persistence. Extracting
`IOrderPricingStrategy` + `OrderPricingStrategyProcessor` (see `Strategies/`) got this
right in the sense that matters most: adding a third discount rule now means writing one
new class, not editing `CreateOrderAsync` again. It stayed appropriately small - two
strategies, no strategy factory, no configuration-driven registration - which is the
right amount of structure for two rules, not five.

**Where I'd double-check an AI-driven change like this:** strategies applied in sequence
against a running `total` are order-sensitive - two percentage discounts don't commute
the same way an additive one would, and nothing in the type system stops someone from
reordering `OrderPricingStrategyProcessor`'s list and silently changing every customer's
final price. That's exactly the kind of change I'd want a test pinning down before
trusting it, which is why the new unit tests assert on the actual computed total, not just
that a discount was "applied."

**What the AI-assisted test pass caught:** writing `OrderServiceTests` surfaced two of the
original bugs concretely rather than in the abstract - a blocked customer used to risk a
null-reference on `customer.CreditLimit` before the block check ran, and the off-by-one
loop (`i <= Items.Count`) would throw on the last, valid item in the list. Both now have a
regression test.

**At 2am debugging prod:** Claude Code, for anything touching business logic or data
access - it holds context across the whole change and explains its reasoning, which
matters more under time pressure than a single-line suggestion.
