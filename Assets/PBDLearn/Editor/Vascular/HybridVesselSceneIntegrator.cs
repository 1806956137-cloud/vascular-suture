using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PBDLearn
{
    /// <summary>
    /// Connects the reusable hybrid-vessel model to the existing SingLayer
    /// procedure host without copying scene-owned needle, incision, suture,
    /// camera, UI, or debug state into the content prefab.
    /// </summary>
    public static class HybridVesselSceneIntegrator
    {
        private const string ScenePath =
            "Assets/TestScenes/Vessel_PBDTest/SingLayer.unity";
        private const string PrefabPath =
            "Assets/PBDLearn/Content/Vascular/Prefabs/HybridBranchedVessel.prefab";
        private const string LegacyOuterPath =
            "Assets/PBDLearn/Content/Vascular/Models/外壁.fbx";
        private const string LegacyInnerPath =
            "Assets/PBDLearn/Content/Vascular/Models/内壁.fbx";
        private const string HybridRootName = "HybridBranchedVessel";

        [MenuItem("PBDLearn/Vascular/Integrate Hybrid Vessel Into SingLayer")]
        public static void IntegrateSingLayer()
        {
            Scene scene =
                EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Single);
            ClothSimulationDriverDoubleLayer driver =
                FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                    scene,
                    "double-layer driver");

            MeshFilter previousOuter = driver.outerMeshFilter;
            MeshFilter previousInner = driver.innerMeshFilter;
            Transform vesselRoot =
                ResolveVesselRoot(
                    scene,
                    previousOuter,
                    previousInner);

            var rootsToRemove = new HashSet<GameObject>();
            AddReplaceableRoot(previousOuter, vesselRoot, rootsToRemove);
            AddReplaceableRoot(previousInner, vesselRoot, rootsToRemove);
            Transform previousHybrid =
                vesselRoot.Find(HybridRootName);
            if (previousHybrid != null)
                rootsToRemove.Add(previousHybrid.gameObject);

            GameObject prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel prefab was not found at {PrefabPath}.");
            }

            GameObject instance;
            string previousHybridPrefabPath =
                previousHybrid != null
                    ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        previousHybrid.gameObject)
                    : string.Empty;
            if (previousHybrid != null &&
                previousHybridPrefabPath == PrefabPath)
            {
                instance = previousHybrid.gameObject;
                rootsToRemove.Remove(instance);
            }
            else
            {
                instance =
                    PrefabUtility.InstantiatePrefab(
                        prefab,
                        vesselRoot) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate hybrid vessel prefab.");
                }
            }

            instance.name = HybridRootName;
            Transform instanceTransform = instance.transform;
            instanceTransform.localPosition = Vector3.zero;
            instanceTransform.localRotation = Quaternion.identity;
            instanceTransform.localScale = Vector3.one;
            Transform nestedHybrid =
                instanceTransform.Find(HybridRootName);
            if (nestedHybrid != null)
                rootsToRemove.Add(nestedHybrid.gameObject);

            MeshFilter outer =
                RequireMeshFilter(instanceTransform, "SimulationOuter");
            MeshFilter inner =
                RequireMeshFilter(instanceTransform, "SimulationInner");
            ConfigureRendererSlice(
                outer,
                driver,
                ClothSimulationDriverDoubleLayer.VesselLayer.Outer);
            ConfigureRendererSlice(
                inner,
                driver,
                ClothSimulationDriverDoubleLayer.VesselLayer.Inner);

            driver.outerMeshFilter = outer;
            driver.innerMeshFilter = inner;
            EditorUtility.SetDirty(driver);

            AutoSutureRuntimeController autoSuture =
                FindOnlySceneComponent<AutoSutureRuntimeController>(
                    scene,
                    "auto-suture controller");
            autoSuture.tissueAvoidanceSurface =
                RequireMeshFilter(
                    instanceTransform,
                    "TissueSurface");
            autoSuture.avoidTissueBackground = true;
            autoSuture.useObliqueProcedureCamera = true;
            autoSuture.showPlanOverviewCamera = true;
            EditorUtility.SetDirty(autoSuture);

            rootsToRemove.Remove(instance);
            foreach (GameObject root in rootsToRemove)
            {
                if (root != null)
                    UnityEngine.Object.DestroyImmediate(root);
            }

            ValidateScene(scene, driver, instance);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"Failed to save {ScenePath}.");

            Debug.Log(
                $"[HybridVesselIntegration] complete scene={ScenePath} " +
                $"driver={driver.name} parent={vesselRoot.name} " +
                $"removedLegacyRoots={rootsToRemove.Count} " +
                $"outerVerts={outer.sharedMesh.vertexCount} " +
                $"innerVerts={inner.sharedMesh.vertexCount}");
        }

        [MenuItem("PBDLearn/Vascular/Rebuild And Integrate SingLayer")]
        public static void RebuildAndIntegrateSingLayer()
        {
            HybridVesselAssetBuilder.Build();
            IntegrateSingLayer();
        }

        public static void IntegrateBatch()
        {
            try
            {
                IntegrateSingLayer();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static void ValidateBatch()
        {
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                ClothSimulationDriverDoubleLayer driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform hybrid = FindHybridRoot(scene);
                ValidateScene(scene, driver, hybrid.gameObject);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static void ValidateProcedurePreviewBatch()
        {
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                ClothSimulationDriverDoubleLayer driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform hybrid = FindHybridRoot(scene);
                ValidateScene(scene, driver, hybrid.gameObject);

                if (!driver.TryGenerateProceduralIncisionStroke(
                        clearExisting: true,
                        logWarnings: true))
                {
                    throw new InvalidOperationException(
                        "Hybrid vessel failed the current incision-preview rule chain.");
                }

                AutoSutureRuntimeController autoSuture =
                    FindOnlySceneComponent<AutoSutureRuntimeController>(
                        scene,
                        "auto-suture controller");
                if (!autoSuture.GeneratePlanGeometryPreviewFromGuides() ||
                    autoSuture.plan == null ||
                    autoSuture.plan.Count <= 0)
                {
                    throw new InvalidOperationException(
                        "Hybrid vessel incision preview did not produce usable stitch-hole geometry.");
                }
                ValidateStitchHoleGeometry(driver, autoSuture.plan);
                ValidateProcedurePresentationGeometry(
                    driver,
                    autoSuture,
                    autoSuture.plan);

                Debug.Log(
                    $"[HybridVesselIntegration] procedure preview passed " +
                    $"mode={driver.CurrentSutureSideGuideMode} " +
                    $"strokePoints={driver.PendingIncisionPointCount} " +
                    $"stitchPairs={autoSuture.plan.Count} " +
                    $"outerQuality={driver.IncisionOuterQualitySummary} " +
                    $"innerQuality={driver.IncisionInnerQualitySummary}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void ValidateStitchHoleGeometry(
            ClothSimulationDriverDoubleLayer driver,
            AutoSuturePlan plan)
        {
            if (driver == null ||
                plan == null ||
                plan.stitchPairs == null ||
                plan.stitchPairs.Count != 6)
            {
                throw new InvalidOperationException(
                    $"Carrel stitch-hole plan must contain six pairs. actual={plan?.Count ?? 0}");
            }

            float[] expectedStations =
            {
                0f,
                1f / 3f,
                2f / 3f,
                1f / 6f,
                1f / 2f,
                5f / 6f,
            };
            Vector3 bankAxis =
                driver.SutureSideBGuideCenterWorld -
                driver.SutureSideAGuideCenterWorld;
            if (bankAxis.sqrMagnitude <= 1e-12f)
            {
                throw new InvalidOperationException(
                    "Stitch-hole bank axis is degenerate.");
            }
            bankAxis.Normalize();

            float minimumWallPass = float.PositiveInfinity;
            float minimumBankSeparation = float.PositiveInfinity;
            var preview = new List<string>();
            for (int i = 0; i < plan.stitchPairs.Count; i++)
            {
                AutoSutureStitchPair pair = plan.stitchPairs[i];
                AutoSuturePass passA = pair != null ? pair.sideAPass : null;
                AutoSuturePass passB = pair != null ? pair.sideBPass : null;
                if (passA == null || passB == null)
                {
                    throw new InvalidOperationException(
                        $"Stitch-hole pair {i} is incomplete.");
                }
                if (Mathf.Abs(passA.station01 - expectedStations[i]) > 1e-5f ||
                    Mathf.Abs(passB.station01 - expectedStations[i]) > 1e-5f)
                {
                    throw new InvalidOperationException(
                        $"Stitch-hole station order changed at pair {i}. " +
                        $"actual=({passA.station01:F5},{passB.station01:F5}) " +
                        $"expected={expectedStations[i]:F5}");
                }
                if (passA.direction !=
                        ClothSimulationDriverDoubleLayer.SuturePassDirection.OuterToInner ||
                    passB.direction !=
                        ClothSimulationDriverDoubleLayer.SuturePassDirection.InnerToOuter)
                {
                    throw new InvalidOperationException(
                        $"Stitch-hole wall direction changed at pair {i}.");
                }
                if (!IsFinite(passA.entryPointWorld) ||
                    !IsFinite(passA.exitPointWorld) ||
                    !IsFinite(passB.entryPointWorld) ||
                    !IsFinite(passB.exitPointWorld))
                {
                    throw new InvalidOperationException(
                        $"Stitch-hole pair {i} contains a non-finite point.");
                }

                float wallPassA =
                    Vector3.Distance(
                        passA.entryPointWorld,
                        passA.exitPointWorld);
                float wallPassB =
                    Vector3.Distance(
                        passB.entryPointWorld,
                        passB.exitPointWorld);
                minimumWallPass =
                    Mathf.Min(
                        minimumWallPass,
                        Mathf.Min(wallPassA, wallPassB));
                float bankSeparation =
                    Vector3.Dot(
                        passB.exitPointWorld -
                        passA.entryPointWorld,
                        bankAxis);
                minimumBankSeparation =
                    Mathf.Min(minimumBankSeparation, bankSeparation);
                if (wallPassA <= 1e-4f ||
                    wallPassB <= 1e-4f ||
                    bankSeparation <= 1e-4f)
                {
                    throw new InvalidOperationException(
                        $"Stitch-hole pair {i} collapsed or crossed banks. " +
                        $"wall=({wallPassA:F6},{wallPassB:F6}) " +
                        $"bankSeparation={bankSeparation:F6}");
                }

                if (preview.Count < 6)
                {
                    preview.Add(
                        $"P{i + 1}@{passA.station01:F4} " +
                        $"A={passA.entryPointWorld:F4}->{passA.exitPointWorld:F4} " +
                        $"B={passB.entryPointWorld:F4}->{passB.exitPointWorld:F4}");
                }
            }

            Debug.Log(
                $"[HybridVesselIntegration][StitchHoleAudit] pairs={plan.Count} " +
                $"order=stay(0,1/3,2/3)+sector-midpoint(1/6,1/2,5/6) " +
                $"minimumWallPass={minimumWallPass:F6} " +
                $"minimumBankSeparation={minimumBankSeparation:F6} " +
                $"preview={string.Join(" | ", preview)}");
        }

        private static void ValidateProcedurePresentationGeometry(
            ClothSimulationDriverDoubleLayer driver,
            AutoSutureRuntimeController autoSuture,
            AutoSuturePlan plan)
        {
            MethodInfo buildOuterFrame =
                typeof(AutoSutureRuntimeController).GetMethod(
                    "TryGetCircumferentialOuterFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildOuterFrame == null)
                throw new MissingMethodException(
                    "TryGetCircumferentialOuterFrame");
            object[] frameArguments =
            {
                Vector3.zero,
                Vector3.zero,
                0f,
                string.Empty,
            };
            bool frameBuilt =
                (bool)buildOuterFrame.Invoke(
                    autoSuture,
                    frameArguments);
            Vector3 center = (Vector3)frameArguments[0];
            Vector3 planeNormal = (Vector3)frameArguments[1];
            float outerRadius = (float)frameArguments[2];
            string frameFailure = frameArguments[3] as string;
            if (!frameBuilt ||
                planeNormal.sqrMagnitude <= 0.99f ||
                outerRadius <= 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Actual cut-plane frame is invalid. " +
                    $"reason={frameFailure ?? "none"} " +
                    $"normal={planeNormal} radius={outerRadius:F6}");
            }

            MethodInfo buildCameraFrame =
                typeof(AutoSutureRuntimeController).GetMethod(
                    "BuildPlanCameraFrame",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (buildCameraFrame == null)
                throw new MissingMethodException(
                    "BuildPlanCameraFrame");
            object[] cameraArguments =
            {
                Vector3.zero,
                Vector3.zero,
                Vector3.zero,
            };
            buildCameraFrame.Invoke(
                autoSuture,
                cameraArguments);
            Vector3 planRight =
                (Vector3)cameraArguments[0];
            Vector3 planUp =
                (Vector3)cameraArguments[1];
            Vector3 viewForward =
                (Vector3)cameraArguments[2];
            Vector3 bankAxis =
                driver.SutureSideBGuideCenterWorld -
                driver.SutureSideAGuideCenterWorld;
            if (bankAxis.sqrMagnitude <= 1e-12f)
                throw new InvalidOperationException(
                    "Presentation bank axis is degenerate.");
            bankAxis.Normalize();
            float cameraOrthogonality = Mathf.Max(
                Mathf.Abs(Vector3.Dot(planRight, planUp)),
                Mathf.Abs(Vector3.Dot(planRight, viewForward)),
                Mathf.Abs(Vector3.Dot(planUp, viewForward)));
            float bankAlignment =
                Mathf.Abs(
                    Vector3.Dot(
                        planRight.normalized,
                        bankAxis));
            if (planRight.sqrMagnitude <= 0.99f ||
                planUp.sqrMagnitude <= 0.99f ||
                viewForward.sqrMagnitude <= 0.99f ||
                cameraOrthogonality > 1e-3f ||
                bankAlignment < 0.99f)
            {
                throw new InvalidOperationException(
                    $"Guide-derived plan camera frame is invalid. " +
                    $"orthogonality={cameraOrthogonality:E3} " +
                    $"bankAlignment={bankAlignment:F6}");
            }

            AutoSuturePass routeStartPass =
                plan.stitchPairs[0].sideBPass;
            AutoSuturePass routeDestinationPass =
                plan.stitchPairs[1].sideAPass;
            MethodInfo buildExteriorArc =
                typeof(AutoSutureRuntimeController).GetMethod(
                    "TryBuildExteriorArcInPlane",
                    BindingFlags.Static | BindingFlags.NonPublic);
            if (buildExteriorArc == null)
                throw new MissingMethodException(
                    "TryBuildExteriorArcInPlane");
            var route = new List<Vector3>();
            object[] arcArguments =
            {
                center,
                planeNormal,
                routeStartPass.exitPointWorld,
                routeDestinationPass.entryPointWorld,
                outerRadius,
                autoSuture.safeTravelClearance,
                route,
                0f,
                0f,
            };
            bool arcBuilt =
                (bool)buildExteriorArc.Invoke(
                    null,
                    arcArguments);
            float cruiseRadius = (float)arcArguments[7];
            float minimumArcRadius = (float)arcArguments[8];
            if (!arcBuilt ||
                route.Count < 2 ||
                minimumArcRadius <
                    outerRadius - 1e-5f)
            {
                throw new InvalidOperationException(
                    $"Actual cut-plane exterior route is invalid. " +
                    $"waypoints={route.Count} " +
                    $"outerRadius={outerRadius:F6} " +
                    $"minimumArcRadius={minimumArcRadius:F6}");
            }

            Debug.Log(
                $"[HybridVesselIntegration][PresentationAudit] " +
                $"cutPlaneNormal={planeNormal:F4} " +
                $"outerRadius={outerRadius:F6} " +
                $"cameraBankAlignment={bankAlignment:F6} " +
                $"cameraOrthoError={cameraOrthogonality:E3} " +
                $"exteriorWaypoints={route.Count} " +
                $"cruiseRadius={cruiseRadius:F6} " +
                $"minimumArcRadius={minimumArcRadius:F6}");
        }

        private static bool IsFinite(Vector3 value)
        {
            return
                !float.IsNaN(value.x) &&
                !float.IsNaN(value.y) &&
                !float.IsNaN(value.z) &&
                !float.IsInfinity(value.x) &&
                !float.IsInfinity(value.y) &&
                !float.IsInfinity(value.z);
        }

        public static void ValidateLegacyProcedurePreviewBatch()
        {
            ValidateLegacyProcedureMode(
                true,
                "legacy circumferential procedure baseline");
        }

        public static void ValidateLegacyGreenfieldProcedurePreviewBatch()
        {
            ValidateLegacyProcedureMode(
                false,
                "legacy greenfield procedure baseline");
        }

        private static void ValidateLegacyProcedureMode(
            bool circumferentialMode,
            string label)
        {
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                ClothSimulationDriverDoubleLayer driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform vesselRoot =
                    ResolveVesselRoot(
                        scene,
                        driver.outerMeshFilter,
                        driver.innerMeshFilter);
                GameObject legacyOuter =
                    InstantiateLegacyModel(
                        LegacyOuterPath,
                        vesselRoot,
                        "LegacyOuterAudit",
                        Vector3.one);
                GameObject legacyInner =
                    InstantiateLegacyModel(
                        LegacyInnerPath,
                        vesselRoot,
                        "LegacyInnerAudit",
                        Vector3.one * 0.9f);

                MeshFilter originalOuter = driver.outerMeshFilter;
                MeshFilter originalInner = driver.innerMeshFilter;
                bool originalMode =
                    driver.incisionCircumferentialSeparationCut;
                driver.outerMeshFilter =
                    legacyOuter.GetComponentInChildren<MeshFilter>(true);
                driver.innerMeshFilter =
                    legacyInner.GetComponentInChildren<MeshFilter>(true);
                driver.incisionCircumferentialSeparationCut =
                    circumferentialMode;

                bool succeeded =
                    driver.TryGenerateProceduralIncisionStroke(
                        clearExisting: true,
                        logWarnings: true);

                driver.outerMeshFilter = originalOuter;
                driver.innerMeshFilter = originalInner;
                driver.incisionCircumferentialSeparationCut =
                    originalMode;
                UnityEngine.Object.DestroyImmediate(legacyOuter);
                UnityEngine.Object.DestroyImmediate(legacyInner);

                if (!succeeded)
                {
                    throw new InvalidOperationException(
                        $"Legacy vessel failed the {label} rule chain.");
                }

                Debug.Log(
                    $"[HybridVesselIntegration] {label} passed " +
                    $"strokePoints={driver.PendingIncisionPointCount} " +
                    $"outerQuality={driver.IncisionOuterQualitySummary} " +
                    $"innerQuality={driver.IncisionInnerQualitySummary}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static void ValidateGreenfieldProcedurePreviewBatch()
        {
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                ClothSimulationDriverDoubleLayer driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform hybrid = FindHybridRoot(scene);
                ValidateScene(scene, driver, hybrid.gameObject);

                bool originalMode =
                    driver.incisionCircumferentialSeparationCut;
                try
                {
                    driver.incisionCircumferentialSeparationCut = false;
                    if (!driver.TryGenerateProceduralIncisionStroke(
                            clearExisting: true,
                            logWarnings: true))
                    {
                        throw new InvalidOperationException(
                            "Hybrid vessel failed the greenfield incision-preview rule chain.");
                    }

                    Debug.Log(
                        $"[HybridVesselIntegration] greenfield procedure preview passed " +
                        $"strokePoints={driver.PendingIncisionPointCount} " +
                        $"outerQuality={driver.IncisionOuterQualitySummary} " +
                        $"innerQuality={driver.IncisionInnerQualitySummary}");
                }
                finally
                {
                    driver.incisionCircumferentialSeparationCut =
                        originalMode;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static void ValidateRuntimeConstraintContractsBatch()
        {
            ClothSimulationDriverDoubleLayer driver = null;
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform hybrid = FindHybridRoot(scene);
                ValidateScene(scene, driver, hybrid.gameObject);

                MethodInfo initialize =
                    typeof(ClothSimulationDriverDoubleLayer).GetMethod(
                        "InitSim",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                if (initialize == null)
                    throw new MissingMethodException("InitSim");
                initialize.Invoke(driver, null);

                ClothSimulator simulator = driver.Simulator;
                if (simulator == null)
                    throw new InvalidOperationException(
                        "Runtime constraint audit did not create a simulator.");
                if (simulator.activePinConstraintCount <= 0 ||
                    simulator.activePinConstraintCount >=
                        simulator.vertexCount)
                {
                    throw new InvalidOperationException(
                        $"Runtime pin contract failed. active={simulator.activePinConstraintCount} " +
                        $"vertices={simulator.vertexCount}");
                }
                if (simulator.activeLayerSeparationCount !=
                    driver.OuterCount)
                {
                    throw new InvalidOperationException(
                        $"Runtime layer-separation coverage failed. " +
                        $"active={simulator.activeLayerSeparationCount} " +
                        $"outer={driver.OuterCount}");
                }
                if (simulator.crossLayerLinkCount <
                    driver.OuterCount)
                {
                    throw new InvalidOperationException(
                        $"Runtime thickness-link coverage failed. " +
                        $"links={simulator.crossLayerLinkCount} " +
                        $"outer={driver.OuterCount}");
                }

                var primaryCovered =
                    new bool[driver.OuterCount];
                float minRest = float.PositiveInfinity;
                float maxRest = 0f;
                for (int i = 0;
                     i < simulator.crossLayerLinkCount;
                     i++)
                {
                    CrossLayerLinkInfo link =
                        simulator.GetCrossLayerLinkInfo(i);
                    minRest = Mathf.Min(minRest, link.restLength);
                    maxRest = Mathf.Max(maxRest, link.restLength);
                    int outerLocal = -1;
                    int innerLocal = -1;
                    if (link.a >= driver.OuterStart &&
                        link.a < driver.OuterStart + driver.OuterCount &&
                        link.b >= driver.InnerStart &&
                        link.b < driver.InnerStart + driver.InnerCount)
                    {
                        outerLocal = link.a - driver.OuterStart;
                        innerLocal = link.b - driver.InnerStart;
                    }
                    else if (
                        link.b >= driver.OuterStart &&
                        link.b < driver.OuterStart + driver.OuterCount &&
                        link.a >= driver.InnerStart &&
                        link.a < driver.InnerStart + driver.InnerCount)
                    {
                        outerLocal = link.b - driver.OuterStart;
                        innerLocal = link.a - driver.InnerStart;
                    }
                    if (outerLocal >= 0 &&
                        outerLocal == innerLocal)
                        primaryCovered[outerLocal] = true;
                }

                int missingPrimary = 0;
                for (int i = 0; i < primaryCovered.Length; i++)
                {
                    if (!primaryCovered[i])
                        missingPrimary++;
                }
                if (missingPrimary > 0 ||
                    minRest <= 1e-6f ||
                    maxRest > 0.1f)
                {
                    throw new InvalidOperationException(
                        $"Runtime topology-identity links are invalid. " +
                        $"missingPrimary={missingPrimary} " +
                        $"rest=({minRest:F6},{maxRest:F6})");
                }

                FieldInfo middleWallMappingField =
                    typeof(ClothSimulationDriverDoubleLayer).GetField(
                        "_middleWallOuterToInnerLocal",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                int[] middleWallMapping =
                    middleWallMappingField?.GetValue(driver) as int[];
                int middleWallMissing = 0;
                int middleWallIdentityMismatch = 0;
                if (middleWallMapping == null ||
                    middleWallMapping.Length != driver.OuterCount)
                {
                    middleWallMissing = driver.OuterCount;
                }
                else
                {
                    for (int local = 0;
                         local < middleWallMapping.Length;
                         local++)
                    {
                        if (middleWallMapping[local] < 0)
                            middleWallMissing++;
                        else if (middleWallMapping[local] != local)
                            middleWallIdentityMismatch++;
                    }
                }
                if (middleWallMissing > 0 ||
                    middleWallIdentityMismatch > 0)
                {
                    throw new InvalidOperationException(
                        $"Runtime middle-wall mapping contract failed. " +
                        $"missing={middleWallMissing} " +
                        $"identityMismatch={middleWallIdentityMismatch} " +
                        $"outer={driver.OuterCount}");
                }

                Debug.Log(
                    $"[HybridVesselIntegration][RuntimeConstraintAudit] " +
                    $"vertices={simulator.vertexCount} " +
                    $"pins={simulator.activePinConstraintCount} " +
                    $"layerSeparation={simulator.activeLayerSeparationCount} " +
                    $"thicknessLinks={simulator.crossLayerLinkCount} " +
                    $"primaryCovered={driver.OuterCount - missingPrimary}/{driver.OuterCount} " +
                    $"middleWallIdentity={driver.OuterCount - middleWallIdentityMismatch}/{driver.OuterCount} " +
                    $"rest=({minRest:F6},{maxRest:F6})");
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                Debug.LogException(exception.InnerException);
                throw exception.InnerException;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                if (driver != null && driver.Simulator != null)
                {
                    MethodInfo cleanup =
                        typeof(ClothSimulationDriverDoubleLayer).GetMethod(
                            "OnDestroy",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    cleanup?.Invoke(driver, null);
                }
            }
        }

        public static void BenchmarkRuntimeStepsBatch()
        {
            ClothSimulationDriverDoubleLayer driver = null;
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform hybrid = FindHybridRoot(scene);
                ValidateScene(
                    scene,
                    driver,
                    hybrid.gameObject);

                MethodInfo initialize =
                    typeof(ClothSimulationDriverDoubleLayer).GetMethod(
                        "InitSim",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                if (initialize == null)
                    throw new MissingMethodException("InitSim");
                initialize.Invoke(driver, null);

                ClothSimulator simulator = driver.Simulator;
                if (simulator == null)
                {
                    throw new InvalidOperationException(
                        "Runtime step benchmark did not create a simulator.");
                }

                const float deltaTime = 1f / 60f;
                const int warmupSteps = 3;
                const int measuredSteps = 60;
                for (int i = 0; i < warmupSteps; i++)
                {
                    simulator.Step(deltaTime);
                    simulator.ComputeAsyncJobs();
                }

                var stopwatch =
                    System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < measuredSteps; i++)
                {
                    simulator.Step(deltaTime);
                    simulator.ComputeAsyncJobs();
                }
                stopwatch.Stop();

                double averageMilliseconds =
                    stopwatch.Elapsed.TotalMilliseconds /
                    measuredSteps;
                if (double.IsNaN(averageMilliseconds) ||
                    double.IsInfinity(averageMilliseconds) ||
                    averageMilliseconds <= 0.0)
                {
                    throw new InvalidOperationException(
                        $"Runtime step benchmark returned invalid timing " +
                        $"{averageMilliseconds}.");
                }

                Debug.Log(
                    $"[HybridVesselIntegration][RuntimeStepBenchmark] " +
                    $"steps={measuredSteps} " +
                    $"vertices={simulator.vertexCount} " +
                    $"thicknessLinks={simulator.crossLayerLinkCount} " +
                    $"totalMs={stopwatch.Elapsed.TotalMilliseconds:F3} " +
                    $"avgMs={averageMilliseconds:F3} " +
                    $"simulatedHz={1000.0 / averageMilliseconds:F1}");
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException != null)
            {
                Debug.LogException(exception.InnerException);
                throw exception.InnerException;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
            finally
            {
                if (driver != null && driver.Simulator != null)
                {
                    MethodInfo cleanup =
                        typeof(ClothSimulationDriverDoubleLayer).GetMethod(
                            "OnDestroy",
                            BindingFlags.Instance | BindingFlags.NonPublic);
                    cleanup?.Invoke(driver, null);
                }
            }
        }

        public static void CleanGeneratedPreviewArtifactsBatch()
        {
            try
            {
                Scene scene =
                    EditorSceneManager.OpenScene(
                        ScenePath,
                        OpenSceneMode.Single);
                ClothSimulationDriverDoubleLayer driver =
                    FindOnlySceneComponent<ClothSimulationDriverDoubleLayer>(
                        scene,
                        "double-layer driver");
                Transform preview =
                    driver.transform.Find(
                        "MiddleWallFillPreview");
                int removed = 0;
                if (preview != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        preview.gameObject);
                    removed = 1;
                }

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(
                        scene,
                        ScenePath))
                {
                    throw new InvalidOperationException(
                        $"Failed to save cleaned scene {ScenePath}.");
                }

                Debug.Log(
                    $"[HybridVesselIntegration][PreviewArtifactCleanup] " +
                    $"removedMiddleWallPreview={removed} " +
                    $"preservedScene={ScenePath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static GameObject InstantiateLegacyModel(
            string path,
            Transform parent,
            string name,
            Vector3 scale)
        {
            GameObject source =
                AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null)
                throw new InvalidOperationException($"Legacy model was not found at {path}.");
            GameObject instance =
                PrefabUtility.InstantiatePrefab(
                    source,
                    parent) as GameObject;
            if (instance == null)
                throw new InvalidOperationException($"Failed to instantiate {path}.");
            instance.name = name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = scale;
            return instance;
        }

        private static Transform ResolveVesselRoot(
            Scene scene,
            MeshFilter outer,
            MeshFilter inner)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "VesselRoot")
                    return root.transform;
            }

            if (outer != null &&
                inner != null &&
                outer.transform.parent != null &&
                outer.transform.parent == inner.transform.parent)
            {
                return outer.transform.parent;
            }

            throw new InvalidOperationException(
                "SingLayer has no unambiguous VesselRoot for hybrid-vessel integration.");
        }

        private static Transform FindHybridRoot(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform direct = root.transform.Find(HybridRootName);
                if (direct != null)
                    return direct;
                if (root.name == HybridRootName)
                    return root.transform;
            }

            throw new InvalidOperationException(
                $"Scene {scene.path} does not contain {HybridRootName}.");
        }

        private static void AddReplaceableRoot(
            MeshFilter filter,
            Transform vesselRoot,
            HashSet<GameObject> roots)
        {
            if (filter == null || filter.transform.IsChildOf(vesselRoot) == false)
                return;

            GameObject prefabRoot =
                PrefabUtility.GetOutermostPrefabInstanceRoot(
                    filter.gameObject);
            GameObject candidate =
                prefabRoot != null
                    ? prefabRoot
                    : filter.gameObject;
            if (candidate.transform == vesselRoot)
                return;
            roots.Add(candidate);
        }

        private static MeshFilter RequireMeshFilter(
            Transform root,
            string childName)
        {
            Transform child = root.Find(childName);
            MeshFilter filter =
                child != null
                    ? child.GetComponent<MeshFilter>()
                    : null;
            if (filter == null || filter.sharedMesh == null)
            {
                throw new InvalidOperationException(
                    $"Hybrid vessel child {childName} has no valid MeshFilter.");
            }
            return filter;
        }

        private static void ConfigureRendererSlice(
            MeshFilter filter,
            ClothSimulationDriverDoubleLayer driver,
            ClothSimulationDriverDoubleLayer.VesselLayer layer)
        {
            ClothRendererSlice slice =
                filter.GetComponent<ClothRendererSlice>();
            if (slice == null)
                slice = filter.gameObject.AddComponent<ClothRendererSlice>();
            slice.simulation = driver;
            slice.layer = layer;
            slice.recalcBounds = true;
            EditorUtility.SetDirty(slice);
        }

        private static T FindOnlySceneComponent<T>(
            Scene scene,
            string label)
            where T : Component
        {
            T found = null;
            T[] candidates =
                Resources.FindObjectsOfTypeAll<T>();
            for (int i = 0; i < candidates.Length; i++)
            {
                T candidate = candidates[i];
                if (candidate == null ||
                    candidate.gameObject.scene != scene)
                {
                    continue;
                }

                if (found != null)
                {
                    throw new InvalidOperationException(
                        $"Scene {scene.path} contains multiple {label} components.");
                }
                found = candidate;
            }

            if (found == null)
            {
                throw new InvalidOperationException(
                    $"Scene {scene.path} contains no {label} component.");
            }
            return found;
        }

        private static void ValidateScene(
            Scene scene,
            ClothSimulationDriverDoubleLayer driver,
            GameObject hybrid)
        {
            if (hybrid == null || hybrid.scene != scene)
                throw new InvalidOperationException("Hybrid vessel instance is not part of SingLayer.");

            MeshFilter outer =
                RequireMeshFilter(hybrid.transform, "SimulationOuter");
            MeshFilter inner =
                RequireMeshFilter(hybrid.transform, "SimulationInner");
            if (driver.outerMeshFilter != outer ||
                driver.innerMeshFilter != inner)
            {
                throw new InvalidOperationException(
                    "Double-layer driver is not bound to the hybrid simulation pair.");
            }

            ValidateSimulationPair(outer, inner);
            ValidateRendererSlice(
                outer,
                driver,
                ClothSimulationDriverDoubleLayer.VesselLayer.Outer);
            ValidateRendererSlice(
                inner,
                driver,
                ClothSimulationDriverDoubleLayer.VesselLayer.Inner);
            ValidateStaticPresentation(hybrid.transform);
            ValidateProcedureBindings(scene, driver);

            SerializedObject serializedDriver =
                new SerializedObject(driver);
            SerializedProperty cloneAtRuntime =
                serializedDriver.FindProperty("cloneMeshAtRuntime");
            if (cloneAtRuntime == null || !cloneAtRuntime.boolValue)
            {
                throw new InvalidOperationException(
                    "Hybrid simulation requires cloneMeshAtRuntime=true.");
            }

            Debug.Log(
                $"[HybridVesselIntegration] validation passed " +
                $"scene={scene.path} outer={outer.sharedMesh.name} " +
                $"inner={inner.sharedMesh.name} boundaryEdges=48/48 " +
                $"cloneMeshAtRuntime=1");
        }

        private static void ValidateSimulationPair(
            MeshFilter outer,
            MeshFilter inner)
        {
            Mesh outerMesh = outer.sharedMesh;
            Mesh innerMesh = inner.sharedMesh;
            if (outerMesh.vertexCount != innerMesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Hybrid Outer/Inner vertex counts do not match.");
            }

            int[] outerTriangles = outerMesh.triangles;
            int[] innerTriangles = innerMesh.triangles;
            if (outerTriangles.Length != innerTriangles.Length)
            {
                throw new InvalidOperationException(
                    "Hybrid Outer/Inner triangle counts do not match.");
            }
            for (int i = 0; i < outerTriangles.Length; i++)
            {
                if (outerTriangles[i] != innerTriangles[i])
                {
                    throw new InvalidOperationException(
                        $"Hybrid Outer/Inner topology differs at triangle index {i}.");
                }
            }

            Vector2[] outerUv = outerMesh.uv;
            Vector2[] innerUv = innerMesh.uv;
            if (outerUv == null ||
                innerUv == null ||
                outerUv.Length != outerMesh.vertexCount ||
                innerUv.Length != innerMesh.vertexCount)
            {
                throw new InvalidOperationException(
                    "Hybrid Outer/Inner longitudinal material coordinates are missing.");
            }
            float maxUvDelta = 0f;
            float minV = float.PositiveInfinity;
            float maxV = float.NegativeInfinity;
            for (int i = 0; i < outerUv.Length; i++)
            {
                maxUvDelta = Mathf.Max(
                    maxUvDelta,
                    Vector2.Distance(outerUv[i], innerUv[i]));
                minV = Mathf.Min(minV, outerUv[i].y);
                maxV = Mathf.Max(maxV, outerUv[i].y);
            }
            if (maxUvDelta > 1e-4f ||
                maxV - minV <= 1e-6f)
            {
                throw new InvalidOperationException(
                    $"Hybrid material-coordinate identity failed. " +
                    $"maxUvDelta={maxUvDelta:E3} " +
                    $"vRange=({minV:F4},{maxV:F4}).");
            }

            int outerBoundary = CountBoundaryEdges(outerTriangles);
            int innerBoundary = CountBoundaryEdges(innerTriangles);
            if (outerBoundary != 48 || innerBoundary != 48)
            {
                throw new InvalidOperationException(
                    $"Hybrid simulation boundary contract failed: " +
                    $"Outer={outerBoundary}, Inner={innerBoundary}, expected=48.");
            }

            if (outer.transform.localPosition != inner.transform.localPosition ||
                outer.transform.localRotation != inner.transform.localRotation ||
                outer.transform.localScale != inner.transform.localScale)
            {
                throw new InvalidOperationException(
                    "Hybrid Outer/Inner transforms are not aligned.");
            }

            Debug.Log(
                $"[HybridVesselIntegration][MaterialIdentity] " +
                $"vertices={outerUv.Length} maxUvDelta={maxUvDelta:E3} " +
                $"vRange=({minV:F4},{maxV:F4}) " +
                $"triangleIndexMismatch=0");
        }

        private static int CountBoundaryEdges(int[] triangles)
        {
            var multiplicity = new Dictionary<ulong, int>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                AddEdge(multiplicity, triangles[i], triangles[i + 1]);
                AddEdge(multiplicity, triangles[i + 1], triangles[i + 2]);
                AddEdge(multiplicity, triangles[i + 2], triangles[i]);
            }

            int boundary = 0;
            foreach (KeyValuePair<ulong, int> edge in multiplicity)
            {
                if (edge.Value == 1)
                    boundary++;
                else if (edge.Value != 2)
                {
                    throw new InvalidOperationException(
                        $"Hybrid simulation has edge multiplicity {edge.Value}.");
                }
            }
            return boundary;
        }

        private static void AddEdge(
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

        private static void ValidateRendererSlice(
            MeshFilter filter,
            ClothSimulationDriverDoubleLayer driver,
            ClothSimulationDriverDoubleLayer.VesselLayer layer)
        {
            ClothRendererSlice slice =
                filter.GetComponent<ClothRendererSlice>();
            if (slice == null ||
                slice.simulation != driver ||
                slice.layer != layer ||
                !slice.recalcBounds)
            {
                throw new InvalidOperationException(
                    $"Renderer slice binding is invalid for {filter.name}.");
            }
        }

        private static void ValidateStaticPresentation(Transform hybrid)
        {
            string[] staticRoots =
            {
                "StaticTransition",
                "StaticBranchAppearance",
                "TissueSurface",
                "SurgicalTableSupport",
            };
            for (int i = 0; i < staticRoots.Length; i++)
            {
                Transform root = hybrid.Find(staticRoots[i]);
                if (root == null)
                {
                    throw new InvalidOperationException(
                        $"Hybrid presentation child {staticRoots[i]} is missing.");
                }
                if (root.GetComponentInChildren<ClothRendererSlice>(true) != null ||
                    root.GetComponentInChildren<Collider>(true) != null ||
                    root.GetComponentInChildren<Rigidbody>(true) != null ||
                    root.GetComponentInChildren<Cloth>(true) != null)
                {
                    throw new InvalidOperationException(
                        $"Hybrid presentation child {staticRoots[i]} entered a simulation or collision path.");
                }
            }
        }

        private static void ValidateProcedureBindings(
            Scene scene,
            ClothSimulationDriverDoubleLayer driver)
        {
            AutoSutureRuntimeController autoSuture =
                FindOnlySceneComponent<AutoSutureRuntimeController>(
                    scene,
                    "auto-suture controller");
            if (autoSuture.driver != driver ||
                autoSuture.needleRig == null ||
                autoSuture.needleDriver == null)
            {
                throw new InvalidOperationException(
                    "Auto-suture references are not closed after hybrid integration.");
            }
            Transform hybrid = FindHybridRoot(scene);
            MeshFilter tissueSurface =
                RequireMeshFilter(
                    hybrid,
                    "TissueSurface");
            if (!autoSuture.avoidTissueBackground ||
                autoSuture.tissueAvoidanceSurface != tissueSurface ||
                autoSuture.tissueTravelMargin < 0f ||
                autoSuture.tissueEndpointFreeNormalDot <= 0f)
            {
                throw new InvalidOperationException(
                    "Auto-suture tissue-background avoidance is not bound to the hybrid TissueSurface.");
            }
            if (!autoSuture.safeTravelAroundVessel ||
                autoSuture.safeTravelClearance <= 1e-6f ||
                !autoSuture.useOrthographicPlanCamera ||
                !autoSuture.useObliqueProcedureCamera ||
                !autoSuture.showPlanOverviewCamera ||
                autoSuture.procedureCameraObliqueAngle <= 0f)
            {
                throw new InvalidOperationException(
                    "Hybrid procedure must use actual-plane exterior travel " +
                    "and the guide-derived orthographic plan camera.");
            }

            ClothDebugGizmos gizmos =
                FindOnlySceneComponent<ClothDebugGizmos>(
                    scene,
                    "cloth debug gizmo");
            SerializedObject serializedGizmos =
                new SerializedObject(gizmos);
            SerializedProperty simulationDouble =
                serializedGizmos.FindProperty("simulationDouble");
            if (simulationDouble == null ||
                simulationDouble.objectReferenceValue != driver)
            {
                throw new InvalidOperationException(
                    "ClothDebugGizmos is not bound to the active double-layer driver.");
            }

            NeedleSceneControlRig needleRig =
                FindOnlySceneComponent<NeedleSceneControlRig>(
                    scene,
                    "needle scene control rig");
            SerializedObject serializedRig =
                new SerializedObject(needleRig);
            SerializedProperty targetNeedle =
                serializedRig.FindProperty("targetNeedle");
            SerializedProperty needleDriver =
                serializedRig.FindProperty("needleDriver");
            if (targetNeedle == null ||
                targetNeedle.objectReferenceValue == null ||
                needleDriver == null ||
                needleDriver.objectReferenceValue == null)
            {
                throw new InvalidOperationException(
                    "Needle rig references are not closed after hybrid integration.");
            }
        }
    }
}
