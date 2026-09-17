using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PBDLearn
{
    /// <summary>
    /// Builds a project-compatible hybrid vessel:
    /// - a regular, open Outer/Inner simulation tube whose source parameterization
    ///   runs along local Z (runtime rules use UV/topology identity, not local Z);
    /// - a short static transition collar;
    /// - the Tripo branching model as static appearance only.
    ///
    /// Dimensions intentionally match the current Vessel_PBDTest assets.
    /// </summary>
    public static class HybridVesselAssetBuilder
    {
        private const string ContentRoot = "Assets/PBDLearn/Content/Vascular";
        private const string SourceModelPath = ContentRoot + "/Models/TripoBranchStatic.fbx";
        private const string OuterMeshPath = ContentRoot + "/Generated/HybridSimulationOuter.asset";
        private const string InnerMeshPath = ContentRoot + "/Generated/HybridSimulationInner.asset";
        private const string TransitionMeshPath = ContentRoot + "/Generated/HybridStaticTransition.asset";
        private const string StaticBranchMeshPath = ContentRoot + "/Generated/HybridStaticBranches.asset";
        private const string TissueTexturePath = ContentRoot + "/Textures/TissueSurface_Albedo.png";
        private const string TissueMeshPath = ContentRoot + "/Generated/HybridTissueSurface.asset";
        private const string TissueMaterialPath = ContentRoot + "/Materials/HybridTissueSurface.mat";
        private const string SupportMaterialPath = ContentRoot + "/Materials/HybridSurgicalSupport.mat";
        private const string PrefabPath = ContentRoot + "/Prefabs/HybridBranchedVessel.prefab";

        private const string OuterMaterialPath = ContentRoot + "/Materials/外层.mat";
        private const string InnerMaterialPath = ContentRoot + "/Materials/内层.mat";

        // Existing project vessel dimensions after FBX unit conversion.
        private const float OuterRadius = 0.16886057f;
        private const float InnerRadius = 0.15197451f;
        private const float SimulationLength = 1.21439499f;

        // Native regular topology. The current scene may still apply its existing
        // runtime subdivision level after loading these meshes.
        // Keep the dynamic simulation close to the legacy runtime budget.
        // 24x20 yields 504 vertices / 960 triangles per layer; the previous
        // 32x32 source plus one runtime subdivision produced 4160 vertices.
        private const int RadialSegments = 24;
        private const int AxialSegments = 20;
        private const int TransitionAxialSegments = 12;

        private const float TransitionExtension = 0.36f;
        private const float StaticBranchCutNormalized = 0.36f;
        private const float StaticBranchSeamRelaxationRadiusScale = 2.0f;
        private const float StaticBranchDepthBlendRadiusScale = 8.0f;
        private const float StaticBranchMaxSeamTiltDegrees = 45f;
        private const int StaticBranchOrientationSamples = 32768;
        private const float StaticBranchTipInfluenceRadiusScale = 5.5f;
        private const float StaticBranchTipSeparationRadiusScale = 2.2f;
        private const float StaticBranchTipFloatingClearance = 0.07f;
        private const float StaticBranchTipMinimumDownDot = 0.55f;
        private const int StaticBranchTipLimit = 32;
        private const float StaticBranchVisibleClearance = 0.004f;
        private const float StaticBranchMaterialOrphanArea = 0.02f;
        private const float StaticBranchMaterialOrphanGap = 0.03f;
        private const int StaticBranchVisibilitySliceCount = 4;
        private const float StaticBranchVisibilitySliceStep = 0.05f;
        private const int StaticBranchVisibilityProxyOrphanBudget = 3;
        private const float StaticBranchMaxPresentationSpan = 1.90f;
        private const float StaticBranchMinPresentationMean = -0.16f;
        private const float StaticBranchMaxPresentationMean = 0.08f;
        private const float SimulationEndOffsetX = -0.055f;
        private const float SimulationEndTiltDegrees = -8f;
        private const float SimulationSuspensionHeight = 0.16f;
        private const float StaticBranchSuspensionHoldLength = 0.16f;
        private const float StaticBranchSuspensionBlendLength = 0.90f;
        // Root-space distance along the static surface, not along a world axis.
        // Keep the circular seam collar and the distant anatomical branches fixed.
        private const float StaticSmoothCollarLength = 0.06f;
        private const float StaticSmoothRampLength = 0.25f;
        private const float StaticSmoothFullLength = 2.0f;
        private const float StaticSmoothEndLength = 2.5f;
        private const int StaticSmoothSubdivisionPasses = 2;
        private const float StaticInletLevelHold = 0.16f;
        private const float StaticInletLevelEnd = 1.20f;
        private const int StaticInletSectionCount = 24;
        // This is a local diameter correction on StaticBranchAppearance only.
        // Measure away from its seam with StaticTransition: keep the target
        // diameter over the first part of the non-suture branch, then recover
        // the imported anatomical diameter smoothly before leaving that segment.
        // StaticTransition and both simulation meshes are never deformed here.
        private const float StaticBranchDiameterHoldLength = 0.30f;
        private const float StaticBranchDiameterBlendEnd = 0.90f;
        private const int TissueGridSegments = 96;
        private const int TissueConformIterations = 30;
        private const int TissueEmbeddingFieldIterations = 18;
        private const float TissueMargin = 0.70f;
        private const float TissueBaseHeight = -0.205f;
        private const float TissueMaxDisplacement = 0.0185f;
        private const float TissueMaxElevation = 0.24f;
        private const float TissueThickness = 0.42f;
        private const float TissueContactClearance = 0.004f;
        private const float TissueBranchEmbedDepth = 0.035f;
        private const float TissueBranchInfluenceRadius = 0.16f;
        private const float TissueBranchLowerNormalThreshold = -0.30f;
        private const float TissueTextureWorldSize = 3.2f;
        private const float SupportRailHeight = 0.18f;
        private const float SupportRailWidthFraction = 0.17f;
        private const float SupportRailLengthFraction = 1.18f;
        private const float SupportRailInsetFraction = 0.30f;

        private sealed class StaticBranchBuildResult
        {
            public Mesh mesh;
            public List<Vector3> boundary;
            public List<int> boundaryIndices;
            public Vector3 seamCenter;
            public Vector3 seamTangent;
            public float seamRadius;
        }

        private sealed class BranchAlignment
        {
            public float scale;
            public Quaternion rotation;
            public Vector3 position;
            public Vector3 seamTangentRoot;
            public List<Vector3> boundaryRoot;
            public List<Vector3> boundaryNormalsRoot;
        }

        private readonly struct StaticTriangleLink
        {
            public readonly int triangle;
            public readonly int edgeA;
            public readonly int edgeB;

            public StaticTriangleLink(
                int triangle,
                int edgeA,
                int edgeB)
            {
                this.triangle = triangle;
                this.edgeA = edgeA;
                this.edgeB = edgeB;
            }
        }

        private sealed class StaticVisibilityTopology
        {
            public int[] triangles;
            public float[] triangleAreas;
            public bool[] triangleContainsEntrance;
            public List<StaticTriangleLink>[] links;
        }

        private sealed class StaticVisibilityWorkspace
        {
            public float[] clearance;
            public bool[] activeTriangles;
            public int[] triangleComponents;
            public int[] componentTriangleCounts;
            public float[] componentAreas;
            public float[] componentMaxGaps;
            public bool[] componentContainsEntrance;
            public readonly Queue<int> queue = new Queue<int>();
        }

        private readonly struct SourceEdgeKey : IEquatable<SourceEdgeKey>
        {
            public readonly int meshIndex;
            public readonly int a;
            public readonly int b;

            public SourceEdgeKey(int meshIndex, int first, int second)
            {
                this.meshIndex = meshIndex;
                a = Mathf.Min(first, second);
                b = Mathf.Max(first, second);
            }

            public bool Equals(SourceEdgeKey other) =>
                meshIndex == other.meshIndex && a == other.a && b == other.b;

            public override bool Equals(object obj) =>
                obj is SourceEdgeKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = meshIndex;
                    hash = hash * 397 ^ a;
                    hash = hash * 397 ^ b;
                    return hash;
                }
            }
        }

        private readonly struct ClippedVertex
        {
            public readonly Vector3 position;
            public readonly int originalIndex;
            public readonly int edgeA;
            public readonly int edgeB;

            private ClippedVertex(
                Vector3 position,
                int originalIndex,
                int edgeA,
                int edgeB)
            {
                this.position = position;
                this.originalIndex = originalIndex;
                this.edgeA = edgeA;
                this.edgeB = edgeB;
            }

            public static ClippedVertex Original(Vector3 position, int index) =>
                new ClippedVertex(position, index, -1, -1);

            public static ClippedVertex Intersection(
                Vector3 position,
                int first,
                int second) =>
                new ClippedVertex(position, -1, first, second);
        }

        [MenuItem("PBDLearn/Build Hybrid Branched Vessel Assets")]
        public static void Build()
        {
            EnsureSourceModelReadable();
            EnsureTissueTextureReady();
            ValidateSimulationSuspensionProfile();

            // This step uses a temporary empty scene to apply the FBX hierarchy
            // transforms. Build it before creating unsaved procedural Mesh
            // objects, because changing scenes releases those transient objects.
            StaticBranchBuildResult staticBranch = CreateTrimmedStaticBranchMesh();
            BranchAlignment branchAlignment = ComputeBranchAlignment(staticBranch);
            Vector3[] staticBranchBeforeLayout =
                (Vector3[])staticBranch.mesh.vertices.Clone();
            ApplyStaticBranchSuspension(
                staticBranch,
                branchAlignment);
            ValidateStaticBranchDeformation(
                staticBranch.mesh,
                staticBranchBeforeLayout,
                branchAlignment);
            SmoothStaticBranchBends(staticBranch, branchAlignment);
            LevelStaticBranchInlet(staticBranch, branchAlignment);
            SmoothStaticBranchSeamNormals(staticBranch, branchAlignment);

            Mesh outerMesh = CreateOpenCircularSweepMesh(
                "HybridSimulationOuter",
                OuterRadius,
                SimulationLength,
                RadialSegments,
                AxialSegments);

            Mesh innerMesh = CreateOpenCircularSweepMesh(
                "HybridSimulationInner",
                InnerRadius,
                SimulationLength,
                RadialSegments,
                AxialSegments);

            Mesh transitionMesh = CreateTransitionMesh(
                GetRingVertices(outerMesh, AxialSegments, RadialSegments),
                branchAlignment.boundaryRoot,
                branchAlignment.boundaryNormalsRoot,
                branchAlignment.seamTangentRoot);
            Bounds vesselBounds = ComputeAlignedVesselBounds(
                outerMesh,
                transitionMesh,
                staticBranch.mesh,
                branchAlignment);
            Mesh tissueMesh = CreateTissueSurfaceMesh(
                vesselBounds,
                outerMesh,
                transitionMesh,
                staticBranch.mesh,
                branchAlignment);
            Material tissueMaterial = CreateTissueMaterial();
            Material supportMaterial = CreateSupportMaterial();

            ReplaceAsset(outerMesh, OuterMeshPath);
            ReplaceAsset(innerMesh, InnerMeshPath);
            ReplaceAsset(transitionMesh, TransitionMeshPath);
            ReplaceAsset(staticBranch.mesh, StaticBranchMeshPath);
            ReplaceAsset(tissueMesh, TissueMeshPath);
            ReplaceAsset(tissueMaterial, TissueMaterialPath);
            ReplaceAsset(supportMaterial, SupportMaterialPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            BuildPrefab(
                AssetDatabase.LoadAssetAtPath<Mesh>(OuterMeshPath),
                AssetDatabase.LoadAssetAtPath<Mesh>(InnerMeshPath),
                AssetDatabase.LoadAssetAtPath<Mesh>(TransitionMeshPath),
                AssetDatabase.LoadAssetAtPath<Mesh>(StaticBranchMeshPath),
                AssetDatabase.LoadAssetAtPath<Mesh>(TissueMeshPath),
                AssetDatabase.LoadAssetAtPath<Material>(TissueMaterialPath),
                AssetDatabase.LoadAssetAtPath<Material>(SupportMaterialPath),
                branchAlignment);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ValidateGeneratedAssets();
            Debug.Log(
                $"[HybridVessel] Build complete. outerR={OuterRadius:F8} innerR={InnerRadius:F8} " +
                $"thickness={(OuterRadius - InnerRadius):F8} length={SimulationLength:F8} " +
                $"bendOffsetX={SimulationEndOffsetX:F6} endTilt={SimulationEndTiltDegrees:F2}deg " +
                $"suspensionHeight={SimulationSuspensionHeight:F6} " +
                $"staticSuspension={StaticBranchSuspensionHoldLength:F2}+" +
                $"{StaticBranchSuspensionBlendLength:F2} " +
                $"tissue={vesselBounds.size.x + TissueMargin * 2f:F3}x" +
                $"{vesselBounds.size.z + TissueMargin * 2f:F3} " +
                $"topology={RadialSegments}x{AxialSegments} prefab={PrefabPath}");
        }

        public static void BuildBatch()
        {
            try
            {
                Build();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void EnsureSourceModelReadable()
        {
            var importer = AssetImporter.GetAtPath(SourceModelPath) as ModelImporter;
            if (importer == null)
                throw new InvalidOperationException($"Missing source FBX importer at {SourceModelPath}");

            bool changed = false;
            if (!importer.isReadable)
            {
                importer.isReadable = true;
                changed = true;
            }

            if (importer.materialImportMode != ModelImporterMaterialImportMode.None)
            {
                importer.materialImportMode = ModelImporterMaterialImportMode.None;
                changed = true;
            }

            if (changed)
                importer.SaveAndReimport();
        }

        private static void EnsureTissueTextureReady()
        {
            AssetDatabase.ImportAsset(
                TissueTexturePath,
                ImportAssetOptions.ForceSynchronousImport);
            var importer =
                AssetImporter.GetAtPath(TissueTexturePath) as TextureImporter;
            if (importer == null)
                throw new InvalidOperationException(
                    $"Missing tissue texture importer at {TissueTexturePath}");

            bool changed = false;
            if (importer.textureType != TextureImporterType.Default)
            {
                importer.textureType = TextureImporterType.Default;
                changed = true;
            }
            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }
            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                changed = true;
            }
            if (!importer.sRGBTexture)
            {
                importer.sRGBTexture = true;
                changed = true;
            }
            if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
            {
                importer.textureCompression =
                    TextureImporterCompression.CompressedHQ;
                changed = true;
            }
            if (changed)
                importer.SaveAndReimport();
        }

        private static Mesh CreateOpenCircularSweepMesh(
            string meshName,
            float radius,
            float length,
            int radialSegments,
            int axialSegments)
        {
            int ringCount = axialSegments + 1;
            int vertexCount = ringCount * radialSegments;
            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var tangents = new Vector4[vertexCount];
            var uv = new Vector2[vertexCount];

            int vertexIndex = 0;
            for (int ring = 0; ring < ringCount; ring++)
            {
                float v = ring / (float)axialSegments;
                EvaluateSimulationFrame(v, out Vector3 center, out Vector3 ringX, out Vector3 ringY);
                for (int column = 0; column < radialSegments; column++)
                {
                    float u = column / (float)radialSegments;
                    float theta = u * Mathf.PI * 2f;
                    float cos = Mathf.Cos(theta);
                    float sin = Mathf.Sin(theta);
                    Vector3 radial = ringX * cos + ringY * sin;
                    Vector3 circumferential = -ringX * sin + ringY * cos;

                    vertices[vertexIndex] = center + radial * radius;
                    normals[vertexIndex] = radial;
                    tangents[vertexIndex] = new Vector4(
                        circumferential.x,
                        circumferential.y,
                        circumferential.z,
                        1f);
                    uv[vertexIndex] = new Vector2(u, v);
                    vertexIndex++;
                }
            }

            var triangles = new int[axialSegments * radialSegments * 6];
            int triangleIndex = 0;
            for (int ring = 0; ring < axialSegments; ring++)
            {
                int lower = ring * radialSegments;
                int upper = (ring + 1) * radialSegments;
                for (int column = 0; column < radialSegments; column++)
                {
                    int next = (column + 1) % radialSegments;
                    int lowerCurrent = lower + column;
                    int lowerNext = lower + next;
                    int upperCurrent = upper + column;
                    int upperNext = upper + next;

                    // Outward winding. Both project layers intentionally keep the
                    // same winding, matching the current Outer/Inner FBX assets.
                    triangles[triangleIndex++] = lowerCurrent;
                    triangles[triangleIndex++] = lowerNext;
                    triangles[triangleIndex++] = upperCurrent;

                    triangles[triangleIndex++] = lowerNext;
                    triangles[triangleIndex++] = upperNext;
                    triangles[triangleIndex++] = upperCurrent;
                }
            }

            var mesh = new Mesh
            {
                name = meshName,
                vertices = vertices,
                normals = normals,
                tangents = tangents,
                uv = uv,
                triangles = triangles,
            };
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void EvaluateSimulationFrame(
            float t,
            out Vector3 center,
            out Vector3 ringX,
            out Vector3 ringY)
        {
            Vector3 start = Vector3.zero;
            Vector3 end = SimulationEndCenter;
            Vector3 startDerivative = Vector3.forward * SimulationLength;
            Vector3 endDerivative = SimulationEndTangent * SimulationLength;

            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            center =
                h00 * start +
                h10 * startDerivative +
                h01 * end +
                h11 * endDerivative;
            EvaluateSimulationSuspension(
                t,
                out float suspensionHeight,
                out float suspensionDerivative);
            center.y += suspensionHeight;

            float dh00 = 6f * t2 - 6f * t;
            float dh10 = 3f * t2 - 4f * t + 1f;
            float dh01 = -6f * t2 + 6f * t;
            float dh11 = 3f * t2 - 2f * t;
            Vector3 tangent =
                dh00 * start +
                dh10 * startDerivative +
                dh01 * end +
                dh11 * endDerivative;
            tangent.y += suspensionDerivative;
            tangent.Normalize();

            ringX = Vector3.Cross(Vector3.up, tangent).normalized;
            ringY = Vector3.Cross(tangent, ringX).normalized;
        }

        private static void EvaluateSimulationSuspension(
            float t,
            out float height,
            out float derivative)
        {
            height = SimulationSuspensionHeight;
            derivative = 0f;
        }

        private static void ValidateSimulationSuspensionProfile()
        {
            if (SimulationSuspensionHeight <= 0f ||
                StaticBranchSuspensionHoldLength < 0f ||
                StaticBranchSuspensionBlendLength <= 0f)
            {
                throw new InvalidOperationException(
                    $"Simulation suspension parameters are invalid. " +
                    $"height={SimulationSuspensionHeight:F6} " +
                    $"staticHold={StaticBranchSuspensionHoldLength:F6} " +
                    $"staticBlend={StaticBranchSuspensionBlendLength:F6}");
            }

            EvaluateSimulationSuspension(
                0f,
                out float startHeight,
                out float startDerivative);
            EvaluateSimulationSuspension(
                0.5f,
                out float sutureHeight,
                out _);
            EvaluateSimulationSuspension(
                1f,
                out float endHeight,
                out float endDerivative);
            if (Mathf.Abs(startHeight - SimulationSuspensionHeight) > 1e-6f ||
                Mathf.Abs(sutureHeight - SimulationSuspensionHeight) > 1e-6f ||
                Mathf.Abs(endHeight - SimulationSuspensionHeight) > 1e-6f ||
                Mathf.Abs(startDerivative) > 1e-6f ||
                Mathf.Abs(endDerivative) > 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Simulation suspension profile violates the endpoint contract. " +
                    $"start={startHeight:F6}/{startDerivative:F6} " +
                    $"suture={sutureHeight:F6} " +
                    $"end={endHeight:F6}/{endDerivative:F6}");
            }

            Debug.Log(
                $"[HybridVessel][Suspension] height=" +
                $"{SimulationSuspensionHeight:F6} " +
                $"sutureHeight={sutureHeight:F6} " +
                $"interfaceHeight={endHeight:F6} " +
                $"staticHold={StaticBranchSuspensionHoldLength:F2} " +
                $"staticBlend={StaticBranchSuspensionBlendLength:F2} " +
                "interfaceSlope=0.000000");
        }

        private static void ApplyStaticBranchSuspension(
            StaticBranchBuildResult branch,
            BranchAlignment alignment)
        {
            if (branch?.mesh == null ||
                branch.boundaryIndices == null ||
                branch.boundaryIndices.Count < 3 ||
                alignment == null ||
                alignment.scale <= 1e-6f)
            {
                throw new InvalidOperationException(
                    "Static branch suspension input is invalid.");
            }

            Vector3[] vertices = branch.mesh.vertices;
            int[] triangles = branch.mesh.triangles;
            float[] distances =
                ComputeMeshDistancesFromBoundary(
                    vertices,
                    triangles,
                    branch.boundaryIndices);
            Quaternion inverseRotation =
                Quaternion.Inverse(alignment.rotation);
            float blendEnd =
                StaticBranchSuspensionHoldLength +
                StaticBranchSuspensionBlendLength;
            float maxDisplacement = 0f;
            float maxAnchoredDisplacement = 0f;
            int liftedCount = 0;
            int blendedCount = 0;
            int anchoredCount = 0;

            for (int i = 0; i < vertices.Length; i++)
            {
                float rootDistance = distances[i] * alignment.scale;
                float weight;
                if (rootDistance <= StaticBranchSuspensionHoldLength)
                {
                    weight = 1f;
                    liftedCount++;
                }
                else if (rootDistance >= blendEnd)
                {
                    weight = 0f;
                    anchoredCount++;
                }
                else
                {
                    float u =
                        (rootDistance - StaticBranchSuspensionHoldLength) /
                        StaticBranchSuspensionBlendLength;
                    float u2 = u * u;
                    float u3 = u2 * u;
                    float u4 = u3 * u;
                    float u5 = u4 * u;
                    weight =
                        1f -
                        (6f * u5 - 15f * u4 + 10f * u3);
                    blendedCount++;
                }

                Vector3 rootBefore =
                    alignment.position +
                    alignment.rotation *
                    (vertices[i] * alignment.scale);
                Vector3 rootAfter =
                    rootBefore +
                    Vector3.up *
                    (SimulationSuspensionHeight * weight);
                float displacement =
                    Vector3.Distance(rootBefore, rootAfter);
                maxDisplacement =
                    Mathf.Max(maxDisplacement, displacement);
                if (weight <= 0f)
                {
                    maxAnchoredDisplacement =
                        Mathf.Max(
                            maxAnchoredDisplacement,
                            displacement);
                }
                vertices[i] =
                    inverseRotation *
                    (rootAfter - alignment.position) /
                    alignment.scale;
            }

            branch.mesh.vertices = vertices;
            branch.mesh.RecalculateNormals();
            branch.mesh.RecalculateBounds();

            branch.boundary.Clear();
            alignment.boundaryRoot.Clear();
            alignment.boundaryNormalsRoot.Clear();
            for (int i = 0; i < branch.boundaryIndices.Count; i++)
            {
                int index = branch.boundaryIndices[i];
                Vector3 localPoint = vertices[index];
                Vector3 rootPoint =
                    alignment.position +
                    alignment.rotation *
                    (localPoint * alignment.scale);
                branch.boundary.Add(localPoint);
                alignment.boundaryRoot.Add(rootPoint);
            }
            branch.seamCenter = MeanPoint(branch.boundary);
            Vector3 rootCenter = MeanPoint(alignment.boundaryRoot);
            for (int i = 0; i < alignment.boundaryRoot.Count; i++)
            {
                Vector3 radial =
                    alignment.boundaryRoot[i] - rootCenter;
                radial -=
                    alignment.seamTangentRoot *
                    Vector3.Dot(
                        radial,
                        alignment.seamTangentRoot);
                alignment.boundaryNormalsRoot.Add(radial.normalized);
            }

            float seamHeightError =
                Mathf.Abs(
                    rootCenter.y -
                    (SimulationEndCenter.y +
                     SimulationSuspensionHeight));
            if (maxAnchoredDisplacement > 1e-6f ||
                seamHeightError > 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Static branch suspension contract failed. " +
                    $"anchoredMove={maxAnchoredDisplacement:F8} " +
                    $"seamHeightError={seamHeightError:F8}");
            }

            Debug.Log(
                $"[HybridVessel][StaticSuspension] lifted={liftedCount} " +
                $"blended={blendedCount} anchored={anchoredCount} " +
                $"maxDisplacement={maxDisplacement:F6} " +
                $"anchoredMove={maxAnchoredDisplacement:F8} " +
                $"seamHeightError={seamHeightError:F8}");
        }

        // Work in the entrance-connected tube, not every vertex whose projection
        // happens to be near the seam (distant side branches can project here too).
        // Each real mesh section is moved rigidly about its measured centroid.
        // Blending the straight inlet into that measured centerline avoids the dip
        // produced by rotating offsets about an imaginary straight branch axis.
        private static void LevelStaticBranchInlet(
            StaticBranchBuildResult branch, BranchAlignment alignment)
        {
            Vector3[] before = branch.mesh.vertices;
            int[] triangles = branch.mesh.triangles;
            var root = new Vector3[before.Length];
            var axial = new float[before.Length];
            Vector3 center = MeanPoint(alignment.boundaryRoot);
            Vector3 oldAxis = alignment.seamTangentRoot.normalized;
            Vector3 newAxis = SimulationEndTangent.normalized;
            for (int i = 0; i < root.Length; i++)
            {
                root[i] = alignment.position + alignment.rotation * (before[i] * alignment.scale);
                axial[i] = Vector3.Dot(root[i] - center, oldAxis);
            }
            List<int>[] adjacency = BuildMeshAdjacency(root.Length, triangles);
            var entrance = new bool[root.Length];
            var queue = new Queue<int>();
            foreach (int id in branch.boundaryIndices)
            {
                entrance[id] = true;
                queue.Enqueue(id);
            }
            while (queue.Count > 0)
            {
                int id = queue.Dequeue();
                foreach (int neighbor in adjacency[id])
                {
                    if (entrance[neighbor] || axial[neighbor] >= StaticInletLevelEnd)
                        continue;
                    entrance[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }

            float step = StaticInletLevelEnd / StaticInletSectionCount;
            var centers = new Vector3[StaticInletSectionCount + 1];
            centers[0] = center;
            for (int i = 1; i < centers.Length; i++)
                centers[i] = MeasureInletSectionCenter(
                    root, triangles, axial, entrance, center, oldAxis, i * step);
            Vector3[] second = FitInletCenterSpline(centers, step, oldAxis);
            // A convex blend of a horizontal inlet and the original centerline
            // stays above the seam only if the source centerline does too. Prove
            // that precondition; never clamp bad geometry into a successful build.
            float minCenterHeight = float.PositiveInfinity;
            for (int i = 0; i <= StaticInletSectionCount * 8; i++)
            {
                float s = StaticInletLevelEnd * i / (StaticInletSectionCount * 8f);
                Vector3 reference = EvaluateInletCenter(centers, second, step, s);
                if (reference.y < center.y - 1e-5f)
                    throw new InvalidOperationException($"Inlet source centerline descends below seam at s={s:F6}.");
                Vector3 target = Vector3.Lerp(center + newAxis * s, reference, InletRestoreWeight(s));
                minCenterHeight = Mathf.Min(minCenterHeight, target.y);
            }

            var after = (Vector3[])before.Clone();
            Quaternion inverse = Quaternion.Inverse(alignment.rotation);
            float maxSectionRadiusError = 0f;
            float maxRadiusAdjustment = 0f;
            float maxMove = 0f;
            int moved = 0;
            int radiusAdjusted = 0;
            for (int i = 0; i < after.Length; i++)
            {
                if (!entrance[i])
                    continue; // Keep all distant vertices bitwise unchanged.
                if (axial[i] < -1e-5f)
                    throw new InvalidOperationException($"Inlet vertex {i} is behind its boundary plane.");
                float s = Mathf.Max(0f, axial[i]);
                Vector3 reference = EvaluateInletCenter(centers, second, step, s);
                float restore = InletRestoreWeight(s);
                Vector3 target = Vector3.Lerp(center + newAxis * s, reference, restore);
                Vector3 sectionAxis = Vector3.Lerp(newAxis, oldAxis, restore).normalized;
                Vector3 offset = root[i] - reference;
                float axialOffset = Vector3.Dot(offset, oldAxis);
                Vector3 radialOffset = offset - oldAxis * axialOffset;
                float nativeRadius = radialOffset.magnitude;
                Vector3 harmonizedOffset = offset;
                float radiusRestore = StaticBranchDiameterNativeWeight(s);
                if (nativeRadius > 1e-8f && radiusRestore < 1f - 1e-7f)
                {
                    float harmonizedRadius = Mathf.Lerp(
                        OuterRadius,
                        nativeRadius,
                        radiusRestore);
                    harmonizedOffset =
                        radialOffset * (harmonizedRadius / nativeRadius) +
                        oldAxis * axialOffset;
                    maxRadiusAdjustment = Mathf.Max(
                        maxRadiusAdjustment,
                        Mathf.Abs(harmonizedRadius - nativeRadius));
                    radiusAdjusted++;
                }
                Vector3 rotated =
                    Quaternion.FromToRotation(oldAxis, sectionAxis) *
                    harmonizedOffset;
                Vector3 point = target + rotated;
                maxSectionRadiusError = Mathf.Max(maxSectionRadiusError,
                    Mathf.Abs(rotated.magnitude - harmonizedOffset.magnitude));
                maxMove = Mathf.Max(maxMove, Vector3.Distance(point, root[i]));
                after[i] = inverse * (point - alignment.position) / alignment.scale;
                moved++;
            }
            if (maxSectionRadiusError > 1e-5f || minCenterHeight < center.y - 1e-5f)
                throw new InvalidOperationException("Inlet leveling violates section radius/height contract.");

            branch.mesh.vertices = after;
            ValidateStaticBranchDeformation(branch.mesh, before, alignment);
            branch.mesh.RecalculateNormals();
            branch.mesh.RecalculateBounds();
            alignment.seamTangentRoot = newAxis;
            branch.seamTangent = (inverse * newAxis).normalized;
            branch.boundary.Clear();
            alignment.boundaryRoot.Clear();
            alignment.boundaryNormalsRoot.Clear();
            foreach (int id in branch.boundaryIndices)
            {
                branch.boundary.Add(after[id]);
                Vector3 point = alignment.position + alignment.rotation * (after[id] * alignment.scale);
                alignment.boundaryRoot.Add(point);
                alignment.boundaryNormalsRoot.Add(Vector3.ProjectOnPlane(point - center, newAxis).normalized);
            }
            branch.seamCenter = MeanPoint(branch.boundary);
            float seamMove = Vector3.Distance(MeanPoint(alignment.boundaryRoot), center);
            if (seamMove > 1e-5f)
                throw new InvalidOperationException($"Inlet seam center moved by {seamMove:F8}.");
            Debug.Log($"[HybridVessel][InletLevel] entrance={moved} anchored={after.Length - moved} " +
                $"hold={StaticInletLevelHold:F3} end={StaticInletLevelEnd:F3} " +
                $"staticBranchDiameterAdjusted={radiusAdjusted} " +
                $"diameterHold={StaticBranchDiameterHoldLength:F3} " +
                $"diameterEnd={StaticBranchDiameterBlendEnd:F3} " +
                $"maxDiameterAdjust={maxRadiusAdjustment:F6} " +
                $"oldTilt={Vector3.Angle(oldAxis, newAxis):F3} newTangent={newAxis:F6} " +
                $"minCenterY={minCenterHeight:F6} radiusError={maxSectionRadiusError:F8} " +
                $"seamMove={seamMove:F8} maxMove={maxMove:F6}");
        }

        private static float InletRestoreWeight(float s)
        {
            float u = Mathf.Clamp01((s - StaticInletLevelHold) /
                (StaticInletLevelEnd - StaticInletLevelHold));
            return QuinticSmooth01(u);
        }

        private static float StaticBranchDiameterNativeWeight(float s)
        {
            float u = Mathf.Clamp01((s - StaticBranchDiameterHoldLength) /
                (StaticBranchDiameterBlendEnd - StaticBranchDiameterHoldLength));
            return QuinticSmooth01(u);
        }

        private static float QuinticSmooth01(float u)
        {
            return u * u * u * (10f + u * (-15f + 6f * u));
        }

        private static Vector3 MeasureInletSectionCenter(
            Vector3[] vertices, int[] triangles, float[] axial, bool[] entrance,
            Vector3 seam, Vector3 axis, float station)
        {
            Vector3 origin = seam + axis * station;
            Vector3 weighted = Vector3.zero;
            float twiceArea = 0f;
            var hits = new Vector3[2];
            var loop = new Dictionary<ulong, List<ulong>>();
            var hitEdges = new ulong[2];
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                if (!entrance[a] && !entrance[b] && !entrance[c])
                    continue;
                int count = 0;
                for (int j = 0; j < 3; j++)
                {
                    int first = triangles[i + j], last = triangles[i + (j + 1) % 3];
                    if (!((axial[first] <= station && axial[last] > station) ||
                          (axial[last] <= station && axial[first] > station)))
                        continue;
                    hits[count] = Vector3.Lerp(vertices[first], vertices[last],
                        (station - axial[first]) / (axial[last] - axial[first]));
                    hitEdges[count++] = ((ulong)(uint)Mathf.Min(first, last) << 32) |
                        (uint)Mathf.Max(first, last);
                }
                if (count == 0)
                    continue;
                if (count != 2)
                    throw new InvalidOperationException($"Inlet section {station:F4} has a non-segment intersection.");
                for (int j = 0; j < 2; j++)
                {
                    if (!loop.TryGetValue(hitEdges[j], out List<ulong> neighbors))
                        loop.Add(hitEdges[j], neighbors = new List<ulong>(2));
                    neighbors.Add(hitEdges[1 - j]);
                }
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                if (Vector3.Dot(hits[1] - hits[0], Vector3.Cross(axis, normal)) < 0f)
                {
                    Vector3 swap = hits[0]; hits[0] = hits[1]; hits[1] = swap;
                }
                // Signed polygon area is essential: the reference axis can lie
                // outside the section once the anatomical branch turns sideways.
                float area = Vector3.Dot(Vector3.Cross(hits[0] - origin, hits[1] - origin), axis);
                twiceArea += area;
                weighted += (origin + hits[0] + hits[1]) * (area / 3f);
            }
            var visited = new HashSet<ulong>();
            var pending = new Queue<ulong>();
            foreach (var pair in loop)
            {
                if (pair.Value.Count != 2)
                    throw new InvalidOperationException($"Inlet section {station:F4} is open or branched.");
                if (pending.Count == 0 && visited.Count == 0)
                {
                    visited.Add(pair.Key);
                    pending.Enqueue(pair.Key);
                }
            }
            while (pending.Count > 0)
                foreach (ulong neighbor in loop[pending.Dequeue()])
                    if (visited.Add(neighbor)) pending.Enqueue(neighbor);
            if (loop.Count < 3 || visited.Count != loop.Count || twiceArea <= 1e-8f)
                throw new InvalidOperationException($"Inlet section {station:F4} is not one oriented closed tube.");
            return weighted / twiceArea;
        }

        // Clamped cubic spline: C2 across measured sections, with the known seam
        // tangent at the entrance. No piecewise-linear centerline corners.
        private static Vector3[] FitInletCenterSpline(Vector3[] points, float step, Vector3 startDerivative)
        {
            int last = points.Length - 1;
            var diagonal = new float[points.Length];
            var rhs = new Vector3[points.Length];
            for (int i = 0; i <= last; i++)
            {
                diagonal[i] = i == 0 || i == last ? 2f : 4f;
                rhs[i] = i == 0 ? ((points[1] - points[0]) / step - startDerivative) * (6f / step) :
                    i == last ? Vector3.zero :
                    (points[i + 1] - 2f * points[i] + points[i - 1]) * (6f / (step * step));
            }
            for (int i = 1; i <= last; i++)
            {
                float factor = 1f / diagonal[i - 1];
                diagonal[i] -= factor;
                rhs[i] -= rhs[i - 1] * factor;
            }
            var second = new Vector3[points.Length];
            second[last] = rhs[last] / diagonal[last];
            for (int i = last - 1; i >= 0; i--)
                second[i] = (rhs[i] - second[i + 1]) / diagonal[i];
            return second;
        }

        private static Vector3 EvaluateInletCenter(Vector3[] points, Vector3[] second, float step, float s)
        {
            int i = Mathf.Clamp(Mathf.FloorToInt(s / step), 0, points.Length - 2);
            float b = (s - i * step) / step, a = 1f - b;
            return a * points[i] + b * points[i + 1] +
                ((a * a * a - a) * second[i] + (b * b * b - b) * second[i + 1]) * (step * step / 6f);
        }

        private static Vector3 SimulationEndCenter =>
            new Vector3(SimulationEndOffsetX, 0f, SimulationLength);

        private static Vector3 SimulationEndTangent =>
            Quaternion.AngleAxis(SimulationEndTiltDegrees, Vector3.up) *
            Vector3.forward;

        private static List<Vector3> GetRingVertices(
            Mesh mesh,
            int ringIndex,
            int radialSegments)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            Vector3[] vertices = mesh.vertices;
            int start = ringIndex * radialSegments;
            if (start < 0 || start + radialSegments > vertices.Length)
                throw new InvalidOperationException(
                    $"Ring {ringIndex} is outside mesh {mesh.name}.");

            var ring = new List<Vector3>(radialSegments);
            for (int i = 0; i < radialSegments; i++)
                ring.Add(vertices[start + i]);
            return ring;
        }

        private static Mesh CreateTransitionMesh(
            List<Vector3> simulationEndRing,
            List<Vector3> branchBoundaryRoot,
            List<Vector3> branchBoundaryNormalsRoot,
            Vector3 branchSeamTangentRoot)
        {
            if (simulationEndRing == null ||
                simulationEndRing.Count != RadialSegments)
            {
                throw new InvalidOperationException(
                    $"Transition start ring mismatch. expected={RadialSegments} " +
                    $"actual={simulationEndRing?.Count ?? 0}");
            }
            if (branchBoundaryRoot == null ||
                branchBoundaryNormalsRoot == null ||
                branchBoundaryRoot.Count != branchBoundaryNormalsRoot.Count)
            {
                throw new InvalidOperationException(
                    "Transition branch-side boundary data is inconsistent.");
            }
            if (branchSeamTangentRoot.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Transition branch-side seam tangent is invalid.");
            }
            branchSeamTangentRoot.Normalize();

            List<Vector3> exactEndRing = AlignBoundaryRingToReference(
                branchBoundaryRoot,
                simulationEndRing);
            if (exactEndRing.Count < 3)
                throw new InvalidOperationException(
                    "Transition end ring does not contain a usable branch boundary.");
            List<Vector3> sampledEndRing = ResampleClosedRing(
                exactEndRing,
                RadialSegments);
            var exactApproachRing = new List<Vector3>(exactEndRing.Count);
            var exactEndNormals = new List<Vector3>(exactEndRing.Count);
            float approachDistance =
                TransitionExtension / TransitionAxialSegments;
            for (int i = 0; i < exactEndRing.Count; i++)
            {
                int sourceIndex = FindNearestPointIndex(
                    branchBoundaryRoot,
                    exactEndRing[i]);
                exactApproachRing.Add(
                    exactEndRing[i] -
                    branchSeamTangentRoot * approachDistance);
                exactEndNormals.Add(
                    branchBoundaryNormalsRoot[sourceIndex].normalized);
            }

            Vector3 startCenter = MeanPoint(simulationEndRing);
            Vector3 endCenter = MeanPoint(exactEndRing);
            Vector3 approachCenter = MeanPoint(exactApproachRing);
            if (Mathf.Abs(startCenter.y - SimulationSuspensionHeight) > 1e-5f ||
                Mathf.Abs(endCenter.y - startCenter.y) > 1e-5f ||
                Vector3.Distance(branchSeamTangentRoot, SimulationEndTangent) > 1e-5f)
                throw new InvalidOperationException("Transition requires level, tangent-aligned inlet endpoints.");
            // Rotate the whole circular section by one phase, rather than lerping
            // offsets toward polygon-resampled points and narrowing the tube.
            Quaternion endPhase = Quaternion.FromToRotation(
                simulationEndRing[0] - startCenter, sampledEndRing[0] - endCenter);
            float approachParameter =
                (TransitionAxialSegments - 1f) / TransitionAxialSegments;
            float derivativeLength =
                Vector3.Distance(startCenter, endCenter) * approachParameter;
            int regularRingCount = TransitionAxialSegments - 1;
            int approachStart = regularRingCount * RadialSegments;
            int endStart = approachStart + exactApproachRing.Count;
            int vertexCount = endStart + exactEndRing.Count;
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];

            int vertexIndex = 0;
            for (int ring = 0; ring < regularRingCount; ring++)
            {
                float v = ring / (float)TransitionAxialSegments;
                float t = v / approachParameter;
                float smooth = t * t * (3f - 2f * t);
                // The old linear centerline only turned at the last ring. Match
                // the longitudinal tangents before joining the straight seam collar.
                Vector3 center = EvaluateHermiteCenter(
                    startCenter, approachCenter,
                    SimulationEndTangent * derivativeLength,
                    branchSeamTangentRoot * derivativeLength, t);

                for (int column = 0; column < RadialSegments; column++)
                {
                    float u = column / (float)RadialSegments;
                    Vector3 startOffset = simulationEndRing[column] - startCenter;
                    vertices[vertexIndex] =
                        center + Quaternion.Slerp(Quaternion.identity, endPhase, smooth) * startOffset;
                    uv[vertexIndex] = new Vector2(u, v);
                    vertexIndex++;
                }
            }

            for (int column = 0; column < exactApproachRing.Count; column++)
            {
                vertices[vertexIndex] = exactApproachRing[column];
                uv[vertexIndex] = new Vector2(
                    column / (float)exactApproachRing.Count,
                    (TransitionAxialSegments - 1f) /
                    TransitionAxialSegments);
                vertexIndex++;
            }
            for (int column = 0; column < exactEndRing.Count; column++)
            {
                vertices[vertexIndex] = exactEndRing[column];
                uv[vertexIndex] = new Vector2(
                    column / (float)exactEndRing.Count,
                    1f);
                vertexIndex++;
            }

            var triangles = new List<int>(
                (regularRingCount - 1) * RadialSegments * 6 +
                (RadialSegments + exactApproachRing.Count) * 3 +
                exactEndRing.Count * 6);
            for (int ring = 0; ring + 1 < regularRingCount; ring++)
            {
                int lower = ring * RadialSegments;
                int upper = (ring + 1) * RadialSegments;
                for (int column = 0; column < RadialSegments; column++)
                {
                    int next = (column + 1) % RadialSegments;
                    int lowerCurrent = lower + column;
                    int lowerNext = lower + next;
                    int upperCurrent = upper + column;
                    int upperNext = upper + next;
                    triangles.Add(lowerCurrent);
                    triangles.Add(lowerNext);
                    triangles.Add(upperCurrent);
                    triangles.Add(lowerNext);
                    triangles.Add(upperNext);
                    triangles.Add(upperCurrent);
                }
            }
            AddUnequalRingStrip(
                triangles,
                (regularRingCount - 1) * RadialSegments,
                RadialSegments,
                approachStart,
                exactApproachRing.Count);
            AddEqualRingStrip(
                triangles,
                approachStart,
                endStart,
                exactEndRing.Count);

            var mesh = new Mesh
            {
                name = "HybridStaticTransition",
                vertices = vertices,
                uv = uv,
                triangles = triangles.ToArray(),
            };
            mesh.RecalculateNormals();
            Vector3[] normals = mesh.normals;
            for (int column = 0; column < RadialSegments; column++)
            {
                normals[column] =
                    (simulationEndRing[column] - startCenter).normalized;
            }
            for (int column = 0; column < exactEndRing.Count; column++)
            {
                normals[endStart + column] = exactEndNormals[column];
            }
            mesh.normals = normals;
            mesh.RecalculateBounds();
            float maxCenterHeightError = 0f;
            float maxRadiusError = 0f;
            for (int ring = 0; ring <= TransitionAxialSegments; ring++)
            {
                int start = ring < regularRingCount ? ring * RadialSegments :
                    ring == regularRingCount ? approachStart : endStart;
                int count = ring < regularRingCount ? RadialSegments : exactEndRing.Count;
                Vector3 ringCenter = Vector3.zero;
                for (int j = 0; j < count; j++) ringCenter += vertices[start + j];
                ringCenter /= count;
                maxCenterHeightError = Mathf.Max(maxCenterHeightError,
                    Mathf.Abs(ringCenter.y - SimulationSuspensionHeight));
                for (int j = 0; j < count; j++)
                    maxRadiusError = Mathf.Max(maxRadiusError,
                        Mathf.Abs(Vector3.Distance(vertices[start + j], ringCenter) - OuterRadius));
            }
            if (maxCenterHeightError > 1e-5f || maxRadiusError > 1e-5f)
                throw new InvalidOperationException($"Transition height/radius failed: {maxCenterHeightError:F8}/{maxRadiusError:F8}.");
            Debug.Log($"[HybridVessel][TransitionLevel] rings={TransitionAxialSegments + 1} " +
                $"centerY={SimulationSuspensionHeight:F6} heightError={maxCenterHeightError:F8} radiusError={maxRadiusError:F8}");
            Debug.Log(
                $"[HybridVessel] Transition seam approach boundary={exactEndRing.Count} " +
                $"step={approachDistance:F8} " +
                $"branchTangent={branchSeamTangentRoot:F6} centerline=Hermite");
            return mesh;
        }

        private static Vector3 EvaluateHermiteCenter(
            Vector3 start, Vector3 end, Vector3 startDerivative,
            Vector3 endDerivative, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * start +
                   (t3 - 2f * t2 + t) * startDerivative +
                   (-2f * t3 + 3f * t2) * end +
                   (t3 - t2) * endDerivative;
        }

        private static void AddEqualRingStrip(
            List<int> triangles,
            int lowerStart,
            int upperStart,
            int ringCount)
        {
            if (triangles == null || ringCount < 3)
                throw new InvalidOperationException(
                    "Transition equal-ring strip input is invalid.");

            for (int column = 0; column < ringCount; column++)
            {
                int next = (column + 1) % ringCount;
                triangles.Add(lowerStart + column);
                triangles.Add(lowerStart + next);
                triangles.Add(upperStart + column);
                triangles.Add(lowerStart + next);
                triangles.Add(upperStart + next);
                triangles.Add(upperStart + column);
            }
        }

        private static void AddUnequalRingStrip(
            List<int> triangles,
            int lowerStart,
            int lowerCount,
            int upperStart,
            int upperCount)
        {
            if (triangles == null ||
                lowerCount < 3 ||
                upperCount < 3)
            {
                throw new InvalidOperationException(
                    "Transition unequal-ring strip input is invalid.");
            }

            int lower = 0;
            int upper = 0;
            while (lower < lowerCount || upper < upperCount)
            {
                float nextLower =
                    lower < lowerCount
                        ? (lower + 1f) / lowerCount
                        : float.PositiveInfinity;
                float nextUpper =
                    upper < upperCount
                        ? (upper + 1f) / upperCount
                        : float.PositiveInfinity;
                int lowerCurrent = lowerStart + lower % lowerCount;
                int upperCurrent = upperStart + upper % upperCount;

                if (Mathf.Abs(nextLower - nextUpper) <= 1e-6f)
                {
                    int lowerNext =
                        lowerStart + (lower + 1) % lowerCount;
                    int upperNext =
                        upperStart + (upper + 1) % upperCount;
                    triangles.Add(lowerCurrent);
                    triangles.Add(lowerNext);
                    triangles.Add(upperCurrent);
                    triangles.Add(lowerNext);
                    triangles.Add(upperNext);
                    triangles.Add(upperCurrent);
                    lower++;
                    upper++;
                }
                else if (nextLower < nextUpper)
                {
                    int lowerNext =
                        lowerStart + (lower + 1) % lowerCount;
                    triangles.Add(lowerCurrent);
                    triangles.Add(lowerNext);
                    triangles.Add(upperCurrent);
                    lower++;
                }
                else
                {
                    int upperNext =
                        upperStart + (upper + 1) % upperCount;
                    triangles.Add(lowerCurrent);
                    triangles.Add(upperNext);
                    triangles.Add(upperCurrent);
                    upper++;
                }
            }
        }

        private static StaticBranchBuildResult CreateTrimmedStaticBranchMesh()
        {
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourceModelPath);
            if (sourcePrefab == null)
                throw new InvalidOperationException($"Source FBX could not be loaded at {SourceModelPath}");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var sourceInstance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (sourceInstance == null)
                throw new InvalidOperationException("Could not instantiate the Tripo branch source prefab.");

            try
            {
                MeshFilter[] meshFilters = sourceInstance.GetComponentsInChildren<MeshFilter>(true);
                if (meshFilters.Length == 0)
                    throw new InvalidOperationException("Tripo branch source contains no MeshFilter.");

                // Bake the complete imported FBX hierarchy into the output mesh.
                // In particular, retain Unity's FBX axis conversion instead of
                // transforming vertices back into the file's pre-conversion root.
                Matrix4x4 worldToSource = Matrix4x4.identity;
                float minAxis = float.PositiveInfinity;
                float maxAxis = float.NegativeInfinity;
                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    Mesh sourceMesh = meshFilters[meshIndex].sharedMesh;
                    if (sourceMesh == null)
                        continue;

                    Matrix4x4 toSource = worldToSource * meshFilters[meshIndex].transform.localToWorldMatrix;
                    Vector3[] sourceVertices = sourceMesh.vertices;
                    for (int vertexIndex = 0; vertexIndex < sourceVertices.Length; vertexIndex++)
                    {
                        Vector3 sourcePoint = toSource.MultiplyPoint3x4(sourceVertices[vertexIndex]);
                        float axisValue = sourcePoint.y;
                        minAxis = Mathf.Min(minAxis, axisValue);
                        maxAxis = Mathf.Max(maxAxis, axisValue);
                    }
                }

                if (float.IsNaN(minAxis) ||
                    float.IsInfinity(minAxis) ||
                    float.IsNaN(maxAxis) ||
                    float.IsInfinity(maxAxis) ||
                    maxAxis <= minAxis)
                    throw new InvalidOperationException("Tripo branch anatomical bounds are invalid.");

                float cutAxis = Mathf.Lerp(minAxis, maxAxis, StaticBranchCutNormalized);
                // The trim location is defined along the imported trunk's local
                // Y axis. Angling this plane toward a fitted centerline crosses
                // more than one surface component in this source mesh, so the
                // single-loop entrance contract must remain explicit.
                Vector3 seamTangent = Vector3.up;
                Vector3 seamPlaneCenter = new Vector3(0f, cutAxis, 0f);

                var outputVertices = new List<Vector3>(4096);
                var outputTriangles = new List<int>(8192);
                var vertexMap = new Dictionary<ulong, int>();
                var edgeIntersectionMap = new Dictionary<SourceEdgeKey, int>();
                int keptSourceTriangles = 0;
                int clippedSourceTriangles = 0;
                int removedSourceTriangles = 0;

                for (int meshIndex = 0; meshIndex < meshFilters.Length; meshIndex++)
                {
                    Mesh sourceMesh = meshFilters[meshIndex].sharedMesh;
                    if (sourceMesh == null)
                        continue;

                    Matrix4x4 toSource = worldToSource * meshFilters[meshIndex].transform.localToWorldMatrix;
                    Vector3[] sourceVertices = sourceMesh.vertices;
                    int[] sourceTriangles = sourceMesh.triangles;
                    for (int triangleIndex = 0; triangleIndex + 2 < sourceTriangles.Length; triangleIndex += 3)
                    {
                        int ia = sourceTriangles[triangleIndex];
                        int ib = sourceTriangles[triangleIndex + 1];
                        int ic = sourceTriangles[triangleIndex + 2];
                        Vector3 a = toSource.MultiplyPoint3x4(sourceVertices[ia]);
                        Vector3 b = toSource.MultiplyPoint3x4(sourceVertices[ib]);
                        Vector3 c = toSource.MultiplyPoint3x4(sourceVertices[ic]);
                        float da = Vector3.Dot(a - seamPlaneCenter, seamTangent);
                        float db = Vector3.Dot(b - seamPlaneCenter, seamTangent);
                        float dc = Vector3.Dot(c - seamPlaneCenter, seamTangent);
                        List<ClippedVertex> clipped = ClipTriangleAbovePlane(
                            ia,
                            a,
                            da,
                            ib,
                            b,
                            db,
                            ic,
                            c,
                            dc,
                            seamPlaneCenter,
                            seamTangent);
                        if (clipped.Count < 3)
                        {
                            removedSourceTriangles++;
                            continue;
                        }

                        int insideCount =
                            (da >= 0f ? 1 : 0) +
                            (db >= 0f ? 1 : 0) +
                            (dc >= 0f ? 1 : 0);
                        if (insideCount == 3)
                            keptSourceTriangles++;
                        else
                            clippedSourceTriangles++;

                        int first = MapClippedStaticBranchVertex(
                            meshIndex,
                            clipped[0],
                            outputVertices,
                            vertexMap,
                            edgeIntersectionMap);
                        for (int polygonIndex = 1;
                             polygonIndex + 1 < clipped.Count;
                             polygonIndex++)
                        {
                            int second = MapClippedStaticBranchVertex(
                                meshIndex,
                                clipped[polygonIndex],
                                outputVertices,
                                vertexMap,
                                edgeIntersectionMap);
                            int third = MapClippedStaticBranchVertex(
                                meshIndex,
                                clipped[polygonIndex + 1],
                                outputVertices,
                                vertexMap,
                                edgeIntersectionMap);
                            if (first == second || second == third || third == first)
                                continue;
                            outputTriangles.Add(first);
                            outputTriangles.Add(second);
                            outputTriangles.Add(third);
                        }
                    }
                }

                if (outputVertices.Count < 8 || outputTriangles.Count < 3)
                    throw new InvalidOperationException("Static branch trimming removed all usable geometry.");

                var mesh = new Mesh
                {
                    name = "HybridStaticBranches",
                    vertices = outputVertices.ToArray(),
                    triangles = outputTriangles.ToArray(),
                };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                LogStaticBranchComponents(mesh, "trimmed-source");

                List<int> boundaryIndices = FindSingleBoundaryLoop(
                    outputTriangles,
                    outputVertices.Count,
                    "static-branch-trim");
                var boundary = new List<Vector3>(boundaryIndices.Count);
                for (int i = 0; i < boundaryIndices.Count; i++)
                    boundary.Add(outputVertices[boundaryIndices[i]]);

                Vector3 seamCenter = MeanPoint(boundary);
                float radiusSum = 0f;
                for (int i = 0; i < boundary.Count; i++)
                {
                    Vector3 delta = boundary[i] - seamCenter;
                    Vector3 radial = delta - seamTangent * Vector3.Dot(delta, seamTangent);
                    radiusSum += radial.magnitude;
                }
                float seamRadius = Mathf.Max(1e-6f, radiusSum / boundary.Count);
                float seamRelaxationLength =
                    seamRadius * StaticBranchSeamRelaxationRadiusScale;
                RegularizeStaticBranchBoundary(
                    outputVertices,
                    boundary,
                    boundaryIndices,
                    seamCenter,
                    seamTangent,
                    seamRadius,
                    seamRelaxationLength);
                mesh.vertices = outputVertices.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                Debug.Log(
                    $"[HybridVessel] Static branch trim cutNormalized={StaticBranchCutNormalized:F3} " +
                    $"cutAxis={cutAxis:F6} keptVerts={mesh.vertexCount} " +
                    $"keptTris={keptSourceTriangles} clippedTris={clippedSourceTriangles} " +
                    $"removedTris={removedSourceTriangles} " +
                    $"boundary={boundary.Count} seamRadius={seamRadius:F8} " +
                    $"relaxation={seamRelaxationLength:F8} tangent={seamTangent:F6}");
                return new StaticBranchBuildResult
                {
                    mesh = mesh,
                    boundary = boundary,
                    boundaryIndices = boundaryIndices,
                    seamCenter = seamCenter,
                    seamTangent = seamTangent,
                    seamRadius = seamRadius,
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sourceInstance);
            }
        }

        private static void LogStaticBranchComponents(
            Mesh mesh,
            string label)
        {
            if (mesh == null)
                throw new ArgumentNullException(nameof(mesh));

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int triangleCount = triangles.Length / 3;
            var triangleByVertex = new List<int>[vertices.Length];
            for (int vertex = 0; vertex < vertices.Length; vertex++)
                triangleByVertex[vertex] = new List<int>(8);
            for (int triangle = 0; triangle < triangleCount; triangle++)
            {
                int start = triangle * 3;
                triangleByVertex[triangles[start]].Add(triangle);
                triangleByVertex[triangles[start + 1]].Add(triangle);
                triangleByVertex[triangles[start + 2]].Add(triangle);
            }

            var visited = new bool[triangleCount];
            var queue = new Queue<int>();
            var summaries = new List<string>();
            var componentTriangleCounts = new List<int>();
            var componentVertexCounts = new List<int>();
            var componentBounds = new List<Bounds>();
            for (int seed = 0; seed < triangleCount; seed++)
            {
                if (visited[seed])
                    continue;

                visited[seed] = true;
                queue.Enqueue(seed);
                int trianglesInComponent = 0;
                var componentVertices = new HashSet<int>();
                bool hasBounds = false;
                var bounds = new Bounds();
                while (queue.Count > 0)
                {
                    int triangle = queue.Dequeue();
                    trianglesInComponent++;
                    int start = triangle * 3;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = triangles[start + corner];
                        if (componentVertices.Add(vertex))
                        {
                            if (!hasBounds)
                            {
                                bounds = new Bounds(
                                    vertices[vertex],
                                    Vector3.zero);
                                hasBounds = true;
                            }
                            else
                            {
                                bounds.Encapsulate(vertices[vertex]);
                            }
                        }

                        List<int> neighbors = triangleByVertex[vertex];
                        for (int neighborIndex = 0;
                             neighborIndex < neighbors.Count;
                             neighborIndex++)
                        {
                            int neighbor = neighbors[neighborIndex];
                            if (visited[neighbor])
                                continue;
                            visited[neighbor] = true;
                            queue.Enqueue(neighbor);
                        }
                    }
                }

                componentTriangleCounts.Add(trianglesInComponent);
                componentVertexCounts.Add(componentVertices.Count);
                componentBounds.Add(bounds);
            }

            var order = new List<int>(componentTriangleCounts.Count);
            for (int component = 0;
                 component < componentTriangleCounts.Count;
                 component++)
            {
                order.Add(component);
            }
            order.Sort(
                (a, b) =>
                    componentTriangleCounts[b].CompareTo(
                        componentTriangleCounts[a]));
            int previewCount = Mathf.Min(12, order.Count);
            for (int rank = 0; rank < previewCount; rank++)
            {
                int component = order[rank];
                Bounds bounds = componentBounds[component];
                summaries.Add(
                    $"#{rank} tris={componentTriangleCounts[component]} " +
                    $"verts={componentVertexCounts[component]} " +
                    $"center={bounds.center:F4} size={bounds.size:F4}");
            }

            Debug.Log(
                $"[HybridVessel] Static component scan label={label} " +
                $"count={order.Count} preview=[" +
                string.Join("; ", summaries) +
                "]");
        }

        private static void RegularizeStaticBranchBoundary(
            List<Vector3> vertices,
            List<Vector3> boundary,
            List<int> boundaryIndices,
            Vector3 seamCenter,
            Vector3 seamTangent,
            float seamRadius,
            float relaxationLength)
        {
            if (vertices == null ||
                boundary == null ||
                boundaryIndices == null ||
                boundary.Count != boundaryIndices.Count ||
                boundary.Count < 3 ||
                seamRadius <= 1e-6f || relaxationLength <= 1e-6f)
            {
                throw new InvalidOperationException(
                    "Static branch boundary-regularization input is invalid.");
            }

            Vector3 ringX =
                boundary[0] -
                seamCenter -
                seamTangent *
                Vector3.Dot(
                    boundary[0] - seamCenter,
                    seamTangent);
            if (ringX.sqrMagnitude <= 1e-12f)
                throw new InvalidOperationException(
                    "Static branch boundary has no radial direction.");
            ringX.Normalize();
            Vector3 ringY =
                Vector3.Cross(seamTangent, ringX).normalized;
            Vector3 secondOffset =
                boundary[1] -
                seamCenter -
                seamTangent *
                Vector3.Dot(
                    boundary[1] - seamCenter,
                    seamTangent);
            if (Vector3.Dot(secondOffset, ringY) < 0f)
                ringY = -ringY;

            // The clipped ring is not uniformly sampled. Preserve its real
            // angular order, then extend the SAME monotone angular map through
            // the collar. Moving only the boundary identities folds the faces
            // between a newly uniform ring and the old irregular interior.
            var sourceAngles = new float[boundary.Count + 1];
            float fullTurn = 2f * Mathf.PI;
            float maxAngularWarp = 0f;
            for (int i = 1; i < boundary.Count; i++)
            {
                Vector3 offset = boundary[i] - seamCenter;
                float angle = Mathf.Atan2(Vector3.Dot(offset, ringY), Vector3.Dot(offset, ringX));
                if (angle < 0f)
                    angle += fullTurn;
                if (angle <= sourceAngles[i - 1] + 1e-7f)
                    throw new InvalidOperationException(
                        $"Static seam angular order is not monotone at sample {i}.");
                sourceAngles[i] = angle;
                maxAngularWarp = Mathf.Max(maxAngularWarp,
                    Mathf.Abs(angle - fullTurn * i / boundary.Count));
            }
            sourceAngles[boundary.Count] = fullTurn;
            int warpedCount = 0;
            for (int i = 0; i < vertices.Count; i++)
            {
                Vector3 offset = vertices[i] - seamCenter;
                float axial = Vector3.Dot(offset, seamTangent);
                if (axial < -1e-7f || axial >= relaxationLength)
                    continue;
                Vector3 radial = offset - axial * seamTangent;
                float angle = Mathf.Atan2(Vector3.Dot(radial, ringY), Vector3.Dot(radial, ringX));
                if (angle < 0f)
                    angle += fullTurn;
                int segment = 0;
                while (segment + 1 < boundary.Count && sourceAngles[segment + 1] < angle)
                    segment++;
                float fraction = Mathf.InverseLerp(sourceAngles[segment], sourceAngles[segment + 1], angle);
                float mapped = fullTurn * (segment + fraction) / boundary.Count;
                float t = Mathf.Clamp01(axial / relaxationLength);
                float blend = t * t * t * (10f + t * (-15f + 6f * t));
                float outputAngle = Mathf.Lerp(mapped, angle, blend);
                float radius = Mathf.Lerp(seamRadius, radial.magnitude, blend);
                vertices[i] = seamCenter + axial * seamTangent + radius *
                    (ringX * Mathf.Cos(outputAngle) + ringY * Mathf.Sin(outputAngle));
                warpedCount++;
            }
            for (int i = 0; i < boundary.Count; i++)
            {
                float angle = fullTurn * i / boundary.Count;
                Vector3 point = seamCenter + seamRadius *
                    (ringX * Mathf.Cos(angle) + ringY * Mathf.Sin(angle));
                boundary[i] = point;
                vertices[boundaryIndices[i]] = point;
            }
            Debug.Log($"[HybridVessel][SeamRegularization] samples={boundary.Count} " +
                $"warpedVertices={warpedCount} maxAngularWarp={maxAngularWarp * Mathf.Rad2Deg:F3} " +
                "angularOrder=monotone collarMap=shared");
        }

        private static int MapStaticBranchVertex(
            int meshIndex,
            int sourceIndex,
            Vector3 position,
            List<Vector3> outputVertices,
            Dictionary<ulong, int> vertexMap)
        {
            ulong key = ((ulong)(uint)meshIndex << 32) | (uint)sourceIndex;
            if (vertexMap.TryGetValue(key, out int outputIndex))
                return outputIndex;

            outputIndex = outputVertices.Count;
            outputVertices.Add(position);
            vertexMap.Add(key, outputIndex);
            return outputIndex;
        }

        private static List<ClippedVertex> ClipTriangleAbovePlane(
            int ia,
            Vector3 a,
            float da,
            int ib,
            Vector3 b,
            float db,
            int ic,
            Vector3 c,
            float dc,
            Vector3 planeCenter,
            Vector3 planeNormal)
        {
            var input = new[]
            {
                ClippedVertex.Original(a, ia),
                ClippedVertex.Original(b, ib),
                ClippedVertex.Original(c, ic),
            };
            var signedDistances = new[] { da, db, dc };
            var output = new List<ClippedVertex>(4);
            for (int i = 0; i < input.Length; i++)
            {
                ClippedVertex current = input[i];
                ClippedVertex next = input[(i + 1) % input.Length];
                float currentDistance = signedDistances[i];
                float nextDistance = signedDistances[(i + 1) % input.Length];
                bool currentInside = currentDistance >= 0f;
                bool nextInside = nextDistance >= 0f;

                if (currentInside && nextInside)
                {
                    AddUniqueClippedVertex(output, next);
                }
                else if (currentInside && !nextInside)
                {
                    AddUniqueClippedVertex(
                        output,
                        IntersectPlane(
                            current,
                            currentDistance,
                            next,
                            nextDistance,
                            planeCenter,
                            planeNormal));
                }
                else if (!currentInside && nextInside)
                {
                    AddUniqueClippedVertex(
                        output,
                        IntersectPlane(
                            current,
                            currentDistance,
                            next,
                            nextDistance,
                            planeCenter,
                            planeNormal));
                    AddUniqueClippedVertex(output, next);
                }
            }

            if (output.Count > 1 &&
                (output[0].position - output[output.Count - 1].position).sqrMagnitude <= 1e-14f)
            {
                output.RemoveAt(output.Count - 1);
            }
            return output;
        }

        private static ClippedVertex IntersectPlane(
            ClippedVertex first,
            float firstDistance,
            ClippedVertex second,
            float secondDistance,
            Vector3 planeCenter,
            Vector3 planeNormal)
        {
            float denominator = firstDistance - secondDistance;
            if (Mathf.Abs(denominator) <= 1e-12f)
                return first;
            float t = Mathf.Clamp01(firstDistance / denominator);
            Vector3 position = Vector3.Lerp(first.position, second.position, t);
            position -=
                planeNormal *
                Vector3.Dot(position - planeCenter, planeNormal);
            return ClippedVertex.Intersection(
                position,
                first.originalIndex,
                second.originalIndex);
        }

        private static void AddUniqueClippedVertex(
            List<ClippedVertex> output,
            ClippedVertex vertex)
        {
            if (output.Count > 0 &&
                (output[output.Count - 1].position - vertex.position).sqrMagnitude <= 1e-14f)
            {
                return;
            }
            output.Add(vertex);
        }

        private static int MapClippedStaticBranchVertex(
            int meshIndex,
            ClippedVertex vertex,
            List<Vector3> outputVertices,
            Dictionary<ulong, int> vertexMap,
            Dictionary<SourceEdgeKey, int> edgeIntersectionMap)
        {
            if (vertex.originalIndex >= 0)
            {
                return MapStaticBranchVertex(
                    meshIndex,
                    vertex.originalIndex,
                    vertex.position,
                    outputVertices,
                    vertexMap);
            }

            var key = new SourceEdgeKey(
                meshIndex,
                vertex.edgeA,
                vertex.edgeB);
            if (edgeIntersectionMap.TryGetValue(key, out int outputIndex))
                return outputIndex;

            outputIndex = outputVertices.Count;
            outputVertices.Add(vertex.position);
            edgeIntersectionMap.Add(key, outputIndex);
            return outputIndex;
        }

        private static List<int> FindSingleBoundaryLoop(
            List<int> triangles,
            int vertexCount,
            string label)
        {
            var multiplicity = new Dictionary<ulong, int>();
            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                AddEdgeMultiplicity(multiplicity, triangles[i], triangles[i + 1]);
                AddEdgeMultiplicity(multiplicity, triangles[i + 1], triangles[i + 2]);
                AddEdgeMultiplicity(multiplicity, triangles[i + 2], triangles[i]);
            }

            var adjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<ulong, int> pair in multiplicity)
            {
                if (pair.Value != 1)
                    continue;

                int a = (int)(pair.Key >> 32);
                int b = (int)(pair.Key & 0xffffffffUL);
                if (a < 0 || a >= vertexCount || b < 0 || b >= vertexCount)
                    throw new InvalidOperationException($"{label}: boundary edge index is invalid.");
                AddBoundaryNeighbor(adjacency, a, b);
                AddBoundaryNeighbor(adjacency, b, a);
            }

            if (adjacency.Count < 3)
                throw new InvalidOperationException($"{label}: no usable boundary loop.");

            foreach (KeyValuePair<int, List<int>> pair in adjacency)
            {
                if (pair.Value.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"{label}: boundary is open or branched at vertex {pair.Key}, degree={pair.Value.Count}.");
                }
            }

            int start = int.MaxValue;
            foreach (int vertex in adjacency.Keys)
                start = Mathf.Min(start, vertex);

            var loop = new List<int>(adjacency.Count);
            int previous = -1;
            int current = start;
            for (int guard = 0; guard <= adjacency.Count; guard++)
            {
                loop.Add(current);
                List<int> neighbors = adjacency[current];
                int next = neighbors[0] != previous ? neighbors[0] : neighbors[1];
                previous = current;
                current = next;
                if (current == start)
                    break;
            }

            if (current != start || loop.Count != adjacency.Count)
            {
                throw new InvalidOperationException(
                    $"{label}: expected one simple boundary loop, visited={loop.Count}, boundaryVerts={adjacency.Count}.");
            }
            return loop;
        }

        private static void AddBoundaryNeighbor(
            Dictionary<int, List<int>> adjacency,
            int vertex,
            int neighbor)
        {
            if (!adjacency.TryGetValue(vertex, out List<int> neighbors))
            {
                neighbors = new List<int>(2);
                adjacency.Add(vertex, neighbors);
            }
            neighbors.Add(neighbor);
        }

        private static List<Vector3> ResampleClosedRing(
            List<Vector3> source,
            int targetCount)
        {
            if (source == null || source.Count < 3 || targetCount < 3)
                throw new InvalidOperationException(
                    "Closed-ring resampling input is invalid.");

            float totalLength = 0f;
            var cumulative = new float[source.Count + 1];
            for (int i = 0; i < source.Count; i++)
            {
                totalLength += Vector3.Distance(
                    source[i],
                    source[(i + 1) % source.Count]);
                cumulative[i + 1] = totalLength;
            }
            if (totalLength <= 1e-8f)
                throw new InvalidOperationException(
                    "Closed-ring perimeter is degenerate.");

            var result = new List<Vector3>(targetCount);
            int edge = 0;
            for (int sample = 0; sample < targetCount; sample++)
            {
                float distance = totalLength * sample / targetCount;
                while (edge + 1 < source.Count &&
                       cumulative[edge + 1] < distance)
                {
                    edge++;
                }

                int next = (edge + 1) % source.Count;
                float edgeLength = Mathf.Max(
                    1e-8f,
                    cumulative[edge + 1] - cumulative[edge]);
                float t = (distance - cumulative[edge]) / edgeLength;
                result.Add(Vector3.Lerp(source[edge], source[next], t));
            }
            return result;
        }

        private static List<Vector3> AlignBoundaryRingToReference(
            List<Vector3> ring,
            List<Vector3> reference)
        {
            if (ring == null ||
                reference == null ||
                ring.Count < 3 ||
                reference.Count < 3)
            {
                throw new InvalidOperationException(
                    "Transition boundary correspondence input is invalid.");
            }

            Vector3 referenceCenter = MeanPoint(reference);
            float bestError = float.PositiveInfinity;
            List<Vector3> best = null;
            for (int directionIndex = 0;
                 directionIndex < 2;
                 directionIndex++)
            {
                int direction = directionIndex == 0 ? 1 : -1;
                for (int offset = 0; offset < ring.Count; offset++)
                {
                    var candidate = new List<Vector3>(ring.Count);
                    for (int i = 0; i < ring.Count; i++)
                    {
                        int sourceIndex =
                            (offset + direction * i) % ring.Count;
                        if (sourceIndex < 0)
                            sourceIndex += ring.Count;
                        candidate.Add(ring[sourceIndex]);
                    }

                    List<Vector3> sampled = ResampleClosedRing(
                        candidate,
                        reference.Count);
                    Vector3 sampledCenter = MeanPoint(sampled);
                    float error = 0f;
                    for (int i = 0; i < reference.Count; i++)
                    {
                        Vector3 sourceDirection =
                            (sampled[i] - sampledCenter).normalized;
                        Vector3 referenceDirection =
                            (reference[i] - referenceCenter).normalized;
                        error +=
                            (sourceDirection - referenceDirection).sqrMagnitude;
                    }

                    if (error < bestError)
                    {
                        bestError = error;
                        best = candidate;
                    }
                }
            }

            if (best == null)
                throw new InvalidOperationException(
                    "Transition boundary correspondence could not be resolved.");
            return best;
        }

        private static int FindNearestPointIndex(
            List<Vector3> points,
            Vector3 target)
        {
            if (points == null || points.Count == 0)
                throw new InvalidOperationException(
                    "Cannot search an empty point set.");

            int bestIndex = 0;
            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float distanceSq = (points[i] - target).sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }
            }
            return bestIndex;
        }

        private static Vector3 MeanPoint(List<Vector3> points)
        {
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("Cannot compute the mean of an empty point set.");

            Vector3 sum = Vector3.zero;
            for (int i = 0; i < points.Count; i++)
                sum += points[i];
            return sum / points.Count;
        }

        private static Bounds ComputeAlignedVesselBounds(
            Mesh outerMesh,
            Mesh transitionMesh,
            Mesh staticBranchMesh,
            BranchAlignment branchAlignment)
        {
            if (outerMesh == null ||
                transitionMesh == null ||
                staticBranchMesh == null ||
                branchAlignment == null)
            {
                throw new InvalidOperationException(
                    "Cannot compute bounds for incomplete vessel geometry.");
            }

            Vector3[] outerVertices = outerMesh.vertices;
            if (outerVertices.Length == 0)
                throw new InvalidOperationException(
                    "Simulation outer mesh contains no vertices.");
            var bounds = new Bounds(
                outerVertices[0],
                Vector3.zero);
            EncapsulateMeshVertices(ref bounds, outerVertices);
            EncapsulateMeshVertices(ref bounds, transitionMesh.vertices);

            Vector3[] branchVertices = staticBranchMesh.vertices;
            for (int i = 0; i < branchVertices.Length; i++)
            {
                bounds.Encapsulate(
                    branchAlignment.position +
                    branchAlignment.rotation *
                    (branchVertices[i] * branchAlignment.scale));
            }
            return bounds;
        }

        private static void EncapsulateMeshVertices(
            ref Bounds bounds,
            Vector3[] vertices)
        {
            for (int i = 0; i < vertices.Length; i++)
                bounds.Encapsulate(vertices[i]);
        }

        private static Mesh CreateTissueSurfaceMesh(
            Bounds vesselBounds,
            Mesh outerMesh,
            Mesh transitionMesh,
            Mesh staticBranchMesh,
            BranchAlignment branchAlignment)
        {
            float minX = vesselBounds.min.x - TissueMargin;
            float maxX = vesselBounds.max.x + TissueMargin;
            float minZ = vesselBounds.min.z - TissueMargin;
            float maxZ = vesselBounds.max.z + TissueMargin;
            int columns = TissueGridSegments + 1;
            int topVertexCount = columns * columns;
            int vertexCount = topVertexCount * 2;
            var vertices = new Vector3[vertexCount];
            var baselineHeights = new float[topVertexCount];
            var uv = new Vector2[vertexCount];

            int vertexIndex = 0;
            for (int row = 0; row < columns; row++)
            {
                float v = row / (float)TissueGridSegments;
                float z = Mathf.Lerp(minZ, maxZ, v);
                for (int column = 0; column < columns; column++)
                {
                    float u = column / (float)TissueGridSegments;
                    float x = Mathf.Lerp(minX, maxX, u);
                    float broadFold =
                        Mathf.Sin(
                            (u * 1.35f + v * 0.28f) *
                            Mathf.PI * 2f) *
                        0.010f;
                    float crossingFold =
                        Mathf.Sin(
                            (v * 1.10f - u * 0.22f + 0.31f) *
                            Mathf.PI * 2f) *
                        0.006f;
                    float fineUndulation =
                        Mathf.Sin(
                            (u * 3.10f + v * 2.35f + 0.17f) *
                            Mathf.PI * 2f) *
                        0.0025f;
                    vertices[vertexIndex] =
                        new Vector3(
                            x,
                            TissueBaseHeight +
                            broadFold +
                            crossingFold +
                            fineUndulation,
                            z);
                    baselineHeights[vertexIndex] =
                        vertices[vertexIndex].y;
                    uv[vertexIndex] =
                        new Vector2(
                            (x - minX) / TissueTextureWorldSize,
                            (z - minZ) / TissueTextureWorldSize);
                    vertexIndex++;
                }
            }

            ShapeTissueAroundVessel(
                vertices,
                baselineHeights,
                minX,
                maxX,
                minZ,
                maxZ,
                outerMesh,
                transitionMesh,
                staticBranchMesh,
                branchAlignment);

            float bottomY =
                Mathf.Min(
                    TissueBaseHeight - TissueThickness,
                    vesselBounds.min.y -
                    TissueBranchEmbedDepth * 2f);
            for (int i = 0; i < topVertexCount; i++)
            {
                vertices[topVertexCount + i] =
                    new Vector3(
                        vertices[i].x,
                        bottomY,
                        vertices[i].z);
                uv[topVertexCount + i] = uv[i];
            }

            var triangles = new List<int>(
                TissueGridSegments * TissueGridSegments * 12 +
                TissueGridSegments * 24);
            for (int row = 0; row < TissueGridSegments; row++)
            {
                for (int column = 0;
                     column < TissueGridSegments;
                     column++)
                {
                    int a = row * columns + column;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles.Add(a);
                    triangles.Add(c);
                    triangles.Add(b);
                    triangles.Add(b);
                    triangles.Add(c);
                    triangles.Add(d);

                    int bottomA = topVertexCount + a;
                    int bottomB = topVertexCount + b;
                    int bottomC = topVertexCount + c;
                    int bottomD = topVertexCount + d;
                    triangles.Add(bottomA);
                    triangles.Add(bottomB);
                    triangles.Add(bottomC);
                    triangles.Add(bottomB);
                    triangles.Add(bottomD);
                    triangles.Add(bottomC);
                }
            }
            for (int column = 0;
                 column < TissueGridSegments;
                 column++)
            {
                int frontA = column;
                int frontB = column + 1;
                AddTissueSideQuad(
                    triangles,
                    frontA,
                    frontB,
                    topVertexCount,
                    false);

                int backA =
                    TissueGridSegments * columns + column;
                int backB = backA + 1;
                AddTissueSideQuad(
                    triangles,
                    backA,
                    backB,
                    topVertexCount,
                    true);
            }
            for (int row = 0; row < TissueGridSegments; row++)
            {
                int leftA = row * columns;
                int leftB = leftA + columns;
                AddTissueSideQuad(
                    triangles,
                    leftA,
                    leftB,
                    topVertexCount,
                    true);

                int rightA = row * columns + TissueGridSegments;
                int rightB = rightA + columns;
                AddTissueSideQuad(
                    triangles,
                    rightA,
                    rightB,
                    topVertexCount,
                    false);
            }

            var mesh = new Mesh
            {
                name = "HybridTissueSurface",
                vertices = vertices,
                uv = uv,
                triangles = triangles.ToArray(),
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            Debug.Log(
                $"[HybridVessel] Tissue surface size={maxX - minX:F6}x" +
                $"{maxZ - minZ:F6} baseY={TissueBaseHeight:F6} " +
                $"bottomY={bottomY:F6} thickness={TissueThickness:F6} " +
                $"grid={TissueGridSegments}x{TissueGridSegments}");
            return mesh;
        }

        private static void AddTissueSideQuad(
            List<int> triangles,
            int topA,
            int topB,
            int bottomOffset,
            bool reverse)
        {
            int bottomA = bottomOffset + topA;
            int bottomB = bottomOffset + topB;
            if (reverse)
            {
                triangles.Add(topA);
                triangles.Add(bottomA);
                triangles.Add(topB);
                triangles.Add(topB);
                triangles.Add(bottomA);
                triangles.Add(bottomB);
            }
            else
            {
                triangles.Add(topA);
                triangles.Add(topB);
                triangles.Add(bottomA);
                triangles.Add(topB);
                triangles.Add(bottomB);
                triangles.Add(bottomA);
            }
        }

        private static void ShapeTissueAroundVessel(
            Vector3[] tissueVertices,
            float[] baselineHeights,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            Mesh outerMesh,
            Mesh transitionMesh,
            Mesh staticBranchMesh,
            BranchAlignment branchAlignment)
        {
            if (tissueVertices == null ||
                baselineHeights == null ||
                tissueVertices.Length < baselineHeights.Length ||
                outerMesh == null ||
                transitionMesh == null ||
                staticBranchMesh == null ||
                branchAlignment == null)
            {
                throw new InvalidOperationException(
                    "Tissue conformation input is invalid.");
            }

            int columns = TissueGridSegments + 1;
            var maximumHeight = new float[baselineHeights.Length];
            var minimumHeight = new float[baselineHeights.Length];
            for (int i = 0; i < baselineHeights.Length; i++)
            {
                maximumHeight[i] =
                    baselineHeights[i] +
                    TissueMaxElevation;
                minimumHeight[i] =
                    baselineHeights[i] -
                    TissueMaxDisplacement;
            }

            int constrainedNodeCount = 0;
            ConstrainTissueNodesBelowMesh(
                maximumHeight,
                minX,
                maxX,
                minZ,
                maxZ,
                outerMesh.vertices,
                Matrix4x4.identity,
                ref constrainedNodeCount);
            ConstrainTissueNodesBelowMesh(
                maximumHeight,
                minX,
                maxX,
                minZ,
                maxZ,
                transitionMesh.vertices,
                Matrix4x4.identity,
                ref constrainedNodeCount);
            int embeddedNodeCount =
                RaiseTissueAroundStaticBranch(
                    minimumHeight,
                    maximumHeight,
                    baselineHeights,
                    minX,
                    maxX,
                    minZ,
                    maxZ,
                    staticBranchMesh,
                    branchAlignment);
            SmoothTissueEmbeddingField(
                minimumHeight,
                maximumHeight,
                baselineHeights);

            float requiredMaxElevation = 0f;
            int conflictingNodeCount = 0;
            var conflictPreview = new List<string>();
            for (int i = 0; i < baselineHeights.Length; i++)
            {
                requiredMaxElevation = Mathf.Max(
                    requiredMaxElevation,
                    minimumHeight[i] - baselineHeights[i]);
                if (minimumHeight[i] <= maximumHeight[i] + 1e-6f)
                    continue;
                conflictingNodeCount++;
                if (conflictPreview.Count < 12)
                {
                    conflictPreview.Add(
                        $"#{i}:min={minimumHeight[i]:F6},max={maximumHeight[i]:F6}");
                }
            }
            if (conflictingNodeCount > 0)
            {
                throw new InvalidOperationException(
                    $"Tissue embedding conflicts with the simulated vessel. " +
                    $"conflicts={conflictingNodeCount} preview=[" +
                    string.Join(", ", conflictPreview) +
                    "]");
            }
            if (requiredMaxElevation > TissueMaxElevation + 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Tissue embedding elevation is invalid. " +
                    $"maxAllowed={TissueMaxElevation:F6} " +
                    $"required={requiredMaxElevation:F6}");
            }

            float requiredMaxDepression = 0f;
            int overLimitNodeCount = 0;
            var overLimitPreview = new List<string>();
            for (int i = 0; i < baselineHeights.Length; i++)
            {
                float requiredDepression =
                    baselineHeights[i] - maximumHeight[i];
                requiredMaxDepression = Mathf.Max(
                    requiredMaxDepression,
                    requiredDepression);
                if (requiredDepression <= TissueMaxDisplacement + 1e-6f)
                    continue;

                overLimitNodeCount++;
                if (overLimitPreview.Count < 12)
                {
                    overLimitPreview.Add(
                        $"#{i}:required={requiredDepression:F6}");
                }
            }
            if (overLimitNodeCount > 0)
            {
                throw new InvalidOperationException(
                    $"Tissue support displacement is invalid. " +
                    $"maxAllowed={TissueMaxDisplacement:F6} " +
                    $"requiredMax={requiredMaxDepression:F6} " +
                    $"overLimitNodes={overLimitNodeCount} preview=[" +
                    string.Join(", ", overLimitPreview) +
                    "]");
            }

            var heights = new float[baselineHeights.Length];
            var nextHeights = new float[baselineHeights.Length];
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] =
                    Mathf.Clamp(
                        baselineHeights[i],
                        minimumHeight[i],
                        maximumHeight[i]);
            }

            for (int iteration = 0;
                 iteration < TissueConformIterations;
                 iteration++)
            {
                for (int row = 0; row < columns; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        int index = row * columns + column;
                        if (row == 0 ||
                            row == TissueGridSegments ||
                            column == 0 ||
                            column == TissueGridSegments)
                        {
                            nextHeights[index] = baselineHeights[index];
                            continue;
                        }

                        float neighborAverage =
                            (heights[index - 1] +
                             heights[index + 1] +
                             heights[index - columns] +
                             heights[index + columns]) *
                            0.25f;
                        float smoothed =
                            Mathf.Lerp(
                                heights[index],
                                neighborAverage,
                                0.42f);
                        nextHeights[index] =
                            Mathf.Clamp(
                                smoothed,
                                minimumHeight[index],
                                maximumHeight[index]);
                    }
                }

                float[] swap = heights;
                heights = nextHeights;
                nextHeights = swap;
            }

            float maxDepression = 0f;
            float maxElevation = 0f;
            for (int i = 0; i < baselineHeights.Length; i++)
            {
                maxDepression = Mathf.Max(
                    maxDepression,
                    baselineHeights[i] - heights[i]);
                maxElevation = Mathf.Max(
                    maxElevation,
                    heights[i] - baselineHeights[i]);
                tissueVertices[i].y = heights[i];
            }
            if (maxDepression > TissueMaxDisplacement + 1e-6f ||
                maxElevation > TissueMaxElevation + 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Smoothed tissue shape is invalid. " +
                    $"maxDepression={maxDepression:F6}/" +
                    $"{TissueMaxDisplacement:F6} maxElevation=" +
                    $"{maxElevation:F6}/{TissueMaxElevation:F6}");
            }
            Debug.Log(
                $"[HybridVessel] Tissue conformation constrainedNodes=" +
                $"{constrainedNodeCount} embeddedNodes={embeddedNodeCount} " +
                $"iterations={TissueConformIterations} " +
                $"requiredMaxDepression={requiredMaxDepression:F6} " +
                $"maxDepression={maxDepression:F6} " +
                $"requiredMaxElevation={requiredMaxElevation:F6} " +
                $"maxElevation={maxElevation:F6}");
        }

        private static int RaiseTissueAroundStaticBranch(
            float[] minimumHeight,
            float[] maximumHeight,
            float[] baselineHeights,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            Mesh staticBranchMesh,
            BranchAlignment branchAlignment)
        {
            if (minimumHeight == null ||
                maximumHeight == null ||
                baselineHeights == null ||
                minimumHeight.Length != baselineHeights.Length ||
                maximumHeight.Length != baselineHeights.Length ||
                staticBranchMesh == null ||
                branchAlignment == null)
            {
                throw new InvalidOperationException(
                    "Static branch tissue-embedding input is invalid.");
            }

            Vector3[] vertices = staticBranchMesh.vertices;
            Vector3[] normals = staticBranchMesh.normals;
            int[] triangles = staticBranchMesh.triangles;
            List<int> boundaryIndices =
                FindSingleBoundaryLoop(
                    new List<int>(triangles),
                    vertices.Length,
                    "static-branch-tissue-embedding");
            float[] boundaryDistances =
                ComputeMeshDistancesFromBoundary(
                    vertices,
                    triangles,
                    boundaryIndices);
            float blendLength =
                OuterRadius *
                StaticBranchDepthBlendRadiusScale /
                branchAlignment.scale;
            Matrix4x4 branchToRoot =
                Matrix4x4.TRS(
                    branchAlignment.position,
                    branchAlignment.rotation,
                    Vector3.one * branchAlignment.scale);
            float cellSizeX =
                (maxX - minX) / TissueGridSegments;
            float cellSizeZ =
                (maxZ - minZ) / TissueGridSegments;
            int columns = TissueGridSegments + 1;
            int touchedNodes = 0;
            var touched = new bool[baselineHeights.Length];
            float maxLiftTarget = float.NegativeInfinity;
            float maxAppliedHeight = float.NegativeInfinity;
            int contributingVertices = 0;
            int simulationLimitedNodes = 0;
            for (int i = 0; i < vertices.Length; i++)
            {
                float embedWeight =
                    Mathf.SmoothStep(
                        0f,
                        1f,
                        Mathf.Clamp01(
                            boundaryDistances[i] /
                            Mathf.Max(blendLength, 1e-6f)));
                if (embedWeight <= 1e-4f)
                    continue;

                Vector3 rootNormal =
                    (branchAlignment.rotation * normals[i]).normalized;
                if (rootNormal.y > TissueBranchLowerNormalThreshold)
                    continue;
                float lowerNormalWeight =
                    Mathf.Clamp01(
                        (TissueBranchLowerNormalThreshold - rootNormal.y) /
                        (TissueBranchLowerNormalThreshold + 1f));
                Vector3 rootPoint =
                    branchToRoot.MultiplyPoint3x4(vertices[i]);
                float targetHeight =
                    rootPoint.y +
                    TissueBranchEmbedDepth *
                    embedWeight;
                maxLiftTarget = Mathf.Max(
                    maxLiftTarget,
                    targetHeight);
                contributingVertices++;

                int minColumn =
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            (rootPoint.x - TissueBranchInfluenceRadius - minX) /
                            cellSizeX),
                        0,
                        TissueGridSegments);
                int maxColumn =
                    Mathf.Clamp(
                        Mathf.CeilToInt(
                            (rootPoint.x + TissueBranchInfluenceRadius - minX) /
                            cellSizeX),
                        0,
                        TissueGridSegments);
                int minRow =
                    Mathf.Clamp(
                        Mathf.FloorToInt(
                            (rootPoint.z - TissueBranchInfluenceRadius - minZ) /
                            cellSizeZ),
                        0,
                        TissueGridSegments);
                int maxRow =
                    Mathf.Clamp(
                        Mathf.CeilToInt(
                            (rootPoint.z + TissueBranchInfluenceRadius - minZ) /
                            cellSizeZ),
                        0,
                        TissueGridSegments);
                for (int row = minRow; row <= maxRow; row++)
                {
                    float z = Mathf.Lerp(
                        minZ,
                        maxZ,
                        row / (float)TissueGridSegments);
                    for (int column = minColumn;
                         column <= maxColumn;
                         column++)
                    {
                        float x = Mathf.Lerp(
                            minX,
                            maxX,
                            column / (float)TissueGridSegments);
                        float planarDistance =
                            Vector2.Distance(
                                new Vector2(x, z),
                                new Vector2(rootPoint.x, rootPoint.z));
                        if (planarDistance > TissueBranchInfluenceRadius)
                            continue;
                        float radialWeight =
                            Mathf.SmoothStep(
                                1f,
                                0f,
                                planarDistance /
                                TissueBranchInfluenceRadius);
                        float totalWeight =
                            embedWeight *
                            lowerNormalWeight *
                            radialWeight;
                        if (totalWeight <= 1e-4f)
                            continue;

                        int node = row * columns + column;
                        float desiredHeight =
                            Mathf.Lerp(
                                baselineHeights[node],
                                targetHeight,
                                totalWeight);
                        desiredHeight =
                            Mathf.Min(
                                desiredHeight,
                                baselineHeights[node] +
                                TissueMaxElevation);
                        if (desiredHeight >
                            maximumHeight[node] + 1e-6f)
                        {
                            simulationLimitedNodes++;
                            desiredHeight = maximumHeight[node];
                        }
                        if (desiredHeight <= minimumHeight[node])
                            continue;
                        minimumHeight[node] = desiredHeight;
                        maxAppliedHeight =
                            Mathf.Max(
                                maxAppliedHeight,
                                desiredHeight);
                        if (!touched[node])
                        {
                            touched[node] = true;
                            touchedNodes++;
                        }
                    }
                }
            }
            Debug.Log(
                $"[HybridVessel] Tissue branch embedding " +
                $"contributors={contributingVertices} " +
                $"touchedNodes={touchedNodes} " +
                $"simulationLimited={simulationLimitedNodes} " +
                $"maxLiftTarget={maxLiftTarget:F6} " +
                $"maxAppliedHeight={maxAppliedHeight:F6}");
            return touchedNodes;
        }

        private static void SmoothTissueEmbeddingField(
            float[] minimumHeight,
            float[] maximumHeight,
            float[] baselineHeights)
        {
            if (minimumHeight == null ||
                maximumHeight == null ||
                baselineHeights == null ||
                minimumHeight.Length != baselineHeights.Length ||
                maximumHeight.Length != baselineHeights.Length)
            {
                throw new InvalidOperationException(
                    "Tissue embedding-field input is invalid.");
            }

            int columns = TissueGridSegments + 1;
            var lift = new float[baselineHeights.Length];
            var nextLift = new float[baselineHeights.Length];
            float maxRawLift = 0f;
            for (int i = 0; i < lift.Length; i++)
            {
                lift[i] =
                    Mathf.Max(
                        0f,
                        minimumHeight[i] -
                        baselineHeights[i]);
                maxRawLift =
                    Mathf.Max(
                        maxRawLift,
                        lift[i]);
            }

            for (int iteration = 0;
                 iteration < TissueEmbeddingFieldIterations;
                 iteration++)
            {
                for (int row = 0; row < columns; row++)
                {
                    for (int column = 0;
                         column < columns;
                         column++)
                    {
                        int index = row * columns + column;
                        if (row == 0 ||
                            row == TissueGridSegments ||
                            column == 0 ||
                            column == TissueGridSegments)
                        {
                            nextLift[index] = 0f;
                            continue;
                        }

                        float neighborAverage =
                            (lift[index - 1] +
                             lift[index + 1] +
                             lift[index - columns] +
                             lift[index + columns]) *
                            0.25f;
                        float allowedLift =
                            Mathf.Max(
                                0f,
                                maximumHeight[index] -
                                baselineHeights[index]);
                        nextLift[index] =
                            Mathf.Min(
                                Mathf.Lerp(
                                    lift[index],
                                    neighborAverage,
                                    0.58f),
                                allowedLift);
                    }
                }

                float[] swap = lift;
                lift = nextLift;
                nextLift = swap;
            }

            float maxSmoothedLift = 0f;
            int liftedNodeCount = 0;
            for (int i = 0; i < lift.Length; i++)
            {
                if (lift[i] <= 1e-5f)
                {
                    minimumHeight[i] =
                        baselineHeights[i] -
                        TissueMaxDisplacement;
                    continue;
                }
                minimumHeight[i] =
                    baselineHeights[i] +
                    lift[i];
                maxSmoothedLift =
                    Mathf.Max(
                        maxSmoothedLift,
                        lift[i]);
                liftedNodeCount++;
            }

            Debug.Log(
                $"[HybridVessel] Tissue embedding field " +
                $"iterations={TissueEmbeddingFieldIterations} " +
                $"liftedNodes={liftedNodeCount} " +
                $"maxRawLift={maxRawLift:F6} " +
                $"maxSmoothedLift={maxSmoothedLift:F6}");
        }

        private static void ConstrainTissueNodesBelowMesh(
            float[] maximumHeight,
            float minX,
            float maxX,
            float minZ,
            float maxZ,
            Vector3[] meshVertices,
            Matrix4x4 meshToRoot,
            ref int constrainedNodeCount)
        {
            int columns = TissueGridSegments + 1;
            for (int i = 0; i < meshVertices.Length; i++)
            {
                Vector3 point =
                    meshToRoot.MultiplyPoint3x4(meshVertices[i]);
                float gridX =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(minX, maxX, point.x)) *
                    TissueGridSegments;
                float gridZ =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(minZ, maxZ, point.z)) *
                    TissueGridSegments;
                int column = Mathf.Min(
                    TissueGridSegments - 1,
                    Mathf.FloorToInt(gridX));
                int row = Mathf.Min(
                    TissueGridSegments - 1,
                    Mathf.FloorToInt(gridZ));
                float allowedHeight =
                    point.y -
                    TissueContactClearance;
                int a = row * columns + column;
                int b = a + 1;
                int c = a + columns;
                int d = c + 1;
                ConstrainTissueNode(
                    maximumHeight,
                    a,
                    allowedHeight,
                    ref constrainedNodeCount);
                ConstrainTissueNode(
                    maximumHeight,
                    b,
                    allowedHeight,
                    ref constrainedNodeCount);
                ConstrainTissueNode(
                    maximumHeight,
                    c,
                    allowedHeight,
                    ref constrainedNodeCount);
                ConstrainTissueNode(
                    maximumHeight,
                    d,
                    allowedHeight,
                    ref constrainedNodeCount);
            }
        }

        private static void ConstrainTissueNode(
            float[] maximumHeight,
            int index,
            float allowedHeight,
            ref int constrainedNodeCount)
        {
            if (allowedHeight >= maximumHeight[index])
                return;
            maximumHeight[index] = allowedHeight;
            constrainedNodeCount++;
        }

        private static float MeasureTissueClearanceViolation(
            Vector3[] tissueVertices,
            Vector3[] vesselVertices)
        {
            int columns = TissueGridSegments + 1;
            int topVertexCount = columns * columns;
            if (tissueVertices == null ||
                tissueVertices.Length < topVertexCount ||
                vesselVertices == null)
            {
                throw new InvalidOperationException(
                    "Tissue clearance validation input is invalid.");
            }

            float minX = tissueVertices[0].x;
            float maxX = tissueVertices[TissueGridSegments].x;
            float minZ = tissueVertices[0].z;
            float maxZ =
                tissueVertices[TissueGridSegments * columns].z;
            float maxViolation = 0f;
            for (int i = 0; i < vesselVertices.Length; i++)
            {
                Vector3 point = vesselVertices[i];
                float gridX =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(minX, maxX, point.x)) *
                    TissueGridSegments;
                float gridZ =
                    Mathf.Clamp01(
                        Mathf.InverseLerp(minZ, maxZ, point.z)) *
                    TissueGridSegments;
                int column = Mathf.Min(
                    TissueGridSegments - 1,
                    Mathf.FloorToInt(gridX));
                int row = Mathf.Min(
                    TissueGridSegments - 1,
                    Mathf.FloorToInt(gridZ));
                int a = row * columns + column;
                int b = a + 1;
                int c = a + columns;
                int d = c + 1;
                float highestCellNode =
                    Mathf.Max(
                        tissueVertices[a].y,
                        tissueVertices[b].y,
                        tissueVertices[c].y,
                        tissueVertices[d].y);
                maxViolation = Mathf.Max(
                    maxViolation,
                    highestCellNode +
                    TissueContactClearance -
                    point.y);
            }
            return Mathf.Max(0f, maxViolation);
        }

        private static Material CreateTissueMaterial()
        {
            Texture2D texture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    TissueTexturePath);
            if (texture == null)
                throw new InvalidOperationException(
                    $"Tissue texture could not be loaded at {TissueTexturePath}");

            Shader shader = Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException(
                    "Unity Standard shader is unavailable.");

            var material = new Material(shader)
            {
                name = "HybridTissueSurface",
                mainTexture = texture,
                color = new Color(1f, 0.94f, 0.92f, 1f),
            };
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Glossiness", 0.36f);
            return material;
        }

        private static Material CreateSupportMaterial()
        {
            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                throw new InvalidOperationException(
                    "Unity Unlit/Color shader is unavailable.");

            var material = new Material(shader)
            {
                name = "HybridSurgicalSupport",
                color = Color.white,
            };
            return material;
        }

        private static void BuildPrefab(
            Mesh outerMesh,
            Mesh innerMesh,
            Mesh transitionMesh,
            Mesh staticBranchMesh,
            Mesh tissueMesh,
            Material tissueMaterial,
            Material supportMaterial,
            BranchAlignment branchAlignment)
        {
            if (outerMesh == null ||
                innerMesh == null ||
                transitionMesh == null ||
                staticBranchMesh == null ||
                tissueMesh == null ||
                tissueMaterial == null ||
                supportMaterial == null)
                throw new InvalidOperationException("Generated hybrid vessel meshes could not be loaded.");

            Material outerMaterial = AssetDatabase.LoadAssetAtPath<Material>(OuterMaterialPath);
            Material innerMaterial = AssetDatabase.LoadAssetAtPath<Material>(InnerMaterialPath);
            if (outerMaterial == null || innerMaterial == null)
                throw new InvalidOperationException("Current project vessel materials could not be loaded.");

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("HybridBranchedVessel");

            CreateMeshChild(
                root.transform,
                "TissueSurface",
                tissueMesh,
                tissueMaterial);
            CreateSurgicalTableSupport(
                root.transform,
                tissueMesh.bounds,
                supportMaterial);
            CreateMeshChild(root.transform, "SimulationOuter", outerMesh, outerMaterial);
            CreateMeshChild(root.transform, "SimulationInner", innerMesh, innerMaterial);
            CreateMeshChild(root.transform, "StaticTransition", transitionMesh, outerMaterial);

            var branchContainer = new GameObject("StaticBranchAppearance");
            branchContainer.transform.SetParent(root.transform, false);

            CreateMeshChild(
                branchContainer.transform,
                "TripoBranchesTrimmed",
                staticBranchMesh,
                outerMaterial);
            branchContainer.transform.localScale = Vector3.one * branchAlignment.scale;
            branchContainer.transform.localRotation = branchAlignment.rotation;
            branchContainer.transform.localPosition = branchAlignment.position;

            var transitionRenderer = root.transform.Find("StaticTransition").gameObject
                .AddComponent<HybridVesselTransitionRenderer>();
            transitionRenderer.simulationOuter = root.transform.Find("SimulationOuter").GetComponent<MeshFilter>();
            transitionRenderer.simulationRestMesh = outerMesh;
            transitionRenderer.staticBranch = branchContainer.transform.Find("TripoBranchesTrimmed").GetComponent<MeshFilter>();

            ReplacePrefab(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void CreateSurgicalTableSupport(
            Transform parent,
            Bounds tissueBounds,
            Material supportMaterial)
        {
            var supportRoot = new GameObject("SurgicalTableSupport");
            supportRoot.transform.SetParent(parent, false);
            float railWidth =
                tissueBounds.size.x *
                SupportRailWidthFraction;
            float railLength =
                tissueBounds.size.z *
                SupportRailLengthFraction;
            float railY =
                tissueBounds.min.y -
                0.025f -
                SupportRailHeight * 0.5f;
            float inset =
                tissueBounds.size.x *
                SupportRailInsetFraction;
            float[] offsets = { -inset, 0f, inset };
            for (int i = 0; i < offsets.Length; i++)
            {
                GameObject rail =
                    GameObject.CreatePrimitive(PrimitiveType.Cube);
                rail.name = $"WhiteSupportRail_{i + 1}";
                rail.transform.SetParent(supportRoot.transform, false);
                rail.transform.localPosition =
                    new Vector3(
                        tissueBounds.center.x + offsets[i],
                        railY,
                        tissueBounds.center.z);
                rail.transform.localScale =
                    new Vector3(
                        railWidth,
                        SupportRailHeight,
                        railLength);
                Collider collider = rail.GetComponent<Collider>();
                if (collider != null)
                    UnityEngine.Object.DestroyImmediate(collider);
                rail.GetComponent<MeshRenderer>().sharedMaterial =
                    supportMaterial;
            }
        }

        private static GameObject CreateMeshChild(
            Transform parent,
            string objectName,
            Mesh mesh,
            Material material)
        {
            var child = new GameObject(objectName);
            child.transform.SetParent(parent, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;
            return child;
        }

        private static BranchAlignment ComputeBranchAlignment(StaticBranchBuildResult branch)
        {
            if (branch == null ||
                branch.boundary == null ||
                branch.boundary.Count < 3 ||
                branch.seamRadius <= 1e-6f)
            {
                throw new InvalidOperationException("Static branch seam data is invalid.");
            }

            float uniformScale = OuterRadius / branch.seamRadius;
            Quaternion baseRotation = Quaternion.FromToRotation(
                branch.seamTangent,
                SimulationEndTangent);
            EvaluateBranchVerticalSpread(
                branch.mesh,
                branch.seamCenter,
                baseRotation,
                out float baseRms,
                out float baseSpan);
            Quaternion rotation =
                FindStaticBranchPresentationRotation(
                    branch,
                    uniformScale,
                    out float bestRms,
                    out float bestSpan,
                    out float seamTiltDegrees);
            Vector3 seamTangentRoot =
                (rotation * branch.seamTangent).normalized;
            Vector3 targetCenter =
                SimulationEndCenter +
                SimulationEndTangent * TransitionExtension;
            Vector3 position =
                targetCenter - rotation * (branch.seamCenter * uniformScale);

            var boundaryRoot = new List<Vector3>(branch.boundary.Count);
            var boundaryNormalsRoot =
                new List<Vector3>(branch.boundary.Count);
            for (int i = 0; i < branch.boundary.Count; i++)
            {
                Vector3 boundaryPoint =
                    position +
                    rotation *
                    (branch.boundary[i] * uniformScale);
                boundaryRoot.Add(boundaryPoint);
                Vector3 radial =
                    boundaryPoint - targetCenter;
                radial -=
                    seamTangentRoot *
                    Vector3.Dot(
                        radial,
                        seamTangentRoot);
                boundaryNormalsRoot.Add(
                    radial.normalized);
            }

            Debug.Log(
                $"[HybridVessel] Static branch alignment sourceRadius={branch.seamRadius:F8} " +
                $"scale={uniformScale:F6} seamCenter={branch.seamCenter:F6} " +
                $"tangent={branch.seamTangent:F6} targetCenter={targetCenter:F6} " +
                $"seamTangentRoot={seamTangentRoot:F6} " +
                $"seamTilt={seamTiltDegrees:F3}deg " +
                $"verticalSpan={baseSpan * uniformScale:F6}->" +
                $"{bestSpan * uniformScale:F6} " +
                $"verticalRms={baseRms * uniformScale:F6}->" +
                $"{bestRms * uniformScale:F6}");
            return new BranchAlignment
            {
                scale = uniformScale,
                rotation = rotation,
                position = position,
                seamTangentRoot = seamTangentRoot,
                boundaryRoot = boundaryRoot,
                boundaryNormalsRoot = boundaryNormalsRoot,
            };
        }

        private static Quaternion FindStaticBranchPresentationRotation(
            StaticBranchBuildResult branch,
            float uniformScale,
            out float bestRms,
            out float bestSpan,
            out float seamTiltDegrees)
        {
            Vector3[] vertices = branch.mesh.vertices;
            Vector3 sourceTangent = branch.seamTangent.normalized;
            var centeredRootVertices =
                new Vector3[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                centeredRootVertices[i] =
                    (vertices[i] -
                     branch.seamCenter) *
                    uniformScale;
            }
            List<int>[] adjacency =
                BuildMeshAdjacency(
                    vertices.Length,
                    branch.mesh.triangles);
            float[] entranceDistance =
                ComputeMeshDistancesFromSources(
                    centeredRootVertices,
                    adjacency,
                    branch.boundaryIndices,
                    "static-rigid-pose-entrance");
            List<int> terminals =
                FindStaticBranchTerminalVertices(
                    centeredRootVertices,
                    entranceDistance,
                    adjacency);
            var terminalDirections =
                new Vector3[terminals.Count];
            float terminalInfluenceLength =
                OuterRadius *
                StaticBranchTipInfluenceRadiusScale;
            for (int terminalIndex = 0;
                 terminalIndex < terminals.Count;
                 terminalIndex++)
            {
                int tip = terminals[terminalIndex];
                float[] distanceFromTip =
                    ComputeMeshDistancesFromSources(
                        centeredRootVertices,
                        adjacency,
                        new[] { tip },
                        $"static-rigid-pose-tip-{tip}");
                Vector3 anchor =
                    ComputeStaticTerminalAnchor(
                        tip,
                        centeredRootVertices,
                        entranceDistance,
                        distanceFromTip,
                        terminalInfluenceLength);
                terminalDirections[terminalIndex] =
                    (centeredRootVertices[tip] -
                     anchor).normalized;
            }
            StaticVisibilityTopology visibilityTopology =
                BuildStaticVisibilityTopology(
                    centeredRootVertices,
                    branch.mesh.triangles,
                    branch.boundaryIndices);
            StaticVisibilityWorkspace visibilityWorkspace =
                CreateStaticVisibilityWorkspace(
                    centeredRootVertices.Length,
                    branch.mesh.triangles.Length / 3);
            float maxVerticalTangent =
                Mathf.Sin(
                    StaticBranchMaxSeamTiltDegrees *
                    Mathf.Deg2Rad);
            float goldenAngle =
                Mathf.PI * (3f - Mathf.Sqrt(5f));
            Vector3 bestSourceUp = Vector3.zero;
            bestRms = float.PositiveInfinity;
            bestSpan = float.PositiveInfinity;
            float bestMean = 0f;
            int bestMaterialOrphanCount = int.MaxValue;
            int bestMaterialOrphanTriangles = int.MaxValue;
            float bestMaterialOrphanArea =
                float.PositiveInfinity;
            int bestVisibleComponentCount = int.MaxValue;
            int bestMainTriangleCount = 0;
            int bestFloatingCount = int.MaxValue;
            float bestFloatingPenalty =
                float.PositiveInfinity;
            var orphanByFloating =
                new int[StaticBranchTipLimit + 1];
            var orphanTrianglesByFloating =
                new int[StaticBranchTipLimit + 1];
            var sourceUpByFloating =
                new Vector3[StaticBranchTipLimit + 1];
            for (int i = 0;
                 i < orphanByFloating.Length;
                 i++)
            {
                orphanByFloating[i] = int.MaxValue;
                orphanTrianglesByFloating[i] = int.MaxValue;
            }

            for (int sample = 0;
                 sample < StaticBranchOrientationSamples;
                 sample++)
            {
                float y =
                    1f -
                    2f *
                    (sample + 0.5f) /
                    StaticBranchOrientationSamples;
                float radius =
                    Mathf.Sqrt(
                        Mathf.Max(0f, 1f - y * y));
                float angle = goldenAngle * sample;
                Vector3 sourceUp =
                    new Vector3(
                        Mathf.Cos(angle) * radius,
                        y,
                        Mathf.Sin(angle) * radius);
                float verticalTangent =
                    Vector3.Dot(
                        sourceUp,
                        sourceTangent);
                if (Mathf.Abs(verticalTangent) >
                    maxVerticalTangent + 1e-6f)
                {
                    continue;
                }

                EvaluateBranchProjectedSpread(
                    vertices,
                    branch.seamCenter,
                    sourceUp,
                    out float rms,
                    out float span,
                    out float mean);
                float rootSpan =
                    span * uniformScale;
                float rootMean =
                    mean * uniformScale;
                if (rootSpan >
                        StaticBranchMaxPresentationSpan ||
                    rootMean <
                        StaticBranchMinPresentationMean ||
                    rootMean >
                        StaticBranchMaxPresentationMean)
                {
                    continue;
                }

                int materialOrphanCount = 0;
                int materialOrphanTriangles = 0;
                float materialOrphanArea = 0f;
                int visibleComponentCount = 0;
                int mainTriangleCount = int.MaxValue;
                for (int slice = 0;
                     slice < StaticBranchVisibilitySliceCount;
                     slice++)
                {
                    float surfaceHeight =
                        TissueBaseHeight +
                        slice *
                        StaticBranchVisibilitySliceStep;
                    EvaluateStaticVisibility(
                        centeredRootVertices,
                        sourceUp,
                        surfaceHeight,
                        visibilityTopology,
                        visibilityWorkspace,
                        out int sliceOrphanCount,
                        out int sliceOrphanTriangles,
                        out float sliceOrphanArea,
                        out int sliceComponentCount,
                        out int sliceMainTriangleCount);
                    materialOrphanCount +=
                        sliceOrphanCount;
                    materialOrphanTriangles +=
                        sliceOrphanTriangles;
                    materialOrphanArea +=
                        sliceOrphanArea;
                    visibleComponentCount =
                        Mathf.Max(
                            visibleComponentCount,
                            sliceComponentCount);
                    mainTriangleCount =
                        Mathf.Min(
                            mainTriangleCount,
                            sliceMainTriangleCount);
                }
                int floatingCount = 0;
                float floatingPenalty = 0f;
                for (int terminalIndex = 0;
                     terminalIndex < terminals.Count;
                     terminalIndex++)
                {
                    int tip = terminals[terminalIndex];
                    float gap =
                        Vector3.Dot(
                            centeredRootVertices[tip],
                            sourceUp) -
                        TissueBaseHeight;
                    float downDot =
                        -Vector3.Dot(
                            terminalDirections[terminalIndex],
                            sourceUp);
                    if (gap >
                            StaticBranchTipFloatingClearance &&
                        downDot <
                            StaticBranchTipMinimumDownDot)
                    {
                        floatingCount++;
                        floatingPenalty +=
                            (gap -
                             StaticBranchTipFloatingClearance) *
                            (1f +
                             Mathf.Max(
                                 0f,
                                 StaticBranchTipMinimumDownDot -
                                 downDot));
                    }
                }

                if (materialOrphanCount <
                        orphanByFloating[floatingCount] ||
                    (materialOrphanCount ==
                         orphanByFloating[floatingCount] &&
                     materialOrphanTriangles <
                         orphanTrianglesByFloating[floatingCount]))
                {
                    orphanByFloating[floatingCount] =
                        materialOrphanCount;
                    orphanTrianglesByFloating[floatingCount] =
                        materialOrphanTriangles;
                    sourceUpByFloating[floatingCount] =
                        sourceUp;
                }

                bool better = false;
                bool candidateWithinProxyBudget =
                    materialOrphanCount <=
                    StaticBranchVisibilityProxyOrphanBudget;
                bool bestWithinProxyBudget =
                    bestMaterialOrphanCount <=
                    StaticBranchVisibilityProxyOrphanBudget;
                if (candidateWithinProxyBudget !=
                    bestWithinProxyBudget)
                {
                    better = candidateWithinProxyBudget;
                }
                else if (candidateWithinProxyBudget &&
                         floatingCount != bestFloatingCount)
                {
                    better =
                        floatingCount < bestFloatingCount;
                }
                else if (materialOrphanCount !=
                    bestMaterialOrphanCount)
                {
                    better =
                        materialOrphanCount <
                        bestMaterialOrphanCount;
                }
                else if (materialOrphanTriangles !=
                         bestMaterialOrphanTriangles)
                {
                    better =
                        materialOrphanTriangles <
                        bestMaterialOrphanTriangles;
                }
                else if (Mathf.Abs(
                             materialOrphanArea -
                             bestMaterialOrphanArea) > 1e-6f)
                {
                    better =
                        materialOrphanArea <
                        bestMaterialOrphanArea;
                }
                else if (floatingCount != bestFloatingCount)
                {
                    better =
                        floatingCount < bestFloatingCount;
                }
                else if (Mathf.Abs(
                             floatingPenalty -
                             bestFloatingPenalty) > 1e-6f)
                {
                    better =
                        floatingPenalty <
                        bestFloatingPenalty;
                }
                else if (Mathf.Abs(span - bestSpan) > 1e-6f)
                {
                    better = span < bestSpan;
                }
                else
                {
                    better = rms < bestRms;
                }

                if (better)
                {
                    bestSourceUp = sourceUp;
                    bestRms = rms;
                    bestSpan = span;
                    bestMean = mean;
                    bestMaterialOrphanCount =
                        materialOrphanCount;
                    bestMaterialOrphanTriangles =
                        materialOrphanTriangles;
                    bestMaterialOrphanArea =
                        materialOrphanArea;
                    bestVisibleComponentCount =
                        visibleComponentCount;
                    bestMainTriangleCount =
                        mainTriangleCount;
                    bestFloatingCount =
                        floatingCount;
                    bestFloatingPenalty =
                        floatingPenalty;
                }
            }

            if (bestSourceUp.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Static branch rigid orientation search found no valid pose.");
            }

            Quaternion levelRotation =
                Quaternion.FromToRotation(
                    bestSourceUp,
                    Vector3.up);
            Vector3 leveledTangent =
                levelRotation * sourceTangent;
            Vector3 horizontalTangent =
                Vector3.ProjectOnPlane(
                    leveledTangent,
                    Vector3.up);
            if (horizontalTangent.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Static branch rigid orientation produced a vertical seam.");
            }
            horizontalTangent.Normalize();
            Vector3 targetHorizontalTangent =
                Vector3.ProjectOnPlane(
                    SimulationEndTangent,
                    Vector3.up).normalized;
            float yawDegrees =
                Vector3.SignedAngle(
                    horizontalTangent,
                    targetHorizontalTangent,
                    Vector3.up);
            Quaternion rotation =
                Quaternion.AngleAxis(
                    yawDegrees,
                    Vector3.up) *
                levelRotation;
            seamTiltDegrees =
                Mathf.Asin(
                    Mathf.Clamp(
                        Mathf.Abs(
                            (rotation * sourceTangent).normalized.y),
                        0f,
                        1f)) *
                Mathf.Rad2Deg;
            if (seamTiltDegrees >
                StaticBranchMaxSeamTiltDegrees + 1e-3f)
            {
                throw new InvalidOperationException(
                    $"Static branch seam tilt exceeds its rigid-pose limit. " +
                    $"actual={seamTiltDegrees:F6} " +
                    $"limit={StaticBranchMaxSeamTiltDegrees:F6}");
            }

            Debug.Log(
                $"[HybridVessel] Static rigid pose samples=" +
                $"{StaticBranchOrientationSamples} " +
                $"sourceUp={bestSourceUp:F6} yaw={yawDegrees:F3}deg " +
                $"meanHeight={bestMean:F6} terminals={terminals.Count} " +
                $"materialOrphans={bestMaterialOrphanCount} " +
                $"materialOrphanTriangles=" +
                $"{bestMaterialOrphanTriangles} " +
                $"materialOrphanArea={bestMaterialOrphanArea:F6} " +
                $"visibleComponents={bestVisibleComponentCount} " +
                $"mainTriangles={bestMainTriangleCount} " +
                $"floating={bestFloatingCount} " +
                $"floatingPenalty={bestFloatingPenalty:F6} " +
                $"maxSpan={StaticBranchMaxPresentationSpan:F3} " +
                $"meanRange=[{StaticBranchMinPresentationMean:F3}," +
                $"{StaticBranchMaxPresentationMean:F3}]");
            var paretoPreview = new List<string>();
            int bestSeenOrphans = int.MaxValue;
            int bestSeenTriangles = int.MaxValue;
            for (int floating = 0;
                 floating < orphanByFloating.Length;
                 floating++)
            {
                if (orphanByFloating[floating] == int.MaxValue ||
                    (orphanByFloating[floating] >
                         bestSeenOrphans &&
                     orphanTrianglesByFloating[floating] >=
                         bestSeenTriangles))
                {
                    continue;
                }
                bestSeenOrphans =
                    Mathf.Min(
                        bestSeenOrphans,
                        orphanByFloating[floating]);
                bestSeenTriangles =
                    Mathf.Min(
                        bestSeenTriangles,
                        orphanTrianglesByFloating[floating]);
                paretoPreview.Add(
                    $"floating={floating},orphans=" +
                    $"{orphanByFloating[floating]}," +
                    $"orphanTris=" +
                    $"{orphanTrianglesByFloating[floating]}," +
                    $"up={sourceUpByFloating[floating]:F6}");
            }
            Debug.Log(
                $"[HybridVessel] Static rigid pose Pareto " +
                $"preview=[{string.Join("; ", paretoPreview)}]");
            return rotation;
        }

        private static StaticVisibilityTopology
            BuildStaticVisibilityTopology(
                Vector3[] rootVertices,
                int[] triangles,
                List<int> entranceVertices)
        {
            if (rootVertices == null ||
                triangles == null ||
                triangles.Length % 3 != 0 ||
                entranceVertices == null ||
                entranceVertices.Count < 3)
            {
                throw new InvalidOperationException(
                    "Static branch visibility-topology input is invalid.");
            }

            int triangleCount = triangles.Length / 3;
            var topology =
                new StaticVisibilityTopology
                {
                    triangles = triangles,
                    triangleAreas = new float[triangleCount],
                    triangleContainsEntrance =
                        new bool[triangleCount],
                    links =
                        new List<StaticTriangleLink>[triangleCount],
                };
            var entranceSet =
                new HashSet<int>(entranceVertices);
            var edgeOwners =
                new Dictionary<ulong, int>();
            for (int triangle = 0;
                 triangle < triangleCount;
                 triangle++)
            {
                topology.links[triangle] =
                    new List<StaticTriangleLink>(3);
                int start = triangle * 3;
                int a = triangles[start];
                int b = triangles[start + 1];
                int c = triangles[start + 2];
                topology.triangleAreas[triangle] =
                    Vector3.Cross(
                        rootVertices[b] - rootVertices[a],
                        rootVertices[c] - rootVertices[a]).magnitude *
                    0.5f;
                topology.triangleContainsEntrance[triangle] =
                    entranceSet.Contains(a) ||
                    entranceSet.Contains(b) ||
                    entranceSet.Contains(c);
                AddStaticVisibilityEdge(
                    edgeOwners,
                    topology.links,
                    triangle,
                    a,
                    b);
                AddStaticVisibilityEdge(
                    edgeOwners,
                    topology.links,
                    triangle,
                    b,
                    c);
                AddStaticVisibilityEdge(
                    edgeOwners,
                    topology.links,
                    triangle,
                    c,
                    a);
            }
            return topology;
        }

        private static void AddStaticVisibilityEdge(
            Dictionary<ulong, int> edgeOwners,
            List<StaticTriangleLink>[] links,
            int triangle,
            int a,
            int b)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            if (!edgeOwners.TryGetValue(
                    key,
                    out int owner))
            {
                edgeOwners[key] = triangle;
                return;
            }
            links[owner].Add(
                new StaticTriangleLink(
                    triangle,
                    a,
                    b));
            links[triangle].Add(
                new StaticTriangleLink(
                    owner,
                    a,
                    b));
        }

        private static StaticVisibilityWorkspace
            CreateStaticVisibilityWorkspace(
                int vertexCount,
                int triangleCount)
        {
            return new StaticVisibilityWorkspace
            {
                clearance = new float[vertexCount],
                activeTriangles = new bool[triangleCount],
                triangleComponents = new int[triangleCount],
                componentTriangleCounts = new int[triangleCount],
                componentAreas = new float[triangleCount],
                componentMaxGaps = new float[triangleCount],
                componentContainsEntrance =
                    new bool[triangleCount],
            };
        }

        private static void EvaluateStaticVisibility(
            Vector3[] rootVertices,
            Vector3 sourceUp,
            float surfaceHeight,
            StaticVisibilityTopology topology,
            StaticVisibilityWorkspace workspace,
            out int materialOrphanCount,
            out int materialOrphanTriangles,
            out float materialOrphanArea,
            out int visibleComponentCount,
            out int mainTriangleCount)
        {
            for (int vertex = 0;
                 vertex < rootVertices.Length;
                 vertex++)
            {
                workspace.clearance[vertex] =
                    Vector3.Dot(
                        rootVertices[vertex],
                        sourceUp) -
                    surfaceHeight;
            }

            int triangleCount =
                topology.triangles.Length / 3;
            for (int triangle = 0;
                 triangle < triangleCount;
                 triangle++)
            {
                int start = triangle * 3;
                int a = topology.triangles[start];
                int b = topology.triangles[start + 1];
                int c = topology.triangles[start + 2];
                workspace.activeTriangles[triangle] =
                    Mathf.Max(
                        workspace.clearance[a],
                        workspace.clearance[b],
                        workspace.clearance[c]) >
                    StaticBranchVisibleClearance;
                workspace.triangleComponents[triangle] = -1;
            }

            workspace.queue.Clear();
            int componentCount = 0;
            for (int seed = 0;
                 seed < triangleCount;
                 seed++)
            {
                if (!workspace.activeTriangles[seed] ||
                    workspace.triangleComponents[seed] >= 0)
                {
                    continue;
                }

                int component = componentCount++;
                workspace.componentTriangleCounts[component] = 0;
                workspace.componentAreas[component] = 0f;
                workspace.componentMaxGaps[component] =
                    float.NegativeInfinity;
                workspace.componentContainsEntrance[component] =
                    false;
                workspace.triangleComponents[seed] = component;
                workspace.queue.Enqueue(seed);
                while (workspace.queue.Count > 0)
                {
                    int triangle =
                        workspace.queue.Dequeue();
                    int start = triangle * 3;
                    int a = topology.triangles[start];
                    int b = topology.triangles[start + 1];
                    int c = topology.triangles[start + 2];
                    workspace.componentTriangleCounts[component]++;
                    workspace.componentAreas[component] +=
                        topology.triangleAreas[triangle];
                    workspace.componentMaxGaps[component] =
                        Mathf.Max(
                            workspace.componentMaxGaps[component],
                            workspace.clearance[a],
                            workspace.clearance[b],
                            workspace.clearance[c]);
                    workspace.componentContainsEntrance[component] |=
                        topology.triangleContainsEntrance[triangle];

                    List<StaticTriangleLink> links =
                        topology.links[triangle];
                    for (int linkIndex = 0;
                         linkIndex < links.Count;
                         linkIndex++)
                    {
                        StaticTriangleLink link =
                            links[linkIndex];
                        int neighbor = link.triangle;
                        if (!workspace.activeTriangles[neighbor] ||
                            workspace.triangleComponents[neighbor] >= 0 ||
                            (workspace.clearance[link.edgeA] +
                             workspace.clearance[link.edgeB]) *
                            0.5f <=
                            StaticBranchVisibleClearance)
                        {
                            continue;
                        }
                        workspace.triangleComponents[neighbor] =
                            component;
                        workspace.queue.Enqueue(neighbor);
                    }
                }
            }

            int mainComponent = -1;
            for (int component = 0;
                 component < componentCount;
                 component++)
            {
                if (!workspace.componentContainsEntrance[component])
                    continue;
                mainComponent = component;
                break;
            }
            if (mainComponent < 0)
            {
                for (int component = 0;
                     component < componentCount;
                     component++)
                {
                    if (mainComponent < 0 ||
                        workspace.componentTriangleCounts[component] >
                        workspace.componentTriangleCounts[mainComponent])
                    {
                        mainComponent = component;
                    }
                }
            }

            materialOrphanCount = 0;
            materialOrphanTriangles = 0;
            materialOrphanArea = 0f;
            for (int component = 0;
                 component < componentCount;
                 component++)
            {
                if (component == mainComponent ||
                    workspace.componentAreas[component] <
                        StaticBranchMaterialOrphanArea ||
                    workspace.componentMaxGaps[component] <
                        StaticBranchMaterialOrphanGap)
                {
                    continue;
                }
                materialOrphanCount++;
                materialOrphanTriangles +=
                    workspace.componentTriangleCounts[component];
                materialOrphanArea +=
                    workspace.componentAreas[component];
            }
            visibleComponentCount = componentCount;
            mainTriangleCount =
                mainComponent >= 0
                    ? workspace.componentTriangleCounts[mainComponent]
                    : 0;
        }

        private static void EvaluateBranchProjectedSpread(
            Vector3[] vertices,
            Vector3 seamCenter,
            Vector3 sourceUp,
            out float rms,
            out float span,
            out float mean)
        {
            float sum = 0f;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                float height =
                    Vector3.Dot(
                        vertices[i] - seamCenter,
                        sourceUp);
                sum += height;
                min = Mathf.Min(min, height);
                max = Mathf.Max(max, height);
            }
            mean = sum / vertices.Length;
            float squaredSum = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                float height =
                    Vector3.Dot(
                        vertices[i] - seamCenter,
                        sourceUp);
                float delta = height - mean;
                squaredSum += delta * delta;
            }
            rms = Mathf.Sqrt(
                squaredSum / vertices.Length);
            span = max - min;
        }

        private static List<int>
            FindStaticBranchTerminalVertices(
                Vector3[] rootVertices,
                float[] entranceDistance,
                List<int>[] adjacency)
        {
            var maxima = new List<int>();
            for (int vertex = 0;
                 vertex < rootVertices.Length;
                 vertex++)
            {
                bool isMaximum = true;
                List<int> neighbors =
                    adjacency[vertex];
                for (int neighborIndex = 0;
                     neighborIndex < neighbors.Count;
                     neighborIndex++)
                {
                    if (entranceDistance[
                            neighbors[neighborIndex]] >
                        entranceDistance[vertex] + 1e-6f)
                    {
                        isMaximum = false;
                        break;
                    }
                }
                if (isMaximum)
                    maxima.Add(vertex);
            }
            maxima.Sort(
                (a, b) =>
                    entranceDistance[b].CompareTo(
                        entranceDistance[a]));

            float separation =
                OuterRadius *
                StaticBranchTipSeparationRadiusScale;
            var terminals = new List<int>();
            for (int candidateIndex = 0;
                 candidateIndex < maxima.Count &&
                 terminals.Count < StaticBranchTipLimit;
                 candidateIndex++)
            {
                int candidate =
                    maxima[candidateIndex];
                bool overlaps = false;
                for (int terminalIndex = 0;
                     terminalIndex < terminals.Count;
                     terminalIndex++)
                {
                    if (Vector3.Distance(
                            rootVertices[candidate],
                            rootVertices[
                                terminals[terminalIndex]]) <
                        separation)
                    {
                        overlaps = true;
                        break;
                    }
                }
                if (!overlaps)
                    terminals.Add(candidate);
            }
            if (terminals.Count < 3)
            {
                throw new InvalidOperationException(
                    $"Static branch terminal extraction is " +
                    $"insufficient. maxima={maxima.Count} " +
                    $"terminals={terminals.Count}");
            }
            return terminals;
        }

        private static Vector3 ComputeStaticTerminalAnchor(
            int tip,
            Vector3[] rootVertices,
            float[] entranceDistance,
            float[] distanceFromTip,
            float influenceLength)
        {
            Vector3 anchorSum = Vector3.zero;
            float anchorWeightSum = 0f;
            float tipEntranceDistance =
                entranceDistance[tip];
            for (int vertex = 0;
                 vertex < rootVertices.Length;
                 vertex++)
            {
                float tipDistance =
                    distanceFromTip[vertex];
                if (tipDistance <
                        influenceLength * 0.35f ||
                    tipDistance >
                        influenceLength * 0.90f ||
                    entranceDistance[vertex] >
                        tipEntranceDistance -
                        influenceLength * 0.20f)
                {
                    continue;
                }
                float center =
                    influenceLength * 0.63f;
                float weight =
                    1f -
                    Mathf.Abs(
                        tipDistance - center) /
                    (influenceLength * 0.28f);
                weight =
                    Mathf.Max(
                        weight,
                        0.05f);
                anchorSum +=
                    rootVertices[vertex] *
                    weight;
                anchorWeightSum += weight;
            }
            if (anchorWeightSum <= 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Static branch terminal anchor is incomplete " +
                    $"for tip {tip}.");
            }
            return anchorSum / anchorWeightSum;
        }

        private static float[] ComputeMeshDistancesFromSources(
            Vector3[] vertices,
            List<int>[] adjacency,
            IList<int> sources,
            string label)
        {
            if (vertices == null ||
                adjacency == null ||
                adjacency.Length != vertices.Length ||
                sources == null ||
                sources.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Static branch distance input is invalid for {label}.");
            }

            var distances = new float[vertices.Length];
            var inQueue = new bool[vertices.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < distances.Length; i++)
                distances[i] = float.PositiveInfinity;
            for (int i = 0; i < sources.Count; i++)
            {
                int source = sources[i];
                if (source < 0 ||
                    source >= vertices.Length)
                {
                    throw new InvalidOperationException(
                        $"Static branch distance source {source} " +
                        $"is invalid for {label}.");
                }
                if (distances[source] <= 0f)
                    continue;
                distances[source] = 0f;
                inQueue[source] = true;
                queue.Enqueue(source);
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                inQueue[current] = false;
                List<int> neighbors =
                    adjacency[current];
                for (int neighborIndex = 0;
                     neighborIndex < neighbors.Count;
                     neighborIndex++)
                {
                    int neighbor =
                        neighbors[neighborIndex];
                    float candidate =
                        distances[current] +
                        Vector3.Distance(
                            vertices[current],
                            vertices[neighbor]);
                    if (candidate >=
                        distances[neighbor] - 1e-7f)
                    {
                        continue;
                    }
                    distances[neighbor] = candidate;
                    if (inQueue[neighbor])
                        continue;
                    inQueue[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            return distances;
        }

        private static void EvaluateBranchVerticalSpread(
            Mesh mesh,
            Vector3 seamCenter,
            Quaternion rotation,
            out float rms,
            out float span)
        {
            if (mesh == null || mesh.vertexCount == 0)
                throw new InvalidOperationException(
                    "Static branch mesh is empty during layout evaluation.");

            Vector3[] vertices = mesh.vertices;
            float sum = 0f;
            float min = float.PositiveInfinity;
            float max = float.NegativeInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                float y =
                    (rotation *
                     (vertices[i] - seamCenter)).y;
                sum += y;
                min = Mathf.Min(min, y);
                max = Mathf.Max(max, y);
            }

            float mean = sum / vertices.Length;
            float squaredSum = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                float y =
                    (rotation *
                     (vertices[i] - seamCenter)).y;
                float delta = y - mean;
                squaredSum += delta * delta;
            }

            rms = Mathf.Sqrt(squaredSum / vertices.Length);
            span = max - min;
        }

        private static float[] ComputeMeshDistancesFromBoundary(
            Vector3[] vertices,
            int[] triangles,
            List<int> boundaryIndices)
        {
            if (vertices == null ||
                triangles == null ||
                boundaryIndices == null ||
                boundaryIndices.Count < 3)
            {
                throw new InvalidOperationException(
                    "Static branch boundary-distance input is invalid.");
            }

            List<int>[] adjacency =
                BuildMeshAdjacency(vertices.Length, triangles);

            var distances = new float[vertices.Length];
            var inQueue = new bool[vertices.Length];
            var queue = new Queue<int>();
            for (int i = 0; i < distances.Length; i++)
                distances[i] = float.PositiveInfinity;
            for (int i = 0; i < boundaryIndices.Count; i++)
            {
                int boundary = boundaryIndices[i];
                if (boundary < 0 || boundary >= vertices.Length)
                {
                    throw new InvalidOperationException(
                        $"Static branch boundary index {boundary} is invalid.");
                }
                if (distances[boundary] <= 0f)
                    continue;
                distances[boundary] = 0f;
                inQueue[boundary] = true;
                queue.Enqueue(boundary);
            }

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                inQueue[current] = false;
                List<int> neighbors = adjacency[current];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    float candidate =
                        distances[current] +
                        Vector3.Distance(
                            vertices[current],
                            vertices[neighbor]);
                    if (candidate >= distances[neighbor] - 1e-7f)
                        continue;
                    distances[neighbor] = candidate;
                    if (inQueue[neighbor])
                        continue;
                    inQueue[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
            return distances;
        }

        private static List<int>[] BuildMeshAdjacency(
            int vertexCount,
            int[] triangles)
        {
            if (vertexCount <= 0 || triangles == null)
            {
                throw new InvalidOperationException(
                    "Static branch adjacency input is invalid.");
            }

            var adjacency = new List<int>[vertexCount];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>(8);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddMeshNeighbor(adjacency, a, b);
                AddMeshNeighbor(adjacency, b, c);
                AddMeshNeighbor(adjacency, c, a);
            }
            return adjacency;
        }

        private static void AddMeshNeighbor(
            List<int>[] adjacency,
            int a,
            int b)
        {
            if (a < 0 ||
                b < 0 ||
                a >= adjacency.Length ||
                b >= adjacency.Length)
            {
                throw new InvalidOperationException(
                    $"Static branch triangle edge ({a}, {b}) is invalid.");
            }
            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private static void ValidateStaticBranchDeformation(
            Mesh mesh,
            Vector3[] beforeVertices,
            BranchAlignment alignment)
        {
            if (mesh == null ||
                beforeVertices == null ||
                beforeVertices.Length != mesh.vertexCount ||
                alignment == null)
            {
                throw new InvalidOperationException(
                    "Static branch deformation validation input is invalid.");
            }

            Vector3[] afterVertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            int flippedCount = 0;
            int collapsedCount = 0;
            var flippedPreview = new List<string>();
            float minAreaRatio = float.PositiveInfinity;
            float maxAreaRatio = 0f;
            float minAfterArea = float.PositiveInfinity;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                Vector3 beforeA =
                    alignment.rotation *
                    (beforeVertices[a] * alignment.scale);
                Vector3 beforeB =
                    alignment.rotation *
                    (beforeVertices[b] * alignment.scale);
                Vector3 beforeC =
                    alignment.rotation *
                    (beforeVertices[c] * alignment.scale);
                Vector3 afterA =
                    alignment.rotation *
                    (afterVertices[a] * alignment.scale);
                Vector3 afterB =
                    alignment.rotation *
                    (afterVertices[b] * alignment.scale);
                Vector3 afterC =
                    alignment.rotation *
                    (afterVertices[c] * alignment.scale);
                Vector3 beforeCross =
                    Vector3.Cross(
                        beforeB - beforeA,
                        beforeC - beforeA);
                Vector3 afterCross =
                    Vector3.Cross(
                        afterB - afterA,
                        afterC - afterA);
                float beforeArea = beforeCross.magnitude * 0.5f;
                float afterArea = afterCross.magnitude * 0.5f;
                float areaRatio =
                    afterArea / Mathf.Max(beforeArea, 1e-12f);
                minAreaRatio = Mathf.Min(minAreaRatio, areaRatio);
                maxAreaRatio = Mathf.Max(maxAreaRatio, areaRatio);
                minAfterArea = Mathf.Min(minAfterArea, afterArea);
                if (Vector3.Dot(beforeCross, afterCross) <= 0f)
                {
                    flippedCount++;
                    if (flippedPreview.Count < 12)
                    {
                        float targetY =
                            MeanPoint(alignment.boundaryRoot).y;
                        float beforeDepthA =
                            alignment.position.y + beforeA.y - targetY;
                        float beforeDepthB =
                            alignment.position.y + beforeB.y - targetY;
                        float beforeDepthC =
                            alignment.position.y + beforeC.y - targetY;
                        float afterDepthA =
                            alignment.position.y + afterA.y - targetY;
                        float afterDepthB =
                            alignment.position.y + afterB.y - targetY;
                        float afterDepthC =
                            alignment.position.y + afterC.y - targetY;
                        flippedPreview.Add(
                            $"tri={i / 3} ids=({a},{b},{c}) " +
                            $"before=({beforeA:F4};{beforeB:F4};{beforeC:F4}) " +
                            $"after=({afterA:F4};{afterB:F4};{afterC:F4}) " +
                            $"depthScale=(" +
                            $"{afterDepthA / beforeDepthA:F4}," +
                            $"{afterDepthB / beforeDepthB:F4}," +
                            $"{afterDepthC / beforeDepthC:F4}) " +
                            $"areaRatio={areaRatio:F6}");
                    }
                }
                if (afterArea <= 1e-10f ||
                    areaRatio <= 0.02f)
                {
                    collapsedCount++;
                }
            }

            Debug.Log(
                $"[HybridVessel] Static deformation quality " +
                $"minAreaRatio={minAreaRatio:F6} " +
                $"maxAreaRatio={maxAreaRatio:F6} " +
                $"minArea={minAfterArea:E6} " +
                $"flipped={flippedCount} collapsed={collapsedCount} " +
                $"flippedPreview=[" +
                string.Join("; ", flippedPreview) +
                "]");
            if (flippedCount > 0 || collapsedCount > 0)
            {
                throw new InvalidOperationException(
                    $"Static branch deformation is invalid. " +
                    $"flipped={flippedCount} collapsed={collapsedCount} " +
                    $"minAreaRatio={minAreaRatio:F8}");
            }
        }

        private static void SmoothStaticBranchBends(
            StaticBranchBuildResult branch, BranchAlignment alignment)
        {
            Mesh mesh = branch.mesh;
            Vector3[] original = mesh.vertices;
            float[] distances = ComputeMeshDistancesFromBoundary(
                original, mesh.triangles, branch.boundaryIndices);
            var weights = new List<float>(original.Length);
            for (int i = 0; i < original.Length; i++)
            {
                float distance = distances[i] * alignment.scale;
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    StaticSmoothCollarLength, StaticSmoothRampLength, distance));
                float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    StaticSmoothFullLength, StaticSmoothEndLength, distance));
                weights.Add(ramp * (1f - fade));
            }
            float beforeAngle = MeasureStaticBendAngle(mesh, weights);
            SmoothStaticBendPass(mesh, weights, alignment, 8);
            for (int pass = 0; pass < StaticSmoothSubdivisionPasses; pass++)
            {
                RefineStaticBendPatch(mesh, weights);
                SmoothStaticBendPass(mesh, weights, alignment, 4);
            }

            // Refinement keeps original vertex identities. It must neither split
            // the seam nor move any original vertex outside the selected region.
            List<int> boundary = FindSingleBoundaryLoop(
                new List<int>(mesh.triangles), mesh.vertexCount, "static-bend-smooth");
            var expectedBoundary = new HashSet<int>(branch.boundaryIndices);
            if (!expectedBoundary.SetEquals(boundary))
                throw new InvalidOperationException("Static bend smoothing changed seam identities.");
            Vector3[] result = mesh.vertices;
            float pinnedMove = 0f;
            float maxMove = 0f;
            for (int i = 0; i < original.Length; i++)
            {
                float move = Vector3.Distance(original[i], result[i]) * alignment.scale;
                maxMove = Mathf.Max(maxMove, move);
                if (weights[i] == 0f)
                    pinnedMove = Mathf.Max(pinnedMove, move);
            }
            float afterAngle = MeasureStaticBendAngle(mesh, weights);
            float[] collarDistances = ComputeMeshDistancesFromBoundary(
                result, mesh.triangles, branch.boundaryIndices);
            var collarWeights = new List<float>(result.Length);
            for (int i = 0; i < result.Length; i++)
                collarWeights.Add(collarDistances[i] * alignment.scale <= StaticSmoothRampLength ? 1f : 0f);
            float collarAngle = MeasureStaticBendAngle(mesh, collarWeights);
            Debug.Log(
                $"[HybridVessel][BendSmooth] verts={original.Length}->{mesh.vertexCount} " +
                $"maxDihedral={beforeAngle:F3}->{afterAngle:F3} " +
                $"maxMove={maxMove:F6} pinnedMove={pinnedMove:F8} " +
                $"seamVertices={boundary.Count} collarDihedral={collarAngle:F3} " +
                $"passes={StaticSmoothSubdivisionPasses}");
            if (pinnedMove > 1e-7f || maxMove > OuterRadius * 0.5f ||
                afterAngle >= beforeAngle || collarAngle >= 90f)
            {
                throw new InvalidOperationException(
                    "Static bend smoothing violated the pinned/shape/smoothness contract.");
            }
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private static void SmoothStaticBendPass(
            Mesh mesh, List<float> weights, BranchAlignment alignment, int iterations)
        {
            Vector3[] before = mesh.vertices;
            Vector3[] vertices = (Vector3[])before.Clone();
            var next = new Vector3[vertices.Length];
            List<int>[] adjacency = BuildMeshAdjacency(vertices.Length, mesh.triangles);
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<int>(new HashSet<int>(adjacency[i]));
            // Paired low-pass / inflation steps reduce the shrinkage of repeated
            // averaging. The weight field is fixed in the original material domain.
            for (int iteration = 0; iteration < iterations * 2; iteration++)
            {
                float factor = iteration % 2 == 0 ? 0.5f : -0.53f;
                for (int i = 0; i < vertices.Length; i++)
                {
                    next[i] = vertices[i];
                    if (weights[i] == 0f || adjacency[i].Count == 0)
                        continue;
                    Vector3 mean = Vector3.zero;
                    foreach (int neighbor in adjacency[i])
                        mean += vertices[neighbor];
                    mean /= adjacency[i].Count;
                    next[i] += factor * weights[i] * (mean - vertices[i]);
                }
                Vector3[] swap = vertices;
                vertices = next;
                next = swap;
            }
            mesh.vertices = vertices;
            // Fail here, before refinement or asset replacement, on any flipped
            // or collapsed face; use the same geometric gates as suspension.
            ValidateStaticBranchDeformation(mesh, before, alignment);
        }

        private static ulong StaticSmoothEdgeKey(int a, int b) =>
            ((ulong)(uint)Mathf.Min(a, b) << 32) | (uint)Mathf.Max(a, b);

        private static void RefineStaticBendPatch(Mesh mesh, List<float> weights)
        {
            var vertices = new List<Vector3>(mesh.vertices);
            int[] source = mesh.triangles;
            var triangles = new List<int>(source.Length * 2);
            var midpoints = new Dictionary<ulong, int>();
            int Midpoint(int a, int b)
            {
                // Both the untouched anatomy and the seam collar keep their
                // original edges. Every shared active edge gets exactly one ID.
                if (weights[a] == 0f && weights[b] == 0f)
                    return -1;
                ulong key = StaticSmoothEdgeKey(a, b);
                if (midpoints.TryGetValue(key, out int existing))
                    return existing;
                int index = vertices.Count;
                vertices.Add((vertices[a] + vertices[b]) * 0.5f);
                weights.Add((weights[a] + weights[b]) * 0.5f);
                midpoints.Add(key, index);
                return index;
            }
            void Triangle(int a, int b, int c)
            {
                triangles.Add(a);
                triangles.Add(b);
                triangles.Add(c);
            }
            for (int i = 0; i < source.Length; i += 3)
            {
                int a = source[i], b = source[i + 1], c = source[i + 2];
                int ab = Midpoint(a, b), bc = Midpoint(b, c), ca = Midpoint(c, a);
                int splitCount = (ab >= 0 ? 1 : 0) + (bc >= 0 ? 1 : 0) + (ca >= 0 ? 1 : 0);
                if (splitCount == 0)
                    Triangle(a, b, c);
                else if (splitCount == 3)
                {
                    Triangle(a, ab, ca);
                    Triangle(ab, b, bc);
                    Triangle(ca, bc, c);
                    Triangle(ab, bc, ca);
                }
                else
                {
                    // Rotate the face to the one-edge or two-edge canonical case.
                    // Green triangles on the patch boundary prevent T-junctions.
                    while (ab < 0 || (splitCount == 2 && bc < 0))
                    {
                        (a, b, c) = (b, c, a);
                        (ab, bc, ca) = (bc, ca, ab);
                    }
                    if (splitCount == 1)
                    {
                        Triangle(a, ab, c);
                        Triangle(ab, b, c);
                    }
                    else
                    {
                        Triangle(b, bc, ab);
                        Triangle(a, ab, c);
                        Triangle(ab, bc, c);
                    }
                }
            }
            mesh.Clear();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
        }

        private static float MeasureStaticBendAngle(Mesh mesh, List<float> weights)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var firstNormals = new Dictionary<ulong, Vector3>();
            float maxAngle = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                Vector3 normal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]).normalized;
                for (int edge = 0; edge < 3; edge++)
                {
                    int p = triangles[i + edge], q = triangles[i + (edge + 1) % 3];
                    if (weights[p] < 0.99f || weights[q] < 0.99f)
                        continue;
                    ulong key = StaticSmoothEdgeKey(p, q);
                    if (firstNormals.TryGetValue(key, out Vector3 other))
                        maxAngle = Mathf.Max(maxAngle, Vector3.Angle(normal, other));
                    else
                        firstNormals.Add(key, normal);
                }
            }
            return maxAngle;
        }

        private static void SmoothStaticBranchSeamNormals(
            StaticBranchBuildResult branch,
            BranchAlignment alignment)
        {
            if (branch?.mesh == null ||
                branch.boundaryIndices == null ||
                alignment?.boundaryRoot == null ||
                branch.boundaryIndices.Count != alignment.boundaryRoot.Count)
            {
                throw new InvalidOperationException(
                    "Static branch seam normal input is invalid.");
            }

            Vector3 center = MeanPoint(alignment.boundaryRoot);
            Vector3[] normals = branch.mesh.normals;
            Vector3[] vertices = branch.mesh.vertices;
            Quaternion inverseRotation = Quaternion.Inverse(alignment.rotation);
            float[] seamDistances = ComputeMeshDistancesFromBoundary(
                vertices, branch.mesh.triangles, branch.boundaryIndices);
            // The validated collar is cylindrical at the interface. Carry its
            // analytic normal into the collar with a smooth fade instead of
            // overriding one row next to irregularly sized triangle normals.
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 offset = vertices[i] - branch.seamCenter;
                float axial = Vector3.Dot(offset, branch.seamTangent);
                float rootAxial = axial * alignment.scale;
                float rootDistance = seamDistances[i] * alignment.scale;
                if (rootAxial < -1e-6f || rootDistance >= StaticSmoothRampLength)
                    continue;
                Vector3 radial = offset - axial * branch.seamTangent;
                if (radial.sqrMagnitude <= 1e-12f)
                    continue;
                float weight = 1f - Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(rootDistance / StaticSmoothRampLength));
                normals[i] = Vector3.Slerp(normals[i], radial.normalized, weight).normalized;
            }
            for (int i = 0; i < branch.boundaryIndices.Count; i++)
            {
                Vector3 offset = alignment.boundaryRoot[i] - center;
                Vector3 rootRadial =
                    offset -
                    alignment.seamTangentRoot *
                    Vector3.Dot(
                        offset,
                        alignment.seamTangentRoot);
                if (rootRadial.sqrMagnitude <= 1e-12f)
                    continue;
                normals[branch.boundaryIndices[i]] =
                    (inverseRotation * rootRadial.normalized).normalized;
            }
            branch.mesh.normals = normals;
        }

        private static void ReplaceAsset(UnityEngine.Object asset, string path)
        {
            UnityEngine.Object existing =
                AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(asset, path);
                return;
            }

            if (existing.GetType() != asset.GetType())
            {
                throw new InvalidOperationException(
                    $"Cannot replace {path}: existing type {existing.GetType().Name} " +
                    $"does not match generated type {asset.GetType().Name}.");
            }

            EditorUtility.CopySerialized(asset, existing);
            existing.name = asset.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(asset);
        }

        private static void ReplacePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }

        private static void ValidateGeneratedAssets()
        {
            Mesh outer = AssetDatabase.LoadAssetAtPath<Mesh>(OuterMeshPath);
            Mesh inner = AssetDatabase.LoadAssetAtPath<Mesh>(InnerMeshPath);
            Mesh transition = AssetDatabase.LoadAssetAtPath<Mesh>(TransitionMeshPath);
            Mesh staticBranches = AssetDatabase.LoadAssetAtPath<Mesh>(StaticBranchMeshPath);
            Mesh tissue = AssetDatabase.LoadAssetAtPath<Mesh>(TissueMeshPath);
            Material tissueMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    TissueMaterialPath);
            Material supportMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(
                    SupportMaterialPath);
            Texture2D tissueTexture =
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    TissueTexturePath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (outer == null ||
                inner == null ||
                transition == null ||
                staticBranches == null ||
                tissue == null ||
                tissueMaterial == null ||
                supportMaterial == null ||
                tissueTexture == null ||
                prefab == null)
                throw new InvalidOperationException("Hybrid vessel output validation failed: missing asset.");

            int expectedTissueVertexCount =
                (TissueGridSegments + 1) *
                (TissueGridSegments + 1) *
                2;
            int expectedTissueIndexCount =
                TissueGridSegments *
                TissueGridSegments *
                12 +
                TissueGridSegments *
                24;
            if (tissue.vertexCount != expectedTissueVertexCount ||
                tissue.triangles.Length != expectedTissueIndexCount ||
                tissueMaterial.mainTexture != tissueTexture ||
                tissueTexture.wrapMode != TextureWrapMode.Repeat)
            {
                throw new InvalidOperationException(
                    $"Hybrid tissue asset mismatch. expectedVerts={expectedTissueVertexCount} " +
                    $"actualVerts={tissue.vertexCount} expectedIndices={expectedTissueIndexCount} " +
                    $"actualIndices={tissue.triangles.Length} " +
                    $"textureAssigned={tissueMaterial.mainTexture == tissueTexture} " +
                    $"wrap={tissueTexture.wrapMode}");
            }

            int expectedVertexCount = (AxialSegments + 1) * RadialSegments;
            int expectedTriangleIndexCount = AxialSegments * RadialSegments * 6;
            if (outer.vertexCount != expectedVertexCount ||
                inner.vertexCount != expectedVertexCount ||
                outer.triangles.Length != expectedTriangleIndexCount ||
                inner.triangles.Length != expectedTriangleIndexCount)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel topology mismatch. expectedVerts={expectedVertexCount} " +
                    $"outerVerts={outer.vertexCount} innerVerts={inner.vertexCount} " +
                    $"expectedIndices={expectedTriangleIndexCount} " +
                    $"outerIndices={outer.triangles.Length} innerIndices={inner.triangles.Length}");
            }

            int expectedBoundaryEdgeCount = RadialSegments * 2;
            int outerBoundaryEdges = CountBoundaryEdges(outer.triangles);
            int innerBoundaryEdges = CountBoundaryEdges(inner.triangles);
            int transitionBoundaryEdges = CountBoundaryEdges(transition.triangles);
            int staticBoundaryEdges = CountBoundaryEdges(staticBranches.triangles);
            int expectedTransitionBoundaryEdges =
                RadialSegments + staticBoundaryEdges;
            if (outerBoundaryEdges != expectedBoundaryEdgeCount ||
                innerBoundaryEdges != expectedBoundaryEdgeCount ||
                staticBoundaryEdges < 3 ||
                transitionBoundaryEdges != expectedTransitionBoundaryEdges)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel open-boundary mismatch. simulationExpected={expectedBoundaryEdgeCount} " +
                    $"outer={outerBoundaryEdges} inner={innerBoundaryEdges} " +
                    $"transitionExpected={expectedTransitionBoundaryEdges} " +
                    $"transition={transitionBoundaryEdges} static={staticBoundaryEdges}");
            }

            Vector3[] outerVertices = outer.vertices;
            Vector3[] innerVertices = inner.vertices;
            float expectedThickness = OuterRadius - InnerRadius;
            float maxThicknessError = 0f;
            float maxCenterError = 0f;
            float maxOuterRadiusError = 0f;
            float maxInnerRadiusError = 0f;
            float maxLayerDirectionError = 0f;
            for (int ring = 0; ring <= AxialSegments; ring++)
            {
                EvaluateSimulationFrame(
                    ring / (float)AxialSegments,
                    out Vector3 expectedCenter,
                    out _,
                    out _);
                Vector3 outerCenter = Vector3.zero;
                Vector3 innerCenter = Vector3.zero;
                int ringStart = ring * RadialSegments;
                for (int column = 0; column < RadialSegments; column++)
                {
                    outerCenter += outerVertices[ringStart + column];
                    innerCenter += innerVertices[ringStart + column];
                }
                outerCenter /= RadialSegments;
                innerCenter /= RadialSegments;
                maxCenterError = Mathf.Max(
                    maxCenterError,
                    Vector3.Distance(outerCenter, expectedCenter),
                    Vector3.Distance(innerCenter, expectedCenter));

                for (int column = 0; column < RadialSegments; column++)
                {
                    int index = ringStart + column;
                    Vector3 outerOffset = outerVertices[index] - outerCenter;
                    Vector3 innerOffset = innerVertices[index] - innerCenter;
                    maxOuterRadiusError = Mathf.Max(
                        maxOuterRadiusError,
                        Mathf.Abs(outerOffset.magnitude - OuterRadius));
                    maxInnerRadiusError = Mathf.Max(
                        maxInnerRadiusError,
                        Mathf.Abs(innerOffset.magnitude - InnerRadius));
                    maxThicknessError = Mathf.Max(
                        maxThicknessError,
                        Mathf.Abs(
                            Vector3.Distance(
                                outerVertices[index],
                                innerVertices[index]) -
                            expectedThickness));
                    maxLayerDirectionError = Mathf.Max(
                        maxLayerDirectionError,
                        Vector3.Distance(
                            outerOffset.normalized,
                            innerOffset.normalized));
                }
            }

            if (maxThicknessError > 1e-5f ||
                maxCenterError > 1e-5f ||
                maxOuterRadiusError > 1e-5f ||
                maxInnerRadiusError > 1e-5f ||
                maxLayerDirectionError > 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel circular sweep mismatch. " +
                    $"thickness={maxThicknessError} center={maxCenterError} " +
                    $"outerRadius={maxOuterRadiusError} innerRadius={maxInnerRadiusError} " +
                    $"layerDirection={maxLayerDirectionError}");
            }

            Transform outerTransform = prefab.transform.Find("SimulationOuter");
            Transform innerTransform = prefab.transform.Find("SimulationInner");
            Transform transitionTransform = prefab.transform.Find("StaticTransition");
            Transform tissueTransform = prefab.transform.Find("TissueSurface");
            Transform supportTransform = prefab.transform.Find("SurgicalTableSupport");
            Transform branchTransform = prefab.transform.Find("StaticBranchAppearance");
            Transform branchMeshTransform = branchTransform != null
                ? branchTransform.Find("TripoBranchesTrimmed")
                : null;
            if (outerTransform == null ||
                innerTransform == null ||
                transitionTransform == null ||
                tissueTransform == null ||
                supportTransform == null ||
                branchTransform == null ||
                branchMeshTransform == null)
                throw new InvalidOperationException("Hybrid vessel prefab is missing required semantic children.");
            if (tissueTransform.GetComponent<MeshFilter>()?.sharedMesh != tissue ||
                tissueTransform.GetComponent<MeshRenderer>()?.sharedMaterial != tissueMaterial)
            {
                throw new InvalidOperationException(
                    "Hybrid vessel tissue child does not reference generated assets.");
            }
            if (supportTransform.childCount != 3)
            {
                throw new InvalidOperationException(
                    $"Hybrid surgical support rail count is invalid. " +
                    $"expected=3 actual={supportTransform.childCount}");
            }
            for (int i = 0; i < supportTransform.childCount; i++)
            {
                MeshRenderer railRenderer =
                    supportTransform.GetChild(i).GetComponent<MeshRenderer>();
                if (railRenderer == null ||
                    railRenderer.sharedMaterial != supportMaterial ||
                    supportTransform.GetChild(i).GetComponent<Collider>() != null)
                {
                    throw new InvalidOperationException(
                        $"Hybrid surgical support rail {i} is invalid.");
                }
            }

            Vector3[] transitionVertices = transition.vertices;
            float maxSimulationTransitionGap = 0f;
            int outerTopStart = AxialSegments * RadialSegments;
            for (int i = 0; i < RadialSegments; i++)
            {
                maxSimulationTransitionGap = Mathf.Max(
                    maxSimulationTransitionGap,
                    Vector3.Distance(
                        outerVertices[outerTopStart + i],
                        transitionVertices[i]));
            }

            List<int> staticBoundaryIndices = FindSingleBoundaryLoop(
                new List<int>(staticBranches.triangles),
                staticBranches.vertexCount,
                "static-branch-validation");
            Vector3[] staticVertices = staticBranches.vertices;
            var staticBoundaryRoot = new List<Vector3>(staticBoundaryIndices.Count);
            Matrix4x4 branchToRoot =
                prefab.transform.worldToLocalMatrix * branchMeshTransform.localToWorldMatrix;
            for (int i = 0; i < staticBoundaryIndices.Count; i++)
            {
                staticBoundaryRoot.Add(
                    branchToRoot.MultiplyPoint3x4(staticVertices[staticBoundaryIndices[i]]));
            }
            Vector3 staticSeamCenter = MeanPoint(staticBoundaryRoot);
            int transitionEndCount = staticBoundaryRoot.Count;
            int transitionEndStart =
                transitionVertices.Length - transitionEndCount;
            int transitionApproachStart =
                transitionEndStart - transitionEndCount;
            Vector3 staticSeamTangent = Vector3.zero;
            for (int i = 0; i < transitionEndCount; i++)
            {
                staticSeamTangent +=
                    transitionVertices[transitionEndStart + i] -
                    transitionVertices[transitionApproachStart + i];
            }
            if (staticSeamTangent.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Hybrid transition does not expose a branch-side seam tangent.");
            }
            staticSeamTangent.Normalize();
            float maxStaticSeamRadiusError = 0f;
            float maxStaticSeamPlaneError = 0f;
            for (int i = 0; i < staticBoundaryRoot.Count; i++)
            {
                Vector3 offset =
                    staticBoundaryRoot[i] - staticSeamCenter;
                float axial =
                    Vector3.Dot(
                        offset,
                        staticSeamTangent);
                Vector3 radial =
                    offset -
                    staticSeamTangent * axial;
                maxStaticSeamPlaneError = Mathf.Max(
                    maxStaticSeamPlaneError,
                    Mathf.Abs(axial));
                maxStaticSeamRadiusError = Mathf.Max(
                    maxStaticSeamRadiusError,
                    Mathf.Abs(radial.magnitude - OuterRadius));
            }

            Bounds vesselRootBounds =
                new Bounds(
                    outerVertices[0],
                    Vector3.zero);
            EncapsulateMeshVertices(
                ref vesselRootBounds,
                outerVertices);
            EncapsulateMeshVertices(
                ref vesselRootBounds,
                transitionVertices);
            for (int i = 0; i < staticVertices.Length; i++)
            {
                vesselRootBounds.Encapsulate(
                    branchToRoot.MultiplyPoint3x4(
                        staticVertices[i]));
            }
            Bounds tissueBounds = tissue.bounds;
            bool tissueCoversVessel =
                tissueBounds.min.x <= vesselRootBounds.min.x &&
                tissueBounds.max.x >= vesselRootBounds.max.x &&
                tissueBounds.min.z <= vesselRootBounds.min.z &&
                tissueBounds.max.z >= vesselRootBounds.max.z;
            Vector3[] tissueVertices = tissue.vertices;
            float maxOuterTissueViolation =
                MeasureTissueClearanceViolation(
                    tissueVertices,
                    outerVertices);
            float maxTransitionTissueViolation =
                MeasureTissueClearanceViolation(
                    tissueVertices,
                    transitionVertices);
            float supportMaxY = float.NegativeInfinity;
            for (int i = 0; i < supportTransform.childCount; i++)
            {
                supportMaxY = Mathf.Max(
                    supportMaxY,
                    supportTransform.GetChild(i)
                        .GetComponent<MeshRenderer>()
                        .bounds.max.y);
            }
            if (!tissueCoversVessel ||
                tissueBounds.size.y < TissueThickness - 0.01f ||
                maxOuterTissueViolation > 1e-5f ||
                maxTransitionTissueViolation > 1e-5f ||
                supportMaxY >= tissueBounds.min.y - 0.005f)
            {
                throw new InvalidOperationException(
                    $"Hybrid tissue layout mismatch. covers={tissueCoversVessel} " +
                    $"tissue={tissueBounds} vessel={vesselRootBounds} " +
                    $"outerViolation={maxOuterTissueViolation:F8} " +
                    $"transitionViolation={maxTransitionTissueViolation:F8} " +
                    $"supportMaxY={supportMaxY:F6}");
            }

            float maxTransitionToBranchGap = 0f;
            float maxBranchToTransitionGap = 0f;
            for (int i = 0; i < transitionEndCount; i++)
            {
                Vector3 point = transitionVertices[transitionEndStart + i];
                float nearest = float.PositiveInfinity;
                for (int branchIndex = 0;
                     branchIndex < staticBoundaryRoot.Count;
                     branchIndex++)
                {
                    nearest = Mathf.Min(
                        nearest,
                        Vector3.Distance(
                            point,
                            staticBoundaryRoot[branchIndex]));
                }
                maxTransitionToBranchGap = Mathf.Max(
                    maxTransitionToBranchGap,
                    nearest);
            }
            for (int branchIndex = 0;
                 branchIndex < staticBoundaryRoot.Count;
                 branchIndex++)
            {
                float nearest = float.PositiveInfinity;
                for (int transitionIndex = 0;
                     transitionIndex < transitionEndCount;
                     transitionIndex++)
                {
                    nearest = Mathf.Min(
                        nearest,
                        Vector3.Distance(
                            staticBoundaryRoot[branchIndex],
                            transitionVertices[
                                transitionEndStart + transitionIndex]));
                }
                maxBranchToTransitionGap = Mathf.Max(
                    maxBranchToTransitionGap,
                    nearest);
            }

            if (transitionEndCount != staticBoundaryRoot.Count ||
                maxSimulationTransitionGap > 1e-5f ||
                maxTransitionToBranchGap > 1e-5f ||
                maxBranchToTransitionGap > 1e-5f ||
                maxStaticSeamRadiusError > 1e-5f ||
                maxStaticSeamPlaneError > 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel three-part continuity mismatch. " +
                    $"simulationToTransition={maxSimulationTransitionGap} " +
                    $"transitionEnd={transitionEndCount} " +
                    $"staticBoundary={staticBoundaryRoot.Count} " +
                    $"transitionToBranch={maxTransitionToBranchGap} " +
                    $"branchToTransition={maxBranchToTransitionGap} " +
                    $"staticSeamRadius={maxStaticSeamRadiusError} " +
                    $"staticSeamPlane={maxStaticSeamPlaneError}");
            }

            Debug.Log(
                $"[HybridVessel] continuity simulationToTransition={maxSimulationTransitionGap:F8} " +
                $"transitionToBranch={maxTransitionToBranchGap:F8} " +
                $"branchToTransition={maxBranchToTransitionGap:F8} " +
                $"transitionBoundary={transitionBoundaryEdges} " +
                $"staticBoundary={staticBoundaryIndices.Count} " +
                $"staticSeamRadius={maxStaticSeamRadiusError:F8} " +
                $"staticSeamPlane={maxStaticSeamPlaneError:F8} " +
                $"tissueSize={tissueBounds.size.x:F4}x{tissueBounds.size.z:F4} " +
                $"tissueMaxY={tissueBounds.max.y:F6} " +
                $"circularOuterError={maxOuterRadiusError:F8} " +
                $"circularInnerError={maxInnerRadiusError:F8}");
        }

        private static int CountBoundaryEdges(int[] triangles)
        {
            var multiplicity = new Dictionary<ulong, int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdgeMultiplicity(multiplicity, triangles[i], triangles[i + 1]);
                AddEdgeMultiplicity(multiplicity, triangles[i + 1], triangles[i + 2]);
                AddEdgeMultiplicity(multiplicity, triangles[i + 2], triangles[i]);
            }

            int boundaryCount = 0;
            foreach (KeyValuePair<ulong, int> pair in multiplicity)
            {
                if (pair.Value == 1)
                    boundaryCount++;
            }
            return boundaryCount;
        }

        private static void AddEdgeMultiplicity(
            Dictionary<ulong, int> multiplicity,
            int a,
            int b)
        {
            uint min = (uint)Mathf.Min(a, b);
            uint max = (uint)Mathf.Max(a, b);
            ulong key = ((ulong)min << 32) | max;
            multiplicity.TryGetValue(key, out int count);
            multiplicity[key] = count + 1;
        }
    }
}
