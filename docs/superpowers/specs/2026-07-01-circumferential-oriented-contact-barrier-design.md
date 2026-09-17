# Circumferential Oriented Contact Barrier Design

## Goal

Keep the two circumferential cut sides from crossing while a PullLine continues to tighten until the cut is physically closed. Contact must not stop, freeze, or deadlock the suture. Once the cut faces touch, additional line shortening must become sustained contact pressure, tangential sliding, and whole-wall folding instead of penetration.

This design applies to the committed `CircumferentialSeparation` path whose VirtualSidewall registration is already complete. It must preserve the visible cut, the middle render layer, the existing greenfield-cut path, and the `40` unique collision stations / `80` registered boundary edges contract.

## Current evidence and primary design defect

Runtime evidence proves that registration and consumption are working:

- `displayPts=41 collisionStations=40 requested=80 accepted=80 runtimeActive=80 complete=1`;
- an active PullLine reports `sidewallEdges=80` and non-zero `virtualSidewallTriA/B`;
- VT, EE, and swept candidates are detected near the visible intersection.

The remaining failure is in contact semantics and constraint coupling. The current VT solver projects its response normal away from `closureAxis`. For a circumferential cut, the PullLine closure direction is the direction that the cut-to-cut barrier must oppose. Removing that component makes the contact correction unable to carry the suture's normal load. Detection can therefore succeed while the solver produces only a small sideways correction and the surfaces continue through one another.

The same vector is also being asked to serve two incompatible purposes: preventing penetration and choosing a lateral folding direction. Those roles must be separated.

## Chosen behavior

The approved behavior is continuous tightening with a non-penetrating material response:

- PullLine target rest distance continues to decrease after contact.
- The contact barrier is a one-sided inequality and cannot be crossed.
- The line is not considered blocked merely because the local face gap has reached its material minimum.
- Normal shortening that cannot become additional face approach is converted into contact load and whole-wall compression demand.
- Compression demand drives tangential sliding and lateral folding of a support band on both sides.
- A completed stitch may retain non-zero tensile lambda and contact pressure. Completion does not require two finite-thickness walls to occupy the same position.

The system must not implement this as two independent corrections that repeatedly cancel: a PullLine push through the wall followed by a contact push back along the same scalar degree of freedom. The line and barrier are solved as a coupled contact state.

## Geometry and identity contract

Each circumferential VirtualSidewall boundary edge already identifies:

- `SideA` or `SideB`;
- the unique station ordinal;
- the source edge;
- two Outer and two Inner topology vertices.

The oriented-contact path extends the runtime contact representation with:

- a committed SideA-to-SideB gap axis in Simulator coordinates;
- stable triangle/edge feature identities derived from station and source-edge identity;
- an explicit source side on every VirtualSidewall triangle;
- enough identity to retain a contact manifold across solver iterations and frames.

Only opposite committed sides may form a circumferential barrier pair. Same-side VirtualSidewall pairs are invalid. Outer and Inner remain a single whole-wall column; their indices are not rematched spatially and their existing one-time layer offset rule remains unchanged.

Pair selection is local to the active PullLine ROI. It may use the same station or a bounded neighboring-station window so that tangential sliding is possible. It must not become a strict station-to-station zipper, because strict ordinal locking would prevent natural circumferential slip and create regular artificial folds.

## Separate barrier and fold directions

The contact state owns two distinct directions:

### Barrier normal

`barrierNormal` carries the non-penetration inequality. It is initialized from the committed SideA-to-SideB gap axis and updated from current contact geometry without changing legal side.

- It must retain the component parallel to PullLine closure.
- It must never pass through a `ProjectAwayFromClosureAxis` operation.
- Current triangle winding cannot reverse it. A geometric normal is flipped, if necessary, to remain aligned with the committed axis.
- If current geometry is degenerate or nearly orthogonal to the committed direction, the committed axis is the deterministic fallback.

### Fold direction hint

`foldDirectionHint` is perpendicular to the closure direction and is used only to choose a stable lateral buckling direction. Existing portal normals and the current projected `contactNormalHint` are suitable inputs for this role. It does not define which side of a collision surface is legal.

This separation prevents the anti-penetration normal from being weakened for the sake of producing a fold.

## Continuous contact and persistent manifold

Circumferential contact uses a persistent, oriented manifold per active PullLine.

Each manifold entry stores:

- stable feature key;
- SideA and SideB feature identities;
- station interval;
- oriented normal;
- barycentric or edge parameters;
- non-negative XPBD normal lambda;
- active/release age;
- current normal load contribution.

VT and EE tests use previous and predicted positions. If a feature sweep would cross the legal material gap, the contact is activated at the first admissible position rather than after the signed side has changed. Once active, the legal orientation comes from committed identity and manifold history, not from the sign of the already-crossed current position.

The inequality is conceptually:

`C = dot(pointB - pointA, barrierNormal) - materialGap >= 0`

with complementarity:

- normal lambda is non-negative;
- positive separation has no compressive lambda;
- active contact may carry normal load without allowing `C` to become negative.

Manifold release requires all of the following for a small hysteresis window: separation above the release gap, separating relative velocity, and no swept re-entry. This avoids frame-to-frame latch chatter.

## Coupled no-deadlock solve

When no barrier is active, PullLine uses its existing approach behavior.

When a barrier becomes active, each line correction is decomposed into:

