# Oriented Barrier Load Transfer Stability Design

## Problem

The circumferential VirtualSidewall registration contract succeeds (`41/40/80/80/80 complete=1`), but the first oriented contact can collapse the vessel. Runtime evidence shows `CompressionFold` with a positive gap, zero normal load, and `compressionDemand=0.179620`, followed by endpoint distance growth from `0.061913` to `0.301573`.

The failure is in load transfer, not identity or rendering:

- compression demand is recursively accumulated once per manifold and once per barrier schedule;
- retained/releasing manifolds are counted as active even when their normal lambda is zero;
- the oriented fold path accepts stale demand without current load-bearing contact;
- the fold correction is capped before multiplying by inverse mass, so the configured positional limit is not an actual displacement limit;
- applied demand is never consumed, allowing unbounded repeated transverse translation.

## Required behavior

VirtualSidewall identity, explicit SideA/SideB seam semantics, registration counts, rendering isolation, and PullLine target progression remain unchanged.

The load-transfer path must satisfy these contracts:

1. A fold may run only while at least one manifold currently carries normal load or resolves a current barrier violation.
2. `compressionDemand` is a length-valued, per-solve demand. It is reset before aggregation and is the maximum unique blocked closure seen in that solve, not a recursive sum across manifolds.
3. XPBD normal lambda remains a separate load value and is never added directly to a length-valued demand.
4. Every fold vertex's actual positional correction is bounded by the configured per-iteration maximum for every positive vertex mass.
5. Releasing manifolds with positive gap and zero lambda may retain lifecycle identity, but they report no active load and produce no fold.
6. Continuous PullLine shortening remains enabled. Fresh blocked closure can generate fresh fold demand, but old demand is not replayed indefinitely.

## Solver structure

`SutureOrientedBarrierJob` begins each constraint solve with zero compression demand and zero load-bearing manifold count. Each manifold computes a length-valued blocked closure from current penetration depth and forbidden incremental normal motion. The constraint retains the maximum contribution. The manifold's normal lambda is tracked separately.

`SutureLineCompressionFoldJob` calls a pure gate that requires a valid oriented contract, a current barrier state, a positive load-bearing manifold count, positive normal load, finite gap, and positive demand. It applies the fold amount as a positional displacement. Mass determines whether a vertex is movable; it does not multiply an already positional correction.

The fold remains transverse to the closure direction and operates on validated whole-wall fold columns. Because demand is reset and regenerated from fresh blocked motion, the finite PullLine target supplies a finite total fold response.

## Failure behavior and diagnostics

Invalid barrier identity still rejects PullLine creation. A valid line with no current load continues tightening but does not fold. Acceptance output must not report `CompressionFold` when `load=0` or when no manifold is load-bearing. The existing compact output remains the primary Unity Play diagnostic.

## Tests

- A positive-gap, zero-lambda retained manifold cannot enable fold.
- A positive normal load plus current blocked closure can enable fold.
- A positional correction remains bounded for masses below, equal to, and above one.
- Multiple manifold contributions select the maximum unique blocked closure instead of recursively summing it.
- Source scheduling still runs the barrier after PullLine and as the final gate.
- Existing registration, middle-render isolation, greenfield isolation, and `80/80/80` assertions remain green.
