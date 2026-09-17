# Circumferential VirtualSidewall Complete Registration Design

## Goal

Register the committed circumferential cut as closed VirtualSidewall collision geometry without changing the visible cut, natural-retraction behavior, or middle-wall rendering behavior of the restored baseline.

## Authoritative data

The generator owns circumferential topology identity. Display polylines remain closed lists with the first point repeated (`41` points for the current mesh). Collision stations are a separate list of unique topology stations (`40` points) and never contain the repeated display endpoint. A closed collision ring requires at least three unique stations.

The common geometric seam is the Outer transform local `+X` direction projected onto the common ring plane. The positive ring direction is defined by the Outer local `+Z` axis and that seam. Outer and Inner rings are sorted independently in this shared frame. Registration requires equal counts, the same seam interval, the same direction, and ordinal-by-ordinal angular correspondence. A cyclic shift, reversal, duplicate identity, or ambiguous ordinal is a hard VirtualSidewall registration failure; it is not repaired by nearest-point matching.

## Cut and registration state

`GenerateResult` records the generated cut type. The driver copies that value into a committed-cut field only after the new meshes are installed. Rebuild and registration use the committed value, never the mutable `incisionCircumferentialSeparationCut` inspector field.

Circumferential collision stations have dedicated committed Outer and Inner collections. They do not enter legacy incision station or lip-pair collections. Consequently they cannot activate natural retraction, incision-nearness suppression, geometric opening collapse, or middle-wall suppression. Preview data may draw boundaries but may not register collision state in the simulator.

`RebuildSutureSideGuidesFromCurrentIncisionState` remains callable when legacy incision pairs are empty. Neither it nor its committed-state caller is guarded by `HasAnyIncisionPairs`.

## Registration chain

For each of the two cut sides, the driver emits `(i, (i + 1) % count)` for every unique station. A 40-station ring therefore requests 80 sidewall boundary edges. Outer and Inner indices remain layer-local while held by the generator and driver. The driver converts them to merged Simulator indices in one function: Outer adds `OuterStart`; Inner adds `InnerStart` exactly once.

The Simulator validates enabled state, side, duplicate edges, layer ownership, and live vertex range. Its registration result records:

- `requested`: edges produced by the driver builder;
- `accepted`: edges stored by the Simulator after validation;
- `runtimeActive`: accepted edges still valid against the live merged topology.

Builder count alone is never logged as registration success. Complete circumferential registration requires `requested == accepted == runtimeActive == 2 * stationCount`.

## Failure behavior

Any collision-contract failure clears the Simulator VirtualSidewall edges and reports the earliest reason with counts and a short preview. It does not roll back the committed cut meshes, clear the visible ring boundaries, populate legacy incision state, suppress the middle wall, or use a partial/fallback edge set.

## Verification

Automated regression coverage must prove:

- two stations cannot define a closed collision ring;
- three or more stations close last-to-first exactly once;
- a 41-point display polyline produces 40 collision stations;
- shared-seam validation rejects shifted, reversed, or ordinal-mismatched rings;
- circumferential state is not classified as legacy incision state;
- Inner indices receive exactly one `InnerStart` offset;
- a 40-station valid input requests and registers 80 edges;
- Simulator requested, accepted, and runtime-active counts are distinct recorded layers.

Both `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj` must be force rebuilt to avoid stale Unity DLLs. Unity Play verification must preserve the restored visible cut and middle layer and produce a registration line containing `displayPts=41 collisionStations=40 requested=80 accepted=80 runtimeActive=80`.
