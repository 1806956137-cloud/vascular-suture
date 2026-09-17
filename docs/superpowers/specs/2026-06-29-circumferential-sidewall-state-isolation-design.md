# Circumferential VirtualSidewall State Isolation Design

## Problem

The circumferential cut generator now exposes valid lower/upper ring vertex identities, but the driver copies those identities into the existing incision lip station and lip-pair collections. Those collections are not collision-only state: they activate natural lip retraction, incision-nearness/connectivity behavior, and middle-wall fill suppression. In the current Play run this produced `NaturalRetractionStart outerStations=40 innerStations=40`, repeatedly moved 80 vertices, and removed the previously visible middle layer.

VirtualSidewall registration still failed independently. All 80 intended closed-ring edges were rejected by the open/slit station-spacing guard (`outerJoin=innerJoin=0.026416`, `maxJoin=0.008750`), producing `edges=0 expected=80 missing=80`. The ordered circumferential ring already supplies explicit adjacency, so this distance heuristic is not the correct proof for the closed-loop path.

## Chosen Design

Introduce dedicated Outer and Inner circumferential sidewall station collections owned by the VirtualSidewall registration path. On a committed circumferential greenfield result, copy only valid generated ring identities and local station geometry into these collections. Do not add them to the legacy incision lip station or lip-pair collections. Existing slit/precise-incision behavior continues using the legacy collections unchanged.

The suture sidewall builder selects its station source by cut mode:

- circumferential separation cut: dedicated circumferential sidewall stations;
- slit/precise incision: existing incision lip stations.

For circumferential mode, segments are the explicit ordered pairs `(i, (i + 1) % count)`. Identity validity, equal Outer/Inner station counts, unique endpoints, and complete edge count remain mandatory. The open/slit distance discontinuity guard is not applied to these explicit ring neighbors. A missing identity or incomplete ring remains a hard registration failure; there is no nearest-vertex fallback and no partial-success path.

The dedicated collections follow the same clear/rebuild lifecycle as committed incision state and are populated before `RebuildSutureSideGuidesFromCurrentIncisionState` runs.

## Rejected Alternatives

1. Keep the shared collections and add circumferential-mode checks to every legacy consumer. This is fragile because the collections have multiple hidden consumers and a future consumer could reintroduce the regression.
2. Increase the spacing threshold until the current ring passes. This would encode one mesh scale, weaken discontinuity detection, and still treat a topology contract as a geometric guess.
3. Roll back ring identities. This restores the former rendering behavior but leaves VirtualSidewall unregistered.

## Failure Contract and Diagnostics

Registration succeeds only when both dedicated rings contain at least two stations, counts match, every selected boundary identity is valid, and exactly `2 * stationCount` sidewall edges are produced. The success log must identify the dedicated circumferential source and report `missing=0`. Failures report the earliest local reason and do not register partial edges.

Circumferential collision identity must not make `HasAnyIncisionPairs` true, populate incision lip pairs, start natural retraction, or suppress middle-wall fill.

## Verification

Automated regression coverage must prove:

- a 40-station closed ring produces 80 explicit sidewall segments, including both closing segments;
- the closed-ring path does not reject valid explicit neighbors because of the open/slit spacing guard;
- circumferential collision state is classified separately from legacy incision deformation/render state;
- existing open sidewall sequencing remains unchanged.

Build both `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj`. In Unity Play verification, require a committed circumferential cut with the original visible cut/middle layer preserved, no circumferential-identity-triggered `NaturalRetractionStart`, and a registration log equivalent to `edges=80 expected=80 missing=0 source=circumferential-sidewall-state`.