- admissible motion that closes the remaining legal gap;
- tangential motion along the contact plane;
- normal closure demand that would violate the barrier.

The first two components may update the contacted geometry. The forbidden normal component is not discarded and is not applied through the wall. It becomes `compressionDemand` and contributes to retained suture tension.

`compressionDemand` is distributed to a validated support band on both cut sides as a bending moment along `foldDirectionHint`. The contact vertices remain on the legal barrier while vertices behind the contact are allowed to translate, rotate, and fold. Outer and Inner support vertices receive compatible weighted corrections so the wall folds as a thickness-bearing column instead of shearing its layers apart.

The PullLine target continues to shorten throughout this process. The stitch reaches physical closure when the material gap is maintained, the line remains in tension, and the remaining target reduction is represented by compression/fold response. It does not wait for an impossible zero-distance equality between finite-thickness faces.

`contactLatched` may report that the barrier is carrying load, but it must not freeze target progression or disable PullLine tension.

## State model

The circumferential barrier lifecycle is independent of the old distance-based contact latch:

1. `Inactive`: no relevant opposite-side pair is near the barrier.
2. `SweptCandidate`: predicted motion reaches the activation gap this frame.
3. `BarrierActive`: the inequality and persistent normal lambda are active.
4. `CompressionFold`: barrier load produces non-zero compression demand and support-band folding.
5. `Settled`: the stitch is physically closed and retains tension; the barrier remains active as needed.

The lifecycle cannot transition to an opposite legal side. A released manifold returns to `Inactive`; it is not re-created from a flipped current sign.

## Solver ordering

The current contact pass occurs before later thickness, layer-separation, and final constraints, so later corrections can reintroduce an intersection. The oriented barrier becomes the final geometric gate.

Each solver iteration uses this order:

1. material distance and bend constraints;
2. PullLine target/tension proposal;
3. whole-wall thickness coupling;
4. oriented barrier and CCD projection;
5. compression-demand folding and tangential response;
6. existing final constraints;
7. final oriented barrier projection.

The second barrier pass is not a stronger fallback. It enforces the same inequality after every other position-changing constraint. The last solver iteration therefore also ends with a legal cut-to-cut state.

## Creation-time validation and failure behavior

A PullLine that will use circumferential oriented contact is created only after validating:

- complete SideA/SideB VirtualSidewall registration;
- valid committed gap axis and side ownership;
- valid mobile support bands on both sides;
- valid whole-wall Outer/Inner membership;
- deterministic manifold capacity for the configured ROI;
- valid fold direction input.

This prevents a line from starting successfully and then freezing halfway because contact storage or support mobility is missing. Validation failure rejects PullLine creation before any tightening is applied and reports the earliest contract failure.

For a successfully created line, normal tightening is never paused merely because contact activates. Runtime contact is required to carry load and route closure demand into deformation.

There is no fallback from a failed circumferential oriented barrier to the old unsigned nearest-side contact. Such a fallback could restore visible motion by allowing the original penetration bug. Greenfield and non-circumferential paths retain their existing solver behavior unless a separate design explicitly changes them.

## Rendering and state isolation

VirtualSidewall remains collision-only geometry. The oriented barrier must not:

- populate legacy incision stations or lip pairs;
- trigger natural retraction;
- clear or rebuild the visible circumferential ring;
- suppress the middle render layer;
- require render vertices to become collision identities.

The render layer continues to follow the physical Outer/Inner simulation result. Runtime acceptance must compare the render result with the legal simulation barrier so a collision-correct physical mesh cannot be hidden by a separately crossing render reconstruction.

## Automated verification

Regression coverage must prove:

- circumferential barrier normals retain their closure-axis component;
- only opposite committed sides form barrier pairs;
- a previous-to-predicted sweep is stopped before legal-side inversion;
- already-deformed triangle winding cannot reverse the committed legal direction;
- tangential motion remains available under active normal contact;
- rejected normal closure becomes compression demand rather than disappearing;
- PullLine target progression continues while the barrier is active;
- whole-wall corrections include compatible Outer and Inner motion;
- a final barrier pass leaves all active inequalities non-negative after other constraints;
- manifold identity and lambda persist without contact chatter;
- invalid setup rejects PullLine creation before motion rather than freezing an active line;
- the `40/80/80/80` circumferential registration contract remains intact;
- greenfield-cut contact behavior remains unchanged.

The tests must exercise the pure contact math independently of Unity scene timing and then cover the Simulator scheduling/integration contract.

## Unity Play acceptance

A valid circumferential test scene must demonstrate:

- `displayPts=41`, `collisionStations=40`, and `requested=accepted=runtimeActive=80`;
- non-zero VirtualSidewall candidates in the active PullLine ROI;
- continuous target shortening after barrier activation;
- stable retained line tension with no pull/contact deadlock;
- no cut-side sign inversion or visible interpenetration during approach, contact, fold, or settled hold;
- natural tangential slip and whole-wall folding instead of a rigid ordinal zipper;
- no layer shear between Outer and Inner;
- no visible jitter, explosive correction, or contact latch chatter;
- preserved visible circumferential cut and middle render layer;
- stable behavior for one PullLine and for multiple sequential PullLines.

Parameter increases alone are not acceptance. The result must pass with the oriented identity, continuous barrier, coupled tension transfer, and final-gate ordering active.
