# Hybrid branched vessel

This folder contains the managed source, generated meshes, materials, textures,
and prefab for a project-compatible vessel assembly built from the supplied
Tripo branching model.

## Semantic split

- `SimulationOuter`: dynamic Outer layer. Assign this `MeshFilter` to
  `ClothSimulationDriverDoubleLayer.outerMeshFilter`.
- `SimulationInner`: dynamic Inner layer. Assign this `MeshFilter` to
  `ClothSimulationDriverDoubleLayer.innerMeshFilter`.
- `StaticTransition`: appearance-only deforming collar. Its inlet follows the
  rendered Outer boundary after simulation; a quintic displacement falloff keeps
  the branch-side ring fixed. Do not add it to PBD.
- `StaticBranchAppearance`: trimmed branching appearance. Do not add it to PBD.

`Prefabs/HybridBranchedVessel.prefab` contains all four objects with their
alignment and materials already assigned. Generated mesh assets live under
`Generated`; the source FBX stays read-only under `Models`.

## Geometry contract

- Source longitudinal parameterization: local `+Z` / `UV.v=0..1`
- Simulation length: `1.21439499`
- Outer radius: `0.16886057`
- Inner radius: `0.15197451`
- Wall thickness: `0.01688606`
- Native topology: 24 circumferential segments x 20 axial segments
- Layer vertices: 504 per layer
- Layer triangles: 960 per layer
- Both simulation layers are open at both ends.
- Outer and Inner have identical vertex ordering, ring ordering, and triangle
  connectivity.

These dimensions match the current `Vessel_PBDTest` cylinder. The dynamic
topology is intentionally close to the previous program's subdivided runtime
budget: 1008 particles across both layers before cutting, while retaining enough
longitudinal stations and circumferential samples for the curved centerline,
whole-ring cut, and six-point Carrel plan.

## Runtime rule contract

Local `Z` describes only the generated source asset. Procedure and simulation
rules do not reclassify the curved vessel by local axes:

- end clamps use normalized longitudinal `UV.v`;
- circumferential cutting uses longitudinal topology stations and the actual
  centerline tangent at the cut;
- `SideA` / `SideB` mean proximal / distal topology banks;
- Outer / Inner synchronization uses identical vertex and triangle identity;
- thickness links use the matching vertex plus its one-ring topology neighbors;
- stitch holes are sampled in the best-fit 3D cut plane, in Carrel order
  `0, 1/3, 2/3, 1/6, 1/2, 5/6`;
- inter-stitch needle travel uses an exterior arc in the actual cut plane;
- middle-wall rendering uses `Outer[i] -> Inner[i]`, including edit preview.
- `HybridVesselTransitionRenderer` verifies both collar boundary edge loops at
  rest, rebinds the live inlet by its original material identity after topology
  replacement, and follows the final Outer render positions and normals. It
  operates on an instance mesh and restores the source mesh when disabled.
- `[HybridTransition][InterfaceConformality]` reports inlet gap, normal angle,
  and branch-side displacement. Missing or duplicate identities fail explicitly.
- runtime subdivision is bounded by `runtimeVertexBudgetPerLayer`; the managed
  hybrid source runs at subdivision level 0 rather than expanding fourfold.

If vertex order, triangle order, or UV identity diverges between the two dynamic
layers, validation fails explicitly. There is no nearest-point or local-axis
fallback for a successful runtime path.

## Rebuilding

Use the Unity menu:

`PBDLearn > Build Hybrid Branched Vessel Assets`

The source FBX remains at `Models/TripoBranchStatic.fbx`. The builder trims the
original lower trunk before the first major branching region, aligns the retained
static tree to the simulation tube, and regenerates the derived meshes under
`Generated` and the prefab under `Prefabs`.

## SingLayer integration

Use:

`PBDLearn > Vascular > Integrate Hybrid Vessel Into SingLayer`

The integration tool keeps the existing `VesselSim` component and all serialized
incision, needle, portal, suture, debug, camera, and UI settings. It replaces only
the legacy Outer/Inner model instances, attaches `ClothRendererSlice` to
`SimulationOuter` and `SimulationInner`, and rebinds the existing driver.

For a deterministic full refresh, use:

`PBDLearn > Vascular > Rebuild And Integrate SingLayer`

## Validation

The editor integration utility provides batch-callable checks for:

- scene references, dynamic/static responsibility, UV/topology identity;
- circumferential cut and six stitch-hole locations;
- actual-cut-plane camera and exterior needle route;
- end pins, cross-layer thickness links, layer separation, and middle-wall
  identity after subdivision.

The current greenfield slit preview reaches `TriangulationQuality` on both the
hybrid model and the legacy straight regression model. That pre-existing
reconstruction-quality failure remains visible and is not treated as a
successful fallback path.
