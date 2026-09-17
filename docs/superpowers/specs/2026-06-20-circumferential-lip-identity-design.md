# Circumferential VirtualSidewall Identity and Registration

## Problem

The circumferential separation path creates valid lower and upper cut-ring vertices, but `BuildCircumferentialStations` stores `-1` for both station vertex identities. The committed-state capture rejects every such station. World-space SideA/SideB guide polylines can still appear valid, while `BuildSutureSidewallBoundaryEdges` receives no indexed stations and the simulator reports `SW=0`, `VW=0/0`, and `SWPairs=0/0/0`. The yellow middle-wall mesh is render-only, so it can interpenetrate when this identity-to-proxy chain is absent.

## Chosen Design

Propagate the existing `lowerCutRing[i]` and `upperCutRing[i]` output-mesh indices into each circumferential `StationResult`.

- Lower cut-ring identity maps to `leftVertexIndex` and the existing lower boundary.
- Upper cut-ring identity maps to `rightVertexIndex` and the existing upper boundary.
- The closed polyline's duplicate terminal world point does not create a duplicate station; stations remain one per unique ring vertex.
- Circumferential sidewall edge generation is explicitly closed: each side emits all consecutive edges plus the final last-to-first edge. Slit mode remains open and unchanged.
- A SideGuide rebuild is not successful for the circumferential mode unless the complete indexed station chains produce non-zero SideA/SideB sidewall edges.
- No nearest-vertex search, identity rematerialization, geometry change, support-domain change, or Outer/Inner synchronization change is allowed.

## Alternatives Rejected

1. Recover identities later by nearest world-space vertex. This can select the wrong side when the opening is collapsed or vertices are close, violating the 3D identity contract.
2. Disable circumferential separation mode. This changes the requested cut behavior instead of fixing the missing identity output.

## Data Flow

`BuildCircumferentialSeparationLayer` already owns the authoritative output indices in `lowerCutRing` and `upperCutRing`. It passes those arrays to `BuildCircumferentialStations`, which validates complete equal-length identity chains and emits stations with matching world positions and vertex identities. `CaptureGreenfieldCommittedState` then materializes non-empty Outer/Inner station lists without fallback. `BuildSutureSidewallBoundaryEdges` consumes those identities, closes the ring only in circumferential mode, and registers the completed edge set with the current `ClothSimulator`. PullLine contact-triangle generation can then emit `FlagVirtualSidewall` triangles from real Outer/Inner particles.

## Failure Handling

If boundary point counts and ring-index counts disagree, station generation must produce no partial identity chain. If either layer lacks a complete chain, any index is invalid, or sidewall edge construction produces zero edges, the circumferential SideGuide must remain invalid and the failure must be logged at identity/registration rather than reaching PullLine contact generation.

## Verification

- A regression test must fail against the current `-1/-1` behavior and pass only when circumferential stations preserve the supplied ring indices.
- A regression seam must verify that an `N`-station circumferential ring emits `N` edges per side, including the last-to-first edge, while slit mode remains open.
- `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj` must compile.
- Runtime acceptance requires `sidewallMissing=0`, `SW=2*N`, and non-zero `VW` on both PullLine endpoints. `SWPairs` is only required after the two sides enter contact range.
- The code diff must remain limited to identity propagation, closed-ring edge generation, registration validation, and regression seams; no contact threshold, stiffness, geometry, support-domain, render, or scene changes.
