using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PBDLearn
{
    internal static class DoubleLayerIncisionGenerator
    {
        // Regression baseline: keep structured rows as the shape oracle, but do not
        // use the experimental structured-cell remesher as the success path.
        private static readonly bool UseTaperClipRebuild = true;
        private static readonly bool UseStructuredCellRebuild = false;

        internal enum LayerKind
        {
            Outer = 0,
            Inner = 1,
        }

        internal enum DebugTriangleKind
        {
            Patch = 0,
            Excision = 1,
        }

        internal struct Preset
        {
            public float normalizedZ;
            public float circumferenceAngleDeg;
            public float directionAngleDeg;
            public float lengthWorld;
            public float openWidthWorld;
            public bool circumferentialSeparationCut;
            public float separationDistanceWorld;
            public int sampleCount;
            public float surfaceLiftWorld;
            public Vector3 centerOffsetLocal;
        }

        internal struct Options
        {
            public int subdivisionPasses;
            public float pathSampleSpacingWorld;
            public float pathSnapPenalty;
            public int maxAnchorCount;
            public int maxStationCount;
        }

        internal sealed class GenerateResult
        {
            public bool success;
            public string failStage;
            public string failReason;
            public readonly List<Vector3> targetStrokeWorld = new List<Vector3>();
            public LayerResult outer;
            public LayerResult inner;
        }

        internal sealed class LayerResult
        {
            public bool success;
            public bool isCircumferentialSeparationCut;
            public int layer;
            public string failStage;
            public string failReason;
            public Mesh mesh;
            public float averageEdgeLength;
            public float masterLength;
            public int removedTriangleCount;
            public int patchTriangleCount;
            public int keptTriangleCount;
            public int leftBoundaryCount;
            public int rightBoundaryCount;
            public int openBoundaryEdgeCount;
            public bool usedBoundaryVertexFallback;
            public bool usedFallbackPath;
            public int patchComponentCount;
            public int excisionComponentCount;
            public int stripRowCount;
            public int supportQuadCount;
            public int excisionQuadCount;
            public int boundaryContinuityViolationCount;
            public float maxPatchTriangleAspect;
            public float averageRealizedOpenWidth;
            public float endTaperLengthWorld;
            public int guidePointCount;
            public int denseGuidePointCount;
            public float guideSampleSpacingWorld;
            public int centerCurvePointCount;
            public float centerCurveLength;
            public int canonicalStationCount;
            public float stationSpacingWorld;
            public float patchHalfWidthWorld;
            public float patchSPadWorld;
            public float patchNormalTolWorld;
            public int patchCandidateTriangleCount;
            public int patchSeedTriangleCount;
            public int patchBestComponentTriangleCount;
            public int patchBestComponentSeedHitCount;
            public float supportOffsetWorld;
            public float supportOffsetBaseWorld;
            public float supportOffsetCapWorld;
            public float supportStripMarginWorld;
            public bool usedReferenceStripRows;
            public int referenceStripRowCount;
            public int remeshKeptPolygonCount;
            public int remeshRemovedPolygonCount;
            public int remeshKeptPolygonVertexCount;
            public int remeshRemovedPolygonVertexCount;
            public int taggedBoundaryEdgeCount;
            public int leftTaggedBoundaryEdgeCount;
            public int rightTaggedBoundaryEdgeCount;
            public int leftBoundaryComponentCount;
            public int rightBoundaryComponentCount;
            public string leftBoundaryComponentSizes;
            public string rightBoundaryComponentSizes;
            public int leftBoundaryComponentVertexCount;
            public int rightBoundaryComponentVertexCount;
            public int leftBoundaryOrderedVertexCount;
            public int rightBoundaryOrderedVertexCount;
            public int leftBoundaryDegree1VertexCount;
            public int rightBoundaryDegree1VertexCount;
            public int leftBoundaryDegree2VertexCount;
            public int rightBoundaryDegree2VertexCount;
            public int leftBoundaryBranchVertexCount;
            public int rightBoundaryBranchVertexCount;
            public float minPatchTriangleArea;
            public int triangulationWindingMismatchCount;
            public int degeneratePatchTriangleCount;
            public int overAspectPatchTriangleCount;
            public int auditedPatchTriangleCount;
            public int outsideQualityTriangleCount;
            public int outsideQualityWindingMismatchCount;
            public int outsideQualityOverAspectCount;
            public float outsideQualityMaxAspect;
            public int supportQualityTriangleCount;
            public int supportQualityWindingMismatchCount;
            public int supportQualityOverAspectCount;
            public float supportQualityMaxAspect;
            public int remeshedVertexCount;
            public int remeshedTriangleCount;
            public int attemptedStripRowCount;
            public int collapsedStripRowCount;
            public int repeatedStripRowCount;
            public int crossedStripRowCount;
            public int tipStripRowCount;
            public bool keptStartTipRow;
            public bool keptEndTipRow;
            public float minStructuredBoundaryWidth;
            public float minStructuredSupportWidth;
            public float maxStructuredBoundaryJump;
            public float minLeftStructuredGap;
            public float minCenterStructuredGap;
            public float minRightStructuredGap;
            public int leftOrderViolationCount;
            public int centerOrderViolationCount;
            public int rightOrderViolationCount;
            public string crossedRowPreview;
            public int leftSupportAnchorCount;
            public int leftBoundaryAnchorCount;
            public int rightBoundaryAnchorCount;
            public int rightSupportAnchorCount;
            public int leftSupportPathVertexCount;
            public int leftBoundaryPathVertexCount;
            public int rightBoundaryPathVertexCount;
            public int rightSupportPathVertexCount;
            public int leftSupportSampleCount;
            public int leftBoundarySampleCount;
            public int rightBoundarySampleCount;
            public int rightSupportSampleCount;
            public int rowDefinitionMismatchCount;
            public string rowDefinitionSummary;
            public string taperClipInputSummary;
            public int stripCoveredTriangleCount;
            public int supportCoveredTriangleCount;
            public int excisionCoveredTriangleCount;
            public int patchOutsideStripTriangleCount;
            public int changedOutsideStripTriangleCount;
            public int clippedPatchTriangleCount;
            public int preservedOutsideFragmentCount;
            public int discardedSupportFragmentCount;
            public int discardedExcisionFragmentCount;
            public bool usedLatticePartition;
            public bool visibleBoundaryBuiltFromTaggedEdges;
            public int supportPartitionPolygonCount;
            public int excisionPartitionPolygonCount;
            public int supportMergedPolygonCount;
            public int excisionMergedPolygonCount;
            public int interfaceNormalizationSourceEdgeKeyCount;
            public int interfaceNormalizationSourceEdgeInsertCount;
            public int interfaceNormalizationBoundarySegmentKeyCount;
            public int interfaceNormalizationBoundaryInsertCount;
            public int interfaceNormalizationPolygonsTouchedCount;
            public int interfaceScanSegmentCount;
            public int interfaceScanMatchedOutsideSupportCount;
            public int interfaceScanMatchedSupportExcisionCount;
            public int interfaceScanOutsideExcisionCount;
            public int interfaceScanSingleSideCount;
            public int interfaceScanMultiUseCount;
            public int interfaceScanTripleUseCount;
            public int interfaceHardMismatchEdgeCount;
            public int interfaceAuditMismatchEdgeCount;
            public string interfaceScanByFlagSummary;
            public string interfaceScanByRowSummary;
            public string interfaceScanOwnershipSummary;
            public string interfaceFeCategorySummary;
            public string interfaceFeActualMaskSummary;
            public string interfaceScanDetailSummary;
            public string tipOwnershipSummary;
            public string tipInterfaceKeyCompareSummary;
            public string startTipWedgeHelperSummary;
            public string startTipWedgeSourceNormSummary;
            public string startTipWedgeBoundaryNormSummary;
            public string startTipWedgeCanonicalSummary;
            public string startTipWedgePostCanonicalSourceNormSummary;
            public string startTipWedgePostCanonicalBoundaryNormSummary;
            public string endTipEarlyHelperProbeSummary;
            public string endTipOrdinaryAuditSummary;
            public int interfaceRepairNormalizedSegmentCount;
            public int interfaceRepairVertexReuseCount;
            public int interfaceRepairVertexInsertCount;
            public int interfaceRepairOutsideTouchedCount;
            public int interfaceRepairSupportTouchedCount;
            public int interfaceRepairExcisionTouchedCount;
            public int interfaceRepairResidualMismatchCount;
            public int cellMergeFallbackCount;
            public int cellTopologyCellCount;
            public int cellTopologyTracedCount;
            public int cellMembershipScanCellCount;
            public int cellMembershipScanMismatchCount;
            public string cellMembershipScanSummary;
            public int failedCellRowIndex;
            public int failedCellColumnIndex;
            public int failedCellBoundaryEdgeCount;
            public int failedCellBoundaryVertexCount;
            public int failedCellLoopCount;
            public int failedCellDegree1VertexCount;
            public int failedCellDegree2VertexCount;
            public int failedCellBranchVertexCount;
            public bool failedCellSelfIntersection;
            public string failedCellSemanticCoverage;
            public string failedCellReason;
            public int failedCellDefinitionMismatchCount;
            public string failedCellDefinitionSummary;
            public string failedCellMembershipSummary;
            public string failedCellSideTopologySummary;
            public string failedCellCornerResolutionSummary;
            public string failedCellOpenEndpointSummary;
            public string failedCellBranchSummary;
            public int cellPolygonScanCount;
            public int cellPolygonIssueCount;
            public int cellPolygonConcaveCount;
            public int cellPolygonSelfIntersectionCount;
            public int cellPolygonRepeatedVertexCount;
            public int cellPolygonTinyEdgeCount;
            public int cellPolygonTinyAngleCount;
            public string cellPolygonScanSummary;
            public string failedCellPolygonSummary;
            public int interfaceMismatchEdgeCount;
            public string failedInterfaceEdgeSummary;
            public int outsidePartitionTriangleCount;
            public int supportPartitionTriangleCount;
            public int droppedDegeneratePartitionPolygonCount;
            public string boundarySourceSummary;
            public int keptOutsideTriangleCount;
            public int keptSupportTriangleCount;
            public string syncSummary;
            public string projectionAlignSummary;
            public float leftBoundaryPolylineLength;
            public float rightBoundaryPolylineLength;
            public float leftBoundarySMin;
            public float leftBoundarySMax;
            public float rightBoundarySMin;
            public float rightBoundarySMax;
            public readonly List<int> masterPathVertexIndices = new List<int>();
            public readonly List<Vector3> masterChainWorld = new List<Vector3>();
            public readonly List<Vector3> centerCurveWorld = new List<Vector3>();
            public readonly List<Vector3> centerlineWorld = new List<Vector3>();
            public readonly List<Vector3> leftBoundaryWorld = new List<Vector3>();
            public readonly List<Vector3> rightBoundaryWorld = new List<Vector3>();
            public readonly List<Vector3> precommitRowCentersWorld = new List<Vector3>();
            public readonly List<Vector3> precommitLeftBoundaryWorld = new List<Vector3>();
            public readonly List<Vector3> precommitRightBoundaryWorld = new List<Vector3>();
            public readonly List<Vector3> precommitLeftSupportWorld = new List<Vector3>();
            public readonly List<Vector3> precommitRightSupportWorld = new List<Vector3>();
            public readonly List<Vector3> precommitFailedOwnershipEdgesWorld = new List<Vector3>();
            public readonly List<string> precommitFailedOwnershipEdgeLabels = new List<string>();
            public readonly List<StationResult> stations = new List<StationResult>();
            public readonly List<DebugTriangle> debugTriangles = new List<DebugTriangle>();
            internal readonly List<StructuredStripReferenceRow> structuredStripReferenceRows = new List<StructuredStripReferenceRow>();
        }

        internal struct StationResult
        {
            public float s;
            public float strokeT;
            public float targetOpen;
            public Vector3 centerWorld;
            public Vector3 tangentWorld;
            public Vector3 normalWorld;
            public Vector3 binormalWorld;
            public Vector3 leftWorld;
            public Vector3 rightWorld;
            public int leftVertexIndex;
            public int rightVertexIndex;
        }

        internal struct DebugTriangle
        {
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;
            public int layer;
            public DebugTriangleKind kind;
        }

        private struct AxisFrame
        {
            public Bounds bounds;
            public Vector3 centerOffsetLocal;
            public List<Vector3> stationCentersLocal;
            public List<Vector3> stationTangentsLocal;
            public List<Vector3> stationRightLocal;
            public List<Vector3> stationUpLocal;
            public List<float> stationRadiiLocal;
            public List<float> stationDistancesLocal;
            public float totalStationDistanceLocal;
            public string stationSourceSummary;
            public bool hasSweepStations;
        }

        private struct PolylineProjection
        {
            public Vector3 point;
            public Vector3 tangent;
            public float s;
            public float tOnSegment;
            public int segmentIndex;
            public float distanceSq;
        }

        private struct FrameSample
        {
            public Vector3 center;
            public Vector3 tangent;
            public Vector3 normal;
            public Vector3 binormal;
            public float s;
            public float strokeT;
            public float targetOpen;
        }

        private struct GraphEdge
        {
            public int to;
            public float length;
            public Vector3 midpointWorld;
            public Vector3 directionWorld;
        }

        private struct TriangleEval
        {
            public bool patch;
            public bool seed;
            public float signedLateral;
            public float s;
        }

        private struct BoundaryEdgeState
        {
            public int a;
            public int b;
            public int removedCount;
            public int keptCount;
            public int provenanceCount;
            public string provenance;
        }

        private struct BoundaryVertexSample
        {
            public int vertexIndex;
            public float s;
            public Vector3 world;
        }

        private enum ClipScalarAxis
        {
            S = 0,
            U = 1,
        }

        private enum ClipBoundaryKind
        {
            None = 0,
            Start = 1,
            End = 2,
            Left = 3,
            Right = 4,
            Row = 5,
            LeftSupport = 6,
            RightSupport = 7,
        }

        [Flags]
        private enum ClipBoundaryFlags
        {
            None = 0,
            Start = 1 << 0,
            End = 1 << 1,
            Left = 1 << 2,
            Right = 1 << 3,
            LeftSupport = 1 << 4,
            RightSupport = 1 << 5,
            Row = 1 << 6,
        }

        private struct SurfaceCoord
        {
            public FrameSample frame;
            public float s;
            public float u;
            public float v;
            public float distanceToCenter;
        }

        private struct ClipVertex
        {
            public Vector3 local;
            public Vector3 world;
            public Vector2 uv;
            public Vector4 tangent;
            public Color color;
            public float s;
            public float strokeT;
            public float u;
            public float v;
            public float targetOpen;
            public float distanceToCenter;
            public int materializedIndex;
            public int supportA;
            public int supportB;
            public ClipBoundaryFlags boundaryFlags;
        }

        private struct PolygonTriangulationContext
        {
            public bool useLocalPlane;
            public Vector3 planeNormal;
            public float signedArea;
        }

        private struct VertexWeldKey : IEquatable<VertexWeldKey>
        {
            public int x;
            public int y;
            public int z;

            public bool Equals(VertexWeldKey other)
            {
                return x == other.x && y == other.y && z == other.z;
            }

            public override bool Equals(object obj)
            {
                return obj is VertexWeldKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = x;
                    hash = (hash * 397) ^ y;
                    hash = (hash * 397) ^ z;
                    return hash;
                }
            }
        }

        private struct TaggedBoundaryEdge
        {
            public int a;
            public int b;
            public ClipBoundaryFlags flags;
        }

        private struct TriangleProjection
        {
            public int triangleIndex;
            public Vector3 point;
            public float distanceSq;
        }

        private sealed class PatchSelectionResult
        {
            public bool success;
            public string failReason;
            public int componentCount;
            public int validTriangleCount;
            public int seedTriangleCount;
            public int bestComponentTriangleCount;
            public int bestSeedHits;
            public float patchHalfWidth;
            public float sPad;
            public float normalTol;
            public readonly HashSet<int> patchTriangles = new HashSet<int>();
        }

        private struct RemeshDiagnostics
        {
            public float supportOffset;
            public float supportOffsetBase;
            public float supportOffsetCap;
            public float supportStripMargin;
            public bool usedReferenceStripRows;
            public int referenceStripRowCount;
            public int keptPolygonCount;
            public int removedPolygonCount;
            public int keptPolygonVertexCount;
            public int removedPolygonVertexCount;
            public int taggedBoundaryEdgeCount;
            public int leftTaggedEdgeCount;
            public int rightTaggedEdgeCount;
            public int leftBoundaryComponentCount;
            public int rightBoundaryComponentCount;
            public string leftBoundaryComponentSizes;
            public string rightBoundaryComponentSizes;
            public int leftBoundaryComponentVertexCount;
            public int rightBoundaryComponentVertexCount;
            public int leftBoundaryOrderedVertexCount;
            public int rightBoundaryOrderedVertexCount;
            public int leftBoundaryDegree1VertexCount;
            public int rightBoundaryDegree1VertexCount;
            public int leftBoundaryDegree2VertexCount;
            public int rightBoundaryDegree2VertexCount;
            public int leftBoundaryBranchVertexCount;
            public int rightBoundaryBranchVertexCount;
            public float minPatchArea;
            public int triangulationWindingMismatchCount;
            public int degeneratePatchTriangleCount;
            public int overAspectPatchTriangleCount;
            public int auditedPatchTriangleCount;
            public int outsideQualityTriangleCount;
            public int outsideQualityWindingMismatchCount;
            public int outsideQualityOverAspectCount;
            public float outsideQualityMaxAspect;
            public int supportQualityTriangleCount;
            public int supportQualityWindingMismatchCount;
            public int supportQualityOverAspectCount;
            public float supportQualityMaxAspect;
            public int remeshedVertexCount;
            public int remeshedTriangleCount;
            public float patchHalfWidth;
            public float patchSPad;
            public float patchNormalTol;
            public int patchCandidateTriangleCount;
            public int patchSeedTriangleCount;
            public int patchBestComponentTriangleCount;
            public int patchBestComponentSeedHits;
            public int attemptedRowCount;
            public int keptRowCount;
            public int collapsedRowCount;
            public int repeatedRowCount;
            public int crossedRowCount;
            public int tipRowCount;
            public bool keptStartTipRow;
            public bool keptEndTipRow;
            public float minBoundaryWidth;
            public float minSupportWidth;
            public float maxBoundaryJump;
            public float minLeftGap;
            public float minCenterGap;
            public float minRightGap;
            public int leftOrderViolationCount;
            public int centerOrderViolationCount;
            public int rightOrderViolationCount;
            public string crossedRowPreview;
            public int leftSupportAnchorCount;
            public int leftBoundaryAnchorCount;
            public int rightBoundaryAnchorCount;
            public int rightSupportAnchorCount;
            public int leftSupportPathVertexCount;
            public int leftBoundaryPathVertexCount;
            public int rightBoundaryPathVertexCount;
            public int rightSupportPathVertexCount;
            public int leftSupportSampleCount;
            public int leftBoundarySampleCount;
            public int rightBoundarySampleCount;
            public int rightSupportSampleCount;
            public int rowDefinitionMismatchCount;
            public string rowDefinitionSummary;
            public string taperClipInputSummary;
            public int stripCoveredTriangleCount;
            public int supportCoveredTriangleCount;
            public int excisionCoveredTriangleCount;
            public int patchOutsideStripTriangleCount;
            public int changedOutsideStripTriangleCount;
            public int clippedPatchTriangleCount;
            public int preservedOutsideFragmentCount;
            public int discardedSupportFragmentCount;
            public int discardedExcisionFragmentCount;
            public bool usedLatticePartition;
            public bool visibleBoundaryBuiltFromTaggedEdges;
            public int supportPartitionPolygonCount;
            public int excisionPartitionPolygonCount;
            public int supportMergedPolygonCount;
            public int excisionMergedPolygonCount;
            public int interfaceNormalizationSourceEdgeKeyCount;
            public int interfaceNormalizationSourceEdgeInsertCount;
            public int interfaceNormalizationBoundarySegmentKeyCount;
            public int interfaceNormalizationBoundaryInsertCount;
            public int interfaceNormalizationPolygonsTouchedCount;
            public int interfaceScanSegmentCount;
            public int interfaceScanMatchedOutsideSupportCount;
            public int interfaceScanMatchedSupportExcisionCount;
            public int interfaceScanOutsideExcisionCount;
            public int interfaceScanSingleSideCount;
            public int interfaceScanMultiUseCount;
            public int interfaceScanTripleUseCount;
            public int interfaceHardMismatchEdgeCount;
            public int interfaceAuditMismatchEdgeCount;
            public string interfaceScanByFlagSummary;
            public string interfaceScanByRowSummary;
            public string interfaceScanOwnershipSummary;
            public string interfaceFeCategorySummary;
            public string interfaceFeActualMaskSummary;
            public string interfaceScanDetailSummary;
            public string tipOwnershipSummary;
            public string tipInterfaceKeyCompareSummary;
            public string startTipWedgeHelperSummary;
            public string startTipWedgeSourceNormSummary;
            public string startTipWedgeBoundaryNormSummary;
            public string startTipWedgeCanonicalSummary;
            public string startTipWedgePostCanonicalSourceNormSummary;
            public string startTipWedgePostCanonicalBoundaryNormSummary;
            public string endTipEarlyHelperProbeSummary;
            public string endTipOrdinaryAuditSummary;
            public int interfaceRepairNormalizedSegmentCount;
            public int interfaceRepairVertexReuseCount;
            public int interfaceRepairVertexInsertCount;
            public int interfaceRepairOutsideTouchedCount;
            public int interfaceRepairSupportTouchedCount;
            public int interfaceRepairExcisionTouchedCount;
            public int interfaceRepairResidualMismatchCount;
            public int cellMergeFallbackCount;
            public int cellTopologyCellCount;
            public int cellTopologyTracedCount;
            public int cellMembershipScanCellCount;
            public int cellMembershipScanMismatchCount;
            public string cellMembershipScanSummary;
            public int failedCellRowIndex;
            public int failedCellColumnIndex;
            public int failedCellBoundaryEdgeCount;
            public int failedCellBoundaryVertexCount;
            public int failedCellLoopCount;
            public int failedCellDegree1VertexCount;
            public int failedCellDegree2VertexCount;
            public int failedCellBranchVertexCount;
            public bool failedCellSelfIntersection;
            public string failedCellSemanticCoverage;
            public string failedCellReason;
            public int failedCellDefinitionMismatchCount;
            public string failedCellDefinitionSummary;
            public string failedCellMembershipSummary;
            public string failedCellSideTopologySummary;
            public string failedCellCornerResolutionSummary;
            public string failedCellOpenEndpointSummary;
            public string failedCellBranchSummary;
            public int cellPolygonScanCount;
            public int cellPolygonIssueCount;
            public int cellPolygonConcaveCount;
            public int cellPolygonSelfIntersectionCount;
            public int cellPolygonRepeatedVertexCount;
            public int cellPolygonTinyEdgeCount;
            public int cellPolygonTinyAngleCount;
            public string cellPolygonScanSummary;
            public string failedCellPolygonSummary;
            public int interfaceMismatchEdgeCount;
            public string failedInterfaceEdgeSummary;
            public int outsidePartitionTriangleCount;
            public int supportPartitionTriangleCount;
            public int droppedDegeneratePartitionPolygonCount;
            public string boundarySourceSummary;
            public int keptOutsideTriangleCount;
            public int keptSupportTriangleCount;
            public string syncSummary;
            public string projectionAlignSummary;
            public float leftBoundaryPolylineLength;
            public float rightBoundaryPolylineLength;
            public float leftBoundarySMin;
            public float leftBoundarySMax;
            public float rightBoundarySMin;
            public float rightBoundarySMax;
            public List<Vector3> precommitRowCentersWorld;
            public List<Vector3> precommitLeftBoundaryWorld;
            public List<Vector3> precommitRightBoundaryWorld;
            public List<Vector3> precommitLeftSupportWorld;
            public List<Vector3> precommitRightSupportWorld;
            public List<Vector3> precommitFailedOwnershipEdgesWorld;
            public List<string> precommitFailedOwnershipEdgeLabels;
        }

        private struct BoundaryBuildDiagnostics
        {
            public int edgeCount;
            public int componentCount;
            public string componentSizes;
            public int preBridgeComponentCount;
            public string preBridgeComponentSizes;
            public int railBridgeEdgeCount;
            public bool railFallbackUsed;
            public int railFallbackSampleCount;
            public int selectedComponentVertexCount;
            public int orderedVertexCount;
            public int degree1VertexCount;
            public int degree2VertexCount;
            public int branchVertexCount;
            public string failReason;
        }

        private struct BoundaryOrderingResult
        {
            public List<BoundaryVertexSample> samples;
            public int componentVertexCount;
            public int orderedVertexCount;
            public int degree1VertexCount;
            public int degree2VertexCount;
            public int branchVertexCount;
        }

        private struct PrecommitPolylineStats
        {
            public int pointCount;
            public int segmentCount;
            public float totalLength;
            public float maxJump;
            public int componentCount;
        }

        private struct PrecommitLinePairStats
        {
            public int segmentCount;
            public float totalLength;
        }

        private struct StructuredCellTopologyDiagnostics
        {
            public int failedRowIndex;
            public int failedColumnIndex;
            public int failedBoundaryEdgeCount;
            public int failedBoundaryVertexCount;
            public int failedLoopCount;
            public int failedDegree1VertexCount;
            public int failedDegree2VertexCount;
            public int failedBranchVertexCount;
            public bool failedSelfIntersection;
            public string failedSemanticCoverage;
            public string failedReason;
            public int failedDefinitionMismatchCount;
            public string failedDefinitionSummary;
            public string failedMembershipSummary;
            public string failedSideTopologySummary;
            public string failedCornerResolutionSummary;
            public string failedOpenEndpointSummary;
            public string failedBranchSummary;
        }

        private struct PatchQualityBucketStats
        {
            public int auditedTriangleCount;
            public int overAspectTriangleCount;
            public float maxAspectRatio;
        }

        private struct RailBuildDiagnostics
        {
            public int anchorCount;
            public int pathVertexCount;
            public int sampleCount;
        }

        private struct StructuredStripBuildDiagnostics
        {
            public int attemptedRowCount;
            public int keptRowCount;
            public int collapsedRowCount;
            public int repeatedRowCount;
            public int crossedRowCount;
            public int tipRowCount;
            public bool keptStartTipRow;
            public bool keptEndTipRow;
            public float minBoundaryWidth;
            public float minSupportWidth;
            public float maxBoundaryJump;
            public float minLeftGap;
            public float minCenterGap;
            public float minRightGap;
            public int leftOrderViolationCount;
            public int centerOrderViolationCount;
            public int rightOrderViolationCount;
            public string crossedRowPreview;
            public RailBuildDiagnostics leftSupportRail;
            public RailBuildDiagnostics leftBoundaryRail;
            public RailBuildDiagnostics rightBoundaryRail;
            public RailBuildDiagnostics rightSupportRail;
        }

        private struct StructuredStripRow
        {
            public FrameSample frame;
            public int frameIndex;
            public bool isTipRow;
            public BoundaryVertexSample leftSupport;
            public BoundaryVertexSample leftBoundary;
            public BoundaryVertexSample rightBoundary;
            public BoundaryVertexSample rightSupport;
        }

        private struct StructuredCellKey : IEquatable<StructuredCellKey>
        {
            public int rowIndex;
            public int columnIndex;

            public bool Equals(StructuredCellKey other)
            {
                return rowIndex == other.rowIndex && columnIndex == other.columnIndex;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuredCellKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (rowIndex * 397) ^ columnIndex;
                }
            }
        }

        internal struct StructuredStripReferenceRow
        {
            public float s;
            public float strokeT;
            public float targetOpen;
            public float leftSupportU;
            public float leftBoundaryU;
            public float rightBoundaryU;
            public float rightSupportU;
        }

        private struct StructuredStripCell2D
        {
            public Vector2 a;
            public Vector2 b;
            public Vector2 c;
            public Vector2 d;
            public BoundaryVertexSample sampleA;
            public BoundaryVertexSample sampleB;
            public BoundaryVertexSample sampleC;
            public BoundaryVertexSample sampleD;
            public bool excision;
            public int rowIndex;
            public int columnIndex;
        }

        private enum StructuredCellSideKind
        {
            Start = 0,
            Right = 1,
            End = 2,
            Left = 3,
        }

        private struct StructuredCellExpectedCorners
        {
            public int startLeft;
            public int startRight;
            public int endRight;
            public int endLeft;
        }

        private struct StructuredCellFragment
        {
            public List<ClipVertex> vertices;
            public int sourceTriangleIndex;
            public int sourcePieceIndex;
        }

        private struct StructuredCellSideDefinition
        {
            public ClipBoundaryFlags flag;
            public Vector2 a;
            public Vector2 b;
        }

        private struct SourceEdgePoint
        {
            public ClipVertex vertex;
            public float t;
        }

        private struct StructuredBoundaryEdgeContext
        {
            public bool valid;
            public StructuredBoundarySegmentKey key;
            public ClipBoundaryFlags boundaryFlag;
            public float ta;
            public float tb;
        }

        private struct StructuredBoundaryVertexBinding
        {
            public bool valid;
            public StructuredBoundarySegmentKey key;
            public ClipBoundaryFlags boundaryFlag;
            public float t;
        }

        private struct StructuredBoundarySegmentKey : IEquatable<StructuredBoundarySegmentKey>
        {
            public int rowIndex;
            public ClipBoundaryFlags boundaryFlag;

            public bool Equals(StructuredBoundarySegmentKey other)
            {
                return rowIndex == other.rowIndex && boundaryFlag == other.boundaryFlag;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuredBoundarySegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (rowIndex * 397) ^ (int)boundaryFlag;
                }
            }
        }

        private struct StructuredCellCornerFragmentPresence
        {
            public int vertexOccurrences;
            public int fragmentOccurrences;
        }

        private enum PartitionRegionKind
        {
            Outside = 0,
            Support = 1,
            Excision = 2,
        }

        private struct InterfaceEdgeAccumulator
        {
            public int outsideCount;
            public int supportCount;
            public int excisionCount;
            public bool usePresenceOwnership;
            public int rowIndex;
            public string vertexA;
            public string vertexB;
            public ClipBoundaryFlags flags;
            public Vector3 worldA;
            public Vector3 worldB;
            public bool hasResolvedSegment;
            public StructuredBoundarySegmentKey resolvedSegmentKey;
            public float resolvedSegmentTMin;
            public float resolvedSegmentTMax;
        }

        private enum StructuredPointMergeResult
        {
            Ignored = 0,
            Added = 1,
            Reused = 2,
        }

        private struct StructuredNormalizationStats
        {
            public int keyCount;
            public int insertCount;
            public int reuseCount;
            public int polygonsTouched;
        }

        private struct StructuredSourceNormalizationOptions
        {
            public List<StructuredStripRow> rows;
            public HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints;
            public bool preserveInsertedPointFlags;
            public bool useCanonicalCleanup;
        }

        private struct StructuredBoundaryNormalizationOptions
        {
            public List<StructuredStripRow> rows;
            public HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints;
            public bool preserveInsertedPointFlags;
            public bool useCanonicalCleanup;
        }

        private struct StructuredInterfaceScanDiagnostics
        {
            public int mismatchCount;
            public int segmentCount;
            public int outsideEdgeCount;
            public int supportEdgeCount;
            public int excisionEdgeCount;
            public int matchedOutsideSupportCount;
            public int matchedSupportExcisionCount;
            public int outsideExcisionCount;
            public int singleSideCount;
            public int multiUseCount;
            public int tripleUseCount;
            public int hardMismatchCount;
            public int auditMismatchCount;
            public int canceledInternalCount;
            public string byFlagSummary;
            public string byRowSummary;
            public string ownershipSummary;
            public string categorySummary;
            public string actualMaskSummary;
            public string detailSummary;
            public string tipSummary;
            public string tipInterfaceKeyCompareSummary;
            public string previewSummary;
            public int closingTipOwnerRepairCount;
        }

        private struct StructuredTipInterfaceBucketKey : IEquatable<StructuredTipInterfaceBucketKey>
        {
            public int rowIndex;
            public ClipBoundaryFlags boundaryFlag;
            public int tMinQuantized;
            public int tMaxQuantized;

            public bool Equals(StructuredTipInterfaceBucketKey other)
            {
                return rowIndex == other.rowIndex &&
                    boundaryFlag == other.boundaryFlag &&
                    tMinQuantized == other.tMinQuantized &&
                    tMaxQuantized == other.tMaxQuantized;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuredTipInterfaceBucketKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = rowIndex;
                    hash = (hash * 397) ^ (int)boundaryFlag;
                    hash = (hash * 397) ^ tMinQuantized;
                    hash = (hash * 397) ^ tMaxQuantized;
                    return hash;
                }
            }
        }

        private struct StructuredTipInterfaceBucketStats
        {
            public int rowIndex;
            public ClipBoundaryFlags boundaryFlag;
            public int tMinQuantized;
            public int tMaxQuantized;
            public int stateCount;
            public bool unionOutside;
            public bool unionSupport;
            public bool unionExcision;
            public int actualOCount;
            public int actualSCount;
            public int actualXCount;
            public int actualOSCount;
            public int actualSXCount;
            public int actualOXCount;
            public int actualOSXCount;
        }

        private struct StructuredStartTipWedgeDiagnostics
        {
            public int startHelperInvocationCount;
            public int startDomainPieceCount;
            public int startLbSplitLeftSupportPieceCount;
            public int startLbSplitCenterRightPieceCount;
            public int startRbSplitExcisionPieceCount;
            public int startRbSplitRightSupportPieceCount;
            public int row0BypassedOrdinaryCellCount;
            public int endHelperInvocationCount;
            public int endDomainPieceCount;
            public int endLbSplitLeftSupportPieceCount;
            public int endLbSplitCenterRightPieceCount;
            public int endRbSplitExcisionPieceCount;
            public int endRbSplitRightSupportPieceCount;
            public int endTipBypassedOrdinaryCellCount;
            public string endEarlyHelperProbeSummary;
            public string endOrdinaryAuditFirstBreakSummary;
            public List<string> endOrdinaryAuditPreviewEntries;
            public int endOrdinaryAuditWatchedCellCount;
        }

        private struct StructuredBoundaryOwnerInterval
        {
            public ClipBoundaryFlags boundaryFlag;
            public float tMin;
            public float tMax;
            public PartitionRegionKind regionKind;
            public StructuredBoundarySegmentKey segmentKey;
            public ClipBoundaryFlags sourceBoundaryFlags;
            public string sourceStage;
            public int sourceCellRowIndex;
            public int sourceCellColumnIndex;
            public int sourceTriangleIndex;
        }

        private struct StructuredEndTipProbeResult
        {
            public string rowSummary;
            public string contributorSummary;
            public bool hasShared;
            public bool hasExcisionOnly;
        }

        private struct StructuredCanonicalBreakpointKey : IEquatable<StructuredCanonicalBreakpointKey>
        {
            public StructuredBoundarySegmentKey segmentKey;
            public int tQuantized;

            public bool Equals(StructuredCanonicalBreakpointKey other)
            {
                return segmentKey.Equals(other.segmentKey) && tQuantized == other.tQuantized;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuredCanonicalBreakpointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (segmentKey.GetHashCode() * 397) ^ tQuantized;
                }
            }
        }

        private struct StructuredResolvedInterfaceSubsegmentKey : IEquatable<StructuredResolvedInterfaceSubsegmentKey>
        {
            public StructuredBoundarySegmentKey segmentKey;
            public int tMinQuantized;
            public int tMaxQuantized;

            public bool Equals(StructuredResolvedInterfaceSubsegmentKey other)
            {
                return segmentKey.Equals(other.segmentKey) &&
                    tMinQuantized == other.tMinQuantized &&
                    tMaxQuantized == other.tMaxQuantized;
            }

            public override bool Equals(object obj)
            {
                return obj is StructuredResolvedInterfaceSubsegmentKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = segmentKey.GetHashCode();
                    hash = (hash * 397) ^ tMinQuantized;
                    hash = (hash * 397) ^ tMaxQuantized;
                    return hash;
                }
            }
        }

        private enum StructuredTipInterfaceBucketDiagnosis
        {
            None = 0,
            MissingOppositeOwner = 1,
            EdgeKeySplit = 2,
            Mixed = 3,
        }

        private struct EdgeBoundaryKey : IEquatable<EdgeBoundaryKey>
        {
            public int a;
            public int b;
            public ClipBoundaryKind boundary;
            public int token;

            public bool Equals(EdgeBoundaryKey other)
            {
                return a == other.a && b == other.b && boundary == other.boundary && token == other.token;
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeBoundaryKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = a;
                    hash = (hash * 397) ^ b;
                    hash = (hash * 397) ^ (int)boundary;
                    hash = (hash * 397) ^ token;
                    return hash;
                }
            }
        }

        private sealed class MeshWorkData
        {
            public readonly List<Vector3> vertices = new List<Vector3>();
            public readonly List<int> triangles = new List<int>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<Vector4> tangents = new List<Vector4>();
            public readonly List<Color> colors = new List<Color>();
            public readonly Dictionary<VertexWeldKey, List<int>> vertexWeldBuckets = new Dictionary<VertexWeldKey, List<int>>();
            public bool hasUV;
            public bool hasTangents;
            public bool hasColors;
            public bool vertexWeldBucketsInitialized;

            public static MeshWorkData FromMesh(Mesh mesh)
            {
                if (mesh == null)
                    return null;

                var data = new MeshWorkData();
                Vector3[] srcVertices = mesh.vertices;
                int[] srcTriangles = mesh.triangles;
                if (srcVertices != null)
                    data.vertices.AddRange(srcVertices);
                if (srcTriangles != null)
                    data.triangles.AddRange(srcTriangles);

                Vector2[] srcUV = mesh.uv;
                Vector4[] srcTangents = mesh.tangents;
                Color[] srcColors = mesh.colors;
                data.hasUV = srcUV != null && srcUV.Length == data.vertices.Count;
                data.hasTangents = srcTangents != null && srcTangents.Length == data.vertices.Count;
                data.hasColors = srcColors != null && srcColors.Length == data.vertices.Count;

                if (data.hasUV)
                    data.uv.AddRange(srcUV);
                if (data.hasTangents)
                    data.tangents.AddRange(srcTangents);
                if (data.hasColors)
                    data.colors.AddRange(srcColors);

                return data;
            }

            public MeshWorkData Clone()
            {
                var data = new MeshWorkData
                {
                    hasUV = hasUV,
                    hasTangents = hasTangents,
                    hasColors = hasColors,
                };
                data.vertices.AddRange(vertices);
                data.triangles.AddRange(triangles);
                data.uv.AddRange(uv);
                data.tangents.AddRange(tangents);
                data.colors.AddRange(colors);
                return data;
            }

            public void ApplyToMesh(Mesh mesh)
            {
                if (mesh == null)
                    return;

                mesh.Clear();
                mesh.indexFormat = vertices.Count > 65535
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0, true);

                if (hasUV && uv.Count == vertices.Count)
                    mesh.SetUVs(0, uv);
                else
                    mesh.uv = new Vector2[0];

                if (hasTangents && tangents.Count == vertices.Count)
                    mesh.SetTangents(tangents);
                else
                    mesh.tangents = new Vector4[0];

                if (hasColors && colors.Count == vertices.Count)
                    mesh.SetColors(colors);
                else
                    mesh.colors = new Color[0];

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
            }

            public void Subdivide(int passes)
            {
                int clampedPasses = Mathf.Clamp(passes, 0, 2);
                for (int pass = 0; pass < clampedPasses; pass++)
                    SubdivideOnce();
            }

            private void SubdivideOnce()
            {
                if (triangles.Count < 3)
                    return;

                var midpointCache = new Dictionary<ulong, int>(triangles.Count);
                var nextTriangles = new List<int>(triangles.Count * 4);
                int triangleCount = triangles.Count / 3;
                for (int tri = 0; tri < triangleCount; tri++)
                {
                    int baseIndex = tri * 3;
                    int a = triangles[baseIndex + 0];
                    int b = triangles[baseIndex + 1];
                    int c = triangles[baseIndex + 2];
                    if (!IsValidVertexIndex(a) || !IsValidVertexIndex(b) || !IsValidVertexIndex(c))
                        continue;

                    int ab = GetOrCreateMidpoint(a, b, midpointCache);
                    int bc = GetOrCreateMidpoint(b, c, midpointCache);
                    int ca = GetOrCreateMidpoint(c, a, midpointCache);

                    nextTriangles.Add(a);
                    nextTriangles.Add(ab);
                    nextTriangles.Add(ca);

                    nextTriangles.Add(ab);
                    nextTriangles.Add(b);
                    nextTriangles.Add(bc);

                    nextTriangles.Add(ca);
                    nextTriangles.Add(bc);
                    nextTriangles.Add(c);

                    nextTriangles.Add(ab);
                    nextTriangles.Add(bc);
                    nextTriangles.Add(ca);
                }

                triangles.Clear();
                triangles.AddRange(nextTriangles);
            }

            private int GetOrCreateMidpoint(int a, int b, Dictionary<ulong, int> cache)
            {
                ulong key = MakeUndirectedEdgeKey(a, b);
                if (cache.TryGetValue(key, out int existing))
                    return existing;

                Vector3 p = 0.5f * (vertices[a] + vertices[b]);
                int index = vertices.Count;
                vertices.Add(p);

                if (hasUV)
                    uv.Add(0.5f * (uv[a] + uv[b]));

                if (hasTangents)
                    tangents.Add(0.5f * (tangents[a] + tangents[b]));

                if (hasColors)
                    colors.Add(Color.Lerp(colors[a], colors[b], 0.5f));

                cache.Add(key, index);
                return index;
            }

            public int DuplicateVertex(int sourceIndex)
            {
                if (!IsValidVertexIndex(sourceIndex))
                    return -1;

                int index = vertices.Count;
                vertices.Add(vertices[sourceIndex]);

                if (hasUV && uv.Count == index)
                    uv.Add(uv[sourceIndex]);

                if (hasTangents && tangents.Count == index)
                    tangents.Add(tangents[sourceIndex]);

                if (hasColors && colors.Count == index)
                    colors.Add(colors[sourceIndex]);

                return index;
            }

            private bool IsValidVertexIndex(int index)
            {
                return index >= 0 && index < vertices.Count;
            }
        }

        private sealed class MinHeap
        {
            private struct Node
            {
                public int vertex;
                public float cost;
            }

            private readonly List<Node> _heap = new List<Node>();

            public bool TryPop(out int vertex, out float cost)
            {
                vertex = -1;
                cost = 0f;
                if (_heap.Count == 0)
                    return false;

                Node root = _heap[0];
                Node last = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                if (_heap.Count > 0)
                {
                    _heap[0] = last;
                    SiftDown(0);
                }

                vertex = root.vertex;
                cost = root.cost;
                return true;
            }

            public void Push(int vertex, float cost)
            {
                _heap.Add(new Node { vertex = vertex, cost = cost });
                int index = _heap.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (_heap[parent].cost <= _heap[index].cost)
                        break;

                    Node tmp = _heap[parent];
                    _heap[parent] = _heap[index];
                    _heap[index] = tmp;
                    index = parent;
                }
            }

            private void SiftDown(int index)
            {
                int count = _heap.Count;
                while (true)
                {
                    int left = index * 2 + 1;
                    int right = left + 1;
                    int smallest = index;
                    if (left < count && _heap[left].cost < _heap[smallest].cost)
                        smallest = left;
                    if (right < count && _heap[right].cost < _heap[smallest].cost)
                        smallest = right;
                    if (smallest == index)
                        break;

                    Node tmp = _heap[index];
                    _heap[index] = _heap[smallest];
                    _heap[smallest] = tmp;
                    index = smallest;
                }
            }
        }

        internal static GenerateResult Generate(
            Mesh outerMesh,
            Transform outerTransform,
            Mesh innerMesh,
            Transform innerTransform,
            Preset preset,
            Options options)
        {
            var result = new GenerateResult();
            if (outerMesh == null || innerMesh == null || outerTransform == null || innerTransform == null)
            {
                result.failStage = "Input";
                result.failReason = "Outer/inner mesh or transform is null.";
                return result;
            }

            AxisFrame outerAxis = BuildAxisFrame(outerMesh, preset.centerOffsetLocal);
            AxisFrame innerAxis = BuildAxisFrame(innerMesh, preset.centerOffsetLocal);

            if (!BuildTargetStrokeWorld(outerMesh, outerTransform, outerAxis, preset, result.targetStrokeWorld))
            {
                result.failStage = "TargetStroke";
                result.failReason = "Failed to build target stroke.";
                return result;
            }

            MeshWorkData outerWork = MeshWorkData.FromMesh(outerMesh);
            MeshWorkData innerWork = MeshWorkData.FromMesh(innerMesh);
            if (outerWork == null || innerWork == null || outerWork.vertices.Count == 0 || innerWork.vertices.Count == 0)
            {
                result.failStage = "MeshClone";
                result.failReason = "Failed to clone mesh data.";
                return result;
            }

            outerWork.Subdivide(options.subdivisionPasses);
            innerWork.Subdivide(options.subdivisionPasses);

            LayerResult outerLayer = BuildLayer(
                LayerKind.Outer,
                outerWork,
                outerTransform,
                outerAxis,
                result.targetStrokeWorld,
                preset.lengthWorld,
                preset.openWidthWorld,
                options,
                null,
                null);
            result.outer = outerLayer;
            if (outerLayer == null || !outerLayer.success)
            {
                result.failStage = outerLayer != null ? outerLayer.failStage : "OuterLayer";
                result.failReason = outerLayer != null ? outerLayer.failReason : "Outer layer build failed.";
                return result;
            }

            var innerGuide = new List<Vector3>(outerLayer.stations.Count);
            BuildInnerGuideFromOuterStations(outerLayer.stations, innerWork, outerTransform, innerTransform, outerAxis, innerAxis, innerGuide);
            if (innerGuide.Count < 2)
            {
                result.failStage = "InnerGuide";
                result.failReason = "Failed to map outer stations onto the inner layer.";
                return result;
            }

            LayerResult innerLayer = BuildLayer(
                LayerKind.Inner,
                innerWork,
                innerTransform,
                innerAxis,
                innerGuide,
                preset.lengthWorld,
                preset.openWidthWorld,
                options,
                outerLayer.stations,
                outerLayer.structuredStripReferenceRows);
            result.inner = innerLayer;
            string syncSummary = BuildStructuredLayerSyncSummary(outerLayer, innerLayer);
            if (outerLayer != null)
                outerLayer.syncSummary = syncSummary;
            if (innerLayer != null)
                innerLayer.syncSummary = syncSummary;
            if (innerLayer == null || !innerLayer.success)
            {
                result.failStage = innerLayer != null ? innerLayer.failStage : "InnerLayer";
                result.failReason = innerLayer != null ? innerLayer.failReason : "Inner layer build failed.";
                return result;
            }

            result.success = true;
            return result;
        }

        private static string BuildStructuredLayerSyncSummary(LayerResult outerLayer, LayerResult innerLayer)
        {
            if (outerLayer == null && innerLayer == null)
                return "none";

            int outerRows = outerLayer != null ? outerLayer.stripRowCount : 0;
            int innerRows = innerLayer != null ? innerLayer.stripRowCount : 0;
            int refRows = outerLayer != null && outerLayer.structuredStripReferenceRows != null
                ? outerLayer.structuredStripReferenceRows.Count
                : 0;
            float targetOpenDeltaMax = 0f;
            if (outerLayer != null && innerLayer != null && outerLayer.stations != null && innerLayer.stations != null)
            {
                int count = Mathf.Min(outerLayer.stations.Count, innerLayer.stations.Count);
                for (int i = 0; i < count; i++)
                    targetOpenDeltaMax = Mathf.Max(targetOpenDeltaMax, Mathf.Abs(outerLayer.stations[i].targetOpen - innerLayer.stations[i].targetOpen));
            }

            return
                $"outerRows={outerRows} innerRows={innerRows} refRows={refRows} " +
                $"targetOpenDeltaMax={targetOpenDeltaMax:F6} boundarySwapApplied=0";
        }

        private struct CircumferentialBoundaryEdge
        {
            public int a;
            public int b;
        }

        private sealed class CircumferentialCutWork
        {
            public LayerResult result;
            public List<Vector3> lowerBoundaryWorld = new List<Vector3>();
            public List<Vector3> upperBoundaryWorld = new List<Vector3>();
            public List<Vector3> centerBoundaryWorld = new List<Vector3>();
            public int duplicatedVertexCount;
            public int boundaryEdgeCount;
            public float targetStationDistanceLocal;
            public float localSeparation;
        }

        private sealed class CircumferentialRing
        {
            public Vector3 center;
            public float stationDistance;
            public float longitudinalCoordinate;
            public readonly List<int> vertices = new List<int>();
        }

        private struct CircumferentialRingVertex
        {
            public int index;
            public float angle;
        }

        internal static GenerateResult GenerateCircumferentialSeparationCut(
            Mesh outerMesh,
            Transform outerTransform,
            Mesh innerMesh,
            Transform innerTransform,
            Preset preset)
        {
            var generated = new GenerateResult();

            CircumferentialCutWork outer = BuildCircumferentialSeparationLayer(outerMesh, outerTransform, LayerKind.Outer, preset);
            CircumferentialCutWork inner = BuildCircumferentialSeparationLayer(innerMesh, innerTransform, LayerKind.Inner, preset);

            generated.outer = outer != null ? outer.result : null;
            generated.inner = inner != null ? inner.result : null;
            generated.success = generated.outer != null && generated.outer.success && generated.inner != null && generated.inner.success;
            if (!generated.success)
            {
                if (generated.outer == null || !generated.outer.success)
                {
                    generated.failStage = generated.outer != null ? generated.outer.failStage : "CircumferentialSeparation";
                    generated.failReason = generated.outer != null ? generated.outer.failReason : "outer layer cut result is null";
                }
                else
                {
                    generated.failStage = generated.inner != null ? generated.inner.failStage : "CircumferentialSeparation";
                    generated.failReason = generated.inner != null ? generated.inner.failReason : "inner layer cut result is null";
                }
                return generated;
            }

            if (!ValidateCircumferentialLayerIdentitySync(generated.outer, generated.inner, out string identitySyncSummary))
            {
                generated.success = false;
                generated.failStage = "CircumferentialIdentitySync";
                generated.failReason = identitySyncSummary;
                return generated;
            }

            generated.outer.syncSummary = identitySyncSummary + " | " + generated.outer.syncSummary;
            generated.inner.syncSummary = identitySyncSummary + " | " + generated.inner.syncSummary;

            CopyCircumferentialPolyline(generated.targetStrokeWorld, outer.centerBoundaryWorld);
            return generated;
        }

        private static bool ValidateCircumferentialLayerIdentitySync(
            LayerResult outer,
            LayerResult inner,
            out string summary)
        {
            int outerCount = outer != null && outer.stations != null ? outer.stations.Count : 0;
            int innerCount = inner != null && inner.stations != null ? inner.stations.Count : 0;
            int invalidOuter = 0;
            int invalidInner = 0;
            int boundaryIdentityMismatch = 0;
            int triangleIdentityMismatch = 0;
            int uvIdentityMismatch = 0;
            if (outer != null && outer.stations != null)
            {
                for (int i = 0; i < outer.stations.Count; i++)
                {
                    StationResult station = outer.stations[i];
                    if (station.leftVertexIndex < 0 || station.rightVertexIndex < 0 || station.leftVertexIndex == station.rightVertexIndex)
                        invalidOuter++;
                }
            }
            if (inner != null && inner.stations != null)
            {
                for (int i = 0; i < inner.stations.Count; i++)
                {
                    StationResult station = inner.stations[i];
                    if (station.leftVertexIndex < 0 || station.rightVertexIndex < 0 || station.leftVertexIndex == station.rightVertexIndex)
                        invalidInner++;
                }
            }

            if (outerCount == innerCount &&
                outer != null &&
                inner != null &&
                outer.stations != null &&
                inner.stations != null)
            {
                for (int i = 0; i < outerCount; i++)
                {
                    StationResult outerStation = outer.stations[i];
                    StationResult innerStation = inner.stations[i];
                    if (outerStation.leftVertexIndex != innerStation.leftVertexIndex ||
                        outerStation.rightVertexIndex != innerStation.rightVertexIndex)
                    {
                        boundaryIdentityMismatch++;
                    }
                }
            }

            int[] outerTriangles =
                outer != null && outer.mesh != null
                    ? outer.mesh.triangles
                    : null;
            int[] innerTriangles =
                inner != null && inner.mesh != null
                    ? inner.mesh.triangles
                    : null;
            if (outerTriangles == null ||
                innerTriangles == null ||
                outerTriangles.Length != innerTriangles.Length)
            {
                triangleIdentityMismatch++;
            }
            else
            {
                for (int i = 0; i < outerTriangles.Length; i++)
                {
                    if (outerTriangles[i] != innerTriangles[i])
                        triangleIdentityMismatch++;
                }
            }

            Vector2[] outerUv =
                outer != null && outer.mesh != null
                    ? outer.mesh.uv
                    : null;
            Vector2[] innerUv =
                inner != null && inner.mesh != null
                    ? inner.mesh.uv
                    : null;
            int outerVertexCount =
                outer != null && outer.mesh != null
                    ? outer.mesh.vertexCount
                    : 0;
            int innerVertexCount =
                inner != null && inner.mesh != null
                    ? inner.mesh.vertexCount
                    : 0;
            if (outerVertexCount <= 0 ||
                outerVertexCount != innerVertexCount ||
                outerUv == null ||
                innerUv == null ||
                outerUv.Length != outerVertexCount ||
                innerUv.Length != innerVertexCount)
            {
                uvIdentityMismatch++;
            }
            else
            {
                const float materialIdentityTolerance = 1e-6f;
                for (int i = 0; i < outerUv.Length; i++)
                {
                    if ((outerUv[i] - innerUv[i]).sqrMagnitude >
                        materialIdentityTolerance *
                        materialIdentityTolerance)
                    {
                        uvIdentityMismatch++;
                    }
                }
            }

            bool valid =
                outerCount >= 4 &&
                outerCount == innerCount &&
                invalidOuter == 0 &&
                invalidInner == 0 &&
                boundaryIdentityMismatch == 0 &&
                triangleIdentityMismatch == 0 &&
                uvIdentityMismatch == 0;
            summary =
                $"circumferentialIdentity outerStations={outerCount} innerStations={innerCount} " +
                $"invalidOuter={invalidOuter} invalidInner={invalidInner} " +
                $"boundaryIdMism={boundaryIdentityMismatch} topologyIdMism={triangleIdentityMismatch} " +
                $"uvIdMism={uvIdentityMismatch} synchronized={(valid ? 1 : 0)}";
            return valid;
        }

        private static CircumferentialCutWork BuildCircumferentialSeparationLayer(
            Mesh sourceMesh,
            Transform meshTransform,
            LayerKind layerKind,
            Preset preset)
        {
            var work = new CircumferentialCutWork();
            var layer = new LayerResult
            {
                success = false,
                isCircumferentialSeparationCut = true,
                layer = (int)layerKind,
                failStage = "CircumferentialRingBandRebuild"
            };
            work.result = layer;

            if (sourceMesh == null)
            {
                layer.failReason = "source mesh is null";
                return work;
            }

            MeshWorkData source = MeshWorkData.FromMesh(sourceMesh);
            if (source == null || source.vertices.Count < 3 || source.triangles.Count < 3)
            {
                layer.failReason = "mesh has no usable triangles";
                return work;
            }

            List<CircumferentialRing> rings;
            int[] vertexStation;
            string stationSummary;
            string stationFailReason;
            if (!TryBuildCircumferentialStationRings(
                    source,
                    out rings,
                    out vertexStation,
                    out stationSummary,
                    out stationFailReason))
            {
                layer.failReason = stationFailReason;
                return work;
            }

            float totalStationDistance = rings[rings.Count - 1].stationDistance;
            if (totalStationDistance <= 1e-6f)
            {
                layer.failReason = "topology ring centerline length is degenerate";
                return work;
            }

            float normalizedStation = Mathf.Clamp01(preset.normalizedZ);
            float targetStationDistance = totalStationDistance * normalizedStation;
            int lowerRingIndex;
            int upperRingIndex;
            if (!TryFindCircumferentialBracketRings(
                    rings,
                    targetStationDistance,
                    out lowerRingIndex,
                    out upperRingIndex))
            {
                layer.failReason = string.Format(
                    "target station has no usable bracket rings. targetDistance={0:0.####} totalDistance={1:0.####} rings={2}",
                    targetStationDistance,
                    totalStationDistance,
                    rings.Count);
                return work;
            }

            CircumferentialRing lowerRing = rings[lowerRingIndex];
            CircumferentialRing upperRing = rings[upperRingIndex];
            Vector3 localCutTangent = upperRing.center - lowerRing.center;
            if (localCutTangent.sqrMagnitude <= 1e-12f)
            {
                layer.failReason = string.Format(
                    "bracket ring centers coincide. lowerStation={0} upperStation={1}",
                    lowerRingIndex,
                    upperRingIndex);
                return work;
            }
            localCutTangent.Normalize();

            float localSeparation = Mathf.Max(
                0f,
                CircumferentialWorldDistanceToLocalDirection(
                    meshTransform,
                    localCutTangent,
                    preset.separationDistanceWorld));
            float halfSeparation = localSeparation * 0.5f;
            Vector3 lowerOffset = -localCutTangent * halfSeparation;
            Vector3 upperOffset = localCutTangent * halfSeparation;
            work.targetStationDistanceLocal = targetStationDistance;
            work.localSeparation = localSeparation;

            List<CircumferentialRingVertex> orderedLower =
                BuildCircumferentialOrderedRingVertices(lowerRing.vertices);
            List<CircumferentialRingVertex> orderedUpper =
                BuildCircumferentialOrderedRingVertices(upperRing.vertices);
            if (orderedLower.Count < 3 || orderedUpper.Count < 3)
            {
                layer.failReason = string.Format("bracket rings are too small. lower={0} upper={1}", orderedLower.Count, orderedUpper.Count);
                return work;
            }

            if (orderedLower.Count != orderedUpper.Count)
            {
                layer.failReason = string.Format(
                    "bracket ring vertex counts differ. lowerStation={0} lowerCount={1} upperStation={2} upperCount={3}",
                    lowerRingIndex,
                    orderedLower.Count,
                    upperRingIndex,
                    orderedUpper.Count);
                return work;
            }

            float stationSpan =
                upperRing.stationDistance -
                lowerRing.stationDistance;
            if (stationSpan <= 1e-6f)
            {
                layer.failReason = string.Format(
                    "bracket rings are too close along centerline. lowerDistance={0:0.####} upperDistance={1:0.####}",
                    lowerRing.stationDistance,
                    upperRing.stationDistance);
                return work;
            }

            float t = Mathf.Clamp01(
                (targetStationDistance - lowerRing.stationDistance) /
                stationSpan);
            int ringCount = orderedLower.Count;
            MeshWorkData outputData = CreateEmptyCircumferentialMeshWorkDataLike(source);
            int[] lowerOriginalMap = CreateCircumferentialIndexMap(source.vertices.Count);
            int[] upperOriginalMap = CreateCircumferentialIndexMap(source.vertices.Count);
            int[] lowerCutRing = new int[ringCount];
            int[] upperCutRing = new int[ringCount];

            for (int i = 0; i < ringCount; i++)
            {
                int lowerSource = orderedLower[i].index;
                int upperSource = orderedUpper[i].index;
                Vector3 cutLocal = Vector3.Lerp(source.vertices[lowerSource], source.vertices[upperSource], t);
                lowerCutRing[i] = AddCircumferentialInterpolatedVertex(source, outputData, lowerSource, upperSource, t, cutLocal, lowerOffset);
                upperCutRing[i] = AddCircumferentialInterpolatedVertex(source, outputData, lowerSource, upperSource, t, cutLocal, upperOffset);
            }

            int triCount = source.triangles.Count / 3;
            int keptLowerTriCount = 0;
            int keptUpperTriCount = 0;
            int removedBeltTriCount = 0;
            for (int tri = 0; tri < triCount; tri++)
            {
                int baseIndex = tri * 3;
                int a = source.triangles[baseIndex + 0];
                int b = source.triangles[baseIndex + 1];
                int c = source.triangles[baseIndex + 2];
                if (!IsCircumferentialValidIndex(a, source.vertices.Count) || !IsCircumferentialValidIndex(b, source.vertices.Count) || !IsCircumferentialValidIndex(c, source.vertices.Count))
                    continue;

                int minStation = Mathf.Min(
                    vertexStation[a],
                    Mathf.Min(vertexStation[b], vertexStation[c]));
                int maxStation = Mathf.Max(
                    vertexStation[a],
                    Mathf.Max(vertexStation[b], vertexStation[c]));

                if (maxStation <= lowerRingIndex)
                {
                    AddCircumferentialWholeTriangle(
                        source,
                        outputData,
                        a,
                        b,
                        c,
                        false,
                        lowerOriginalMap,
                        upperOriginalMap,
                        lowerOffset,
                        upperOffset);
                    keptLowerTriCount++;
                }
                else if (minStation >= upperRingIndex)
                {
                    AddCircumferentialWholeTriangle(
                        source,
                        outputData,
                        a,
                        b,
                        c,
                        true,
                        lowerOriginalMap,
                        upperOriginalMap,
                        lowerOffset,
                        upperOffset);
                    keptUpperTriCount++;
                }
                else
                {
                    removedBeltTriCount++;
                }
            }

            for (int i = 0; i < ringCount; i++)
            {
                int next = (i + 1) % ringCount;
                int lowerA = GetCircumferentialOriginalSideVertex(source, outputData, orderedLower[i].index, false, lowerOriginalMap, upperOriginalMap, lowerOffset, upperOffset);
                int lowerB = GetCircumferentialOriginalSideVertex(source, outputData, orderedLower[next].index, false, lowerOriginalMap, upperOriginalMap, lowerOffset, upperOffset);
                int upperA = GetCircumferentialOriginalSideVertex(source, outputData, orderedUpper[i].index, true, lowerOriginalMap, upperOriginalMap, lowerOffset, upperOffset);
                int upperB = GetCircumferentialOriginalSideVertex(source, outputData, orderedUpper[next].index, true, lowerOriginalMap, upperOriginalMap, lowerOffset, upperOffset);
                int cutLowerA = lowerCutRing[i];
                int cutLowerB = lowerCutRing[next];
                int cutUpperA = upperCutRing[i];
                int cutUpperB = upperCutRing[next];

                AddCircumferentialTriangleIfValid(outputData, lowerA, lowerB, cutLowerB);
                AddCircumferentialTriangleIfValid(outputData, lowerA, cutLowerB, cutLowerA);

                AddCircumferentialTriangleIfValid(outputData, cutUpperA, cutUpperB, upperB);
                AddCircumferentialTriangleIfValid(outputData, cutUpperA, upperB, upperA);
            }

            for (int i = 0; i < ringCount; i++)
            {
                Vector3 lowerWorld = TransformPointSafe(meshTransform, outputData.vertices[lowerCutRing[i]]);
                Vector3 upperWorld = TransformPointSafe(meshTransform, outputData.vertices[upperCutRing[i]]);
                Vector3 centerWorld = 0.5f * (lowerWorld + upperWorld);
                work.lowerBoundaryWorld.Add(lowerWorld);
                work.upperBoundaryWorld.Add(upperWorld);
                work.centerBoundaryWorld.Add(centerWorld);
            }

            EnsureCircumferentialClosedPolyline(work.lowerBoundaryWorld);
            EnsureCircumferentialClosedPolyline(work.upperBoundaryWorld);
            EnsureCircumferentialClosedPolyline(work.centerBoundaryWorld);
            work.boundaryEdgeCount = ringCount;
            work.duplicatedVertexCount = ringCount * 2;

            Mesh output = UnityEngine.Object.Instantiate(sourceMesh);
            output.name = sourceMesh.name + "_CircumferentialRingBandSeparated";
            outputData.ApplyToMesh(output);

            int outputTriCount = outputData.triangles.Count / 3;
            layer.mesh = output;
            layer.success = true;
            layer.failStage = string.Empty;
            layer.failReason = string.Empty;
            layer.averageEdgeLength = ComputeCircumferentialAverageEdgeLength(sourceMesh);
            layer.masterLength = ComputeCircumferentialPolylineLength(work.centerBoundaryWorld);
            layer.patchTriangleCount = outputTriCount;
            layer.keptTriangleCount = outputTriCount;
            layer.removedTriangleCount = removedBeltTriCount;
            layer.leftBoundaryCount = work.lowerBoundaryWorld.Count;
            layer.rightBoundaryCount = work.upperBoundaryWorld.Count;
            layer.openBoundaryEdgeCount = ringCount;
            layer.averageRealizedOpenWidth = preset.separationDistanceWorld;
            layer.centerCurvePointCount = work.centerBoundaryWorld.Count;
            layer.centerCurveLength = layer.masterLength;
            layer.guidePointCount = work.centerBoundaryWorld.Count;
            layer.denseGuidePointCount = work.centerBoundaryWorld.Count;
            layer.canonicalStationCount = work.centerBoundaryWorld.Count;
            layer.stationSpacingWorld = EstimateCircumferentialAverageSpacing(work.centerBoundaryWorld);
            layer.syncSummary = string.Format(
                "CircumferentialRingBandRebuild layer={0} stationSource=topology lowerStation={1} upperStation={2} targetDistance={3:0.####}/{4:0.####} t={5:0.###} ringVerts={6} keptLower={7} keptUpper={8} removedBelt={9} rebuiltBandTris={10} separationWorld={11:0.####} tangentLocal={12} {13}",
                layerKind,
                lowerRingIndex,
                upperRingIndex,
                targetStationDistance,
                totalStationDistance,
                t,
                ringCount,
                keptLowerTriCount,
                keptUpperTriCount,
                removedBeltTriCount,
                ringCount * 4,
                preset.separationDistanceWorld,
                localCutTangent.ToString("F4"),
                stationSummary);
            layer.projectionAlignSummary = "circumferential cut: inserted topology-station ring boundary, rebuilt lower/upper ring bands along local centerline tangent, no purple excision/delete region";
            layer.boundarySourceSummary = layer.syncSummary;
            layer.interfaceScanDetailSummary = layer.syncSummary;

            CopyCircumferentialPolyline(layer.masterChainWorld, work.centerBoundaryWorld);
            CopyCircumferentialPolyline(layer.centerCurveWorld, work.centerBoundaryWorld);
            CopyCircumferentialPolyline(layer.centerlineWorld, work.centerBoundaryWorld);
            CopyCircumferentialPolyline(layer.leftBoundaryWorld, work.lowerBoundaryWorld);
            CopyCircumferentialPolyline(layer.rightBoundaryWorld, work.upperBoundaryWorld);
            CopyCircumferentialPolyline(layer.precommitRowCentersWorld, work.centerBoundaryWorld);
            CopyCircumferentialPolyline(layer.precommitLeftBoundaryWorld, work.lowerBoundaryWorld);
            CopyCircumferentialPolyline(layer.precommitRightBoundaryWorld, work.upperBoundaryWorld);

            BuildCircumferentialStations(layer, work.lowerBoundaryWorld, work.upperBoundaryWorld, lowerCutRing, upperCutRing);
            return work;
        }

        private static MeshWorkData CreateEmptyCircumferentialMeshWorkDataLike(MeshWorkData source)
        {
            return new MeshWorkData
            {
                hasUV = source != null && source.hasUV,
                hasTangents = source != null && source.hasTangents,
                hasColors = source != null && source.hasColors,
            };
        }

        private static int[] CreateCircumferentialIndexMap(int count)
        {
            int[] map = new int[Mathf.Max(0, count)];
            for (int i = 0; i < map.Length; i++)
                map[i] = -1;
            return map;
        }

        private static bool TryBuildCircumferentialStationRings(
            MeshWorkData source,
            out List<CircumferentialRing> rings,
            out int[] vertexStation,
            out string summary,
            out string failReason)
        {
            rings = new List<CircumferentialRing>();
            vertexStation = source != null
                ? CreateCircumferentialIndexMap(source.vertices.Count)
                : new int[0];
            summary = string.Empty;
            failReason = string.Empty;
            if (source == null ||
                source.vertices == null ||
                source.vertices.Count < 6 ||
                source.triangles == null ||
                source.triangles.Count < 6)
            {
                failReason =
                    "mesh has insufficient data for circumferential station classification";
                return false;
            }

            bool[] allActive = new bool[source.vertices.Count];
            for (int i = 0; i < allActive.Length; i++)
                allActive[i] = true;
            List<List<int>> openBoundaryLoops =
                BuildCircumferentialActiveBoundaryLoops(
                    source.triangles,
                    allActive,
                    out string boundaryGraphReason);

            if (openBoundaryLoops != null &&
                openBoundaryLoops.Count == 2)
            {
                return TryBuildCircumferentialTopologyRings(
                    source,
                    out rings,
                    out vertexStation,
                    out summary,
                    out failReason);
            }

            if (TryBuildValidatedPlanarCircumferentialRings(
                    source,
                    out rings,
                    out vertexStation,
                    out summary,
                    out string planarReason))
            {
                summary +=
                    $" classifiedFromOpenBoundaryLoops={(openBoundaryLoops != null ? openBoundaryLoops.Count : -1)}";
                return true;
            }

            failReason =
                "mesh matches neither supported circumferential station contract. " +
                $"openBoundaryLoops={(openBoundaryLoops != null ? openBoundaryLoops.Count : -1)} " +
                $"boundaryDetail={boundaryGraphReason} planarDetail={planarReason}";
            return false;
        }

        private static bool TryBuildCircumferentialTopologyRings(
            MeshWorkData source,
            out List<CircumferentialRing> rings,
            out int[] vertexStation,
            out string summary,
            out string failReason)
        {
            rings = new List<CircumferentialRing>();
            vertexStation = source != null
                ? CreateCircumferentialIndexMap(source.vertices.Count)
                : new int[0];
            summary = string.Empty;
            failReason = string.Empty;
            if (source == null ||
                source.vertices == null ||
                source.vertices.Count < 6 ||
                source.triangles == null ||
                source.triangles.Count < 6)
            {
                failReason = "mesh has insufficient topology for circumferential station extraction";
                return false;
            }

            List<HashSet<int>> sourceAdjacency =
                BuildCircumferentialVertexAdjacency(
                    source.vertices.Count,
                    source.triangles);
            bool[] activeVertex = new bool[source.vertices.Count];
            for (int i = 0; i < activeVertex.Length; i++)
                activeVertex[i] = true;

            List<List<int>> boundaryLoops =
                BuildCircumferentialActiveBoundaryLoops(
                    source.triangles,
                    activeVertex,
                    out string boundaryFailReason);
            if (boundaryLoops == null || boundaryLoops.Count != 2)
            {
                failReason =
                    "expected exactly two simple open-end boundary loops before topology peeling. " +
                    $"loops={(boundaryLoops != null ? boundaryLoops.Count : 0)} " +
                    $"detail={boundaryFailReason}";
                return false;
            }

            if (!TryComputeCircumferentialLongitudinalCoordinate(
                    source,
                    boundaryLoops[0],
                    out float firstLongitudinal) ||
                !TryComputeCircumferentialLongitudinalCoordinate(
                    source,
                    boundaryLoops[1],
                    out float secondLongitudinal))
            {
                failReason =
                    "topology endpoint loops do not carry complete finite longitudinal UV.v identity";
                return false;
            }
            const float longitudinalIdentityTolerance = 1e-6f;
            if (Mathf.Abs(firstLongitudinal - secondLongitudinal) <=
                longitudinalIdentityTolerance)
            {
                failReason =
                    $"topology endpoint longitudinal identity is degenerate. " +
                    $"first={firstLongitudinal:F6} second={secondLongitudinal:F6}";
                return false;
            }
            List<int> current =
                firstLongitudinal < secondLongitudinal
                    ? boundaryLoops[0]
                    : boundaryLoops[1];
            List<int> terminalBoundary =
                firstLongitudinal < secondLongitudinal
                    ? boundaryLoops[1]
                    : boundaryLoops[0];
            int expectedRingVertexCount = current.Count;
            int assignedVertexCount = 0;
            float stationDistance = 0f;
            Vector3 previousCenter = Vector3.zero;
            float previousLongitudinal = float.NegativeInfinity;
            List<int> previousOrdered = null;

            while (current != null && current.Count > 0)
            {
                if (current.Count != expectedRingVertexCount)
                {
                    failReason =
                        $"topology ring size changed during peeling. station={rings.Count} " +
                        $"expected={expectedRingVertexCount} actual={current.Count}";
                    return false;
                }

                List<int> orderedCurrent =
                    previousOrdered == null
                        ? new List<int>(current)
                        : AlignCircumferentialRingToPrevious(
                            source.vertices,
                            previousOrdered,
                            current);
                if (orderedCurrent.Count != expectedRingVertexCount)
                {
                    failReason =
                        $"could not align topology ring to previous station. station={rings.Count}";
                    return false;
                }

                Vector3 center =
                    ComputeCircumferentialRingCenter(
                        source.vertices,
                        orderedCurrent);
                if (!TryComputeCircumferentialLongitudinalCoordinate(
                        source,
                        orderedCurrent,
                        out float longitudinalCoordinate))
                {
                    failReason =
                        $"topology station has no complete finite longitudinal UV.v identity. station={rings.Count}";
                    return false;
                }
                if (rings.Count > 0 &&
                    longitudinalCoordinate <=
                    previousLongitudinal +
                    longitudinalIdentityTolerance)
                {
                    failReason =
                        $"topology station longitudinal order is not strictly increasing. " +
                        $"station={rings.Count} previous={previousLongitudinal:F6} " +
                        $"current={longitudinalCoordinate:F6}";
                    return false;
                }
                if (rings.Count > 0)
                    stationDistance += Vector3.Distance(previousCenter, center);

                var ring = new CircumferentialRing
                {
                    center = center,
                    stationDistance = stationDistance,
                    longitudinalCoordinate = longitudinalCoordinate,
                };
                ring.vertices.AddRange(orderedCurrent);
                int stationIndex = rings.Count;
                rings.Add(ring);

                for (int i = 0; i < orderedCurrent.Count; i++)
                {
                    int vertex = orderedCurrent[i];
                    if (!IsCircumferentialValidIndex(vertex, activeVertex.Length) ||
                        !activeVertex[vertex] ||
                        vertexStation[vertex] >= 0)
                    {
                        failReason =
                            $"topology peeling reused or lost a vertex. station={stationIndex} vertex={vertex}";
                        return false;
                    }
                    vertexStation[vertex] = stationIndex;
                    activeVertex[vertex] = false;
                    assignedVertexCount++;
                }

                if (assignedVertexCount == source.vertices.Count)
                    break;

                int activeVertexCount = 0;
                for (int i = 0; i < activeVertex.Length; i++)
                {
                    if (activeVertex[i])
                        activeVertexCount++;
                }
                if (activeVertexCount == expectedRingVertexCount)
                {
                    bool terminalMatchesActive = true;
                    for (int i = 0; i < terminalBoundary.Count; i++)
                    {
                        int vertex = terminalBoundary[i];
                        if (!IsCircumferentialValidIndex(
                                vertex,
                                activeVertex.Length) ||
                            !activeVertex[vertex])
                        {
                            terminalMatchesActive = false;
                            break;
                        }
                    }
                    if (!terminalMatchesActive)
                    {
                        failReason =
                            $"final topology station does not match the original terminal boundary. " +
                            $"active={activeVertexCount} expected={expectedRingVertexCount}";
                        return false;
                    }

                    previousOrdered = orderedCurrent;
                    previousCenter = center;
                    previousLongitudinal = longitudinalCoordinate;
                    current = terminalBoundary;
                    continue;
                }

                List<List<int>> candidates =
                    BuildCircumferentialActiveBoundaryLoops(
                        source.triangles,
                        activeVertex,
                        out boundaryFailReason);
                if (candidates == null || candidates.Count == 0)
                {
                    failReason =
                        $"topology peeling lost the next boundary loop at station={stationIndex}. " +
                        $"detail={boundaryFailReason}";
                    return false;
                }

                int bestCandidate = -1;
                int bestConnectionCount = 0;
                for (int candidateIndex = 0;
                     candidateIndex < candidates.Count;
                     candidateIndex++)
                {
                    int connectionCount =
                        CountCircumferentialConnections(
                            orderedCurrent,
                            candidates[candidateIndex],
                            sourceAdjacency);
                    if (connectionCount > bestConnectionCount)
                    {
                        bestConnectionCount = connectionCount;
                        bestCandidate = candidateIndex;
                    }
                }
                if (bestCandidate < 0 || bestConnectionCount <= 0)
                {
                    failReason =
                        $"topology peeling could not identify the adjacent station after station={stationIndex}. " +
                        $"candidateLoops={candidates.Count}";
                    return false;
                }

                previousOrdered = orderedCurrent;
                previousCenter = center;
                previousLongitudinal = longitudinalCoordinate;
                current = candidates[bestCandidate];
            }

            if (rings.Count < 2 ||
                assignedVertexCount != source.vertices.Count)
            {
                failReason =
                    $"topology station coverage is incomplete. rings={rings.Count} " +
                    $"assigned={assignedVertexCount}/{source.vertices.Count}";
                return false;
            }

            int invalidTriangleStationSpan = 0;
            for (int i = 0; i + 2 < source.triangles.Count; i += 3)
            {
                int a = source.triangles[i + 0];
                int b = source.triangles[i + 1];
                int c = source.triangles[i + 2];
                if (!IsCircumferentialValidIndex(a, vertexStation.Length) ||
                    !IsCircumferentialValidIndex(b, vertexStation.Length) ||
                    !IsCircumferentialValidIndex(c, vertexStation.Length))
                {
                    invalidTriangleStationSpan++;
                    continue;
                }

                int minStation = Mathf.Min(
                    vertexStation[a],
                    Mathf.Min(vertexStation[b], vertexStation[c]));
                int maxStation = Mathf.Max(
                    vertexStation[a],
                    Mathf.Max(vertexStation[b], vertexStation[c]));
                if (minStation < 0 || maxStation - minStation > 1)
                    invalidTriangleStationSpan++;
            }
            if (invalidTriangleStationSpan > 0)
            {
                failReason =
                    $"topology station triangle membership is invalid. " +
                    $"badTriangleSpans={invalidTriangleStationSpan}";
                return false;
            }

            int weakAdjacentStationPairs = 0;
            for (int station = 0; station + 1 < rings.Count; station++)
            {
                int connections =
                    CountCircumferentialConnections(
                        rings[station].vertices,
                        rings[station + 1].vertices,
                        sourceAdjacency);
                if (connections < expectedRingVertexCount)
                    weakAdjacentStationPairs++;
            }
            if (weakAdjacentStationPairs > 0)
            {
                failReason =
                    $"topology station adjacency is incomplete. " +
                    $"weakPairs={weakAdjacentStationPairs}/{rings.Count - 1} " +
                    $"minimumExpectedConnections={expectedRingVertexCount}";
                return false;
            }

            summary =
                $"topologyRings={rings.Count} ringVerts={expectedRingVertexCount} " +
                $"assigned={assignedVertexCount}/{source.vertices.Count} " +
                $"centerlineLength={stationDistance:0.####} " +
                $"longitudinalRange={rings[0].longitudinalCoordinate:F6}.." +
                $"{rings[rings.Count - 1].longitudinalCoordinate:F6} " +
                $"badTriangleSpans={invalidTriangleStationSpan} " +
                $"weakAdjacentPairs={weakAdjacentStationPairs} endpointOrientation=uv.v";
            return true;
        }

        private static bool TryComputeCircumferentialLongitudinalCoordinate(
            MeshWorkData source,
            List<int> ring,
            out float longitudinalCoordinate)
        {
            longitudinalCoordinate = 0f;
            if (source == null ||
                !source.hasUV ||
                source.uv == null ||
                source.uv.Count != source.vertices.Count ||
                ring == null ||
                ring.Count < 3)
            {
                return false;
            }

            double sum = 0d;
            for (int i = 0; i < ring.Count; i++)
            {
                int vertex = ring[i];
                if (!IsCircumferentialValidIndex(
                        vertex,
                        source.uv.Count))
                    return false;

                float value = source.uv[vertex].y;
                if (float.IsNaN(value) ||
                    float.IsInfinity(value))
                    return false;
                sum += value;
            }

            longitudinalCoordinate =
                (float)(sum / ring.Count);
            return !float.IsNaN(longitudinalCoordinate) &&
                   !float.IsInfinity(longitudinalCoordinate);
        }

        private static bool TryBuildValidatedPlanarCircumferentialRings(
            MeshWorkData source,
            out List<CircumferentialRing> rings,
            out int[] vertexStation,
            out string summary,
            out string failReason)
        {
            rings = new List<CircumferentialRing>();
            vertexStation = source != null
                ? CreateCircumferentialIndexMap(source.vertices.Count)
                : new int[0];
            summary = string.Empty;
            failReason = string.Empty;
            if (source == null ||
                source.vertices == null ||
                source.vertices.Count < 6)
            {
                failReason = "planar station source is empty";
                return false;
            }

            Bounds bounds =
                ComputeCircumferentialLocalBounds(source.vertices);
            float tolerance =
                Mathf.Max(1e-6f, bounds.size.z * 1e-4f);
            var orderedVertices =
                new List<int>(source.vertices.Count);
            for (int i = 0; i < source.vertices.Count; i++)
                orderedVertices.Add(i);
            orderedVertices.Sort(
                (a, b) =>
                    source.vertices[a].z.CompareTo(
                        source.vertices[b].z));

            for (int i = 0; i < orderedVertices.Count; i++)
            {
                int vertex = orderedVertices[i];
                float z = source.vertices[vertex].z;
                CircumferentialRing ring =
                    rings.Count > 0
                        ? rings[rings.Count - 1]
                        : null;
                if (ring == null ||
                    Mathf.Abs(z - ring.center.z) > tolerance)
                {
                    ring = new CircumferentialRing
                    {
                        center = source.vertices[vertex],
                    };
                    rings.Add(ring);
                }
                else
                {
                    ring.center =
                        (ring.center * ring.vertices.Count +
                         source.vertices[vertex]) /
                        (ring.vertices.Count + 1);
                }
                ring.vertices.Add(vertex);
            }

            if (rings.Count < 2)
            {
                failReason =
                    $"validated planar contract has too few stations. rings={rings.Count}";
                return false;
            }

            int expectedRingVertexCount = rings[0].vertices.Count;
            if (expectedRingVertexCount < 3)
            {
                failReason =
                    $"validated planar contract has a small first ring. count={expectedRingVertexCount}";
                return false;
            }

            float stationDistance = 0f;
            List<int> previousOrdered = null;
            Vector3 previousCenter = Vector3.zero;
            for (int station = 0; station < rings.Count; station++)
            {
                CircumferentialRing ring = rings[station];
                if (ring.vertices.Count != expectedRingVertexCount)
                {
                    failReason =
                        $"validated planar ring size mismatch. station={station} " +
                        $"expected={expectedRingVertexCount} actual={ring.vertices.Count}";
                    return false;
                }

                Vector3 center =
                    ComputeCircumferentialRingCenter(
                        source.vertices,
                        ring.vertices);
                ring.vertices.Sort(
                    (a, b) =>
                    {
                        Vector3 pa = source.vertices[a] - center;
                        Vector3 pb = source.vertices[b] - center;
                        return Mathf.Atan2(pa.y, pa.x).CompareTo(
                            Mathf.Atan2(pb.y, pb.x));
                    });
                if (previousOrdered != null)
                {
                    List<int> aligned =
                        AlignCircumferentialRingToPrevious(
                            source.vertices,
                            previousOrdered,
                            ring.vertices);
                    ring.vertices.Clear();
                    ring.vertices.AddRange(aligned);
                    stationDistance +=
                        Vector3.Distance(previousCenter, center);
                }

                ring.center = center;
                ring.stationDistance = stationDistance;
                for (int i = 0; i < ring.vertices.Count; i++)
                    vertexStation[ring.vertices[i]] = station;
                previousOrdered = new List<int>(ring.vertices);
                previousCenter = center;
            }

            int invalidTriangleStationSpan = 0;
            for (int i = 0; i + 2 < source.triangles.Count; i += 3)
            {
                int a = source.triangles[i + 0];
                int b = source.triangles[i + 1];
                int c = source.triangles[i + 2];
                if (!IsCircumferentialValidIndex(a, vertexStation.Length) ||
                    !IsCircumferentialValidIndex(b, vertexStation.Length) ||
                    !IsCircumferentialValidIndex(c, vertexStation.Length))
                {
                    invalidTriangleStationSpan++;
                    continue;
                }

                int minStation = Mathf.Min(
                    vertexStation[a],
                    Mathf.Min(vertexStation[b], vertexStation[c]));
                int maxStation = Mathf.Max(
                    vertexStation[a],
                    Mathf.Max(vertexStation[b], vertexStation[c]));
                if (minStation < 0 || maxStation - minStation > 1)
                    invalidTriangleStationSpan++;
            }
            if (invalidTriangleStationSpan > 0 ||
                stationDistance <= 1e-6f)
            {
                failReason =
                    $"validated planar topology mismatch. badTriangleSpans={invalidTriangleStationSpan} " +
                    $"centerlineLength={stationDistance:0.####}";
                return false;
            }

            summary =
                $"stationContract=validated-planar-z topologyRings={rings.Count} " +
                $"ringVerts={expectedRingVertexCount} assigned={source.vertices.Count}/{source.vertices.Count} " +
                $"centerlineLength={stationDistance:0.####} endpointOrientation=localZ";
            return true;
        }

        private static List<HashSet<int>> BuildCircumferentialVertexAdjacency(
            int vertexCount,
            List<int> triangles)
        {
            var adjacency = new List<HashSet<int>>(Mathf.Max(0, vertexCount));
            for (int i = 0; i < vertexCount; i++)
                adjacency.Add(new HashSet<int>());

            if (triangles == null)
                return adjacency;

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                AddCircumferentialUndirectedAdjacency(adjacency, a, b);
                AddCircumferentialUndirectedAdjacency(adjacency, b, c);
                AddCircumferentialUndirectedAdjacency(adjacency, c, a);
            }
            return adjacency;
        }

        private static void AddCircumferentialUndirectedAdjacency(
            List<HashSet<int>> adjacency,
            int a,
            int b)
        {
            if (adjacency == null ||
                !IsCircumferentialValidIndex(a, adjacency.Count) ||
                !IsCircumferentialValidIndex(b, adjacency.Count) ||
                a == b)
                return;

            adjacency[a].Add(b);
            adjacency[b].Add(a);
        }

        private static List<List<int>> BuildCircumferentialActiveBoundaryLoops(
            List<int> triangles,
            bool[] activeVertex,
            out string failReason)
        {
            failReason = string.Empty;
            var edgeMultiplicity = new Dictionary<ulong, int>();
            if (triangles == null || activeVertex == null)
            {
                failReason = "triangles or active-vertex mask is null";
                return null;
            }

            for (int i = 0; i + 2 < triangles.Count; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsCircumferentialValidIndex(a, activeVertex.Length) ||
                    !IsCircumferentialValidIndex(b, activeVertex.Length) ||
                    !IsCircumferentialValidIndex(c, activeVertex.Length) ||
                    !activeVertex[a] ||
                    !activeVertex[b] ||
                    !activeVertex[c])
                    continue;

                IncrementCircumferentialEdgeMultiplicity(edgeMultiplicity, a, b);
                IncrementCircumferentialEdgeMultiplicity(edgeMultiplicity, b, c);
                IncrementCircumferentialEdgeMultiplicity(edgeMultiplicity, c, a);
            }

            var boundaryAdjacency = new Dictionary<int, List<int>>();
            foreach (KeyValuePair<ulong, int> pair in edgeMultiplicity)
            {
                if (pair.Value != 1)
                    continue;

                DecodeCircumferentialEdgeKey(pair.Key, out int a, out int b);
                AddCircumferentialAdjacency(boundaryAdjacency, a, b);
                AddCircumferentialAdjacency(boundaryAdjacency, b, a);
            }

            foreach (KeyValuePair<int, List<int>> pair in boundaryAdjacency)
            {
                if (pair.Value.Count != 2)
                {
                    failReason =
                        $"active boundary is not a simple loop graph. vertex={pair.Key} degree={pair.Value.Count}";
                    return null;
                }
            }

            var loops = new List<List<int>>();
            var unvisited = new HashSet<int>(boundaryAdjacency.Keys);
            while (unvisited.Count > 0)
            {
                int start = int.MaxValue;
                foreach (int vertex in unvisited)
                    start = Mathf.Min(start, vertex);

                var loop = new List<int>();
                int previous = -1;
                int current = start;
                int guard = boundaryAdjacency.Count + 1;
                while (guard-- > 0)
                {
                    if (current == start && loop.Count > 0)
                        break;
                    if (!unvisited.Remove(current))
                    {
                        failReason =
                            $"active boundary loop revisited vertex before closure. vertex={current}";
                        return null;
                    }

                    loop.Add(current);
                    List<int> neighbors = boundaryAdjacency[current];
                    int next =
                        neighbors[0] != previous
                            ? neighbors[0]
                            : neighbors[1];
                    previous = current;
                    current = next;
                }

                if (current != start || loop.Count < 3)
                {
                    failReason =
                        $"active boundary component did not close as a simple loop. start={start} count={loop.Count}";
                    return null;
                }
                loops.Add(loop);
            }

            return loops;
        }

        private static void IncrementCircumferentialEdgeMultiplicity(
            Dictionary<ulong, int> multiplicity,
            int a,
            int b)
        {
            if (multiplicity == null || a == b || a < 0 || b < 0)
                return;

            ulong key = MakeUndirectedEdgeKey(a, b);
            multiplicity.TryGetValue(key, out int count);
            multiplicity[key] = count + 1;
        }

        private static int CountCircumferentialConnections(
            List<int> fromRing,
            List<int> candidateRing,
            List<HashSet<int>> adjacency)
        {
            if (fromRing == null ||
                candidateRing == null ||
                adjacency == null)
                return 0;

            var candidateSet = new HashSet<int>(candidateRing);
            int count = 0;
            for (int i = 0; i < fromRing.Count; i++)
            {
                int vertex = fromRing[i];
                if (!IsCircumferentialValidIndex(vertex, adjacency.Count))
                    continue;

                foreach (int neighbor in adjacency[vertex])
                {
                    if (candidateSet.Contains(neighbor))
                        count++;
                }
            }
            return count;
        }

        private static List<int> AlignCircumferentialRingToPrevious(
            List<Vector3> vertices,
            List<int> previousRing,
            List<int> currentRing)
        {
            var aligned = new List<int>();
            if (vertices == null ||
                previousRing == null ||
                currentRing == null ||
                previousRing.Count < 3 ||
                previousRing.Count != currentRing.Count)
                return aligned;

            int count = previousRing.Count;
            float bestCost = float.PositiveInfinity;
            int bestShift = 0;
            bool bestReverse = false;
            for (int reverseValue = 0; reverseValue <= 1; reverseValue++)
            {
                bool reverse = reverseValue != 0;
                for (int shift = 0; shift < count; shift++)
                {
                    float cost = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        int currentIndex = reverse
                            ? (shift - i + count) % count
                            : (shift + i) % count;
                        int previousVertex = previousRing[i];
                        int currentVertex = currentRing[currentIndex];
                        if (!IsCircumferentialValidIndex(previousVertex, vertices.Count) ||
                            !IsCircumferentialValidIndex(currentVertex, vertices.Count))
                        {
                            cost = float.PositiveInfinity;
                            break;
                        }
                        cost +=
                            (vertices[previousVertex] -
                             vertices[currentVertex]).sqrMagnitude;
                    }

                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestShift = shift;
                        bestReverse = reverse;
                    }
                }
            }

            if (float.IsInfinity(bestCost))
                return aligned;

            for (int i = 0; i < count; i++)
            {
                int currentIndex = bestReverse
                    ? (bestShift - i + count) % count
                    : (bestShift + i) % count;
                aligned.Add(currentRing[currentIndex]);
            }
            return aligned;
        }

        private static Vector3 ComputeCircumferentialRingCenter(
            List<Vector3> vertices,
            List<int> ring)
        {
            if (vertices == null || ring == null || ring.Count == 0)
                return Vector3.zero;

            Vector3 sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < ring.Count; i++)
            {
                int vertex = ring[i];
                if (!IsCircumferentialValidIndex(vertex, vertices.Count))
                    continue;
                sum += vertices[vertex];
                count++;
            }
            return count > 0 ? sum / count : Vector3.zero;
        }

        private static bool TryFindCircumferentialBracketRings(
            List<CircumferentialRing> rings,
            float targetStationDistance,
            out int lowerRingIndex,
            out int upperRingIndex)
        {
            lowerRingIndex = -1;
            upperRingIndex = -1;
            if (rings == null || rings.Count < 2)
                return false;

            float totalDistance =
                rings[rings.Count - 1].stationDistance;
            float tol = Mathf.Max(1e-7f, totalDistance * 1e-5f);
            for (int i = 0; i < rings.Count; i++)
            {
                if (Mathf.Abs(
                        targetStationDistance -
                        rings[i].stationDistance) <= tol)
                {
                    if (i > 0 && i + 1 < rings.Count)
                    {
                        lowerRingIndex = i - 1;
                        upperRingIndex = i + 1;
                        return true;
                    }

                    if (i == 0 && rings.Count > 1)
                    {
                        lowerRingIndex = 0;
                        upperRingIndex = 1;
                        return true;
                    }

                    if (i == rings.Count - 1 && rings.Count > 1)
                    {
                        lowerRingIndex = rings.Count - 2;
                        upperRingIndex = rings.Count - 1;
                        return true;
                    }
                }
            }

            for (int i = 0; i + 1 < rings.Count; i++)
            {
                if (targetStationDistance > rings[i].stationDistance + tol &&
                    targetStationDistance < rings[i + 1].stationDistance - tol)
                {
                    lowerRingIndex = i;
                    upperRingIndex = i + 1;
                    return true;
                }
            }

            return false;
        }

        private static List<CircumferentialRingVertex> BuildCircumferentialOrderedRingVertices(
            List<int> ringVertices)
        {
            var ordered = new List<CircumferentialRingVertex>();
            if (ringVertices == null)
                return ordered;

            for (int i = 0; i < ringVertices.Count; i++)
            {
                int index = ringVertices[i];
                ordered.Add(
                    new CircumferentialRingVertex
                    {
                        index = index,
                        angle = i,
                    });
            }
            return ordered;
        }

        private static void AddCircumferentialWholeTriangle(
            MeshWorkData source,
            MeshWorkData output,
            int a,
            int b,
            int c,
            bool upper,
            int[] lowerMap,
            int[] upperMap,
            Vector3 lowerOffset,
            Vector3 upperOffset)
        {
            int oa = GetCircumferentialOriginalSideVertex(source, output, a, upper, lowerMap, upperMap, lowerOffset, upperOffset);
            int ob = GetCircumferentialOriginalSideVertex(source, output, b, upper, lowerMap, upperMap, lowerOffset, upperOffset);
            int oc = GetCircumferentialOriginalSideVertex(source, output, c, upper, lowerMap, upperMap, lowerOffset, upperOffset);
            AddCircumferentialTriangleIfValid(output, oa, ob, oc);
        }

        private static int GetCircumferentialOriginalSideVertex(
            MeshWorkData source,
            MeshWorkData output,
            int sourceIndex,
            bool upper,
            int[] lowerMap,
            int[] upperMap,
            Vector3 lowerOffset,
            Vector3 upperOffset)
        {
            if (source == null || output == null || !IsCircumferentialValidIndex(sourceIndex, source.vertices.Count))
                return -1;

            int[] map = upper ? upperMap : lowerMap;
            if (map != null && sourceIndex >= 0 && sourceIndex < map.Length && map[sourceIndex] >= 0)
                return map[sourceIndex];

            Vector3 offset = upper ? upperOffset : lowerOffset;
            int index = AddCircumferentialCopiedVertex(source, output, sourceIndex, offset);
            if (map != null && sourceIndex >= 0 && sourceIndex < map.Length)
                map[sourceIndex] = index;
            return index;
        }

        private static int AddCircumferentialCopiedVertex(MeshWorkData source, MeshWorkData output, int sourceIndex, Vector3 offset)
        {
            int index = output.vertices.Count;
            output.vertices.Add(source.vertices[sourceIndex] + offset);

            if (output.hasUV)
                output.uv.Add(source.hasUV && source.uv.Count > sourceIndex ? source.uv[sourceIndex] : Vector2.zero);

            if (output.hasTangents)
                output.tangents.Add(source.hasTangents && source.tangents.Count > sourceIndex ? source.tangents[sourceIndex] : Vector4.zero);

            if (output.hasColors)
                output.colors.Add(source.hasColors && source.colors.Count > sourceIndex ? source.colors[sourceIndex] : Color.white);

            return index;
        }

        private static int AddCircumferentialInterpolatedVertex(
            MeshWorkData source,
            MeshWorkData output,
            int lowerSourceIndex,
            int upperSourceIndex,
            float t,
            Vector3 cutLocal,
            Vector3 offset)
        {
            int index = output.vertices.Count;
            output.vertices.Add(cutLocal + offset);

            if (output.hasUV)
            {
                Vector2 lowerUv = source.hasUV && source.uv.Count > lowerSourceIndex ? source.uv[lowerSourceIndex] : Vector2.zero;
                Vector2 upperUv = source.hasUV && source.uv.Count > upperSourceIndex ? source.uv[upperSourceIndex] : lowerUv;
                output.uv.Add(Vector2.Lerp(lowerUv, upperUv, t));
            }

            if (output.hasTangents)
            {
                Vector4 lowerTangent = source.hasTangents && source.tangents.Count > lowerSourceIndex ? source.tangents[lowerSourceIndex] : Vector4.zero;
                Vector4 upperTangent = source.hasTangents && source.tangents.Count > upperSourceIndex ? source.tangents[upperSourceIndex] : lowerTangent;
                output.tangents.Add(Vector4.Lerp(lowerTangent, upperTangent, t));
            }

            if (output.hasColors)
            {
                Color lowerColor = source.hasColors && source.colors.Count > lowerSourceIndex ? source.colors[lowerSourceIndex] : Color.white;
                Color upperColor = source.hasColors && source.colors.Count > upperSourceIndex ? source.colors[upperSourceIndex] : lowerColor;
                output.colors.Add(Color.Lerp(lowerColor, upperColor, t));
            }

            return index;
        }

        private static void AddCircumferentialTriangleIfValid(MeshWorkData output, int a, int b, int c)
        {
            if (output == null)
                return;
            if (!IsCircumferentialValidIndex(a, output.vertices.Count) || !IsCircumferentialValidIndex(b, output.vertices.Count) || !IsCircumferentialValidIndex(c, output.vertices.Count))
                return;
            if (a == b || b == c || c == a)
                return;

            Vector3 ab = output.vertices[b] - output.vertices[a];
            Vector3 ac = output.vertices[c] - output.vertices[a];
            if (Vector3.Cross(ab, ac).sqrMagnitude <= 1e-14f)
                return;

            output.triangles.Add(a);
            output.triangles.Add(b);
            output.triangles.Add(c);
        }

        private static Bounds ComputeCircumferentialLocalBounds(List<Vector3> vertices)
        {
            if (vertices == null || vertices.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            for (int i = 1; i < vertices.Count; i++)
                bounds.Encapsulate(vertices[i]);
            return bounds;
        }

        private static bool IsCircumferentialValidIndex(int index, int count)
        {
            return index >= 0 && index < count;
        }

        private static float CircumferentialWorldDistanceToLocalDirection(
            Transform transform,
            Vector3 localDirection,
            float worldDistance)
        {
            float distance = Mathf.Max(0f, worldDistance);
            if (transform == null)
                return distance;

            Vector3 direction =
                localDirection.sqrMagnitude > 1e-12f
                    ? localDirection.normalized
                    : Vector3.forward;
            float worldPerLocalUnit =
                transform.TransformVector(direction).magnitude;
            if (worldPerLocalUnit <= 1e-6f)
                return distance;
            return distance / worldPerLocalUnit;
        }

        private static Vector3 TransformPointSafe(Transform transform, Vector3 localPoint)
        {
            return transform != null ? transform.TransformPoint(localPoint) : localPoint;
        }

        private static List<CircumferentialBoundaryEdge> CollectCircumferentialBoundaryEdges(List<int> triangles, bool[] upperTri, int originalVertexCount)
        {
            var edgeSideMask = new Dictionary<ulong, int>();
            int triCount = triangles.Count / 3;
            for (int tri = 0; tri < triCount; tri++)
            {
                int baseIndex = tri * 3;
                int a = triangles[baseIndex + 0];
                int b = triangles[baseIndex + 1];
                int c = triangles[baseIndex + 2];
                if (!IsCircumferentialValidIndex(a, originalVertexCount) || !IsCircumferentialValidIndex(b, originalVertexCount) || !IsCircumferentialValidIndex(c, originalVertexCount))
                {
                    a = a < originalVertexCount ? a : -1;
                    b = b < originalVertexCount ? b : -1;
                    c = c < originalVertexCount ? c : -1;
                }

                if (!IsCircumferentialValidIndex(a, originalVertexCount) || !IsCircumferentialValidIndex(b, originalVertexCount) || !IsCircumferentialValidIndex(c, originalVertexCount))
                    continue;

                int sideMask = upperTri != null && tri < upperTri.Length && upperTri[tri] ? 2 : 1;
                AddCircumferentialBoundaryEdgeSide(edgeSideMask, a, b, sideMask);
                AddCircumferentialBoundaryEdgeSide(edgeSideMask, b, c, sideMask);
                AddCircumferentialBoundaryEdgeSide(edgeSideMask, c, a, sideMask);
            }

            var edges = new List<CircumferentialBoundaryEdge>();
            foreach (var pair in edgeSideMask)
            {
                if (pair.Value != 3)
                    continue;

                DecodeCircumferentialEdgeKey(pair.Key, out int a, out int b);
                edges.Add(new CircumferentialBoundaryEdge { a = a, b = b });
            }
            return edges;
        }

        private static void AddCircumferentialBoundaryEdgeSide(Dictionary<ulong, int> edgeSideMask, int a, int b, int sideMask)
        {
            if (a == b || a < 0 || b < 0)
                return;

            ulong key = MakeUndirectedEdgeKey(a, b);
            int mask;
            edgeSideMask.TryGetValue(key, out mask);
            edgeSideMask[key] = mask | sideMask;
        }

        private static void DecodeCircumferentialEdgeKey(ulong key, out int a, out int b)
        {
            a = (int)(key >> 32);
            b = (int)(key & 0xffffffffUL);
        }

        private static List<int> OrderCircumferentialBoundaryLoop(List<CircumferentialBoundaryEdge> edges)
        {
            var ordered = new List<int>();
            if (edges == null || edges.Count == 0)
                return ordered;

            var adjacency = new Dictionary<int, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                AddCircumferentialAdjacency(adjacency, edges[i].a, edges[i].b);
                AddCircumferentialAdjacency(adjacency, edges[i].b, edges[i].a);
            }

            var visited = new HashSet<int>();
            List<int> best = null;
            foreach (var pair in adjacency)
            {
                if (visited.Contains(pair.Key))
                    continue;

                List<int> component = TraceCircumferentialComponent(pair.Key, adjacency, visited);
                if (component.Count > 1 && (best == null || component.Count > best.Count))
                    best = component;
            }

            if (best != null)
                ordered.AddRange(best);
            return ordered;
        }

        private static void AddCircumferentialAdjacency(Dictionary<int, List<int>> adjacency, int a, int b)
        {
            List<int> list;
            if (!adjacency.TryGetValue(a, out list))
            {
                list = new List<int>();
                adjacency[a] = list;
            }

            if (!list.Contains(b))
                list.Add(b);
        }

        private static List<int> TraceCircumferentialComponent(int start, Dictionary<int, List<int>> adjacency, HashSet<int> visited)
        {
            var component = new List<int>();
            if (!adjacency.ContainsKey(start))
                return component;

            int current = start;
            int previous = -1;
            int guard = Mathf.Max(8, adjacency.Count + 8);
            for (int step = 0; step < guard; step++)
            {
                component.Add(current);
                visited.Add(current);
                List<int> neighbors = adjacency[current];
                if (neighbors == null || neighbors.Count == 0)
                    break;

                int next = -1;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int candidate = neighbors[i];
                    if (candidate == previous)
                        continue;
                    next = candidate;
                    break;
                }

                if (next < 0 || next == start || component.Contains(next))
                    break;

                previous = current;
                current = next;
            }

            return component;
        }

        private static void EnsureCircumferentialClosedPolyline(List<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return;

            if ((points[0] - points[points.Count - 1]).sqrMagnitude > 1e-10f)
                points.Add(points[0]);
        }

        private static void CopyCircumferentialPolyline(List<Vector3> target, List<Vector3> source)
        {
            if (target == null)
                return;

            target.Clear();
            if (source == null)
                return;

            for (int i = 0; i < source.Count; i++)
                target.Add(source[i]);
        }

        private static float ComputeCircumferentialPolylineLength(List<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            float length = 0f;
            for (int i = 1; i < points.Count; i++)
                length += Vector3.Distance(points[i - 1], points[i]);
            return length;
        }

        private static float EstimateCircumferentialAverageSpacing(List<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            return ComputeCircumferentialPolylineLength(points) / Mathf.Max(1, points.Count - 1);
        }

        private static float ComputeCircumferentialAverageEdgeLength(Mesh mesh)
        {
            if (mesh == null)
                return 0f;

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (vertices == null || triangles == null || vertices.Length == 0 || triangles.Length < 3)
                return 0f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i + 0];
                int b = triangles[i + 1];
                int c = triangles[i + 2];
                if (!IsCircumferentialValidIndex(a, vertices.Length) || !IsCircumferentialValidIndex(b, vertices.Length) || !IsCircumferentialValidIndex(c, vertices.Length))
                    continue;

                sum += Vector3.Distance(vertices[a], vertices[b]);
                sum += Vector3.Distance(vertices[b], vertices[c]);
                sum += Vector3.Distance(vertices[c], vertices[a]);
                count += 3;
            }
            return count > 0 ? sum / count : 0f;
        }

        private static void BuildCircumferentialStations(
            LayerResult layer,
            List<Vector3> lowerBoundary,
            List<Vector3> upperBoundary,
            int[] lowerVertexIndices,
            int[] upperVertexIndices)
        {
            if (layer == null || lowerBoundary == null || upperBoundary == null ||
                lowerVertexIndices == null || upperVertexIndices == null)
                return;

            int count = Mathf.Min(lowerBoundary.Count, upperBoundary.Count);
            int ringCount = Mathf.Min(lowerVertexIndices.Length, upperVertexIndices.Length);
            if (count < 2 || ringCount < 3)
                return;

            float length = 0f;
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    length += Vector3.Distance(0.5f * (lowerBoundary[i - 1] + upperBoundary[i - 1]), 0.5f * (lowerBoundary[i] + upperBoundary[i]));

                Vector3 center = 0.5f * (lowerBoundary[i] + upperBoundary[i]);
                Vector3 binormal = upperBoundary[i] - lowerBoundary[i];
                if (binormal.sqrMagnitude <= 1e-12f)
                    binormal = Vector3.forward;
                else
                    binormal.Normalize();

                Vector3 tangent;
                if (i + 1 < count)
                    tangent = 0.5f * (lowerBoundary[i + 1] + upperBoundary[i + 1]) - center;
                else
                    tangent = center - 0.5f * (lowerBoundary[i - 1] + upperBoundary[i - 1]);
                if (tangent.sqrMagnitude <= 1e-12f)
                    tangent = Vector3.right;
                else
                    tangent.Normalize();

                Vector3 normal = Vector3.Cross(tangent, binormal);
                if (normal.sqrMagnitude <= 1e-12f)
                    normal = Vector3.up;
                else
                    normal.Normalize();

                layer.stations.Add(new StationResult
                {
                    s = length,
                    strokeT = count > 1 ? (float)i / (count - 1) : 0f,
                    targetOpen = Vector3.Distance(lowerBoundary[i], upperBoundary[i]),
                    centerWorld = center,
                    tangentWorld = tangent,
                    normalWorld = normal,
                    binormalWorld = binormal,
                    leftWorld = lowerBoundary[i],
                    rightWorld = upperBoundary[i],
                    leftVertexIndex = lowerVertexIndices[i % ringCount],
                    rightVertexIndex = upperVertexIndices[i % ringCount]
                });
            }
        }

        private static LayerResult BuildLayer(
            LayerKind layerKind,
            MeshWorkData work,
            Transform meshTransform,
            AxisFrame axis,
            List<Vector3> guideWorld,
            float desiredLengthWorld,
            float openWidthWorld,
            Options options,
            List<StationResult> referenceStations,
            List<StructuredStripReferenceRow> referenceStripRows)
        {
            var result = new LayerResult
            {
                layer = (int)layerKind,
            };

            Vector3[] worldVertices = BuildWorldVertexArray(work.vertices, meshTransform);
            List<GraphEdge>[] adjacency = BuildGraphAdjacency(work, worldVertices, out float averageEdgeLength);
            result.averageEdgeLength = averageEdgeLength;
            if (averageEdgeLength <= 1e-6f)
            {
                result.failStage = "Graph";
                result.failReason = "Average edge length is invalid.";
                return result;
            }

            float guideSpacing = options.pathSampleSpacingWorld > 1e-6f
                ? options.pathSampleSpacingWorld
                : Mathf.Max(averageEdgeLength * 0.75f, Mathf.Max(desiredLengthWorld, 1e-4f) / 32f);
            List<Vector3> denseGuide = ResamplePolyline(guideWorld, guideSpacing);
            result.guidePointCount = guideWorld != null ? guideWorld.Count : 0;
            result.denseGuidePointCount = denseGuide.Count;
            result.guideSampleSpacingWorld = guideSpacing;
            if (denseGuide.Count < 2)
            {
                result.failStage = "Guide";
                result.failReason = "Guide polyline is too short.";
                return result;
            }

            List<Vector3> chainSource;
            if (referenceStations != null && referenceStations.Count > 1)
            {
                chainSource = new List<Vector3>(denseGuide);
                result.usedFallbackPath = true;
            }
            else
            {
                List<int> anchors = BuildAnchorVertices(denseGuide, worldVertices, Mathf.Max(4, options.maxAnchorCount));
                if (anchors.Count < 2)
                {
                    result.failStage = "Anchors";
                    result.failReason = "Failed to build enough guide anchors.";
                    return result;
                }

                List<int> path = BuildMasterPath(worldVertices, adjacency, denseGuide, anchors, Mathf.Max(0f, options.pathSnapPenalty));
                if (path.Count < 2)
                {
                    result.failStage = "MasterChain";
                    result.failReason = "Failed to solve a continuous master chain.";
                    return result;
                }

                result.masterPathVertexIndices.AddRange(path);
                var pathWorld = new List<Vector3>(path.Count);
                for (int i = 0; i < path.Count; i++)
                    pathWorld.Add(worldVertices[path[i]]);

                chainSource = ClipChainByGuideCoverage(pathWorld, denseGuide, Mathf.Max(desiredLengthWorld, 1e-5f), out float chainLength);
                if (chainSource.Count < 2 || chainLength <= 1e-6f)
                {
                    result.failStage = "MasterChainClip";
                    result.failReason = "Clipped master chain is empty.";
                    return result;
                }
            }

            result.masterLength = ComputePolylineLength(chainSource);
            result.masterChainWorld.AddRange(chainSource);

            if (!TryBuildExplicitCenterCurve(
                    work,
                    meshTransform,
                    chainSource,
                    averageEdgeLength,
                    out List<Vector3> centerCurveWorld,
                    out bool usedCurveFallback,
                    out string centerCurveFailReason))
            {
                result.failStage = "CenterCurve";
                result.failReason = centerCurveFailReason;
                return result;
            }

            result.usedFallbackPath |= usedCurveFallback;
            result.centerCurveWorld.AddRange(centerCurveWorld);
            result.centerCurvePointCount = centerCurveWorld.Count;
            result.centerCurveLength = ComputePolylineLength(centerCurveWorld);

            List<FrameSample> canonicalFrames = referenceStations != null && referenceStations.Count > 1
                ? BuildReferenceFrames(referenceStations, centerCurveWorld, meshTransform, axis)
                : BuildCanonicalFrames(centerCurveWorld, meshTransform, axis, openWidthWorld, averageEdgeLength, options.maxStationCount);
            if (canonicalFrames.Count < 2)
            {
                result.failStage = "Stations";
                result.failReason = "Failed to build canonical stations.";
                return result;
            }

            bool remeshSuccess = ApplyExplicitBandRemesh(
                work,
                meshTransform,
                worldVertices,
                adjacency,
                centerCurveWorld,
                canonicalFrames,
                openWidthWorld,
                averageEdgeLength,
                Mathf.Max(0f, options.pathSnapPenalty),
                referenceStripRows,
                result.debugTriangles,
                (int)layerKind,
                out List<BoundaryVertexSample> leftBoundary,
                out List<BoundaryVertexSample> rightBoundary,
                out List<FrameSample> realizedFrames,
                out List<StructuredStripReferenceRow> realizedStripReferenceRows,
                out int removedTriangleCount,
                out int patchTriangleCount,
                out int keptTriangleCount,
                out int openBoundaryEdgeCount,
                out int patchComponentCount,
                out int excisionComponentCount,
                out int boundaryContinuityViolationCount,
                out float maxPatchTriangleAspect,
                out bool usedFallbackPath,
                out RemeshDiagnostics remeshDiagnostics,
                out string remeshFailStage,
                out string excisionFailReason);

            List<FrameSample> stationFrames = realizedFrames != null && realizedFrames.Count >= 2 ? realizedFrames : canonicalFrames;
            result.canonicalStationCount = stationFrames.Count;
            result.stationSpacingWorld = stationFrames.Count > 1
                ? stationFrames[stationFrames.Count - 1].s / Mathf.Max(1, stationFrames.Count - 1)
                : 0f;
            result.stripRowCount = stationFrames.Count;
            result.supportQuadCount = Mathf.Max(0, (stationFrames.Count - 1) * 2);
            result.excisionQuadCount = Mathf.Max(0, stationFrames.Count - 1);
            result.endTaperLengthWorld = EstimateTaperLengthFromFrames(stationFrames);
            result.patchHalfWidthWorld = remeshDiagnostics.patchHalfWidth;
            result.patchSPadWorld = remeshDiagnostics.patchSPad;
            result.patchNormalTolWorld = remeshDiagnostics.patchNormalTol;
            result.patchCandidateTriangleCount = remeshDiagnostics.patchCandidateTriangleCount;
            result.patchSeedTriangleCount = remeshDiagnostics.patchSeedTriangleCount;
            result.patchBestComponentTriangleCount = remeshDiagnostics.patchBestComponentTriangleCount;
            result.patchBestComponentSeedHitCount = remeshDiagnostics.patchBestComponentSeedHits;
            result.supportOffsetWorld = remeshDiagnostics.supportOffset;
            result.supportOffsetBaseWorld = remeshDiagnostics.supportOffsetBase;
            result.supportOffsetCapWorld = remeshDiagnostics.supportOffsetCap;
            result.supportStripMarginWorld = remeshDiagnostics.supportStripMargin;
            result.usedReferenceStripRows = remeshDiagnostics.usedReferenceStripRows;
            result.referenceStripRowCount = remeshDiagnostics.referenceStripRowCount;
            result.remeshKeptPolygonCount = remeshDiagnostics.keptPolygonCount;
            result.remeshRemovedPolygonCount = remeshDiagnostics.removedPolygonCount;
            result.remeshKeptPolygonVertexCount = remeshDiagnostics.keptPolygonVertexCount;
            result.remeshRemovedPolygonVertexCount = remeshDiagnostics.removedPolygonVertexCount;
            result.taggedBoundaryEdgeCount = remeshDiagnostics.taggedBoundaryEdgeCount;
            result.leftTaggedBoundaryEdgeCount = remeshDiagnostics.leftTaggedEdgeCount;
            result.rightTaggedBoundaryEdgeCount = remeshDiagnostics.rightTaggedEdgeCount;
            result.leftBoundaryComponentCount = remeshDiagnostics.leftBoundaryComponentCount;
            result.rightBoundaryComponentCount = remeshDiagnostics.rightBoundaryComponentCount;
            result.leftBoundaryComponentSizes = remeshDiagnostics.leftBoundaryComponentSizes;
            result.rightBoundaryComponentSizes = remeshDiagnostics.rightBoundaryComponentSizes;
            result.leftBoundaryComponentVertexCount = remeshDiagnostics.leftBoundaryComponentVertexCount;
            result.rightBoundaryComponentVertexCount = remeshDiagnostics.rightBoundaryComponentVertexCount;
            result.leftBoundaryOrderedVertexCount = remeshDiagnostics.leftBoundaryOrderedVertexCount;
            result.rightBoundaryOrderedVertexCount = remeshDiagnostics.rightBoundaryOrderedVertexCount;
            result.leftBoundaryDegree1VertexCount = remeshDiagnostics.leftBoundaryDegree1VertexCount;
            result.rightBoundaryDegree1VertexCount = remeshDiagnostics.rightBoundaryDegree1VertexCount;
            result.leftBoundaryDegree2VertexCount = remeshDiagnostics.leftBoundaryDegree2VertexCount;
            result.rightBoundaryDegree2VertexCount = remeshDiagnostics.rightBoundaryDegree2VertexCount;
            result.leftBoundaryBranchVertexCount = remeshDiagnostics.leftBoundaryBranchVertexCount;
            result.rightBoundaryBranchVertexCount = remeshDiagnostics.rightBoundaryBranchVertexCount;
            result.minPatchTriangleArea = remeshDiagnostics.minPatchArea;
            result.triangulationWindingMismatchCount = remeshDiagnostics.triangulationWindingMismatchCount;
            result.degeneratePatchTriangleCount = remeshDiagnostics.degeneratePatchTriangleCount;
            result.overAspectPatchTriangleCount = remeshDiagnostics.overAspectPatchTriangleCount;
            result.auditedPatchTriangleCount = remeshDiagnostics.auditedPatchTriangleCount;
            result.outsideQualityTriangleCount = remeshDiagnostics.outsideQualityTriangleCount;
            result.outsideQualityWindingMismatchCount = remeshDiagnostics.outsideQualityWindingMismatchCount;
            result.outsideQualityOverAspectCount = remeshDiagnostics.outsideQualityOverAspectCount;
            result.outsideQualityMaxAspect = remeshDiagnostics.outsideQualityMaxAspect;
            result.supportQualityTriangleCount = remeshDiagnostics.supportQualityTriangleCount;
            result.supportQualityWindingMismatchCount = remeshDiagnostics.supportQualityWindingMismatchCount;
            result.supportQualityOverAspectCount = remeshDiagnostics.supportQualityOverAspectCount;
            result.supportQualityMaxAspect = remeshDiagnostics.supportQualityMaxAspect;
            result.remeshedVertexCount = remeshDiagnostics.remeshedVertexCount;
            result.remeshedTriangleCount = remeshDiagnostics.remeshedTriangleCount;
            result.attemptedStripRowCount = remeshDiagnostics.attemptedRowCount;
            result.collapsedStripRowCount = remeshDiagnostics.collapsedRowCount;
            result.repeatedStripRowCount = remeshDiagnostics.repeatedRowCount;
            result.crossedStripRowCount = remeshDiagnostics.crossedRowCount;
            result.tipStripRowCount = remeshDiagnostics.tipRowCount;
            result.keptStartTipRow = remeshDiagnostics.keptStartTipRow;
            result.keptEndTipRow = remeshDiagnostics.keptEndTipRow;
            result.minStructuredBoundaryWidth = remeshDiagnostics.minBoundaryWidth;
            result.minStructuredSupportWidth = remeshDiagnostics.minSupportWidth;
            result.maxStructuredBoundaryJump = remeshDiagnostics.maxBoundaryJump;
            result.minLeftStructuredGap = remeshDiagnostics.minLeftGap;
            result.minCenterStructuredGap = remeshDiagnostics.minCenterGap;
            result.minRightStructuredGap = remeshDiagnostics.minRightGap;
            result.leftOrderViolationCount = remeshDiagnostics.leftOrderViolationCount;
            result.centerOrderViolationCount = remeshDiagnostics.centerOrderViolationCount;
            result.rightOrderViolationCount = remeshDiagnostics.rightOrderViolationCount;
            result.crossedRowPreview = remeshDiagnostics.crossedRowPreview;
            result.leftSupportAnchorCount = remeshDiagnostics.leftSupportAnchorCount;
            result.leftBoundaryAnchorCount = remeshDiagnostics.leftBoundaryAnchorCount;
            result.rightBoundaryAnchorCount = remeshDiagnostics.rightBoundaryAnchorCount;
            result.rightSupportAnchorCount = remeshDiagnostics.rightSupportAnchorCount;
            result.leftSupportPathVertexCount = remeshDiagnostics.leftSupportPathVertexCount;
            result.leftBoundaryPathVertexCount = remeshDiagnostics.leftBoundaryPathVertexCount;
            result.rightBoundaryPathVertexCount = remeshDiagnostics.rightBoundaryPathVertexCount;
            result.rightSupportPathVertexCount = remeshDiagnostics.rightSupportPathVertexCount;
            result.leftSupportSampleCount = remeshDiagnostics.leftSupportSampleCount;
            result.leftBoundarySampleCount = remeshDiagnostics.leftBoundarySampleCount;
            result.rightBoundarySampleCount = remeshDiagnostics.rightBoundarySampleCount;
            result.rightSupportSampleCount = remeshDiagnostics.rightSupportSampleCount;
            result.rowDefinitionMismatchCount = remeshDiagnostics.rowDefinitionMismatchCount;
            result.rowDefinitionSummary = remeshDiagnostics.rowDefinitionSummary;
            result.taperClipInputSummary = remeshDiagnostics.taperClipInputSummary;
            result.stripCoveredTriangleCount = remeshDiagnostics.stripCoveredTriangleCount;
            result.supportCoveredTriangleCount = remeshDiagnostics.supportCoveredTriangleCount;
            result.excisionCoveredTriangleCount = remeshDiagnostics.excisionCoveredTriangleCount;
            result.patchOutsideStripTriangleCount = remeshDiagnostics.patchOutsideStripTriangleCount;
            result.changedOutsideStripTriangleCount = remeshDiagnostics.changedOutsideStripTriangleCount;
            result.clippedPatchTriangleCount = remeshDiagnostics.clippedPatchTriangleCount;
            result.preservedOutsideFragmentCount = remeshDiagnostics.preservedOutsideFragmentCount;
            result.discardedSupportFragmentCount = remeshDiagnostics.discardedSupportFragmentCount;
            result.discardedExcisionFragmentCount = remeshDiagnostics.discardedExcisionFragmentCount;
            result.usedLatticePartition = remeshDiagnostics.usedLatticePartition;
            result.visibleBoundaryBuiltFromTaggedEdges = remeshDiagnostics.visibleBoundaryBuiltFromTaggedEdges;
            result.supportPartitionPolygonCount = remeshDiagnostics.supportPartitionPolygonCount;
            result.excisionPartitionPolygonCount = remeshDiagnostics.excisionPartitionPolygonCount;
            result.supportMergedPolygonCount = remeshDiagnostics.supportMergedPolygonCount;
            result.excisionMergedPolygonCount = remeshDiagnostics.excisionMergedPolygonCount;
            result.interfaceNormalizationSourceEdgeKeyCount = remeshDiagnostics.interfaceNormalizationSourceEdgeKeyCount;
            result.interfaceNormalizationSourceEdgeInsertCount = remeshDiagnostics.interfaceNormalizationSourceEdgeInsertCount;
            result.interfaceNormalizationBoundarySegmentKeyCount = remeshDiagnostics.interfaceNormalizationBoundarySegmentKeyCount;
            result.interfaceNormalizationBoundaryInsertCount = remeshDiagnostics.interfaceNormalizationBoundaryInsertCount;
            result.interfaceNormalizationPolygonsTouchedCount = remeshDiagnostics.interfaceNormalizationPolygonsTouchedCount;
            result.interfaceScanSegmentCount = remeshDiagnostics.interfaceScanSegmentCount;
            result.interfaceScanMatchedOutsideSupportCount = remeshDiagnostics.interfaceScanMatchedOutsideSupportCount;
            result.interfaceScanMatchedSupportExcisionCount = remeshDiagnostics.interfaceScanMatchedSupportExcisionCount;
            result.interfaceScanOutsideExcisionCount = remeshDiagnostics.interfaceScanOutsideExcisionCount;
            result.interfaceScanSingleSideCount = remeshDiagnostics.interfaceScanSingleSideCount;
            result.interfaceScanMultiUseCount = remeshDiagnostics.interfaceScanMultiUseCount;
            result.interfaceScanTripleUseCount = remeshDiagnostics.interfaceScanTripleUseCount;
            result.interfaceHardMismatchEdgeCount = remeshDiagnostics.interfaceHardMismatchEdgeCount;
            result.interfaceAuditMismatchEdgeCount = remeshDiagnostics.interfaceAuditMismatchEdgeCount;
            result.interfaceScanByFlagSummary = remeshDiagnostics.interfaceScanByFlagSummary;
            result.interfaceScanByRowSummary = remeshDiagnostics.interfaceScanByRowSummary;
            result.interfaceScanOwnershipSummary = remeshDiagnostics.interfaceScanOwnershipSummary;
            result.interfaceFeCategorySummary = remeshDiagnostics.interfaceFeCategorySummary;
            result.interfaceFeActualMaskSummary = remeshDiagnostics.interfaceFeActualMaskSummary;
            result.interfaceScanDetailSummary = remeshDiagnostics.interfaceScanDetailSummary;
            result.tipOwnershipSummary = remeshDiagnostics.tipOwnershipSummary;
            result.tipInterfaceKeyCompareSummary = remeshDiagnostics.tipInterfaceKeyCompareSummary;
            result.startTipWedgeHelperSummary = remeshDiagnostics.startTipWedgeHelperSummary;
            result.startTipWedgeSourceNormSummary = remeshDiagnostics.startTipWedgeSourceNormSummary;
            result.startTipWedgeBoundaryNormSummary = remeshDiagnostics.startTipWedgeBoundaryNormSummary;
            result.startTipWedgeCanonicalSummary = remeshDiagnostics.startTipWedgeCanonicalSummary;
            result.startTipWedgePostCanonicalSourceNormSummary = remeshDiagnostics.startTipWedgePostCanonicalSourceNormSummary;
            result.startTipWedgePostCanonicalBoundaryNormSummary = remeshDiagnostics.startTipWedgePostCanonicalBoundaryNormSummary;
            result.endTipEarlyHelperProbeSummary = remeshDiagnostics.endTipEarlyHelperProbeSummary;
            result.endTipOrdinaryAuditSummary = remeshDiagnostics.endTipOrdinaryAuditSummary;
            result.interfaceRepairNormalizedSegmentCount = remeshDiagnostics.interfaceRepairNormalizedSegmentCount;
            result.interfaceRepairVertexReuseCount = remeshDiagnostics.interfaceRepairVertexReuseCount;
            result.interfaceRepairVertexInsertCount = remeshDiagnostics.interfaceRepairVertexInsertCount;
            result.interfaceRepairOutsideTouchedCount = remeshDiagnostics.interfaceRepairOutsideTouchedCount;
            result.interfaceRepairSupportTouchedCount = remeshDiagnostics.interfaceRepairSupportTouchedCount;
            result.interfaceRepairExcisionTouchedCount = remeshDiagnostics.interfaceRepairExcisionTouchedCount;
            result.interfaceRepairResidualMismatchCount = remeshDiagnostics.interfaceRepairResidualMismatchCount;
            result.cellMergeFallbackCount = remeshDiagnostics.cellMergeFallbackCount;
            result.cellTopologyCellCount = remeshDiagnostics.cellTopologyCellCount;
            result.cellTopologyTracedCount = remeshDiagnostics.cellTopologyTracedCount;
            result.cellMembershipScanCellCount = remeshDiagnostics.cellMembershipScanCellCount;
            result.cellMembershipScanMismatchCount = remeshDiagnostics.cellMembershipScanMismatchCount;
            result.cellMembershipScanSummary = remeshDiagnostics.cellMembershipScanSummary;
            result.failedCellRowIndex = remeshDiagnostics.failedCellRowIndex;
            result.failedCellColumnIndex = remeshDiagnostics.failedCellColumnIndex;
            result.failedCellBoundaryEdgeCount = remeshDiagnostics.failedCellBoundaryEdgeCount;
            result.failedCellBoundaryVertexCount = remeshDiagnostics.failedCellBoundaryVertexCount;
            result.failedCellLoopCount = remeshDiagnostics.failedCellLoopCount;
            result.failedCellDegree1VertexCount = remeshDiagnostics.failedCellDegree1VertexCount;
            result.failedCellDegree2VertexCount = remeshDiagnostics.failedCellDegree2VertexCount;
            result.failedCellBranchVertexCount = remeshDiagnostics.failedCellBranchVertexCount;
            result.failedCellSelfIntersection = remeshDiagnostics.failedCellSelfIntersection;
            result.failedCellSemanticCoverage = remeshDiagnostics.failedCellSemanticCoverage;
            result.failedCellReason = remeshDiagnostics.failedCellReason;
            result.failedCellDefinitionMismatchCount = remeshDiagnostics.failedCellDefinitionMismatchCount;
            result.failedCellDefinitionSummary = remeshDiagnostics.failedCellDefinitionSummary;
            result.failedCellMembershipSummary = remeshDiagnostics.failedCellMembershipSummary;
            result.failedCellSideTopologySummary = remeshDiagnostics.failedCellSideTopologySummary;
            result.failedCellCornerResolutionSummary = remeshDiagnostics.failedCellCornerResolutionSummary;
            result.failedCellOpenEndpointSummary = remeshDiagnostics.failedCellOpenEndpointSummary;
            result.failedCellBranchSummary = remeshDiagnostics.failedCellBranchSummary;
            result.cellPolygonScanCount = remeshDiagnostics.cellPolygonScanCount;
            result.cellPolygonIssueCount = remeshDiagnostics.cellPolygonIssueCount;
            result.cellPolygonConcaveCount = remeshDiagnostics.cellPolygonConcaveCount;
            result.cellPolygonSelfIntersectionCount = remeshDiagnostics.cellPolygonSelfIntersectionCount;
            result.cellPolygonRepeatedVertexCount = remeshDiagnostics.cellPolygonRepeatedVertexCount;
            result.cellPolygonTinyEdgeCount = remeshDiagnostics.cellPolygonTinyEdgeCount;
            result.cellPolygonTinyAngleCount = remeshDiagnostics.cellPolygonTinyAngleCount;
            result.cellPolygonScanSummary = remeshDiagnostics.cellPolygonScanSummary;
            result.failedCellPolygonSummary = remeshDiagnostics.failedCellPolygonSummary;
            result.interfaceMismatchEdgeCount = remeshDiagnostics.interfaceMismatchEdgeCount;
            result.failedInterfaceEdgeSummary = remeshDiagnostics.failedInterfaceEdgeSummary;
            result.outsidePartitionTriangleCount = remeshDiagnostics.outsidePartitionTriangleCount;
            result.supportPartitionTriangleCount = remeshDiagnostics.supportPartitionTriangleCount;
            result.droppedDegeneratePartitionPolygonCount = remeshDiagnostics.droppedDegeneratePartitionPolygonCount;
            result.boundarySourceSummary = remeshDiagnostics.boundarySourceSummary;
            result.keptOutsideTriangleCount = remeshDiagnostics.keptOutsideTriangleCount;
            result.keptSupportTriangleCount = remeshDiagnostics.keptSupportTriangleCount;
            result.syncSummary = remeshDiagnostics.syncSummary;
            result.projectionAlignSummary = remeshDiagnostics.projectionAlignSummary;
            result.leftBoundaryPolylineLength = remeshDiagnostics.leftBoundaryPolylineLength;
            result.rightBoundaryPolylineLength = remeshDiagnostics.rightBoundaryPolylineLength;
            result.leftBoundarySMin = remeshDiagnostics.leftBoundarySMin;
            result.leftBoundarySMax = remeshDiagnostics.leftBoundarySMax;
            result.rightBoundarySMin = remeshDiagnostics.rightBoundarySMin;
            result.rightBoundarySMax = remeshDiagnostics.rightBoundarySMax;
            if (remeshDiagnostics.precommitRowCentersWorld != null)
                result.precommitRowCentersWorld.AddRange(remeshDiagnostics.precommitRowCentersWorld);
            if (remeshDiagnostics.precommitLeftBoundaryWorld != null)
                result.precommitLeftBoundaryWorld.AddRange(remeshDiagnostics.precommitLeftBoundaryWorld);
            if (remeshDiagnostics.precommitRightBoundaryWorld != null)
                result.precommitRightBoundaryWorld.AddRange(remeshDiagnostics.precommitRightBoundaryWorld);
            if (remeshDiagnostics.precommitLeftSupportWorld != null)
                result.precommitLeftSupportWorld.AddRange(remeshDiagnostics.precommitLeftSupportWorld);
            if (remeshDiagnostics.precommitRightSupportWorld != null)
                result.precommitRightSupportWorld.AddRange(remeshDiagnostics.precommitRightSupportWorld);
            if (remeshDiagnostics.precommitFailedOwnershipEdgesWorld != null)
                result.precommitFailedOwnershipEdgesWorld.AddRange(remeshDiagnostics.precommitFailedOwnershipEdgesWorld);
            if (remeshDiagnostics.precommitFailedOwnershipEdgeLabels != null)
                result.precommitFailedOwnershipEdgeLabels.AddRange(remeshDiagnostics.precommitFailedOwnershipEdgeLabels);
            result.projectionAlignSummary = BuildProjectionAlignSummary(
                guideWorld,
                result.masterChainWorld,
                result.centerCurveWorld,
                result.precommitRowCentersWorld,
                result.precommitLeftBoundaryWorld,
                result.precommitRightBoundaryWorld,
                result.precommitLeftSupportWorld,
                result.precommitRightSupportWorld,
                result.precommitFailedOwnershipEdgesWorld);
            result.removedTriangleCount = removedTriangleCount;
            result.patchTriangleCount = patchTriangleCount;
            result.keptTriangleCount = keptTriangleCount;
            result.openBoundaryEdgeCount = openBoundaryEdgeCount;
            result.patchComponentCount = patchComponentCount;
            result.excisionComponentCount = excisionComponentCount;
            result.boundaryContinuityViolationCount = boundaryContinuityViolationCount;
            result.maxPatchTriangleAspect = maxPatchTriangleAspect;
            result.usedBoundaryVertexFallback = usedFallbackPath;
            result.usedFallbackPath |= usedFallbackPath;
            result.leftBoundaryCount = leftBoundary != null ? leftBoundary.Count : 0;
            result.rightBoundaryCount = rightBoundary != null ? rightBoundary.Count : 0;
            if (leftBoundary != null)
            {
                for (int i = 0; i < leftBoundary.Count; i++)
                    result.leftBoundaryWorld.Add(leftBoundary[i].world);
            }
            if (rightBoundary != null)
            {
                for (int i = 0; i < rightBoundary.Count; i++)
                    result.rightBoundaryWorld.Add(rightBoundary[i].world);
            }

            if (!remeshSuccess)
            {
                result.failStage = string.IsNullOrEmpty(remeshFailStage) ? "Excision" : remeshFailStage;
                result.failReason = string.IsNullOrEmpty(excisionFailReason)
                    ? "No excision region was found."
                    : excisionFailReason;
                return result;
            }

            result.structuredStripReferenceRows.Clear();
            if (realizedStripReferenceRows != null)
                result.structuredStripReferenceRows.AddRange(realizedStripReferenceRows);

            BuildStationPairsFromBoundaries(stationFrames, leftBoundary, rightBoundary, openWidthWorld, result.stations);
            if (result.stations.Count < 2)
            {
                result.failStage = "StationPairs";
                result.failReason = "Failed to build synchronized station pairs.";
                return result;
            }

            for (int i = 0; i < result.stations.Count; i++)
                result.centerlineWorld.Add(result.stations[i].centerWorld);
            result.averageRealizedOpenWidth = ComputeAverageRealizedOpenWidth(result.stations);
            result.mesh = new Mesh
            {
                name = layerKind == LayerKind.Outer ? "OuterGreenfieldIncisionMesh" : "InnerGreenfieldIncisionMesh",
            };
            work.ApplyToMesh(result.mesh);
            result.success = true;
            return result;
        }

        private static AxisFrame BuildAxisFrame(Mesh mesh, Vector3 centerOffsetLocal)
        {
            Bounds bounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
            var axis = new AxisFrame
            {
                bounds = bounds,
                centerOffsetLocal = centerOffsetLocal,
                stationCentersLocal = new List<Vector3>(),
                stationTangentsLocal = new List<Vector3>(),
                stationRightLocal = new List<Vector3>(),
                stationUpLocal = new List<Vector3>(),
                stationRadiiLocal = new List<float>(),
                stationDistancesLocal = new List<float>(),
                totalStationDistanceLocal = 0f,
                stationSourceSummary = string.Empty,
                hasSweepStations = false,
            };

            MeshWorkData source = MeshWorkData.FromMesh(mesh);
            if (!TryBuildCircumferentialStationRings(
                    source,
                    out List<CircumferentialRing> rings,
                    out _,
                    out string stationSummary,
                    out string stationReason) ||
                rings == null ||
                rings.Count < 2)
            {
                axis.stationSourceSummary =
                    $"unavailable reason={stationReason}";
                return axis;
            }

            for (int station = 0; station < rings.Count; station++)
            {
                CircumferentialRing ring = rings[station];
                Vector3 before =
                    rings[Mathf.Max(0, station - 1)].center;
                Vector3 after =
                    rings[Mathf.Min(rings.Count - 1, station + 1)].center;
                Vector3 tangent = after - before;
                if (tangent.sqrMagnitude <= 1e-12f)
                    tangent = Vector3.forward;
                tangent.Normalize();

                Vector3 up =
                    Vector3.ProjectOnPlane(Vector3.up, tangent);
                if (up.sqrMagnitude <= 1e-12f)
                    up = Vector3.ProjectOnPlane(Vector3.right, tangent);
                if (up.sqrMagnitude <= 1e-12f)
                    up = Vector3.up;
                up.Normalize();
                Vector3 right =
                    Vector3.Cross(up, tangent).normalized;
                up = Vector3.Cross(tangent, right).normalized;

                float radiusSum = 0f;
                int radiusCount = 0;
                for (int i = 0; i < ring.vertices.Count; i++)
                {
                    int vertex = ring.vertices[i];
                    if (!IsCircumferentialValidIndex(
                            vertex,
                            source.vertices.Count))
                        continue;
                    Vector3 radial =
                        Vector3.ProjectOnPlane(
                            source.vertices[vertex] - ring.center,
                            tangent);
                    if (radial.sqrMagnitude <= 1e-12f)
                        continue;
                    radiusSum += radial.magnitude;
                    radiusCount++;
                }
                float radius = radiusCount > 0
                    ? radiusSum / radiusCount
                    : 0f;

                axis.stationCentersLocal.Add(ring.center);
                axis.stationTangentsLocal.Add(tangent);
                axis.stationRightLocal.Add(right);
                axis.stationUpLocal.Add(up);
                axis.stationRadiiLocal.Add(radius);
                axis.stationDistancesLocal.Add(ring.stationDistance);
            }

            axis.totalStationDistanceLocal =
                rings[rings.Count - 1].stationDistance;
            axis.stationSourceSummary = stationSummary;
            axis.hasSweepStations =
                axis.stationCentersLocal.Count == rings.Count &&
                axis.totalStationDistanceLocal > 1e-6f;
            return axis;
        }

        private static bool BuildTargetStrokeWorld(
            Mesh outerMesh,
            Transform outerTransform,
            AxisFrame axis,
            Preset preset,
            List<Vector3> outStrokeWorld)
        {
            outStrokeWorld.Clear();
            if (outerMesh == null ||
                outerTransform == null ||
                !axis.hasSweepStations)
                return false;

            float targetDistance =
                Mathf.Clamp(
                    axis.totalStationDistanceLocal *
                        Mathf.Clamp01(preset.normalizedZ) +
                    axis.centerOffsetLocal.z,
                    0f,
                    axis.totalStationDistanceLocal);
            if (!TrySampleSweepAxis(
                    axis,
                    targetDistance,
                    out _,
                    out Vector3 targetTangent,
                    out Vector3 targetRight,
                    out Vector3 targetUp,
                    out float targetRadius))
                return false;

            float length = Mathf.Max(1e-5f, preset.lengthWorld);
            int sampleCount = Mathf.Clamp(preset.sampleCount, 4, 256);
            float theta0 = preset.circumferenceAngleDeg * Mathf.Deg2Rad;
            float dirRad = preset.directionAngleDeg * Mathf.Deg2Rad;
            float cosDir = Mathf.Cos(dirRad);
            float sinDir = Mathf.Sin(dirRad);
            float surfaceLift = Mathf.Max(0f, preset.surfaceLiftWorld);
            Vector3 targetCircumferential =
                -targetRight * Mathf.Sin(theta0) +
                targetUp * Mathf.Cos(theta0);
            Vector3 targetSurfaceDirection =
                targetTangent * sinDir +
                targetCircumferential * cosDir;
            float worldPerLocalStroke =
                outerTransform.TransformVector(
                    targetSurfaceDirection).magnitude;
            float localLength =
                worldPerLocalStroke > 1e-6f
                    ? length / worldPerLocalStroke
                    : length;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount > 1 ? i / (float)(sampleCount - 1) : 0.5f;
                float s = (t - 0.5f) * localLength;
                float stationDistance =
                    Mathf.Clamp(
                        targetDistance + sinDir * s,
                        0f,
                        axis.totalStationDistanceLocal);
                if (!TrySampleSweepAxis(
                        axis,
                        stationDistance,
                        out Vector3 center,
                        out _,
                        out Vector3 right,
                        out Vector3 up,
                        out float radius) ||
                    radius <= 1e-6f)
                    continue;

                float theta = theta0 + cosDir * s / Mathf.Max(radius, 1e-5f);
                Vector3 normalLocal =
                    right * Mathf.Cos(theta) +
                    up * Mathf.Sin(theta);
                float worldPerLocalRadial =
                    outerTransform.TransformVector(normalLocal).magnitude;
                float surfaceLiftLocal =
                    worldPerLocalRadial > 1e-6f
                        ? surfaceLift / worldPerLocalRadial
                        : surfaceLift;
                Vector3 localPoint =
                    center +
                    normalLocal *
                    (radius + surfaceLiftLocal);
                outStrokeWorld.Add(outerTransform.TransformPoint(localPoint));
            }

            return outStrokeWorld.Count >= 2;
        }

        private static bool TrySampleSweepAxis(
            AxisFrame axis,
            float distanceLocal,
            out Vector3 centerLocal,
            out Vector3 tangentLocal,
            out Vector3 rightLocal,
            out Vector3 upLocal,
            out float radiusLocal)
        {
            centerLocal = Vector3.zero;
            tangentLocal = Vector3.forward;
            rightLocal = Vector3.right;
            upLocal = Vector3.up;
            radiusLocal = 0f;
            if (!axis.hasSweepStations ||
                axis.stationCentersLocal == null ||
                axis.stationCentersLocal.Count < 2)
                return false;

            float distance = Mathf.Clamp(
                distanceLocal,
                0f,
                axis.totalStationDistanceLocal);
            int upper = 1;
            while (upper < axis.stationDistancesLocal.Count - 1 &&
                   axis.stationDistancesLocal[upper] < distance)
                upper++;
            int lower = Mathf.Max(0, upper - 1);
            float lowerDistance =
                axis.stationDistancesLocal[lower];
            float upperDistance =
                axis.stationDistancesLocal[upper];
            float t = upperDistance - lowerDistance > 1e-8f
                ? Mathf.Clamp01(
                    (distance - lowerDistance) /
                    (upperDistance - lowerDistance))
                : 0f;

            centerLocal = Vector3.Lerp(
                axis.stationCentersLocal[lower],
                axis.stationCentersLocal[upper],
                t);
            tangentLocal = Vector3.Slerp(
                axis.stationTangentsLocal[lower],
                axis.stationTangentsLocal[upper],
                t).normalized;
            rightLocal = Vector3.Slerp(
                axis.stationRightLocal[lower],
                axis.stationRightLocal[upper],
                t);
            rightLocal =
                Vector3.ProjectOnPlane(
                    rightLocal,
                    tangentLocal);
            if (rightLocal.sqrMagnitude <= 1e-12f)
                rightLocal = axis.stationRightLocal[lower];
            rightLocal.Normalize();
            upLocal =
                Vector3.Cross(
                    tangentLocal,
                    rightLocal).normalized;
            radiusLocal = Mathf.Lerp(
                axis.stationRadiiLocal[lower],
                axis.stationRadiiLocal[upper],
                t);
            centerLocal +=
                rightLocal * axis.centerOffsetLocal.x +
                upLocal * axis.centerOffsetLocal.y;
            return radiusLocal > 1e-6f;
        }

        private static bool TryProjectPointToSweepAxis(
            AxisFrame axis,
            Vector3 pointLocal,
            out float stationDistanceLocal,
            out Vector3 centerLocal,
            out Vector3 tangentLocal,
            out Vector3 rightLocal,
            out Vector3 upLocal,
            out float radiusLocal)
        {
            stationDistanceLocal = 0f;
            centerLocal = Vector3.zero;
            tangentLocal = Vector3.forward;
            rightLocal = Vector3.right;
            upLocal = Vector3.up;
            radiusLocal = 0f;
            if (!axis.hasSweepStations)
                return false;

            float bestSqr = float.PositiveInfinity;
            float bestDistance = 0f;
            for (int i = 0;
                 i + 1 < axis.stationCentersLocal.Count;
                 i++)
            {
                Vector3 a = axis.stationCentersLocal[i];
                Vector3 b = axis.stationCentersLocal[i + 1];
                Vector3 ab = b - a;
                float denominator = ab.sqrMagnitude;
                float t = denominator > 1e-12f
                    ? Mathf.Clamp01(
                        Vector3.Dot(pointLocal - a, ab) /
                        denominator)
                    : 0f;
                Vector3 closest = a + ab * t;
                float sqr = (pointLocal - closest).sqrMagnitude;
                if (sqr >= bestSqr)
                    continue;
                bestSqr = sqr;
                bestDistance = Mathf.Lerp(
                    axis.stationDistancesLocal[i],
                    axis.stationDistancesLocal[i + 1],
                    t);
            }

            stationDistanceLocal = bestDistance;
            return TrySampleSweepAxis(
                axis,
                bestDistance,
                out centerLocal,
                out tangentLocal,
                out rightLocal,
                out upLocal,
                out radiusLocal);
        }

        private static Vector3[] BuildWorldVertexArray(List<Vector3> vertices, Transform meshTransform)
        {
            var result = new Vector3[vertices.Count];
            for (int i = 0; i < vertices.Count; i++)
                result[i] = meshTransform.TransformPoint(vertices[i]);
            return result;
        }

        private static List<GraphEdge>[] BuildGraphAdjacency(MeshWorkData work, Vector3[] worldVertices, out float averageEdgeLength)
        {
            var adjacency = new List<GraphEdge>[work.vertices.Count];
            for (int i = 0; i < adjacency.Length; i++)
                adjacency[i] = new List<GraphEdge>(8);

            var edgeLengths = new Dictionary<ulong, float>(work.triangles.Count);
            int triangleCount = work.triangles.Count / 3;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                int baseIndex = tri * 3;
                AppendGraphEdge(work.triangles[baseIndex + 0], work.triangles[baseIndex + 1], worldVertices, adjacency, edgeLengths);
                AppendGraphEdge(work.triangles[baseIndex + 1], work.triangles[baseIndex + 2], worldVertices, adjacency, edgeLengths);
                AppendGraphEdge(work.triangles[baseIndex + 2], work.triangles[baseIndex + 0], worldVertices, adjacency, edgeLengths);
            }

            float sum = 0f;
            int count = 0;
            foreach (var pair in edgeLengths)
            {
                sum += pair.Value;
                count++;
            }

            averageEdgeLength = count > 0 ? (sum / count) : 0f;
            return adjacency;
        }

        private static void AppendGraphEdge(
            int a,
            int b,
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            Dictionary<ulong, float> uniqueEdges)
        {
            if (a < 0 || b < 0 || a >= worldVertices.Length || b >= worldVertices.Length || a == b)
                return;

            ulong key = MakeUndirectedEdgeKey(a, b);
            if (uniqueEdges.ContainsKey(key))
                return;

            Vector3 wa = worldVertices[a];
            Vector3 wb = worldVertices[b];
            Vector3 delta = wb - wa;
            float length = delta.magnitude;
            if (length <= 1e-8f)
                return;

            Vector3 direction = delta / length;
            Vector3 midpoint = 0.5f * (wa + wb);
            uniqueEdges.Add(key, length);
            adjacency[a].Add(new GraphEdge { to = b, length = length, midpointWorld = midpoint, directionWorld = direction });
            adjacency[b].Add(new GraphEdge { to = a, length = length, midpointWorld = midpoint, directionWorld = -direction });
        }

        private static List<Vector3> ResamplePolyline(List<Vector3> polyline, float spacing)
        {
            var result = new List<Vector3>();
            if (polyline == null || polyline.Count == 0)
                return result;
            if (polyline.Count == 1)
            {
                result.Add(polyline[0]);
                return result;
            }

            float step = Mathf.Max(1e-5f, spacing);
            List<float> cumulative = BuildPolylineCumulative(polyline);
            float total = cumulative[cumulative.Count - 1];
            if (total <= 1e-6f)
            {
                result.Add(polyline[0]);
                result.Add(polyline[polyline.Count - 1]);
                return result;
            }

            int count = Mathf.Max(2, Mathf.CeilToInt(total / step) + 1);
            for (int i = 0; i < count; i++)
            {
                float distance = Mathf.Min(total, total * i / (count - 1f));
                result.Add(SamplePolylineAtDistance(polyline, cumulative, distance));
            }

            return result;
        }

        private static List<int> BuildAnchorVertices(List<Vector3> denseGuide, Vector3[] worldVertices, int maxAnchorCount)
        {
            var anchors = new List<int>(denseGuide.Count);
            for (int i = 0; i < denseGuide.Count; i++)
            {
                int nearest = FindNearestVertexIndex(denseGuide[i], worldVertices);
                if (nearest < 0)
                    continue;
                if (anchors.Count == 0 || anchors[anchors.Count - 1] != nearest)
                    anchors.Add(nearest);
            }

            if (anchors.Count <= maxAnchorCount)
                return anchors;

            var pruned = new List<int>(maxAnchorCount);
            for (int i = 0; i < maxAnchorCount; i++)
            {
                int index = Mathf.RoundToInt((anchors.Count - 1) * i / Mathf.Max(1f, maxAnchorCount - 1f));
                int value = anchors[Mathf.Clamp(index, 0, anchors.Count - 1)];
                if (pruned.Count == 0 || pruned[pruned.Count - 1] != value)
                    pruned.Add(value);
            }

            if (pruned.Count > 1 && pruned[pruned.Count - 1] != anchors[anchors.Count - 1])
                pruned[pruned.Count - 1] = anchors[anchors.Count - 1];

            return pruned;
        }

        private static List<int> BuildMasterPath(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            List<Vector3> guideWorld,
            List<int> anchors,
            float snapPenalty)
        {
            var result = new List<int>(anchors.Count * 8);
            if (anchors.Count == 0)
                return result;

            result.Add(anchors[0]);
            for (int i = 0; i < anchors.Count - 1; i++)
            {
                int start = anchors[i];
                int end = anchors[i + 1];
                if (start == end)
                    continue;

                Vector3 expectedDir = (worldVertices[end] - worldVertices[start]).normalized;
                if (expectedDir.sqrMagnitude <= 1e-10f)
                    expectedDir = guideWorld[Mathf.Min(guideWorld.Count - 1, i + 1)] - guideWorld[Mathf.Max(0, i)];
                if (expectedDir.sqrMagnitude <= 1e-10f)
                    expectedDir = Vector3.forward;
                expectedDir.Normalize();

                List<int> segmentPath = SolveShortestPath(worldVertices, adjacency, guideWorld, start, end, expectedDir, snapPenalty);
                if (segmentPath.Count < 2)
                    return new List<int>();

                for (int j = 1; j < segmentPath.Count; j++)
                {
                    int vertexIndex = segmentPath[j];
                    if (result[result.Count - 1] != vertexIndex)
                        result.Add(vertexIndex);
                }
            }

            return result;
        }

        private static List<int> SolveShortestPath(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            List<Vector3> guideWorld,
            int start,
            int end,
            Vector3 expectedDir,
            float snapPenalty)
        {
            if (worldVertices == null ||
                adjacency == null ||
                guideWorld == null ||
                guideWorld.Count < 2 ||
                start < 0 ||
                end < 0 ||
                start >= worldVertices.Length ||
                end >= worldVertices.Length ||
                start >= adjacency.Length ||
                end >= adjacency.Length)
            {
                return new List<int>();
            }

            List<float> guideCumulative = BuildPolylineCumulative(guideWorld);
            int vertexCount = Mathf.Min(worldVertices.Length, adjacency.Length);
            var dist = new float[vertexCount];
            var prev = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                dist[i] = float.PositiveInfinity;
                prev[i] = -1;
            }

            var heap = new MinHeap();
            dist[start] = 0f;
            heap.Push(start, 0f);

            while (heap.TryPop(out int current, out float currentCost))
            {
                if (currentCost > dist[current] + 1e-6f)
                    continue;
                if (current == end)
                    break;

                List<GraphEdge> edges = adjacency[current];
                if (edges == null)
                    continue;

                for (int i = 0; i < edges.Count; i++)
                {
                    GraphEdge edge = edges[i];
                    if (edge.to < 0 || edge.to >= vertexCount)
                        continue;

                    PolylineProjection proj = ProjectPointToPolyline(edge.midpointWorld, guideWorld, guideCumulative);
                    float guideDistance = Mathf.Sqrt(Mathf.Max(0f, proj.distanceSq));
                    float alignment = Vector3.Dot(edge.directionWorld, expectedDir);
                    float tangentAlignment = Vector3.Dot(edge.directionWorld, proj.tangent);
                    float cost = edge.length;
                    cost += snapPenalty * guideDistance;
                    cost += edge.length * Mathf.Max(0f, 1f - alignment) * 0.85f;
                    cost += edge.length * Mathf.Max(0f, 1f - tangentAlignment) * 0.45f;
                    if (alignment < -0.1f)
                        cost += edge.length * (0.1f - alignment) * 2.5f;

                    float nextCost = currentCost + cost;
                    if (nextCost + 1e-6f >= dist[edge.to])
                        continue;

                    dist[edge.to] = nextCost;
                    prev[edge.to] = current;
                    heap.Push(edge.to, nextCost);
                }
            }

            if (prev[end] < 0)
                return new List<int>();

            var path = new List<int>();
            int cursor = end;
            while (cursor >= 0)
            {
                path.Add(cursor);
                if (cursor == start)
                    break;
                cursor = prev[cursor];
            }

            path.Reverse();
            return path;
        }

        private static List<Vector3> ClipChainByGuideCoverage(List<Vector3> chainWorld, List<Vector3> guideWorld, float desiredLength, out float clippedLength)
        {
            clippedLength = 0f;
            var result = new List<Vector3>();
            if (chainWorld == null || chainWorld.Count < 2)
                return result;

            List<float> chainCumulative = BuildPolylineCumulative(chainWorld);
            float totalChainLength = chainCumulative[chainCumulative.Count - 1];
            if (totalChainLength <= 1e-6f)
                return result;

            float sMin = float.PositiveInfinity;
            float sMax = float.NegativeInfinity;
            for (int i = 0; i < guideWorld.Count; i++)
            {
                PolylineProjection proj = ProjectPointToPolyline(guideWorld[i], chainWorld, chainCumulative);
                sMin = Mathf.Min(sMin, proj.s);
                sMax = Mathf.Max(sMax, proj.s);
            }

            if (float.IsNaN(sMin) || float.IsInfinity(sMin) || float.IsNaN(sMax) || float.IsInfinity(sMax))
            {
                sMin = 0f;
                sMax = totalChainLength;
            }

            float coverageLength = Mathf.Max(0f, sMax - sMin);
            float targetLength = Mathf.Clamp(desiredLength, 1e-5f, totalChainLength);
            float center = 0.5f * (sMin + sMax);
            float startS;
            float endS;
            if (coverageLength < targetLength || coverageLength > targetLength)
            {
                startS = center - targetLength * 0.5f;
                endS = center + targetLength * 0.5f;
            }
            else
            {
                startS = sMin;
                endS = sMax;
            }

            if (startS < 0f)
            {
                endS = Mathf.Min(totalChainLength, endS - startS);
                startS = 0f;
            }
            if (endS > totalChainLength)
            {
                startS = Mathf.Max(0f, startS - (endS - totalChainLength));
                endS = totalChainLength;
            }

            startS = Mathf.Clamp(startS, 0f, totalChainLength);
            endS = Mathf.Clamp(endS, startS, totalChainLength);
            if (endS - startS <= 1e-6f)
                return result;

            result.Add(SamplePolylineAtDistance(chainWorld, chainCumulative, startS));
            for (int i = 1; i < chainWorld.Count - 1; i++)
            {
                float s = chainCumulative[i];
                if (s > startS + 1e-6f && s < endS - 1e-6f)
                    result.Add(chainWorld[i]);
            }
            result.Add(SamplePolylineAtDistance(chainWorld, chainCumulative, endS));

            clippedLength = endS - startS;
            return result;
        }

        private static List<FrameSample> BuildCanonicalFrames(
            List<Vector3> chainWorld,
            Transform meshTransform,
            AxisFrame axis,
            float openWidthWorld,
            float averageEdgeLength,
            int maxStationCount)
        {
            var result = new List<FrameSample>();
            List<float> cumulative = BuildPolylineCumulative(chainWorld);
            float total = cumulative[cumulative.Count - 1];
            if (total <= 1e-6f)
                return result;

            float desiredSpacing = Mathf.Max(averageEdgeLength * 0.75f, total / 48f);
            int stationCount = Mathf.Clamp(Mathf.RoundToInt(total / Mathf.Max(desiredSpacing, 1e-5f)) + 1, 6, Mathf.Max(6, maxStationCount));
            float taperLength = ComputeEndTaperLength(total, openWidthWorld, averageEdgeLength);
            for (int i = 0; i < stationCount; i++)
            {
                float s = total * i / Mathf.Max(1f, stationCount - 1f);
                float targetOpen = ComputeStationTargetOpen(s, total, openWidthWorld, taperLength);
                result.Add(BuildFrameAtDistance(chainWorld, cumulative, meshTransform, axis, s, targetOpen));
            }

            StabilizeFrameOrientation(result);
            return result;
        }

        private static List<FrameSample> BuildReferenceFrames(
            List<StationResult> referenceStations,
            List<Vector3> chainWorld,
            Transform meshTransform,
            AxisFrame axis)
        {
            var result = new List<FrameSample>(referenceStations.Count);
            List<float> cumulative = BuildPolylineCumulative(chainWorld);
            float total = cumulative[cumulative.Count - 1];
            if (total <= 1e-6f)
                return result;

            for (int i = 0; i < referenceStations.Count; i++)
            {
                StationResult reference = referenceStations[i];
                float sampleS = Mathf.Clamp01(reference.strokeT) * total;
                FrameSample frame = BuildFrameAtDistance(chainWorld, cumulative, meshTransform, axis, sampleS, reference.targetOpen);
                frame.s = reference.s;
                frame.strokeT = reference.strokeT;
                frame.targetOpen = reference.targetOpen;
                result.Add(frame);
            }

            StabilizeFrameOrientation(result);
            return result;
        }

        private static float ComputePolylineLength(List<Vector3> polyline)
        {
            if (polyline == null || polyline.Count < 2)
                return 0f;

            float total = 0f;
            for (int i = 1; i < polyline.Count; i++)
                total += Vector3.Distance(polyline[i - 1], polyline[i]);
            return total;
        }

        private static float ComputeEndTaperLength(float masterLength, float openWidthWorld, float averageEdgeLength)
        {
            return Mathf.Max(
                averageEdgeLength * 2.0f,
                openWidthWorld * 1.25f,
                masterLength * 0.08f);
        }

        private static float ComputeStationTargetOpen(float s, float totalLength, float openWidthWorld, float taperLength)
        {
            if (totalLength <= 1e-6f || openWidthWorld <= 1e-6f)
                return 0f;

            float clampedTaper = Mathf.Clamp(taperLength, 1e-5f, totalLength * 0.5f);
            float startFactor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(s / clampedTaper));
            float endFactor = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((totalLength - s) / clampedTaper));
            return openWidthWorld * Mathf.Min(startFactor, endFactor);
        }

        private static bool TryBuildExplicitCenterCurve(
            MeshWorkData work,
            Transform meshTransform,
            List<Vector3> chainSource,
            float averageEdgeLength,
            out List<Vector3> centerCurveWorld,
            out bool usedFallback,
            out string failReason)
        {
            centerCurveWorld = new List<Vector3>();
            usedFallback = false;
            failReason = null;
            if (work == null || meshTransform == null || chainSource == null || chainSource.Count < 2)
            {
                failReason = "CenterCurve input is invalid.";
                return false;
            }

            float totalLength = ComputePolylineLength(chainSource);
            if (totalLength <= 1e-6f)
            {
                failReason = "CenterCurve source length is invalid.";
                return false;
            }

            float spacing = Mathf.Max(averageEdgeLength * 0.75f, totalLength / 48f, 1e-5f);
            List<Vector3> rawCurve = ResamplePolyline(chainSource, spacing);
            if (rawCurve.Count < 2)
            {
                failReason = "CenterCurve resample is empty.";
                return false;
            }

            Vector3[] worldVertices = BuildWorldVertexArray(work.vertices, meshTransform);
            List<int>[] triangleAdjacency = BuildTriangleAdjacency(work);
            HashSet<int> allowedTriangles = BuildCurveProjectionTriangleSet(work, worldVertices, triangleAdjacency, chainSource);
            if (allowedTriangles.Count == 0)
            {
                int triangleCount = work.triangles.Count / 3;
                for (int tri = 0; tri < triangleCount; tri++)
                    allowedTriangles.Add(tri);
                usedFallback = true;
            }

            List<float> rawCumulative = BuildPolylineCumulative(rawCurve);
            float maxDrift = Mathf.Max(averageEdgeLength * 0.5f, 1e-5f);
            List<Vector3> current = new List<Vector3>(rawCurve);
            for (int pass = 0; pass < 2; pass++)
            {
                List<Vector3> smoothed = ApplyChaikinSmoothing(current);
                if (smoothed.Count < 2)
                    smoothed = new List<Vector3>(current);

                smoothed[0] = rawCurve[0];
                smoothed[smoothed.Count - 1] = rawCurve[rawCurve.Count - 1];
                for (int i = 1; i < smoothed.Count - 1; i++)
                {
                    Vector3 rawReference = ProjectPointToPolyline(smoothed[i], rawCurve, rawCumulative).point;
                    Vector3 clamped = ClampPointToReference(rawReference, smoothed[i], maxDrift);
                    TriangleProjection projection = FindNearestTriangleProjection(work, worldVertices, clamped, allowedTriangles);
                    if (projection.triangleIndex < 0)
                    {
                        projection = FindNearestTriangleProjection(work, worldVertices, clamped, null);
                        if (projection.triangleIndex >= 0)
                            usedFallback = true;
                    }

                    Vector3 projected = projection.triangleIndex >= 0 ? projection.point : clamped;
                    smoothed[i] = ClampPointToReference(rawReference, projected, maxDrift);
                }

                current = ResamplePolyline(smoothed, spacing);
                if (current.Count < 2)
                    current = new List<Vector3>(smoothed);
            }

            centerCurveWorld.AddRange(current);
            return centerCurveWorld.Count >= 2;
        }

        private static HashSet<int> BuildCurveProjectionTriangleSet(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<int>[] triangleAdjacency,
            List<Vector3> sourceCurve)
        {
            var seeds = new HashSet<int>();
            if (work == null || worldVertices == null || triangleAdjacency == null || sourceCurve == null)
                return seeds;

            for (int i = 0; i < sourceCurve.Count; i++)
            {
                TriangleProjection projection = FindNearestTriangleProjection(work, worldVertices, sourceCurve[i], null);
                if (projection.triangleIndex >= 0)
                    seeds.Add(projection.triangleIndex);
            }

            return ExpandTriangleNeighborhood(seeds, triangleAdjacency, 2);
        }

        private static HashSet<int> ExpandTriangleNeighborhood(HashSet<int> seeds, List<int>[] triangleAdjacency, int rings)
        {
            var result = new HashSet<int>();
            if (seeds == null || triangleAdjacency == null)
                return result;

            var frontier = new Queue<int>();
            var depth = new Dictionary<int, int>();
            foreach (int seed in seeds)
            {
                if (seed < 0 || seed >= triangleAdjacency.Length || !result.Add(seed))
                    continue;
                frontier.Enqueue(seed);
                depth[seed] = 0;
            }

            while (frontier.Count > 0)
            {
                int current = frontier.Dequeue();
                int currentDepth = depth[current];
                if (currentDepth >= rings)
                    continue;

                List<int> neighbors = triangleAdjacency[current];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    if (neighbor < 0 || neighbor >= triangleAdjacency.Length || !result.Add(neighbor))
                        continue;
                    depth[neighbor] = currentDepth + 1;
                    frontier.Enqueue(neighbor);
                }
            }

            return result;
        }

        private static List<Vector3> ApplyChaikinSmoothing(List<Vector3> polyline)
        {
            var result = new List<Vector3>();
            if (polyline == null || polyline.Count < 2)
                return result;

            result.Add(polyline[0]);
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                Vector3 q = Vector3.Lerp(a, b, 0.25f);
                Vector3 r = Vector3.Lerp(a, b, 0.75f);
                result.Add(q);
                result.Add(r);
            }
            result.Add(polyline[polyline.Count - 1]);
            return result;
        }

        private static Vector3 ClampPointToReference(Vector3 reference, Vector3 candidate, float maxDistance)
        {
            Vector3 delta = candidate - reference;
            float distance = delta.magnitude;
            if (distance <= maxDistance || distance <= 1e-6f)
                return candidate;
            return reference + delta / distance * maxDistance;
        }

        private static FrameSample BuildFrameAtDistance(
            List<Vector3> polyline,
            List<float> cumulative,
            Transform meshTransform,
            AxisFrame axis,
            float distance,
            float openWidthWorld)
        {
            float total = cumulative[cumulative.Count - 1];
            Vector3 center = SamplePolylineAtDistance(polyline, cumulative, distance);
            float beforeS = Mathf.Max(0f, distance - Mathf.Max(total * 0.01f, 1e-5f));
            float afterS = Mathf.Min(total, distance + Mathf.Max(total * 0.01f, 1e-5f));
            Vector3 before = SamplePolylineAtDistance(polyline, cumulative, beforeS);
            Vector3 after = SamplePolylineAtDistance(polyline, cumulative, afterS);
            Vector3 tangent = (after - before).normalized;
            if (tangent.sqrMagnitude <= 1e-10f)
                tangent = polyline[polyline.Count - 1] - polyline[0];
            if (tangent.sqrMagnitude <= 1e-10f)
                tangent = Vector3.forward;
            tangent.Normalize();

            Vector3 localCenter = meshTransform.InverseTransformPoint(center);
            Vector3 radialLocal;
            if (TryProjectPointToSweepAxis(
                    axis,
                    localCenter,
                    out _,
                    out Vector3 axisCenterLocal,
                    out Vector3 axisTangentLocal,
                    out _,
                    out _,
                    out _))
            {
                radialLocal =
                    Vector3.ProjectOnPlane(
                        localCenter - axisCenterLocal,
                        axisTangentLocal);
            }
            else
            {
                radialLocal = localCenter - axis.bounds.center;
                radialLocal.z = 0f;
            }
            if (radialLocal.sqrMagnitude <= 1e-10f)
                radialLocal = Vector3.right;
            radialLocal.Normalize();

            Vector3 normal = meshTransform.TransformDirection(radialLocal).normalized;
            Vector3 binormal = Vector3.Cross(normal, tangent);
            if (binormal.sqrMagnitude <= 1e-10f)
                binormal = Vector3.Cross(Vector3.up, tangent);
            if (binormal.sqrMagnitude <= 1e-10f)
                binormal = Vector3.Cross(Vector3.forward, tangent);
            binormal.Normalize();

            return new FrameSample
            {
                center = center,
                tangent = tangent,
                normal = normal,
                binormal = binormal,
                s = distance,
                strokeT = total > 1e-6f ? Mathf.Clamp01(distance / total) : 0f,
                targetOpen = openWidthWorld,
            };
        }

        private static void StabilizeFrameOrientation(List<FrameSample> frames)
        {
            for (int i = 1; i < frames.Count; i++)
            {
                FrameSample cur = frames[i];
                if (Vector3.Dot(frames[i - 1].binormal, cur.binormal) < 0f)
                    cur.binormal = -cur.binormal;
                frames[i] = cur;
            }
        }

        private static void BuildTargetBoundaryPolylines(
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            out List<Vector3> leftTargets,
            out List<Vector3> rightTargets)
        {
            leftTargets = new List<Vector3>(canonicalFrames.Count);
            rightTargets = new List<Vector3>(canonicalFrames.Count);
            for (int i = 0; i < canonicalFrames.Count; i++)
            {
                FrameSample frame = canonicalFrames[i];
                float targetOpen = frame.targetOpen >= 0f ? frame.targetOpen : openWidthWorld;
                float halfOpen = Mathf.Max(1e-5f, targetOpen * 0.5f);
                leftTargets.Add(frame.center - frame.binormal * halfOpen);
                rightTargets.Add(frame.center + frame.binormal * halfOpen);
            }
        }

        private static bool ApplyExplicitBandRemesh(
            MeshWorkData work,
            Transform meshTransform,
            Vector3[] worldVertices,
            List<GraphEdge>[] graphAdjacency,
            List<Vector3> centerCurveWorld,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            float pathSnapPenalty,
            List<StructuredStripReferenceRow> referenceStripRows,
            List<DebugTriangle> debugTriangles,
            int layer,
            out List<BoundaryVertexSample> leftBoundary,
            out List<BoundaryVertexSample> rightBoundary,
            out List<FrameSample> realizedFrames,
            out List<StructuredStripReferenceRow> realizedStripReferenceRows,
            out int removedTriangleCount,
            out int patchTriangleCount,
            out int keptTriangleCount,
            out int openBoundaryEdgeCount,
            out int patchComponentCount,
            out int excisionComponentCount,
            out int boundaryContinuityViolationCount,
            out float maxPatchTriangleAspect,
            out bool usedBoundaryVertexFallback,
            out RemeshDiagnostics diagnostics,
            out string failStage,
            out string failReason)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();
            realizedFrames = new List<FrameSample>();
            realizedStripReferenceRows = new List<StructuredStripReferenceRow>();
            removedTriangleCount = 0;
            patchTriangleCount = 0;
            keptTriangleCount = 0;
            openBoundaryEdgeCount = 0;
            patchComponentCount = 0;
            excisionComponentCount = 0;
            boundaryContinuityViolationCount = 0;
            maxPatchTriangleAspect = 0f;
            usedBoundaryVertexFallback = false;
            diagnostics = default;
            InitializePrecommitGeometryDiagnostics(ref diagnostics);
            failStage = null;
            failReason = null;
            diagnostics.cellMergeFallbackCount = 0;
            diagnostics.failedCellRowIndex = -1;
            diagnostics.failedCellColumnIndex = -1;
            diagnostics.taperClipInputSummary = "bypassed";
            diagnostics.interfaceHardMismatchEdgeCount = 0;
            diagnostics.interfaceAuditMismatchEdgeCount = 0;
            diagnostics.interfaceScanByFlagSummary = "bypassed";
            diagnostics.interfaceScanByRowSummary = "bypassed";
            diagnostics.interfaceScanOwnershipSummary = "bypassed";
            diagnostics.interfaceFeCategorySummary = "bypassed";
            diagnostics.interfaceFeActualMaskSummary = "bypassed";
            diagnostics.interfaceScanDetailSummary = "bypassed";
            diagnostics.tipOwnershipSummary = "bypassed";
            diagnostics.tipInterfaceKeyCompareSummary = "bypassed";
            diagnostics.startTipWedgeHelperSummary = "bypassed";
            diagnostics.startTipWedgeSourceNormSummary = "bypassed";
            diagnostics.startTipWedgeBoundaryNormSummary = "bypassed";
            diagnostics.startTipWedgeCanonicalSummary = "bypassed";
            diagnostics.startTipWedgePostCanonicalSourceNormSummary = "bypassed";
            diagnostics.startTipWedgePostCanonicalBoundaryNormSummary = "bypassed";
            diagnostics.endTipEarlyHelperProbeSummary = "bypassed";
            diagnostics.endTipOrdinaryAuditSummary = "bypassed";
            diagnostics.boundarySourceSummary = "unknown";
            diagnostics.syncSummary = "none";
            diagnostics.projectionAlignSummary = "none";
            if (work == null || meshTransform == null || worldVertices == null || graphAdjacency == null ||
                canonicalFrames == null || canonicalFrames.Count < 2 || centerCurveWorld == null || centerCurveWorld.Count < 2)
            {
                failStage = "Remesh";
                failReason = "ApplyExplicitBandRemesh input is invalid.";
                return false;
            }

            if (!TryExtractSingleSidedPatchTriangles(
                    work,
                    worldVertices,
                    centerCurveWorld,
                    canonicalFrames,
                    openWidthWorld,
                    averageEdgeLength,
                    out PatchSelectionResult patchSelection))
            {
                diagnostics.patchHalfWidth = patchSelection != null ? patchSelection.patchHalfWidth : 0f;
                diagnostics.patchSPad = patchSelection != null ? patchSelection.sPad : 0f;
                diagnostics.patchNormalTol = patchSelection != null ? patchSelection.normalTol : 0f;
                diagnostics.patchCandidateTriangleCount = patchSelection != null ? patchSelection.validTriangleCount : 0;
                diagnostics.patchSeedTriangleCount = patchSelection != null ? patchSelection.seedTriangleCount : 0;
                diagnostics.patchBestComponentTriangleCount = patchSelection != null ? patchSelection.bestComponentTriangleCount : 0;
                diagnostics.patchBestComponentSeedHits = patchSelection != null ? patchSelection.bestSeedHits : 0;
                failStage = "Patch";
                failReason = patchSelection != null ? patchSelection.failReason : "Patch extraction failed.";
                return false;
            }

            diagnostics.patchHalfWidth = patchSelection.patchHalfWidth;
            diagnostics.patchSPad = patchSelection.sPad;
            diagnostics.patchNormalTol = patchSelection.normalTol;
            diagnostics.patchCandidateTriangleCount = patchSelection.validTriangleCount;
            diagnostics.patchSeedTriangleCount = patchSelection.seedTriangleCount;
            diagnostics.patchBestComponentTriangleCount = patchSelection.bestComponentTriangleCount;
            diagnostics.patchBestComponentSeedHits = patchSelection.bestSeedHits;
            patchComponentCount = patchSelection.componentCount;
            patchTriangleCount = patchSelection.patchTriangles.Count;

            float supportOffset = ComputeStructuredSupportOffset(
                patchSelection.patchHalfWidth,
                canonicalFrames,
                openWidthWorld,
                averageEdgeLength,
                out float supportOffsetBase,
                out float supportOffsetCap,
                out float supportStripMargin);
            diagnostics.supportOffset = supportOffset;
            diagnostics.supportOffsetBase = supportOffsetBase;
            diagnostics.supportOffsetCap = supportOffsetCap;
            diagnostics.supportStripMargin = supportStripMargin;
            diagnostics.usedReferenceStripRows = referenceStripRows != null && referenceStripRows.Count == canonicalFrames.Count;
            diagnostics.referenceStripRowCount = referenceStripRows != null ? referenceStripRows.Count : 0;

            MeshWorkData rowWork = (UseTaperClipRebuild || !UseStructuredCellRebuild) ? work.Clone() : work;
            Vector3[] rowWorldVertices = (UseTaperClipRebuild || !UseStructuredCellRebuild)
                ? BuildWorldVertexArray(rowWork.vertices, meshTransform)
                : worldVertices;
            HashSet<int> patchVertices = BuildVertexSetFromTriangles(rowWork, patchSelection.patchTriangles);
            List<GraphEdge>[] restrictedAdjacency = BuildRestrictedGraphAdjacency(graphAdjacency, patchVertices);
            if (!TryBuildStructuredStripRows(
                    rowWork,
                    rowWorldVertices,
                    patchSelection.patchTriangles,
                    restrictedAdjacency,
                    patchVertices,
                    canonicalFrames,
                    supportOffset,
                    pathSnapPenalty,
                    referenceStripRows,
                    out List<StructuredStripRow> rows,
                    out StructuredStripBuildDiagnostics stripDiagnostics,
                    out string structuredFailReason))
            {
                diagnostics.attemptedRowCount = stripDiagnostics.attemptedRowCount;
                diagnostics.keptRowCount = stripDiagnostics.keptRowCount;
                diagnostics.collapsedRowCount = stripDiagnostics.collapsedRowCount;
                diagnostics.repeatedRowCount = stripDiagnostics.repeatedRowCount;
                diagnostics.crossedRowCount = stripDiagnostics.crossedRowCount;
                diagnostics.tipRowCount = stripDiagnostics.tipRowCount;
                diagnostics.keptStartTipRow = stripDiagnostics.keptStartTipRow;
                diagnostics.keptEndTipRow = stripDiagnostics.keptEndTipRow;
                diagnostics.minBoundaryWidth = stripDiagnostics.minBoundaryWidth;
                diagnostics.minSupportWidth = stripDiagnostics.minSupportWidth;
                diagnostics.maxBoundaryJump = stripDiagnostics.maxBoundaryJump;
                diagnostics.minLeftGap = stripDiagnostics.minLeftGap;
                diagnostics.minCenterGap = stripDiagnostics.minCenterGap;
                diagnostics.minRightGap = stripDiagnostics.minRightGap;
                diagnostics.leftOrderViolationCount = stripDiagnostics.leftOrderViolationCount;
                diagnostics.centerOrderViolationCount = stripDiagnostics.centerOrderViolationCount;
                diagnostics.rightOrderViolationCount = stripDiagnostics.rightOrderViolationCount;
                diagnostics.crossedRowPreview = stripDiagnostics.crossedRowPreview;
                diagnostics.leftSupportAnchorCount = stripDiagnostics.leftSupportRail.anchorCount;
                diagnostics.leftBoundaryAnchorCount = stripDiagnostics.leftBoundaryRail.anchorCount;
                diagnostics.rightBoundaryAnchorCount = stripDiagnostics.rightBoundaryRail.anchorCount;
                diagnostics.rightSupportAnchorCount = stripDiagnostics.rightSupportRail.anchorCount;
                diagnostics.leftSupportPathVertexCount = stripDiagnostics.leftSupportRail.pathVertexCount;
                diagnostics.leftBoundaryPathVertexCount = stripDiagnostics.leftBoundaryRail.pathVertexCount;
                diagnostics.rightBoundaryPathVertexCount = stripDiagnostics.rightBoundaryRail.pathVertexCount;
                diagnostics.rightSupportPathVertexCount = stripDiagnostics.rightSupportRail.pathVertexCount;
                diagnostics.leftSupportSampleCount = stripDiagnostics.leftSupportRail.sampleCount;
                diagnostics.leftBoundarySampleCount = stripDiagnostics.leftBoundaryRail.sampleCount;
                diagnostics.rightBoundarySampleCount = stripDiagnostics.rightBoundaryRail.sampleCount;
                diagnostics.rightSupportSampleCount = stripDiagnostics.rightSupportRail.sampleCount;
                failStage = "Shape";
                failReason = structuredFailReason;
                return false;
            }

            diagnostics.attemptedRowCount = stripDiagnostics.attemptedRowCount;
            diagnostics.keptRowCount = stripDiagnostics.keptRowCount;
            diagnostics.collapsedRowCount = stripDiagnostics.collapsedRowCount;
            diagnostics.repeatedRowCount = stripDiagnostics.repeatedRowCount;
            diagnostics.crossedRowCount = stripDiagnostics.crossedRowCount;
            diagnostics.tipRowCount = stripDiagnostics.tipRowCount;
            diagnostics.keptStartTipRow = stripDiagnostics.keptStartTipRow;
            diagnostics.keptEndTipRow = stripDiagnostics.keptEndTipRow;
            diagnostics.minBoundaryWidth = stripDiagnostics.minBoundaryWidth;
            diagnostics.minSupportWidth = stripDiagnostics.minSupportWidth;
            diagnostics.maxBoundaryJump = stripDiagnostics.maxBoundaryJump;
            diagnostics.minLeftGap = stripDiagnostics.minLeftGap;
            diagnostics.minCenterGap = stripDiagnostics.minCenterGap;
            diagnostics.minRightGap = stripDiagnostics.minRightGap;
            diagnostics.leftOrderViolationCount = stripDiagnostics.leftOrderViolationCount;
            diagnostics.centerOrderViolationCount = stripDiagnostics.centerOrderViolationCount;
            diagnostics.rightOrderViolationCount = stripDiagnostics.rightOrderViolationCount;
            diagnostics.crossedRowPreview = stripDiagnostics.crossedRowPreview;
            diagnostics.leftSupportAnchorCount = stripDiagnostics.leftSupportRail.anchorCount;
            diagnostics.leftBoundaryAnchorCount = stripDiagnostics.leftBoundaryRail.anchorCount;
            diagnostics.rightBoundaryAnchorCount = stripDiagnostics.rightBoundaryRail.anchorCount;
            diagnostics.rightSupportAnchorCount = stripDiagnostics.rightSupportRail.anchorCount;
            diagnostics.leftSupportPathVertexCount = stripDiagnostics.leftSupportRail.pathVertexCount;
            diagnostics.leftBoundaryPathVertexCount = stripDiagnostics.leftBoundaryRail.pathVertexCount;
            diagnostics.rightBoundaryPathVertexCount = stripDiagnostics.rightBoundaryRail.pathVertexCount;
            diagnostics.rightSupportPathVertexCount = stripDiagnostics.rightSupportRail.pathVertexCount;
            diagnostics.leftSupportSampleCount = stripDiagnostics.leftSupportRail.sampleCount;
            diagnostics.leftBoundarySampleCount = stripDiagnostics.leftBoundaryRail.sampleCount;
            diagnostics.rightBoundarySampleCount = stripDiagnostics.rightBoundaryRail.sampleCount;
            diagnostics.rightSupportSampleCount = stripDiagnostics.rightSupportRail.sampleCount;

            if (UseStructuredCellRebuild)
                worldVertices = BuildWorldVertexArray(work.vertices, meshTransform);

            for (int i = 0; i < rows.Count; i++)
                realizedFrames.Add(rows[i].frame);
            CaptureStructuredStripReferenceRows(rows, realizedStripReferenceRows);
            List<FrameSample> partitionFrames = realizedFrames.Count >= 2 ? realizedFrames : canonicalFrames;
            diagnostics.rowDefinitionSummary = BuildStructuredRowDefinitionSummary(rows, partitionFrames, out diagnostics.rowDefinitionMismatchCount);
            if (diagnostics.rowDefinitionMismatchCount > 0)
            {
                failStage = "ProjectionConsistency";
                failReason = $"Structured row semantics diverged from projected 3D samples. {diagnostics.rowDefinitionSummary}";
                return false;
            }

            if (UseTaperClipRebuild)
            {
                return TryApplyTaperClipRebuild(
                    work,
                    meshTransform,
                    worldVertices,
                    rows,
                    partitionFrames,
                    patchSelection,
                    supportOffset,
                    averageEdgeLength,
                    debugTriangles,
                    layer,
                    out leftBoundary,
                    out rightBoundary,
                    out removedTriangleCount,
                    out keptTriangleCount,
                    out openBoundaryEdgeCount,
                    out excisionComponentCount,
                    out boundaryContinuityViolationCount,
                    ref maxPatchTriangleAspect,
                    ref diagnostics,
                    out failStage,
                    out failReason);
            }

            if (!UseStructuredCellRebuild)
            {
                return TryApplyBandBaselineRemesh(
                work,
                meshTransform,
                worldVertices,
                graphAdjacency,
                partitionFrames,
                patchSelection,
                openWidthWorld,
                averageEdgeLength,
                pathSnapPenalty,
                debugTriangles,
                layer,
                out leftBoundary,
                out rightBoundary,
                out removedTriangleCount,
                out keptTriangleCount,
                out openBoundaryEdgeCount,
                out excisionComponentCount,
                out boundaryContinuityViolationCount,
                ref maxPatchTriangleAspect,
                ref diagnostics,
                out failStage,
                out failReason);
            }

            BuildStructuredStripCoverageCells(
                rows,
                out List<StructuredStripCell2D> supportCells,
                out List<StructuredStripCell2D> excisionCells,
                out List<StructuredStripCell2D> domainCells);

            var removedPatchTriangles = new HashSet<int>();
            var nextTriangles = new List<int>(Mathf.Max(work.triangles.Count, rows.Count * 12));
            var patchTriangleIndices = new HashSet<int>();
            var outsidePatchTriangleIndices = new HashSet<int>();
            var supportPatchTriangleIndices = new HashSet<int>();
            var clipEdgeCache = new Dictionary<EdgeBoundaryKey, ClipVertex>();
            var taggedEdges = new Dictionary<ulong, TaggedBoundaryEdge>();
            var supportCellFragments = new Dictionary<StructuredCellKey, List<StructuredCellFragment>>();
            var excisionCellFragments = new Dictionary<StructuredCellKey, List<StructuredCellFragment>>();
            var allOutsidePolygons = new List<List<ClipVertex>>();
            var allSupportMergedKeys = new List<StructuredCellKey>();
            var allSupportMergedPolygons = new List<List<ClipVertex>>();
            var allExcisionMergedPolygons = new List<List<ClipVertex>>();
            int triangulationWindingMismatchCount = 0;
            int outsideWindingMismatchCount = 0;
            int supportWindingMismatchCount = 0;
            int degeneratePatchTriangleCount = 0;
            int overAspectPatchTriangleCount = 0;
            int auditedPatchTriangleCount = 0;
            int invertedTriangleCount = 0;
            float minPatchArea = float.PositiveInfinity;
            diagnostics.usedLatticePartition = false;
            int triangleCount = work.triangles.Count / 3;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                bool isPatchTriangle = patchSelection.patchTriangles.Contains(tri);
                bool overlapsStructuredDomain =
                    !isPatchTriangle &&
                    TriangleOverlapsStructuredCells2D(
                        worldVertices[ia],
                        worldVertices[ib],
                        worldVertices[ic],
                        partitionFrames,
                        domainCells);
                bool shouldPartitionTriangle = isPatchTriangle || overlapsStructuredDomain;

                if (shouldPartitionTriangle)
                {
                    debugTriangles.Add(new DebugTriangle
                    {
                        a = worldVertices[ia],
                        b = worldVertices[ib],
                        c = worldVertices[ic],
                        layer = layer,
                        kind = DebugTriangleKind.Patch,
                    });
                }

                if (shouldPartitionTriangle)
                {
                    var trianglePolygon = new List<ClipVertex>(3)
                    {
                        CreateClipVertexFromIndex(work, worldVertices, ia, partitionFrames),
                        CreateClipVertexFromIndex(work, worldVertices, ib, partitionFrames),
                        CreateClipVertexFromIndex(work, worldVertices, ic, partitionFrames),
                    };
                    PartitionTriangleAgainstStructuredCells(
                        trianglePolygon,
                        supportCells,
                        excisionCells,
                        domainCells,
                        tri,
                        work,
                        clipEdgeCache,
                        supportCellFragments,
                        excisionCellFragments,
                        out List<List<ClipVertex>> outsidePolygons,
                        out int supportFragmentCount,
                        out int excisionFragmentCount);

                    diagnostics.preservedOutsideFragmentCount += outsidePolygons.Count;
                    diagnostics.supportPartitionPolygonCount += supportFragmentCount;
                    diagnostics.excisionPartitionPolygonCount += excisionFragmentCount;
                    diagnostics.discardedSupportFragmentCount += supportFragmentCount;
                    diagnostics.discardedExcisionFragmentCount += excisionFragmentCount;
                    bool supportCovered = supportFragmentCount > 0;
                    bool excisionCovered = excisionFragmentCount > 0;

                    if (supportCovered)
                        diagnostics.supportCoveredTriangleCount++;
                    if (excisionCovered)
                        diagnostics.excisionCoveredTriangleCount++;

                    if (supportCovered || excisionCovered)
                    {
                        diagnostics.stripCoveredTriangleCount++;
                        diagnostics.clippedPatchTriangleCount++;
                        removedPatchTriangles.Add(tri);
                    }
                    else if (isPatchTriangle)
                    {
                        diagnostics.patchOutsideStripTriangleCount++;
                        if (!IsUnchangedOriginalTrianglePolygon(outsidePolygons, ia, ib, ic))
                            diagnostics.changedOutsideStripTriangleCount++;
                    }

                    for (int poly = 0; poly < outsidePolygons.Count; poly++)
                        allOutsidePolygons.Add(outsidePolygons[poly]);

                    continue;
                }

                nextTriangles.Add(ia);
                nextTriangles.Add(ib);
                nextTriangles.Add(ic);
            }

            var supportCellKeys = new List<StructuredCellKey>(supportCellFragments.Keys);
            supportCellKeys.Sort((a, b) =>
            {
                int cmp = a.rowIndex.CompareTo(b.rowIndex);
                return cmp != 0 ? cmp : a.columnIndex.CompareTo(b.columnIndex);
            });
            diagnostics.cellTopologyCellCount = supportCellKeys.Count + excisionCellFragments.Count;
            ScanStructuredCellMembership(
                rows,
                supportCellFragments,
                excisionCellFragments,
                out diagnostics.cellMembershipScanCellCount,
                out diagnostics.cellMembershipScanMismatchCount,
                out diagnostics.cellMembershipScanSummary);
            if (diagnostics.cellMembershipScanMismatchCount > 0)
            {
                failStage = "CellPartitionCoverage";
                failReason =
                    $"Structured cell partition did not cover expected cell corners. cells={diagnostics.cellMembershipScanCellCount} mism={diagnostics.cellMembershipScanMismatchCount} {diagnostics.cellMembershipScanSummary}";
                return false;
            }

            for (int i = 0; i < supportCellKeys.Count; i++)
            {
                StructuredCellKey key = supportCellKeys[i];
                List<StructuredCellFragment> fragments = supportCellFragments[key];
                bool merged = TryBuildMergedStructuredCellPolygon(
                    fragments,
                    rows,
                    key,
                    partitionFrames,
                    work,
                    out List<ClipVertex> mergedPolygon,
                    out StructuredCellTopologyDiagnostics topologyDiagnostics,
                    out string mergeFailReason);
                if (!merged)
                {
                    ApplyStructuredCellTopologyFailure(ref diagnostics, topologyDiagnostics);
                    failStage = DetermineStructuredCellFailureStage(topologyDiagnostics);
                    failReason = mergeFailReason;
                    return false;
                }

                diagnostics.cellTopologyTracedCount++;
                diagnostics.supportMergedPolygonCount++;
                AccumulateCellPolygonHealth(ref diagnostics, "S", key, mergedPolygon);
                allSupportMergedKeys.Add(key);
                allSupportMergedPolygons.Add(mergedPolygon);
            }

            var excisionCellKeys = new List<StructuredCellKey>(excisionCellFragments.Keys);
            excisionCellKeys.Sort((a, b) =>
            {
                int cmp = a.rowIndex.CompareTo(b.rowIndex);
                return cmp != 0 ? cmp : a.columnIndex.CompareTo(b.columnIndex);
            });
            for (int i = 0; i < excisionCellKeys.Count; i++)
            {
                StructuredCellKey key = excisionCellKeys[i];
                List<StructuredCellFragment> fragments = excisionCellFragments[key];
                bool merged = TryBuildMergedStructuredCellPolygon(
                    fragments,
                    rows,
                    key,
                    partitionFrames,
                    work,
                    out List<ClipVertex> mergedPolygon,
                    out StructuredCellTopologyDiagnostics topologyDiagnostics,
                    out string mergeFailReason);
                if (!merged)
                {
                    ApplyStructuredCellTopologyFailure(ref diagnostics, topologyDiagnostics);
                    failStage = DetermineStructuredCellFailureStage(topologyDiagnostics);
                    failReason = mergeFailReason;
                    return false;
                }

                diagnostics.cellTopologyTracedCount++;
                diagnostics.excisionMergedPolygonCount++;
                AccumulateCellPolygonHealth(ref diagnostics, "E", key, mergedPolygon);
                diagnostics.removedPolygonCount++;
                diagnostics.removedPolygonVertexCount += mergedPolygon.Count;
                allExcisionMergedPolygons.Add(mergedPolygon);
                AppendDebugPolygon(mergedPolygon, debugTriangles, layer, DebugTriangleKind.Excision);
            }

            removedTriangleCount = removedPatchTriangles.Count;
            if (removedPatchTriangles.Count == 0)
            {
                failStage = "ProjectionConsistency";
                failReason = "Structured strip partition found no strip-covered triangles.";
                return false;
            }

            for (int row = 0; row < rows.Count - 1; row++)
            {
                StructuredStripRow cur = rows[row];
                StructuredStripRow next = rows[row + 1];
                AppendStructuredStripDebugQuad(debugTriangles, layer, DebugTriangleKind.Patch, cur.leftSupport.world, cur.leftBoundary.world, next.leftBoundary.world, next.leftSupport.world);
                AppendStructuredStripDebugQuad(debugTriangles, layer, DebugTriangleKind.Patch, cur.rightBoundary.world, cur.rightSupport.world, next.rightSupport.world, next.rightBoundary.world);
                AppendStructuredStripDebugQuad(debugTriangles, layer, DebugTriangleKind.Excision, cur.leftBoundary.world, cur.rightBoundary.world, next.rightBoundary.world, next.leftBoundary.world);
            }

            MaterializeStructuredRegionPolygons(work, allOutsidePolygons, allSupportMergedPolygons, allExcisionMergedPolygons);
            NormalizeStructuredRegionPolygonsBySourceEdges(work, allOutsidePolygons, allSupportMergedPolygons, allExcisionMergedPolygons);
            NormalizeStructuredRegionPolygonsByStructuredBoundaries(work, rows, allOutsidePolygons, allSupportMergedPolygons, allExcisionMergedPolygons);

            diagnostics.failedInterfaceEdgeSummary = BuildStructuredInterfaceSummary(
                rows,
                allOutsidePolygons,
                allSupportMergedPolygons,
                allExcisionMergedPolygons,
                out diagnostics.interfaceMismatchEdgeCount,
                out StructuredInterfaceScanDiagnostics interfaceScan);
            ApplyStructuredInterfaceScanDiagnostics(ref diagnostics, interfaceScan);
            diagnostics.interfaceRepairResidualMismatchCount = diagnostics.interfaceHardMismatchEdgeCount;
            if (diagnostics.interfaceHardMismatchEdgeCount > 0)
            {
                failStage = "InterfaceConformality";
                failReason = $"Structured region interfaces are not conformal. {diagnostics.failedInterfaceEdgeSummary}";
                return false;
            }

            for (int poly = 0; poly < allOutsidePolygons.Count; poly++)
            {
                List<ClipVertex> outsidePolygon = allOutsidePolygons[poly];
                diagnostics.keptPolygonCount++;
                diagnostics.keptPolygonVertexCount += outsidePolygon.Count;
                int firstOutsideTri = nextTriangles.Count / 3;
                int outsideAdded = TriangulatePartitionPolygonIntoMesh(
                    outsidePolygon,
                    work,
                    nextTriangles,
                    ref diagnostics.droppedDegeneratePartitionPolygonCount,
                    out string outsideFailReason,
                    out float outsideAspect,
                    out float outsideMinArea,
                    out int outsideInverted);
                if (!string.IsNullOrEmpty(outsideFailReason))
                {
                    failStage = "TriangulationQuality";
                    failReason = outsideFailReason;
                    return false;
                }
                if (outsideAdded <= 0)
                    continue;

                diagnostics.outsidePartitionTriangleCount += outsideAdded;
                diagnostics.keptOutsideTriangleCount += outsideAdded;
                keptTriangleCount += outsideAdded;
                triangulationWindingMismatchCount += outsideInverted;
                outsideWindingMismatchCount += outsideInverted;
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, outsideAspect);
                if (!float.IsInfinity(outsideMinArea))
                    minPatchArea = Mathf.Min(minPatchArea, outsideMinArea);
                for (int j = 0; j < outsideAdded; j++)
                {
                    patchTriangleIndices.Add(firstOutsideTri + j);
                    outsidePatchTriangleIndices.Add(firstOutsideTri + j);
                }
            }

            for (int i = 0; i < allSupportMergedPolygons.Count; i++)
            {
                StructuredCellKey key = allSupportMergedKeys[i];
                List<ClipVertex> mergedPolygon = allSupportMergedPolygons[i];
                diagnostics.keptPolygonCount++;
                diagnostics.keptPolygonVertexCount += mergedPolygon.Count;
                int firstSupportTri = nextTriangles.Count / 3;
                int supportAdded = TriangulateGeneralPartitionPolygonIntoMesh(
                    mergedPolygon,
                    work,
                    nextTriangles,
                    ref diagnostics.droppedDegeneratePartitionPolygonCount,
                    out string supportFailReason,
                    out float supportAspect,
                    out float supportMinArea,
                    out int supportInverted);
                if (!string.IsNullOrEmpty(supportFailReason))
                {
                    diagnostics.failedCellPolygonSummary = DescribeCellPolygonHealth("S", key, mergedPolygon, true);
                    failStage = "TriangulationQuality";
                    failReason = $"Support cell triangulation failed. row={key.rowIndex} col={key.columnIndex} reason={supportFailReason}";
                    return false;
                }
                if (supportAdded <= 0)
                {
                    diagnostics.failedCellPolygonSummary = DescribeCellPolygonHealth("S", key, mergedPolygon, true);
                    failStage = "TriangulationQuality";
                    failReason = $"Support cell triangulation produced no triangles. row={key.rowIndex} col={key.columnIndex}";
                    return false;
                }

                AddTaggedBoundaryEdgesFromPolygon(mergedPolygon, work, taggedEdges);
                diagnostics.supportPartitionTriangleCount += supportAdded;
                diagnostics.keptSupportTriangleCount += supportAdded;
                keptTriangleCount += supportAdded;
                triangulationWindingMismatchCount += supportInverted;
                supportWindingMismatchCount += supportInverted;
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, supportAspect);
                if (!float.IsInfinity(supportMinArea))
                    minPatchArea = Mathf.Min(minPatchArea, supportMinArea);
                for (int j = 0; j < supportAdded; j++)
                {
                    patchTriangleIndices.Add(firstSupportTri + j);
                    supportPatchTriangleIndices.Add(firstSupportTri + j);
                }
            }

            for (int i = 0; i < allExcisionMergedPolygons.Count; i++)
                AddTaggedBoundaryEdgesFromPolygon(allExcisionMergedPolygons[i], work, taggedEdges);

            if (keptTriangleCount == 0)
            {
                failStage = "TriangulationQuality";
                failReason = "Structured strip partition produced no kept patch triangles.";
                return false;
            }

            work.triangles.Clear();
            work.triangles.AddRange(nextTriangles);

            Vector3[] remeshedWorldVertices = BuildWorldVertexArray(work.vertices, meshTransform);
            bool leftTaggedOk = TryBuildBoundarySamplesFromTaggedEdges(
                taggedEdges,
                ClipBoundaryFlags.Left,
                remeshedWorldVertices,
                partitionFrames,
                rows,
                out leftBoundary,
                out BoundaryBuildDiagnostics leftBoundaryDiagnostics);
            bool rightTaggedOk = TryBuildBoundarySamplesFromTaggedEdges(
                taggedEdges,
                ClipBoundaryFlags.Right,
                remeshedWorldVertices,
                partitionFrames,
                rows,
                out rightBoundary,
                out BoundaryBuildDiagnostics rightBoundaryDiagnostics);

            diagnostics.visibleBoundaryBuiltFromTaggedEdges = leftTaggedOk && rightTaggedOk;
            diagnostics.boundarySourceSummary = BuildTaggedBoundarySourceSummary(leftBoundaryDiagnostics, rightBoundaryDiagnostics);
            diagnostics.leftBoundaryComponentCount = leftBoundaryDiagnostics.componentCount;
            diagnostics.rightBoundaryComponentCount = rightBoundaryDiagnostics.componentCount;
            diagnostics.leftBoundaryComponentSizes = leftBoundaryDiagnostics.componentSizes;
            diagnostics.rightBoundaryComponentSizes = rightBoundaryDiagnostics.componentSizes;
            diagnostics.leftBoundaryComponentVertexCount = leftBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.rightBoundaryComponentVertexCount = rightBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.leftBoundaryOrderedVertexCount = leftBoundaryDiagnostics.orderedVertexCount;
            diagnostics.rightBoundaryOrderedVertexCount = rightBoundaryDiagnostics.orderedVertexCount;
            diagnostics.leftBoundaryDegree1VertexCount = leftBoundaryDiagnostics.degree1VertexCount;
            diagnostics.rightBoundaryDegree1VertexCount = rightBoundaryDiagnostics.degree1VertexCount;
            diagnostics.leftBoundaryDegree2VertexCount = leftBoundaryDiagnostics.degree2VertexCount;
            diagnostics.rightBoundaryDegree2VertexCount = rightBoundaryDiagnostics.degree2VertexCount;
            diagnostics.leftBoundaryBranchVertexCount = leftBoundaryDiagnostics.branchVertexCount;
            diagnostics.rightBoundaryBranchVertexCount = rightBoundaryDiagnostics.branchVertexCount;

            if (!leftTaggedOk || !rightTaggedOk)
            {
                failStage = "BoundarySemantics";
                string leftReason = string.IsNullOrEmpty(leftBoundaryDiagnostics.failReason) ? "left boundary invalid" : leftBoundaryDiagnostics.failReason;
                string rightReason = string.IsNullOrEmpty(rightBoundaryDiagnostics.failReason) ? "right boundary invalid" : rightBoundaryDiagnostics.failReason;
                failReason = $"Visible strip boundaries are invalid. left={leftReason}; right={rightReason}";
                return false;
            }

            ComputeBoundarySampleMetrics(
                leftBoundary,
                out diagnostics.leftBoundaryPolylineLength,
                out diagnostics.leftBoundarySMin,
                out diagnostics.leftBoundarySMax);
            ComputeBoundarySampleMetrics(
                rightBoundary,
                out diagnostics.rightBoundaryPolylineLength,
                out diagnostics.rightBoundarySMin,
                out diagnostics.rightBoundarySMax);

            if (leftBoundary.Count < 2 || rightBoundary.Count < 2)
            {
                failStage = "BoundarySemantics";
                failReason = $"Visible strip boundaries are insufficient. left={leftBoundary.Count}, right={rightBoundary.Count}";
                return false;
            }

            openBoundaryEdgeCount =
                CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left) +
                CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Right);
            boundaryContinuityViolationCount = Mathf.Max(0, diagnostics.leftBoundaryComponentCount - 1) + Mathf.Max(0, diagnostics.rightBoundaryComponentCount - 1);
            excisionComponentCount = rows.Count > 1 ? 1 : 0;

            diagnostics.taggedBoundaryEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left | ClipBoundaryFlags.Right);
            diagnostics.leftTaggedEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left);
            diagnostics.rightTaggedEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Right);
            diagnostics.remeshedVertexCount = work.vertices.Count;
            diagnostics.remeshedTriangleCount = work.triangles.Count / 3;

            if (!TryValidatePatchQuality(
                remeshedWorldVertices,
                work.triangles,
                patchTriangleIndices,
                null,
                ref maxPatchTriangleAspect,
                ref minPatchArea,
                ref invertedTriangleCount,
                ref degeneratePatchTriangleCount,
                ref overAspectPatchTriangleCount,
                ref auditedPatchTriangleCount))
            {
                failStage = "TriangulationQuality";
                failReason = "Patch quality validation failed.";
                return false;
            }

            AuditPatchQualityBucket(remeshedWorldVertices, work.triangles, outsidePatchTriangleIndices, out PatchQualityBucketStats outsideQualityStats);
            AuditPatchQualityBucket(remeshedWorldVertices, work.triangles, supportPatchTriangleIndices, out PatchQualityBucketStats supportQualityStats);

            diagnostics.minPatchArea = float.IsInfinity(minPatchArea) ? 0f : minPatchArea;
            diagnostics.triangulationWindingMismatchCount = triangulationWindingMismatchCount;
            diagnostics.degeneratePatchTriangleCount = degeneratePatchTriangleCount;
            diagnostics.overAspectPatchTriangleCount = overAspectPatchTriangleCount;
            diagnostics.auditedPatchTriangleCount = auditedPatchTriangleCount;
            diagnostics.outsideQualityTriangleCount = outsideQualityStats.auditedTriangleCount;
            diagnostics.outsideQualityWindingMismatchCount = outsideWindingMismatchCount;
            diagnostics.outsideQualityOverAspectCount = outsideQualityStats.overAspectTriangleCount;
            diagnostics.outsideQualityMaxAspect = outsideQualityStats.maxAspectRatio;
            diagnostics.supportQualityTriangleCount = supportQualityStats.auditedTriangleCount;
            diagnostics.supportQualityWindingMismatchCount = supportWindingMismatchCount;
            diagnostics.supportQualityOverAspectCount = supportQualityStats.overAspectTriangleCount;
            diagnostics.supportQualityMaxAspect = supportQualityStats.maxAspectRatio;
            if (invertedTriangleCount > 0 || maxPatchTriangleAspect > 12f)
            {
                failStage = "TriangulationQuality";
                failReason =
                    $"Patch quality is invalid. maxAspect={maxPatchTriangleAspect:F3}, inverted={invertedTriangleCount}, minArea={minPatchArea:F8}";
                return false;
            }

            return true;
        }

        private static bool TryApplyTaperClipRebuild(
            MeshWorkData work,
            Transform meshTransform,
            Vector3[] worldVertices,
            List<StructuredStripRow> rows,
            List<FrameSample> partitionFrames,
            PatchSelectionResult patchSelection,
            float supportOffset,
            float averageEdgeLength,
            List<DebugTriangle> debugTriangles,
            int layer,
            out List<BoundaryVertexSample> leftBoundary,
            out List<BoundaryVertexSample> rightBoundary,
            out int removedTriangleCount,
            out int keptTriangleCount,
            out int openBoundaryEdgeCount,
            out int excisionComponentCount,
            out int boundaryContinuityViolationCount,
            ref float maxPatchTriangleAspect,
            ref RemeshDiagnostics diagnostics,
            out string failStage,
            out string failReason)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();
            removedTriangleCount = 0;
            keptTriangleCount = 0;
            openBoundaryEdgeCount = 0;
            excisionComponentCount = 0;
            boundaryContinuityViolationCount = 0;
            failStage = null;
            failReason = null;

            diagnostics.usedLatticePartition = true;
            diagnostics.visibleBoundaryBuiltFromTaggedEdges = true;
            diagnostics.cellTopologyCellCount = 0;
            diagnostics.cellTopologyTracedCount = 0;
            diagnostics.cellMembershipScanCellCount = 0;
            diagnostics.cellMembershipScanMismatchCount = 0;
            diagnostics.cellMembershipScanSummary = "bypassed(taperClip)";
            diagnostics.failedCellBranchSummary = "bypassed(taperClip)";
            diagnostics.failedInterfaceEdgeSummary = "none";
            diagnostics.interfaceHardMismatchEdgeCount = 0;
            diagnostics.interfaceAuditMismatchEdgeCount = 0;
            diagnostics.interfaceScanByFlagSummary = "none";
            diagnostics.interfaceScanByRowSummary = "none";
            diagnostics.interfaceScanOwnershipSummary = "none";
            diagnostics.interfaceFeCategorySummary = "SX_LB=0 SX_RB=0 OS_LS=0 OS_RS=0 Start=0 End=0 Tip=0 Other=0";
            diagnostics.interfaceFeActualMaskSummary = "actualNone=0 actualO=0 actualS=0 actualX=0 actualOS=0 actualSX=0 actualOX=0 actualOSX=0";
            diagnostics.interfaceScanDetailSummary = "none";
            diagnostics.tipOwnershipSummary = "none";
            diagnostics.tipInterfaceKeyCompareSummary = "none";
            diagnostics.startTipWedgeHelperSummary = "none";
            diagnostics.startTipWedgeSourceNormSummary = "none";
            diagnostics.startTipWedgeBoundaryNormSummary = "none";
            diagnostics.startTipWedgeCanonicalSummary = "none";
            diagnostics.startTipWedgePostCanonicalSourceNormSummary = "none";
            diagnostics.startTipWedgePostCanonicalBoundaryNormSummary = "none";
            diagnostics.endTipEarlyHelperProbeSummary = "none";
            diagnostics.endTipOrdinaryAuditSummary = "none";
            diagnostics.boundarySourceSummary = "taggedEdges";
            diagnostics.syncSummary = "none";
            diagnostics.taperClipInputSummary = "none";

            if (work == null ||
                meshTransform == null ||
                worldVertices == null ||
                rows == null ||
                rows.Count < 2 ||
                partitionFrames == null ||
                partitionFrames.Count < 2 ||
                patchSelection == null ||
                patchSelection.patchTriangles == null ||
                patchSelection.patchTriangles.Count == 0)
            {
                failStage = "CellPartitionCoverage";
                failReason = "Taper clip rebuild input is invalid.";
                return false;
            }

            CaptureStructuredPrecommitGeometry(rows, ref diagnostics);

            BuildStructuredStripCoverageCells(
                rows,
                out List<StructuredStripCell2D> supportCells,
                out List<StructuredStripCell2D> excisionCells,
                out List<StructuredStripCell2D> domainCells);
            var startTipWedgeDiagnostics = new StructuredStartTipWedgeDiagnostics
            {
                row0BypassedOrdinaryCellCount = CountStructuredCoverageCellsBypassedForStartTip(rows, supportCells, excisionCells),
                endTipBypassedOrdinaryCellCount = CountStructuredCoverageCellsBypassedForEndTip(rows, supportCells, excisionCells),
                endOrdinaryAuditPreviewEntries = new List<string>(),
            };

            var nextTriangles = new List<int>(Mathf.Max(work.triangles.Count, rows.Count * 12));
            var patchTriangleIndices = new HashSet<int>();
            var outsidePatchTriangleIndices = new HashSet<int>();
            var supportPatchTriangleIndices = new HashSet<int>();
            var removedPatchTriangles = new HashSet<int>();
            var edgeCache = new Dictionary<EdgeBoundaryKey, ClipVertex>();
            var taggedEdges = new Dictionary<ulong, TaggedBoundaryEdge>();
            var allOutsidePolygons = new List<List<ClipVertex>>();
            var allSupportPolygons = new List<List<ClipVertex>>();
            var allExcisionPolygons = new List<List<ClipVertex>>();
            var helperStageSupportPolygons = new List<List<ClipVertex>>();
            var helperStageExcisionPolygons = new List<List<ClipVertex>>();
            int triangulationWindingMismatchCount = 0;
            int outsideWindingMismatchCount = 0;
            int supportWindingMismatchCount = 0;
            int degeneratePatchTriangleCount = 0;
            int overAspectPatchTriangleCount = 0;
            int auditedPatchTriangleCount = 0;
            int invertedTriangleCount = 0;
            float minPatchArea = float.PositiveInfinity;
            int triangleCount = work.triangles.Count / 3;

            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) ||
                    !IsValidTriangleVertex(ib, worldVertices) ||
                    !IsValidTriangleVertex(ic, worldVertices))
                {
                    continue;
                }

                bool isPatchTriangle = patchSelection.patchTriangles.Contains(tri);
                bool overlapsStructuredDomain =
                    !isPatchTriangle &&
                    TriangleOverlapsStructuredCells2D(
                        worldVertices[ia],
                        worldVertices[ib],
                        worldVertices[ic],
                        partitionFrames,
                        domainCells);
                bool shouldPartitionTriangle = isPatchTriangle || overlapsStructuredDomain;

                if (!shouldPartitionTriangle)
                {
                    nextTriangles.Add(ia);
                    nextTriangles.Add(ib);
                    nextTriangles.Add(ic);
                    continue;
                }

                debugTriangles.Add(new DebugTriangle
                {
                    a = worldVertices[ia],
                    b = worldVertices[ib],
                    c = worldVertices[ic],
                    layer = layer,
                    kind = DebugTriangleKind.Patch,
                });

                var trianglePolygon = new List<ClipVertex>(3)
                {
                    CreateClipVertexFromIndex(work, worldVertices, ia, partitionFrames),
                    CreateClipVertexFromIndex(work, worldVertices, ib, partitionFrames),
                    CreateClipVertexFromIndex(work, worldVertices, ic, partitionFrames),
                };

                PartitionTriangleAgainstStructuredCoverageCells(
                    trianglePolygon,
                    tri,
                    rows,
                    supportCells,
                    excisionCells,
                    domainCells,
                    work,
                    edgeCache,
                    ref startTipWedgeDiagnostics,
                    out List<List<ClipVertex>> outsidePolygons,
                    out List<List<ClipVertex>> supportPolygons,
                    out List<List<ClipVertex>> excisionPolygons,
                    out List<List<ClipVertex>> startTipHelperSupportPolygons,
                    out List<List<ClipVertex>> startTipHelperExcisionPolygons);

                diagnostics.preservedOutsideFragmentCount += outsidePolygons.Count;
                diagnostics.supportPartitionPolygonCount += supportPolygons.Count;
                diagnostics.excisionPartitionPolygonCount += excisionPolygons.Count;
                AppendStructuredDiagnosticPolygonClones(helperStageSupportPolygons, startTipHelperSupportPolygons);
                AppendStructuredDiagnosticPolygonClones(helperStageExcisionPolygons, startTipHelperExcisionPolygons);

                bool supportCovered = supportPolygons.Count > 0;
                bool excisionCovered = excisionPolygons.Count > 0;
                if (supportCovered)
                    diagnostics.supportCoveredTriangleCount++;
                if (excisionCovered)
                    diagnostics.excisionCoveredTriangleCount++;

                if (supportCovered || excisionCovered)
                {
                    diagnostics.stripCoveredTriangleCount++;
                    diagnostics.clippedPatchTriangleCount++;
                }

                if (excisionCovered)
                    removedPatchTriangles.Add(tri);
                else if (isPatchTriangle)
                {
                    diagnostics.patchOutsideStripTriangleCount++;
                    if (!IsUnchangedOriginalTrianglePolygon(outsidePolygons, ia, ib, ic))
                        diagnostics.changedOutsideStripTriangleCount++;
                }

                for (int poly = 0; poly < outsidePolygons.Count; poly++)
                    allOutsidePolygons.Add(outsidePolygons[poly]);
                for (int poly = 0; poly < supportPolygons.Count; poly++)
                    allSupportPolygons.Add(supportPolygons[poly]);
                for (int poly = 0; poly < excisionPolygons.Count; poly++)
                {
                    allExcisionPolygons.Add(excisionPolygons[poly]);
                    AppendDebugPolygon(excisionPolygons[poly], debugTriangles, layer, DebugTriangleKind.Excision);
                }
            }

            removedTriangleCount = removedPatchTriangles.Count;
            diagnostics.taperClipInputSummary =
                $"rows={rows.Count} partitionFrames={partitionFrames.Count} removedTriCandidate={removedPatchTriangles.Count} supportOffset={supportOffset:F6}";
            if (removedTriangleCount == 0)
            {
                failStage = "CellPartitionCoverage";
                failReason =
                    $"Taper clip rebuild found no excision-covered triangles. patch={patchSelection.patchTriangles.Count} supportPoly={allSupportPolygons.Count} excisionPoly={allExcisionPolygons.Count}";
                return false;
            }

            diagnostics.startTipWedgeHelperSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                helperStageSupportPolygons,
                helperStageExcisionPolygons,
                startTipWedgeDiagnostics);
            diagnostics.endTipEarlyHelperProbeSummary =
                string.IsNullOrEmpty(startTipWedgeDiagnostics.endEarlyHelperProbeSummary)
                ? "none"
                : startTipWedgeDiagnostics.endEarlyHelperProbeSummary;
            diagnostics.endTipOrdinaryAuditSummary = BuildStructuredEndTipOrdinaryAuditSummary(startTipWedgeDiagnostics);
            MaterializeStructuredRegionPolygons(work, allOutsidePolygons, allSupportPolygons, allExcisionPolygons);
            NormalizeStructuredRegionPolygonsBySourceEdges(
                work,
                out StructuredNormalizationStats sourceNormStats,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons);
            diagnostics.startTipWedgeSourceNormSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                allSupportPolygons,
                allExcisionPolygons,
                startTipWedgeDiagnostics);
            NormalizeStructuredRegionPolygonsByStructuredBoundaries(
                work,
                rows,
                out StructuredNormalizationStats boundaryNormStats,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons);
            diagnostics.startTipWedgeBoundaryNormSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                allSupportPolygons,
                allExcisionPolygons,
                startTipWedgeDiagnostics);
            diagnostics.interfaceNormalizationSourceEdgeKeyCount = sourceNormStats.keyCount;
            diagnostics.interfaceNormalizationSourceEdgeInsertCount = sourceNormStats.insertCount;
            diagnostics.interfaceNormalizationBoundarySegmentKeyCount = boundaryNormStats.keyCount;
            diagnostics.interfaceNormalizationBoundaryInsertCount = boundaryNormStats.insertCount;
            diagnostics.interfaceNormalizationPolygonsTouchedCount =
                sourceNormStats.polygonsTouched + boundaryNormStats.polygonsTouched;
            string[] outsideSignaturesBeforeCanonical = CapturePolygonRepairSignatures(allOutsidePolygons);
            string[] supportSignaturesBeforeCanonical = CapturePolygonRepairSignatures(allSupportPolygons);
            string[] excisionSignaturesBeforeCanonical = CapturePolygonRepairSignatures(allExcisionPolygons);
            CanonicalizeStructuredRegionPolygonsByExpectedSegments(
                work,
                rows,
                out StructuredNormalizationStats canonicalRepairStats,
                out HashSet<StructuredCanonicalBreakpointKey> canonicalMandatoryBreakpoints,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons);
            diagnostics.startTipWedgeCanonicalSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                allSupportPolygons,
                allExcisionPolygons,
                startTipWedgeDiagnostics);
            MaterializeStructuredRegionPolygons(work, allOutsidePolygons, allSupportPolygons, allExcisionPolygons);
            NormalizeStructuredRegionPolygonsBySourceEdges(
                work,
                new StructuredSourceNormalizationOptions
                {
                    rows = rows,
                    mandatoryBreakpoints = canonicalMandatoryBreakpoints,
                    preserveInsertedPointFlags = true,
                    useCanonicalCleanup = true,
                },
                out StructuredNormalizationStats sourceRepairStats,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons);
            diagnostics.startTipWedgePostCanonicalSourceNormSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                allSupportPolygons,
                allExcisionPolygons,
                startTipWedgeDiagnostics);
            NormalizeStructuredRegionPolygonsByStructuredBoundaries(
                work,
                rows,
                new StructuredBoundaryNormalizationOptions
                {
                    rows = rows,
                    mandatoryBreakpoints = canonicalMandatoryBreakpoints,
                    preserveInsertedPointFlags = true,
                    useCanonicalCleanup = true,
                },
                out StructuredNormalizationStats boundaryRepairStats,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons);
            diagnostics.startTipWedgePostCanonicalBoundaryNormSummary = BuildStructuredStartTipWedgeStageSummary(
                rows,
                allSupportPolygons,
                allExcisionPolygons,
                startTipWedgeDiagnostics);
            diagnostics.interfaceRepairNormalizedSegmentCount = canonicalRepairStats.keyCount + sourceRepairStats.keyCount + boundaryRepairStats.keyCount;
            diagnostics.interfaceRepairVertexReuseCount = canonicalRepairStats.reuseCount + sourceRepairStats.reuseCount + boundaryRepairStats.reuseCount;
            diagnostics.interfaceRepairVertexInsertCount = canonicalRepairStats.insertCount + sourceRepairStats.insertCount + boundaryRepairStats.insertCount;
            diagnostics.interfaceRepairOutsideTouchedCount = CountChangedPolygonSignatures(allOutsidePolygons, outsideSignaturesBeforeCanonical);
            diagnostics.interfaceRepairSupportTouchedCount = CountChangedPolygonSignatures(allSupportPolygons, supportSignaturesBeforeCanonical);
            diagnostics.interfaceRepairExcisionTouchedCount = CountChangedPolygonSignatures(allExcisionPolygons, excisionSignaturesBeforeCanonical);
            diagnostics.supportMergedPolygonCount = allSupportPolygons.Count;
            diagnostics.excisionMergedPolygonCount = allExcisionPolygons.Count;
            diagnostics.failedInterfaceEdgeSummary = BuildStructuredInterfaceSummary(
                rows,
                allOutsidePolygons,
                allSupportPolygons,
                allExcisionPolygons,
                out diagnostics.interfaceMismatchEdgeCount,
                out StructuredInterfaceScanDiagnostics interfaceScan,
                diagnostics.precommitFailedOwnershipEdgesWorld,
                diagnostics.precommitFailedOwnershipEdgeLabels);
            ApplyStructuredInterfaceScanDiagnostics(ref diagnostics, interfaceScan);
            diagnostics.interfaceRepairResidualMismatchCount = diagnostics.interfaceHardMismatchEdgeCount;
            if (diagnostics.interfaceHardMismatchEdgeCount > 0)
            {
                failStage = "InterfaceConformality";
                failReason = $"Taper clip region interfaces are not conformal. {diagnostics.failedInterfaceEdgeSummary}";
                return false;
            }

            diagnostics.supportMergedPolygonCount = allSupportPolygons.Count;
            diagnostics.excisionMergedPolygonCount = allExcisionPolygons.Count;
            for (int poly = 0; poly < allOutsidePolygons.Count; poly++)
            {
                List<ClipVertex> outsidePolygon = allOutsidePolygons[poly];
                diagnostics.keptPolygonCount++;
                diagnostics.keptPolygonVertexCount += outsidePolygon.Count;
                int firstOutsideTri = nextTriangles.Count / 3;
                int outsideAdded = TriangulateGeneralPartitionPolygonIntoMesh(
                    outsidePolygon,
                    work,
                    nextTriangles,
                    ref diagnostics.droppedDegeneratePartitionPolygonCount,
                    out string outsideFailReason,
                    out float outsideAspect,
                    out float outsideMinArea,
                    out int outsideInverted);
                if (!string.IsNullOrEmpty(outsideFailReason))
                {
                    failStage = "TriangulationQuality";
                    failReason = $"Taper clip outside triangulation failed. reason={outsideFailReason}";
                    return false;
                }
                if (outsideAdded <= 0)
                    continue;

                diagnostics.outsidePartitionTriangleCount += outsideAdded;
                diagnostics.keptOutsideTriangleCount += outsideAdded;
                keptTriangleCount += outsideAdded;
                triangulationWindingMismatchCount += outsideInverted;
                outsideWindingMismatchCount += outsideInverted;
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, outsideAspect);
                if (!float.IsInfinity(outsideMinArea))
                    minPatchArea = Mathf.Min(minPatchArea, outsideMinArea);
                for (int j = 0; j < outsideAdded; j++)
                {
                    patchTriangleIndices.Add(firstOutsideTri + j);
                    outsidePatchTriangleIndices.Add(firstOutsideTri + j);
                }
            }

            for (int poly = 0; poly < allSupportPolygons.Count; poly++)
            {
                List<ClipVertex> supportPolygon = allSupportPolygons[poly];
                diagnostics.keptPolygonCount++;
                diagnostics.keptPolygonVertexCount += supportPolygon.Count;
                int firstSupportTri = nextTriangles.Count / 3;
                int supportAdded = TriangulateGeneralPartitionPolygonIntoMesh(
                    supportPolygon,
                    work,
                    nextTriangles,
                    ref diagnostics.droppedDegeneratePartitionPolygonCount,
                    out string supportFailReason,
                    out float supportAspect,
                    out float supportMinArea,
                    out int supportInverted);
                if (!string.IsNullOrEmpty(supportFailReason))
                {
                    failStage = "TriangulationQuality";
                    failReason = $"Taper clip support triangulation failed. reason={supportFailReason}";
                    return false;
                }
                if (supportAdded <= 0)
                    continue;

                AddTaggedBoundaryEdgesFromPolygon(supportPolygon, work, rows, PartitionRegionKind.Support, taggedEdges);
                diagnostics.supportPartitionTriangleCount += supportAdded;
                diagnostics.keptSupportTriangleCount += supportAdded;
                keptTriangleCount += supportAdded;
                triangulationWindingMismatchCount += supportInverted;
                supportWindingMismatchCount += supportInverted;
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, supportAspect);
                if (!float.IsInfinity(supportMinArea))
                    minPatchArea = Mathf.Min(minPatchArea, supportMinArea);
                for (int j = 0; j < supportAdded; j++)
                {
                    patchTriangleIndices.Add(firstSupportTri + j);
                    supportPatchTriangleIndices.Add(firstSupportTri + j);
                }
            }

            for (int poly = 0; poly < allExcisionPolygons.Count; poly++)
            {
                List<ClipVertex> excisionPolygon = allExcisionPolygons[poly];
                diagnostics.removedPolygonCount++;
                diagnostics.removedPolygonVertexCount += excisionPolygon.Count;
                AddTaggedBoundaryEdgesFromPolygon(excisionPolygon, work, rows, PartitionRegionKind.Excision, taggedEdges);
            }

            if (keptTriangleCount == 0)
            {
                failStage = "TriangulationQuality";
                failReason = "Taper clip rebuild produced no kept patch triangles.";
                return false;
            }

            work.triangles.Clear();
            work.triangles.AddRange(nextTriangles);

            Vector3[] remeshedWorldVertices = BuildWorldVertexArray(work.vertices, meshTransform);
            bool leftTaggedOk = TryBuildBoundarySamplesFromTaggedEdges(
                taggedEdges,
                ClipBoundaryFlags.Left,
                remeshedWorldVertices,
                partitionFrames,
                rows,
                out leftBoundary,
                out BoundaryBuildDiagnostics leftBoundaryDiagnostics,
                true);
            bool rightTaggedOk = TryBuildBoundarySamplesFromTaggedEdges(
                taggedEdges,
                ClipBoundaryFlags.Right,
                remeshedWorldVertices,
                partitionFrames,
                rows,
                out rightBoundary,
                out BoundaryBuildDiagnostics rightBoundaryDiagnostics,
                true);

            diagnostics.visibleBoundaryBuiltFromTaggedEdges = leftTaggedOk && rightTaggedOk;
            diagnostics.boundarySourceSummary = BuildTaggedBoundarySourceSummary(leftBoundaryDiagnostics, rightBoundaryDiagnostics);
            diagnostics.leftBoundaryComponentCount = leftBoundaryDiagnostics.componentCount;
            diagnostics.rightBoundaryComponentCount = rightBoundaryDiagnostics.componentCount;
            diagnostics.leftBoundaryComponentSizes = leftBoundaryDiagnostics.componentSizes;
            diagnostics.rightBoundaryComponentSizes = rightBoundaryDiagnostics.componentSizes;
            diagnostics.leftBoundaryComponentVertexCount = leftBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.rightBoundaryComponentVertexCount = rightBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.leftBoundaryOrderedVertexCount = leftBoundaryDiagnostics.orderedVertexCount;
            diagnostics.rightBoundaryOrderedVertexCount = rightBoundaryDiagnostics.orderedVertexCount;
            diagnostics.leftBoundaryDegree1VertexCount = leftBoundaryDiagnostics.degree1VertexCount;
            diagnostics.rightBoundaryDegree1VertexCount = rightBoundaryDiagnostics.degree1VertexCount;
            diagnostics.leftBoundaryDegree2VertexCount = leftBoundaryDiagnostics.degree2VertexCount;
            diagnostics.rightBoundaryDegree2VertexCount = rightBoundaryDiagnostics.degree2VertexCount;
            diagnostics.leftBoundaryBranchVertexCount = leftBoundaryDiagnostics.branchVertexCount;
            diagnostics.rightBoundaryBranchVertexCount = rightBoundaryDiagnostics.branchVertexCount;

            if (!leftTaggedOk || !rightTaggedOk)
            {
                failStage = "BoundarySemantics";
                string leftReason = string.IsNullOrEmpty(leftBoundaryDiagnostics.failReason) ? "left boundary invalid" : leftBoundaryDiagnostics.failReason;
                string rightReason = string.IsNullOrEmpty(rightBoundaryDiagnostics.failReason) ? "right boundary invalid" : rightBoundaryDiagnostics.failReason;
                failReason = $"Taper clip visible boundaries are invalid. left={leftReason}; right={rightReason}";
                return false;
            }

            ComputeBoundarySampleMetrics(
                leftBoundary,
                out diagnostics.leftBoundaryPolylineLength,
                out diagnostics.leftBoundarySMin,
                out diagnostics.leftBoundarySMax);
            ComputeBoundarySampleMetrics(
                rightBoundary,
                out diagnostics.rightBoundaryPolylineLength,
                out diagnostics.rightBoundarySMin,
                out diagnostics.rightBoundarySMax);

            openBoundaryEdgeCount =
                CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left) +
                CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Right);
            boundaryContinuityViolationCount =
                Mathf.Max(0, diagnostics.leftBoundaryComponentCount - 1) +
                Mathf.Max(0, diagnostics.rightBoundaryComponentCount - 1);
            excisionComponentCount = allExcisionPolygons.Count > 0 ? 1 : 0;
            diagnostics.taggedBoundaryEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left | ClipBoundaryFlags.Right);
            diagnostics.leftTaggedEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Left);
            diagnostics.rightTaggedEdgeCount = CountTaggedBoundaryEdges(taggedEdges, ClipBoundaryFlags.Right);
            diagnostics.remeshedVertexCount = work.vertices.Count;
            diagnostics.remeshedTriangleCount = work.triangles.Count / 3;

            if (!TryValidatePatchQuality(
                    remeshedWorldVertices,
                    work.triangles,
                    patchTriangleIndices,
                    null,
                    ref maxPatchTriangleAspect,
                    ref minPatchArea,
                    ref invertedTriangleCount,
                    ref degeneratePatchTriangleCount,
                    ref overAspectPatchTriangleCount,
                    ref auditedPatchTriangleCount))
            {
                failStage = "TriangulationQuality";
                failReason = "Taper clip patch quality validation failed.";
                return false;
            }

            AuditPatchQualityBucket(remeshedWorldVertices, work.triangles, outsidePatchTriangleIndices, out PatchQualityBucketStats outsideQualityStats);
            AuditPatchQualityBucket(remeshedWorldVertices, work.triangles, supportPatchTriangleIndices, out PatchQualityBucketStats supportQualityStats);

            diagnostics.minPatchArea = float.IsInfinity(minPatchArea) ? 0f : minPatchArea;
            diagnostics.triangulationWindingMismatchCount = triangulationWindingMismatchCount;
            diagnostics.degeneratePatchTriangleCount = degeneratePatchTriangleCount;
            diagnostics.overAspectPatchTriangleCount = overAspectPatchTriangleCount;
            diagnostics.auditedPatchTriangleCount = auditedPatchTriangleCount;
            diagnostics.outsideQualityTriangleCount = outsideQualityStats.auditedTriangleCount;
            diagnostics.outsideQualityWindingMismatchCount = outsideWindingMismatchCount;
            diagnostics.outsideQualityOverAspectCount = outsideQualityStats.overAspectTriangleCount;
            diagnostics.outsideQualityMaxAspect = outsideQualityStats.maxAspectRatio;
            diagnostics.supportQualityTriangleCount = supportQualityStats.auditedTriangleCount;
            diagnostics.supportQualityWindingMismatchCount = supportWindingMismatchCount;
            diagnostics.supportQualityOverAspectCount = supportQualityStats.overAspectTriangleCount;
            diagnostics.supportQualityMaxAspect = supportQualityStats.maxAspectRatio;

            if (invertedTriangleCount > 0 || maxPatchTriangleAspect > 12f)
            {
                failStage = "TriangulationQuality";
                failReason =
                    $"Taper clip patch quality is invalid. maxAspect={maxPatchTriangleAspect:F3}, inverted={invertedTriangleCount}, minArea={diagnostics.minPatchArea:F8}";
                return false;
            }

            return true;
        }

        private static bool TryApplyBandBaselineRemesh(
            MeshWorkData work,
            Transform meshTransform,
            Vector3[] worldVertices,
            List<GraphEdge>[] graphAdjacency,
            List<FrameSample> canonicalFrames,
            PatchSelectionResult patchSelection,
            float openWidthWorld,
            float averageEdgeLength,
            float pathSnapPenalty,
            List<DebugTriangle> debugTriangles,
            int layer,
            out List<BoundaryVertexSample> leftBoundary,
            out List<BoundaryVertexSample> rightBoundary,
            out int removedTriangleCount,
            out int keptTriangleCount,
            out int openBoundaryEdgeCount,
            out int excisionComponentCount,
            out int boundaryContinuityViolationCount,
            ref float maxPatchTriangleAspect,
            ref RemeshDiagnostics diagnostics,
            out string failStage,
            out string failReason)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();
            removedTriangleCount = 0;
            keptTriangleCount = 0;
            openBoundaryEdgeCount = 0;
            excisionComponentCount = 0;
            boundaryContinuityViolationCount = 0;
            failStage = null;
            failReason = null;

            diagnostics.usedLatticePartition = true;
            diagnostics.visibleBoundaryBuiltFromTaggedEdges = true;
            diagnostics.cellTopologyCellCount = 0;
            diagnostics.cellTopologyTracedCount = 0;
            diagnostics.cellMembershipScanCellCount = 0;
            diagnostics.cellMembershipScanMismatchCount = 0;
            diagnostics.cellMembershipScanSummary = "bypassed";
            diagnostics.failedCellBranchSummary = "bypassed";
            diagnostics.failedInterfaceEdgeSummary = "bypassed";
            diagnostics.interfaceHardMismatchEdgeCount = 0;
            diagnostics.interfaceAuditMismatchEdgeCount = 0;
            diagnostics.interfaceScanByFlagSummary = "bypassed";
            diagnostics.interfaceScanByRowSummary = "bypassed";
            diagnostics.interfaceScanOwnershipSummary = "bypassed";
            diagnostics.interfaceFeCategorySummary = "bypassed";
            diagnostics.interfaceFeActualMaskSummary = "bypassed";
            diagnostics.interfaceScanDetailSummary = "bypassed";
            diagnostics.tipOwnershipSummary = "bypassed";
            diagnostics.tipInterfaceKeyCompareSummary = "bypassed";
            diagnostics.startTipWedgeHelperSummary = "bypassed";
            diagnostics.startTipWedgeSourceNormSummary = "bypassed";
            diagnostics.startTipWedgeBoundaryNormSummary = "bypassed";
            diagnostics.startTipWedgeCanonicalSummary = "bypassed";
            diagnostics.startTipWedgePostCanonicalSourceNormSummary = "bypassed";
            diagnostics.startTipWedgePostCanonicalBoundaryNormSummary = "bypassed";
            diagnostics.endTipEarlyHelperProbeSummary = "bypassed";
            diagnostics.endTipOrdinaryAuditSummary = "bypassed";
            diagnostics.boundarySourceSummary = "bandBoundaryChains";

            if (work == null || meshTransform == null || worldVertices == null || graphAdjacency == null || canonicalFrames == null || canonicalFrames.Count < 2)
            {
                failStage = "BandBaseline";
                failReason = "Band baseline input is invalid.";
                return false;
            }

            BuildTargetBoundaryPolylines(
                canonicalFrames,
                openWidthWorld,
                out List<Vector3> leftTargets,
                out List<Vector3> rightTargets);
            List<int> leftBoundaryPath = BuildBoundaryPath(
                worldVertices,
                graphAdjacency,
                canonicalFrames,
                leftTargets,
                pathSnapPenalty,
                -1f);
            List<int> rightBoundaryPath = BuildBoundaryPath(
                worldVertices,
                graphAdjacency,
                canonicalFrames,
                rightTargets,
                pathSnapPenalty,
                +1f);
            if (leftBoundaryPath.Count < 2 || rightBoundaryPath.Count < 2)
            {
                failStage = "BandBoundaryPath";
                failReason = $"Band baseline failed to solve target boundary paths. left={leftBoundaryPath.Count} right={rightBoundaryPath.Count}";
                return false;
            }

            List<int> startBridgePath = BuildTipBridgePath(worldVertices, graphAdjacency, leftBoundaryPath[0], rightBoundaryPath[0], pathSnapPenalty);
            List<int> endBridgePath = BuildTipBridgePath(
                worldVertices,
                graphAdjacency,
                leftBoundaryPath[leftBoundaryPath.Count - 1],
                rightBoundaryPath[rightBoundaryPath.Count - 1],
                pathSnapPenalty);
            if (!IsPathConnected(startBridgePath, leftBoundaryPath[0], rightBoundaryPath[0]) ||
                !IsPathConnected(endBridgePath, leftBoundaryPath[leftBoundaryPath.Count - 1], rightBoundaryPath[rightBoundaryPath.Count - 1]))
            {
                failStage = "BandBoundaryPath";
                failReason =
                    $"Band baseline failed to close taper tip bridges. start={startBridgePath.Count} end={endBridgePath.Count}";
                return false;
            }

            HashSet<int> removedTriangles = ExciseInsideBoundaryLoop(
                work,
                worldVertices,
                canonicalFrames,
                leftBoundaryPath,
                rightBoundaryPath,
                startBridgePath,
                endBridgePath,
                openWidthWorld,
                averageEdgeLength,
                debugTriangles,
                layer);
            if (patchSelection != null && patchSelection.patchTriangles.Count > 0)
                removedTriangles.IntersectWith(patchSelection.patchTriangles);

            removedTriangleCount = removedTriangles.Count;
            if (removedTriangleCount == 0)
            {
                failStage = "Excision";
                failReason = "Band baseline produced no removed triangles.";
                return false;
            }

            if (patchSelection != null &&
                patchSelection.patchTriangles.Count > 0 &&
                removedTriangleCount >= patchSelection.patchTriangles.Count)
            {
                failStage = "ExcisionTopology";
                failReason =
                    $"Band baseline removed the whole patch instead of a cut corridor. removed={removedTriangleCount} patch={patchSelection.patchTriangles.Count}";
                return false;
            }

            excisionComponentCount = CountTriangleComponents(work, removedTriangles);
            if (excisionComponentCount != 1)
            {
                failStage = "ExcisionTopology";
                failReason = $"Band baseline expected one excision component, but found {excisionComponentCount}.";
                return false;
            }

            if (!TryExtractBandBoundaryChainsWithDiagnostics(
                    work,
                    worldVertices,
                    canonicalFrames,
                    removedTriangles,
                    openWidthWorld,
                    averageEdgeLength,
                    out leftBoundary,
                    out BoundaryBuildDiagnostics leftBoundaryDiagnostics,
                    out rightBoundary,
                    out BoundaryBuildDiagnostics rightBoundaryDiagnostics,
                    out string boundaryFailReason))
            {
                diagnostics.leftBoundaryComponentCount = leftBoundaryDiagnostics.componentCount;
                diagnostics.rightBoundaryComponentCount = rightBoundaryDiagnostics.componentCount;
                diagnostics.leftBoundaryComponentSizes = leftBoundaryDiagnostics.componentSizes;
                diagnostics.rightBoundaryComponentSizes = rightBoundaryDiagnostics.componentSizes;
                diagnostics.leftBoundaryComponentVertexCount = leftBoundaryDiagnostics.selectedComponentVertexCount;
                diagnostics.rightBoundaryComponentVertexCount = rightBoundaryDiagnostics.selectedComponentVertexCount;
                diagnostics.leftBoundaryOrderedVertexCount = leftBoundaryDiagnostics.orderedVertexCount;
                diagnostics.rightBoundaryOrderedVertexCount = rightBoundaryDiagnostics.orderedVertexCount;
                diagnostics.leftBoundaryDegree1VertexCount = leftBoundaryDiagnostics.degree1VertexCount;
                diagnostics.rightBoundaryDegree1VertexCount = rightBoundaryDiagnostics.degree1VertexCount;
                diagnostics.leftBoundaryDegree2VertexCount = leftBoundaryDiagnostics.degree2VertexCount;
                diagnostics.rightBoundaryDegree2VertexCount = rightBoundaryDiagnostics.degree2VertexCount;
                diagnostics.leftBoundaryBranchVertexCount = leftBoundaryDiagnostics.branchVertexCount;
                diagnostics.rightBoundaryBranchVertexCount = rightBoundaryDiagnostics.branchVertexCount;
                failStage = "BoundarySemantics";
                failReason = boundaryFailReason;
                return false;
            }

            diagnostics.leftBoundaryComponentCount = leftBoundaryDiagnostics.componentCount;
            diagnostics.rightBoundaryComponentCount = rightBoundaryDiagnostics.componentCount;
            diagnostics.leftBoundaryComponentSizes = leftBoundaryDiagnostics.componentSizes;
            diagnostics.rightBoundaryComponentSizes = rightBoundaryDiagnostics.componentSizes;
            diagnostics.leftBoundaryComponentVertexCount = leftBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.rightBoundaryComponentVertexCount = rightBoundaryDiagnostics.selectedComponentVertexCount;
            diagnostics.leftBoundaryOrderedVertexCount = leftBoundaryDiagnostics.orderedVertexCount;
            diagnostics.rightBoundaryOrderedVertexCount = rightBoundaryDiagnostics.orderedVertexCount;
            diagnostics.leftBoundaryDegree1VertexCount = leftBoundaryDiagnostics.degree1VertexCount;
            diagnostics.rightBoundaryDegree1VertexCount = rightBoundaryDiagnostics.degree1VertexCount;
            diagnostics.leftBoundaryDegree2VertexCount = leftBoundaryDiagnostics.degree2VertexCount;
            diagnostics.rightBoundaryDegree2VertexCount = rightBoundaryDiagnostics.degree2VertexCount;
            diagnostics.leftBoundaryBranchVertexCount = leftBoundaryDiagnostics.branchVertexCount;
            diagnostics.rightBoundaryBranchVertexCount = rightBoundaryDiagnostics.branchVertexCount;
            diagnostics.taggedBoundaryEdgeCount = leftBoundaryDiagnostics.edgeCount + rightBoundaryDiagnostics.edgeCount;
            diagnostics.leftTaggedEdgeCount = leftBoundaryDiagnostics.edgeCount;
            diagnostics.rightTaggedEdgeCount = rightBoundaryDiagnostics.edgeCount;
            openBoundaryEdgeCount = diagnostics.taggedBoundaryEdgeCount;
            boundaryContinuityViolationCount =
                Mathf.Max(0, diagnostics.leftBoundaryComponentCount - 1) +
                Mathf.Max(0, diagnostics.rightBoundaryComponentCount - 1);

            ComputeBoundarySampleMetrics(
                leftBoundary,
                out diagnostics.leftBoundaryPolylineLength,
                out diagnostics.leftBoundarySMin,
                out diagnostics.leftBoundarySMax);
            ComputeBoundarySampleMetrics(
                rightBoundary,
                out diagnostics.rightBoundaryPolylineLength,
                out diagnostics.rightBoundarySMin,
                out diagnostics.rightBoundarySMax);

            int originalTriangleCount = work.triangles.Count / 3;
            var nextTriangles = new List<int>(Mathf.Max(0, work.triangles.Count - removedTriangleCount * 3));
            var keptPatchTriangleIndices = new HashSet<int>();
            int newTriangleIndex = 0;
            for (int tri = 0; tri < originalTriangleCount; tri++)
            {
                if (removedTriangles.Contains(tri))
                    continue;

                int baseIndex = tri * 3;
                nextTriangles.Add(work.triangles[baseIndex]);
                nextTriangles.Add(work.triangles[baseIndex + 1]);
                nextTriangles.Add(work.triangles[baseIndex + 2]);
                if (patchSelection != null && patchSelection.patchTriangles.Contains(tri))
                    keptPatchTriangleIndices.Add(newTriangleIndex);
                newTriangleIndex++;
            }

            work.triangles.Clear();
            work.triangles.AddRange(nextTriangles);
            keptTriangleCount = keptPatchTriangleIndices.Count;

            diagnostics.stripCoveredTriangleCount = removedTriangleCount;
            diagnostics.excisionCoveredTriangleCount = removedTriangleCount;
            diagnostics.supportCoveredTriangleCount = 0;
            diagnostics.clippedPatchTriangleCount = removedTriangleCount;
            diagnostics.patchOutsideStripTriangleCount = patchSelection != null
                ? Mathf.Max(0, patchSelection.patchTriangles.Count - removedTriangleCount)
                : 0;
            diagnostics.changedOutsideStripTriangleCount = 0;
            diagnostics.preservedOutsideFragmentCount = diagnostics.patchOutsideStripTriangleCount;
            diagnostics.supportPartitionPolygonCount = 0;
            diagnostics.excisionPartitionPolygonCount = removedTriangleCount;
            diagnostics.supportMergedPolygonCount = 0;
            diagnostics.excisionMergedPolygonCount = 0;
            diagnostics.keptPolygonCount = keptPatchTriangleIndices.Count;
            diagnostics.removedPolygonCount = removedTriangleCount;
            diagnostics.keptPolygonVertexCount = keptPatchTriangleIndices.Count * 3;
            diagnostics.removedPolygonVertexCount = removedTriangleCount * 3;
            diagnostics.outsidePartitionTriangleCount = keptPatchTriangleIndices.Count;
            diagnostics.supportPartitionTriangleCount = 0;
            diagnostics.keptOutsideTriangleCount = keptPatchTriangleIndices.Count;
            diagnostics.keptSupportTriangleCount = 0;
            diagnostics.remeshedVertexCount = work.vertices.Count;
            diagnostics.remeshedTriangleCount = work.triangles.Count / 3;

            Vector3[] remeshedWorldVertices = BuildWorldVertexArray(work.vertices, meshTransform);
            float minPatchArea = float.PositiveInfinity;
            int invertedTriangleCount = 0;
            int degeneratePatchTriangleCount = 0;
            int overAspectPatchTriangleCount = 0;
            int auditedPatchTriangleCount = 0;
            if (!TryValidatePatchQuality(
                    remeshedWorldVertices,
                    work.triangles,
                    keptPatchTriangleIndices,
                    null,
                    ref maxPatchTriangleAspect,
                    ref minPatchArea,
                    ref invertedTriangleCount,
                    ref degeneratePatchTriangleCount,
                    ref overAspectPatchTriangleCount,
                    ref auditedPatchTriangleCount))
            {
                failStage = "TriangulationQuality";
                failReason = "Band baseline patch quality validation failed.";
                return false;
            }

            AuditPatchQualityBucket(remeshedWorldVertices, work.triangles, keptPatchTriangleIndices, out PatchQualityBucketStats outsideQualityStats);
            diagnostics.minPatchArea = float.IsInfinity(minPatchArea) ? 0f : minPatchArea;
            diagnostics.triangulationWindingMismatchCount = 0;
            diagnostics.degeneratePatchTriangleCount = degeneratePatchTriangleCount;
            diagnostics.overAspectPatchTriangleCount = overAspectPatchTriangleCount;
            diagnostics.auditedPatchTriangleCount = auditedPatchTriangleCount;
            diagnostics.outsideQualityTriangleCount = outsideQualityStats.auditedTriangleCount;
            diagnostics.outsideQualityWindingMismatchCount = 0;
            diagnostics.outsideQualityOverAspectCount = outsideQualityStats.overAspectTriangleCount;
            diagnostics.outsideQualityMaxAspect = outsideQualityStats.maxAspectRatio;
            diagnostics.supportQualityTriangleCount = 0;
            diagnostics.supportQualityWindingMismatchCount = 0;
            diagnostics.supportQualityOverAspectCount = 0;
            diagnostics.supportQualityMaxAspect = 0f;

            if (invertedTriangleCount > 0 || maxPatchTriangleAspect > 12f)
            {
                failStage = "TriangulationQuality";
                failReason =
                    $"Band baseline patch quality is invalid. maxAspect={maxPatchTriangleAspect:F3}, inverted={invertedTriangleCount}, minArea={diagnostics.minPatchArea:F8}";
                return false;
            }

            return true;
        }

        private static HashSet<int> BuildVertexSetFromTriangles(MeshWorkData work, HashSet<int> triangleIndices)
        {
            var result = new HashSet<int>();
            if (work == null || triangleIndices == null)
                return result;

            foreach (int tri in triangleIndices)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                result.Add(ia);
                result.Add(ib);
                result.Add(ic);
            }

            return result;
        }

        private static List<GraphEdge>[] BuildRestrictedGraphAdjacency(List<GraphEdge>[] adjacency, HashSet<int> allowedVertices)
        {
            if (adjacency == null)
                return null;

            var result = new List<GraphEdge>[adjacency.Length];
            for (int i = 0; i < adjacency.Length; i++)
            {
                result[i] = new List<GraphEdge>(adjacency[i] != null ? adjacency[i].Count : 0);
                if (allowedVertices != null && !allowedVertices.Contains(i))
                    continue;
                if (adjacency[i] == null)
                    continue;

                for (int j = 0; j < adjacency[i].Count; j++)
                {
                    GraphEdge edge = adjacency[i][j];
                    if (allowedVertices != null && !allowedVertices.Contains(edge.to))
                        continue;
                    result[i].Add(edge);
                }
            }

            return result;
        }

        private static bool TryBuildStructuredStripRows(
            MeshWorkData work,
            Vector3[] worldVertices,
            ICollection<int> patchTriangles,
            List<GraphEdge>[] restrictedAdjacency,
            HashSet<int> allowedVertices,
            List<FrameSample> canonicalFrames,
            float supportOffset,
            float pathSnapPenalty,
            List<StructuredStripReferenceRow> referenceStripRows,
            out List<StructuredStripRow> rows,
            out StructuredStripBuildDiagnostics diagnostics,
            out string failReason)
        {
            rows = new List<StructuredStripRow>();
            diagnostics = new StructuredStripBuildDiagnostics
            {
                minBoundaryWidth = float.PositiveInfinity,
                minSupportWidth = float.PositiveInfinity,
                minLeftGap = float.PositiveInfinity,
                minCenterGap = float.PositiveInfinity,
                minRightGap = float.PositiveInfinity,
            };
            failReason = null;
            if (work == null || worldVertices == null || restrictedAdjacency == null || canonicalFrames == null || canonicalFrames.Count < 2)
            {
                failReason = "Structured strip row input is invalid.";
                return false;
            }

            BuildTargetRailPolylines(
                canonicalFrames,
                supportOffset,
                referenceStripRows,
                out List<Vector3> leftSupportTargets,
                out List<Vector3> leftBoundaryTargets,
                out List<Vector3> rightBoundaryTargets,
                out List<Vector3> rightSupportTargets);

            if (!TryBuildStructuredRailSamples(
                    worldVertices,
                    restrictedAdjacency,
                    allowedVertices,
                    canonicalFrames,
                    leftSupportTargets,
                    -1f,
                    pathSnapPenalty,
                    out List<BoundaryVertexSample> leftSupportSamples,
                    out RailBuildDiagnostics leftSupportDiagnostics,
                    out failReason))
            {
                diagnostics.leftSupportRail = leftSupportDiagnostics;
                return false;
            }

            if (!TryBuildProjectedBoundarySamples(
                    work,
                    worldVertices,
                    patchTriangles,
                    canonicalFrames,
                    leftBoundaryTargets,
                    out List<BoundaryVertexSample> leftBoundarySamples,
                    out RailBuildDiagnostics leftBoundaryDiagnostics,
                    out failReason))
            {
                diagnostics.leftSupportRail = leftSupportDiagnostics;
                diagnostics.leftBoundaryRail = leftBoundaryDiagnostics;
                return false;
            }

            if (!TryBuildProjectedBoundarySamples(
                    work,
                    worldVertices,
                    patchTriangles,
                    canonicalFrames,
                    rightBoundaryTargets,
                    out List<BoundaryVertexSample> rightBoundarySamples,
                    out RailBuildDiagnostics rightBoundaryDiagnostics,
                    out failReason))
            {
                diagnostics.leftSupportRail = leftSupportDiagnostics;
                diagnostics.leftBoundaryRail = leftBoundaryDiagnostics;
                diagnostics.rightBoundaryRail = rightBoundaryDiagnostics;
                return false;
            }

            if (!TryBuildStructuredRailSamples(
                    worldVertices,
                    restrictedAdjacency,
                    allowedVertices,
                    canonicalFrames,
                    rightSupportTargets,
                    +1f,
                    pathSnapPenalty,
                    out List<BoundaryVertexSample> rightSupportSamples,
                    out RailBuildDiagnostics rightSupportDiagnostics,
                    out failReason))
            {
                diagnostics.leftSupportRail = leftSupportDiagnostics;
                diagnostics.leftBoundaryRail = leftBoundaryDiagnostics;
                diagnostics.rightBoundaryRail = rightBoundaryDiagnostics;
                diagnostics.rightSupportRail = rightSupportDiagnostics;
                return false;
            }

            diagnostics.leftSupportRail = leftSupportDiagnostics;
            diagnostics.leftBoundaryRail = leftBoundaryDiagnostics;
            diagnostics.rightBoundaryRail = rightBoundaryDiagnostics;
            diagnostics.rightSupportRail = rightSupportDiagnostics;
            diagnostics.attemptedRowCount = canonicalFrames.Count;

            int leftSupportCursor = 0;
            int leftBoundaryCursor = 0;
            int rightBoundaryCursor = 0;
            int rightSupportCursor = 0;
            for (int i = 0; i < canonicalFrames.Count; i++)
            {
                FrameSample frame = canonicalFrames[i];
                bool endpointRow = i == 0 || i == canonicalFrames.Count - 1;
                StructuredStripRow row = new StructuredStripRow
                {
                    frame = frame,
                    frameIndex = i,
                    leftSupport = FindNearestBoundarySample(leftSupportSamples, frame.s, ref leftSupportCursor),
                    leftBoundary = FindNearestBoundarySample(leftBoundarySamples, frame.s, ref leftBoundaryCursor),
                    rightBoundary = FindNearestBoundarySample(rightBoundarySamples, frame.s, ref rightBoundaryCursor),
                    rightSupport = FindNearestBoundarySample(rightSupportSamples, frame.s, ref rightSupportCursor),
                };

                if (row.leftSupport.vertexIndex < 0 || row.leftBoundary.vertexIndex < 0 || row.rightBoundary.vertexIndex < 0 || row.rightSupport.vertexIndex < 0)
                {
                    diagnostics.collapsedRowCount++;
                    continue;
                }

                EvaluateStructuredRowSignedPositions(
                    row,
                    out float leftSupportSigned,
                    out float leftBoundarySigned,
                    out float rightBoundarySigned,
                    out float rightSupportSigned);
                float leftGap = leftBoundarySigned - leftSupportSigned;
                float centerGap = rightBoundarySigned - leftBoundarySigned;
                float rightGap = rightSupportSigned - rightBoundarySigned;
                bool tipRow = endpointRow &&
                    leftGap > 1e-6f &&
                    rightGap > 1e-6f &&
                    Mathf.Abs(centerGap) <= 1e-5f;
                row.isTipRow = tipRow;
                if (tipRow && TryCanonicalizeStructuredTipRow(ref row))
                {
                    EvaluateStructuredRowSignedPositions(
                        row,
                        out leftSupportSigned,
                        out leftBoundarySigned,
                        out rightBoundarySigned,
                        out rightSupportSigned);
                    leftGap = leftBoundarySigned - leftSupportSigned;
                    centerGap = rightBoundarySigned - leftBoundarySigned;
                    rightGap = rightSupportSigned - rightBoundarySigned;
                }
                diagnostics.minLeftGap = Mathf.Min(diagnostics.minLeftGap, leftGap);
                diagnostics.minCenterGap = Mathf.Min(diagnostics.minCenterGap, centerGap);
                diagnostics.minRightGap = Mathf.Min(diagnostics.minRightGap, rightGap);
                if (!IsStructuredRowOrdered(leftGap, centerGap, rightGap) && !tipRow)
                {
                    diagnostics.crossedRowCount++;
                    if (leftGap <= 1e-6f)
                        diagnostics.leftOrderViolationCount++;
                    if (centerGap <= 1e-6f)
                        diagnostics.centerOrderViolationCount++;
                    if (rightGap <= 1e-6f)
                        diagnostics.rightOrderViolationCount++;
                    AppendCrossedRowPreview(
                        ref diagnostics.crossedRowPreview,
                        i,
                        row,
                        leftSupportSigned,
                        leftBoundarySigned,
                        rightBoundarySigned,
                        rightSupportSigned);
                    continue;
                }

                if (row.leftSupport.vertexIndex == row.leftBoundary.vertexIndex ||
                    (!tipRow && row.leftBoundary.vertexIndex == row.rightBoundary.vertexIndex) ||
                    row.rightBoundary.vertexIndex == row.rightSupport.vertexIndex ||
                    row.leftSupport.vertexIndex == row.rightBoundary.vertexIndex ||
                    row.leftBoundary.vertexIndex == row.rightSupport.vertexIndex)
                {
                    diagnostics.collapsedRowCount++;
                    continue;
                }

                if (rows.Count > 0)
                {
                    StructuredStripRow prev = rows[rows.Count - 1];
                    bool repeated =
                        prev.leftSupport.vertexIndex == row.leftSupport.vertexIndex &&
                        prev.leftBoundary.vertexIndex == row.leftBoundary.vertexIndex &&
                        prev.rightBoundary.vertexIndex == row.rightBoundary.vertexIndex &&
                        prev.rightSupport.vertexIndex == row.rightSupport.vertexIndex;
                    if (repeated)
                    {
                        diagnostics.repeatedRowCount++;
                        continue;
                    }

                    diagnostics.maxBoundaryJump = Mathf.Max(
                        diagnostics.maxBoundaryJump,
                        Mathf.Max(
                            Vector3.Distance(prev.leftBoundary.world, row.leftBoundary.world),
                            Vector3.Distance(prev.rightBoundary.world, row.rightBoundary.world)));
                }

                diagnostics.minBoundaryWidth = Mathf.Min(
                    diagnostics.minBoundaryWidth,
                    Vector3.Distance(row.leftBoundary.world, row.rightBoundary.world));
                diagnostics.minSupportWidth = Mathf.Min(
                    diagnostics.minSupportWidth,
                    Vector3.Distance(row.leftSupport.world, row.rightSupport.world));
                if (tipRow)
                {
                    diagnostics.tipRowCount++;
                    diagnostics.keptStartTipRow |= i == 0;
                    diagnostics.keptEndTipRow |= i == canonicalFrames.Count - 1;
                }
                rows.Add(row);
            }

            diagnostics.keptRowCount = rows.Count;
            if (float.IsInfinity(diagnostics.minBoundaryWidth))
                diagnostics.minBoundaryWidth = 0f;
            if (float.IsInfinity(diagnostics.minSupportWidth))
                diagnostics.minSupportWidth = 0f;
            if (float.IsInfinity(diagnostics.minLeftGap))
                diagnostics.minLeftGap = 0f;
            if (float.IsInfinity(diagnostics.minCenterGap))
                diagnostics.minCenterGap = 0f;
            if (float.IsInfinity(diagnostics.minRightGap))
                diagnostics.minRightGap = 0f;
            if (rows.Count < 2)
            {
                failReason =
                    $"Structured strip rows collapsed after sampling. rows={rows.Count}/{canonicalFrames.Count} collapsed={diagnostics.collapsedRowCount} repeated={diagnostics.repeatedRowCount} crossed={diagnostics.crossedRowCount} tips={diagnostics.tipRowCount} startTip={(diagnostics.keptStartTipRow ? 1 : 0)} endTip={(diagnostics.keptEndTipRow ? 1 : 0)} gaps=({diagnostics.minLeftGap:F6},{diagnostics.minCenterGap:F6},{diagnostics.minRightGap:F6}) preview={diagnostics.crossedRowPreview}";
                return false;
            }

            return true;
        }

        private static bool TryCanonicalizeStructuredTipRow(ref StructuredStripRow row)
        {
            const float collapseEpsilon = 5e-4f;
            if (!row.isTipRow)
                return false;
            if (!TrySelectStructuredTipBoundarySample(row, collapseEpsilon, out BoundaryVertexSample tipSample))
                return false;

            row.leftBoundary = tipSample;
            row.rightBoundary = tipSample;
            return true;
        }

        private static bool TrySelectStructuredTipBoundarySample(
            StructuredStripRow row,
            float collapseEpsilon,
            out BoundaryVertexSample tipSample)
        {
            tipSample = default;
            float centerWidth = GetStructuredRowCenterWidth(row);
            if (Mathf.Abs(centerWidth) > collapseEpsilon)
                return false;

            bool leftValid = row.leftBoundary.vertexIndex >= 0;
            bool rightValid = row.rightBoundary.vertexIndex >= 0;
            if (!leftValid && !rightValid)
                return false;
            if (leftValid && !rightValid)
            {
                tipSample = row.leftBoundary;
                return true;
            }
            if (!leftValid)
            {
                tipSample = row.rightBoundary;
                return true;
            }

            float leftAbs = Mathf.Abs(Vector3.Dot(row.leftBoundary.world - row.frame.center, row.frame.binormal));
            float rightAbs = Mathf.Abs(Vector3.Dot(row.rightBoundary.world - row.frame.center, row.frame.binormal));
            if (leftAbs + 1e-6f < rightAbs)
            {
                tipSample = row.leftBoundary;
                return true;
            }
            if (rightAbs + 1e-6f < leftAbs)
            {
                tipSample = row.rightBoundary;
                return true;
            }

            tipSample = row.leftBoundary.vertexIndex <= row.rightBoundary.vertexIndex
                ? row.leftBoundary
                : row.rightBoundary;
            return true;
        }

        private static void BuildTargetRailPolylines(
            List<FrameSample> canonicalFrames,
            float supportOffset,
            List<StructuredStripReferenceRow> referenceStripRows,
            out List<Vector3> leftSupportTargets,
            out List<Vector3> leftBoundaryTargets,
            out List<Vector3> rightBoundaryTargets,
            out List<Vector3> rightSupportTargets)
        {
            leftSupportTargets = new List<Vector3>(canonicalFrames.Count);
            leftBoundaryTargets = new List<Vector3>(canonicalFrames.Count);
            rightBoundaryTargets = new List<Vector3>(canonicalFrames.Count);
            rightSupportTargets = new List<Vector3>(canonicalFrames.Count);

            for (int i = 0; i < canonicalFrames.Count; i++)
            {
                FrameSample frame = canonicalFrames[i];
                if (referenceStripRows != null && referenceStripRows.Count == canonicalFrames.Count)
                {
                    StructuredStripReferenceRow reference = referenceStripRows[i];
                    leftSupportTargets.Add(frame.center + frame.binormal * reference.leftSupportU);
                    leftBoundaryTargets.Add(frame.center + frame.binormal * reference.leftBoundaryU);
                    rightBoundaryTargets.Add(frame.center + frame.binormal * reference.rightBoundaryU);
                    rightSupportTargets.Add(frame.center + frame.binormal * reference.rightSupportU);
                    continue;
                }

                float halfOpen = 0.5f * frame.targetOpen;
                float supportDistance = halfOpen + supportOffset;
                leftSupportTargets.Add(frame.center - frame.binormal * supportDistance);
                leftBoundaryTargets.Add(frame.center - frame.binormal * halfOpen);
                rightBoundaryTargets.Add(frame.center + frame.binormal * halfOpen);
                rightSupportTargets.Add(frame.center + frame.binormal * supportDistance);
            }
        }

        private static bool TryBuildStructuredRailSamples(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            HashSet<int> allowedVertices,
            List<FrameSample> canonicalFrames,
            List<Vector3> targets,
            float preferredSign,
            float snapPenalty,
            out List<BoundaryVertexSample> samples,
            out RailBuildDiagnostics diagnostics,
            out string failReason)
        {
            samples = new List<BoundaryVertexSample>();
            diagnostics = default;
            failReason = null;
            List<int> anchors = BuildRestrictedBoundaryAnchors(canonicalFrames, targets, worldVertices, preferredSign, allowedVertices);
            diagnostics.anchorCount = anchors.Count;
            if (anchors.Count < 2)
            {
                failReason = $"Structured rail anchors are insufficient. sign={preferredSign:F0} anchorCount={anchors.Count}";
                return false;
            }

            List<int> path = BuildMasterPath(worldVertices, adjacency, targets, anchors, snapPenalty);
            diagnostics.pathVertexCount = path.Count;
            if (path.Count < 2)
            {
                failReason = $"Structured rail path solve failed. sign={preferredSign:F0} anchorCount={anchors.Count}";
                return false;
            }

            samples = BuildBoundarySamplesFromPath(path, worldVertices, canonicalFrames);
            diagnostics.sampleCount = samples.Count;
            if (samples.Count < 2)
            {
                failReason = $"Structured rail sample build failed. sign={preferredSign:F0} pathVerts={path.Count}";
                return false;
            }

            return true;
        }

        private static bool TryBuildProjectedBoundarySamples(
            MeshWorkData work,
            Vector3[] worldVertices,
            ICollection<int> candidateTriangles,
            List<FrameSample> canonicalFrames,
            List<Vector3> targets,
            out List<BoundaryVertexSample> samples,
            out RailBuildDiagnostics diagnostics,
            out string failReason)
        {
            samples = new List<BoundaryVertexSample>(canonicalFrames != null ? canonicalFrames.Count : 0);
            diagnostics = default;
            failReason = null;
            if (work == null || worldVertices == null || canonicalFrames == null || targets == null || canonicalFrames.Count != targets.Count)
            {
                failReason = "Projected boundary sample input is invalid.";
                return false;
            }

            diagnostics.anchorCount = targets.Count;
            diagnostics.pathVertexCount = 0;
            for (int i = 0; i < canonicalFrames.Count; i++)
            {
                TriangleProjection projection = FindNearestTriangleProjection(work, worldVertices, targets[i], candidateTriangles);
                if (projection.triangleIndex < 0)
                {
                    failReason = $"Projected boundary sample failed to find a patch triangle at row={i}.";
                    return false;
                }

                ClipVertex boundaryVertex = CreateClipVertexFromTriangleProjection(
                    work,
                    worldVertices,
                    projection.triangleIndex,
                    projection.point,
                    canonicalFrames);
                boundaryVertex = EnsureVertexMaterialized(boundaryVertex, work);
                diagnostics.sampleCount++;
                samples.Add(new BoundaryVertexSample
                {
                    vertexIndex = boundaryVertex.materializedIndex,
                    s = canonicalFrames[i].s,
                    world = boundaryVertex.world,
                });
            }

            return samples.Count >= 2;
        }

        private static List<int> BuildRestrictedBoundaryAnchors(
            List<FrameSample> canonicalFrames,
            List<Vector3> targets,
            Vector3[] worldVertices,
            float preferredSign,
            HashSet<int> allowedVertices)
        {
            var anchors = new List<int>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                int nearest = FindNearestBoundaryVertex(targets[i], canonicalFrames[i], worldVertices, preferredSign, allowedVertices);
                if (nearest < 0)
                    continue;
                if (anchors.Count == 0 || anchors[anchors.Count - 1] != nearest)
                    anchors.Add(nearest);
            }
            return anchors;
        }

        private static void EvaluateStructuredRowSignedPositions(
            StructuredStripRow row,
            out float leftSupport,
            out float leftBoundary,
            out float rightBoundary,
            out float rightSupport)
        {
            leftSupport = Vector3.Dot(row.leftSupport.world - row.frame.center, row.frame.binormal);
            leftBoundary = Vector3.Dot(row.leftBoundary.world - row.frame.center, row.frame.binormal);
            rightBoundary = Vector3.Dot(row.rightBoundary.world - row.frame.center, row.frame.binormal);
            rightSupport = Vector3.Dot(row.rightSupport.world - row.frame.center, row.frame.binormal);
        }

        private static float GetStructuredRowCenterWidth(StructuredStripRow row)
        {
            EvaluateStructuredRowSignedPositions(
                row,
                out _,
                out float leftBoundary,
                out float rightBoundary,
                out _);
            return rightBoundary - leftBoundary;
        }

        private static void CaptureStructuredStripReferenceRows(
            List<StructuredStripRow> rows,
            List<StructuredStripReferenceRow> output)
        {
            if (output == null)
                return;

            output.Clear();
            if (rows == null)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                EvaluateStructuredRowSignedPositions(
                    rows[i],
                    out float leftSupport,
                    out float leftBoundary,
                    out float rightBoundary,
                    out float rightSupport);
                output.Add(new StructuredStripReferenceRow
                {
                    s = rows[i].frame.s,
                    strokeT = rows[i].frame.strokeT,
                    targetOpen = rows[i].frame.targetOpen,
                    leftSupportU = leftSupport,
                    leftBoundaryU = leftBoundary,
                    rightBoundaryU = rightBoundary,
                    rightSupportU = rightSupport,
                });
            }
        }

        private static float ComputeStructuredSupportOffset(
            float patchHalfWidth,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            out float baseOffset,
            out float capOffset,
            out float stripMargin)
        {
            baseOffset = Mathf.Max(averageEdgeLength * 1.2f, openWidthWorld * 0.5f);
            float maxHalfOpen = 0f;
            if (canonicalFrames != null)
            {
                for (int i = 0; i < canonicalFrames.Count; i++)
                    maxHalfOpen = Mathf.Max(maxHalfOpen, 0.5f * canonicalFrames[i].targetOpen);
            }

            stripMargin = Mathf.Max(
                averageEdgeLength * 0.5f,
                openWidthWorld * 0.5f,
                patchHalfWidth * 0.2f);
            float maxSupportDistance = Mathf.Max(maxHalfOpen + 1e-4f, patchHalfWidth - stripMargin);
            capOffset = Mathf.Max(1e-4f, maxSupportDistance - maxHalfOpen);
            return Mathf.Clamp(baseOffset, 1e-4f, capOffset);
        }

        private static bool IsStructuredRowOrdered(float leftGap, float centerGap, float rightGap)
        {
            const float epsilon = 1e-6f;
            return leftGap > epsilon && centerGap > epsilon && rightGap > epsilon;
        }

        private static void AppendCrossedRowPreview(
            ref string preview,
            int rowIndex,
            StructuredStripRow row,
            float leftSupport,
            float leftBoundary,
            float rightBoundary,
            float rightSupport)
        {
            if (!string.IsNullOrEmpty(preview))
            {
                int itemCount = 1;
                for (int i = 0; i < preview.Length; i++)
                {
                    if (preview[i] == ';')
                        itemCount++;
                }

                if (itemCount >= 3)
                    return;
                preview += "; ";
            }

            preview +=
                $"r{rowIndex}:u=({leftSupport:F4},{leftBoundary:F4},{rightBoundary:F4},{rightSupport:F4}) v=({row.leftSupport.vertexIndex},{row.leftBoundary.vertexIndex},{row.rightBoundary.vertexIndex},{row.rightSupport.vertexIndex})";
        }

        private static List<BoundaryVertexSample> ExtractBoundarySamples(List<StructuredStripRow> rows, bool useLeft)
        {
            var result = new List<BoundaryVertexSample>(rows != null ? rows.Count : 0);
            if (rows == null)
                return result;

            for (int i = 0; i < rows.Count; i++)
                result.Add(useLeft ? rows[i].leftBoundary : rows[i].rightBoundary);
            RemoveDuplicateBoundarySamples(result);
            return result;
        }

        private static void BuildStructuredStripCoverageCells(
            List<StructuredStripRow> rows,
            out List<StructuredStripCell2D> supportCells,
            out List<StructuredStripCell2D> excisionCells,
            out List<StructuredStripCell2D> domainCells)
        {
            supportCells = new List<StructuredStripCell2D>();
            excisionCells = new List<StructuredStripCell2D>();
            domainCells = new List<StructuredStripCell2D>();
            if (rows == null || rows.Count < 2)
                return;

            for (int i = 0; i < rows.Count - 1; i++)
            {
                StructuredStripRow cur = rows[i];
                StructuredStripRow next = rows[i + 1];

                Vector2 curLeftSupport = BuildStructuredStripCoord(cur.frame, cur.leftSupport);
                Vector2 curLeftBoundary = BuildStructuredStripCoord(cur.frame, cur.leftBoundary);
                Vector2 curRightBoundary = BuildStructuredStripCoord(cur.frame, cur.rightBoundary);
                Vector2 curRightSupport = BuildStructuredStripCoord(cur.frame, cur.rightSupport);
                Vector2 nextLeftSupport = BuildStructuredStripCoord(next.frame, next.leftSupport);
                Vector2 nextLeftBoundary = BuildStructuredStripCoord(next.frame, next.leftBoundary);
                Vector2 nextRightBoundary = BuildStructuredStripCoord(next.frame, next.rightBoundary);
                Vector2 nextRightSupport = BuildStructuredStripCoord(next.frame, next.rightSupport);

                domainCells.Add(new StructuredStripCell2D
                {
                    a = curLeftSupport,
                    b = curRightSupport,
                    c = nextRightSupport,
                    d = nextLeftSupport,
                    sampleA = cur.leftSupport,
                    sampleB = cur.rightSupport,
                    sampleC = next.rightSupport,
                    sampleD = next.leftSupport,
                    excision = false,
                    rowIndex = cur.frameIndex,
                    columnIndex = -1,
                });

                if (TryAppendStructuredTipCoverageCells(cur, next, supportCells, excisionCells))
                    continue;

                supportCells.Add(new StructuredStripCell2D
                {
                    a = curLeftSupport,
                    b = curLeftBoundary,
                    c = nextLeftBoundary,
                    d = nextLeftSupport,
                    sampleA = cur.leftSupport,
                    sampleB = cur.leftBoundary,
                    sampleC = next.leftBoundary,
                    sampleD = next.leftSupport,
                    excision = false,
                    rowIndex = cur.frameIndex,
                    columnIndex = 0,
                });
                excisionCells.Add(new StructuredStripCell2D
                {
                    a = curLeftBoundary,
                    b = curRightBoundary,
                    c = nextRightBoundary,
                    d = nextLeftBoundary,
                    sampleA = cur.leftBoundary,
                    sampleB = cur.rightBoundary,
                    sampleC = next.rightBoundary,
                    sampleD = next.leftBoundary,
                    excision = true,
                    rowIndex = cur.frameIndex,
                    columnIndex = 1,
                });
                supportCells.Add(new StructuredStripCell2D
                {
                    a = curRightBoundary,
                    b = curRightSupport,
                    c = nextRightSupport,
                    d = nextRightBoundary,
                    sampleA = cur.rightBoundary,
                    sampleB = cur.rightSupport,
                    sampleC = next.rightSupport,
                    sampleD = next.rightBoundary,
                    excision = false,
                    rowIndex = cur.frameIndex,
                    columnIndex = 2,
                });
            }
        }

        private static bool TryAppendStructuredTipCoverageCells(
            StructuredStripRow cur,
            StructuredStripRow next,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            const float collapseEpsilon = 5e-4f;
            bool curCollapsed = TryGetStructuredCollapsedTipBoundarySample(cur, collapseEpsilon, out BoundaryVertexSample curTip);
            bool nextCollapsed = TryGetStructuredCollapsedTipBoundarySample(next, collapseEpsilon, out BoundaryVertexSample nextTip);
            if (curCollapsed && !nextCollapsed)
            {
                AppendStructuredStartTipCoverageCells(cur, next, curTip, supportCells, excisionCells);
                return true;
            }

            if (!curCollapsed && nextCollapsed)
            {
                AppendStructuredEndTipCoverageCells(cur, next, nextTip, supportCells, excisionCells);
                return true;
            }

            return false;
        }

        private static void AppendStructuredStartTipCoverageCells(
            StructuredStripRow cur,
            StructuredStripRow next,
            BoundaryVertexSample tipSample,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            Vector2 curLeftSupport = BuildStructuredStripCoord(cur.frame, cur.leftSupport);
            Vector2 curRightSupport = BuildStructuredStripCoord(cur.frame, cur.rightSupport);
            Vector2 nextLeftSupport = BuildStructuredStripCoord(next.frame, next.leftSupport);
            Vector2 nextLeftBoundary = BuildStructuredStripCoord(next.frame, next.leftBoundary);
            Vector2 nextRightBoundary = BuildStructuredStripCoord(next.frame, next.rightBoundary);
            Vector2 nextRightSupport = BuildStructuredStripCoord(next.frame, next.rightSupport);
            Vector2 tip = BuildStructuredStripCoord(cur.frame, tipSample);

            supportCells.Add(new StructuredStripCell2D
            {
                a = curLeftSupport,
                b = tip,
                c = nextLeftBoundary,
                d = nextLeftSupport,
                sampleA = cur.leftSupport,
                sampleB = tipSample,
                sampleC = next.leftBoundary,
                sampleD = next.leftSupport,
                excision = false,
                rowIndex = cur.frameIndex,
                columnIndex = 0,
            });
            excisionCells.Add(new StructuredStripCell2D
            {
                a = tip,
                b = tip,
                c = nextRightBoundary,
                d = nextLeftBoundary,
                sampleA = tipSample,
                sampleB = tipSample,
                sampleC = next.rightBoundary,
                sampleD = next.leftBoundary,
                excision = true,
                rowIndex = cur.frameIndex,
                columnIndex = 1,
            });
            supportCells.Add(new StructuredStripCell2D
            {
                a = tip,
                b = curRightSupport,
                c = nextRightSupport,
                d = nextRightBoundary,
                sampleA = tipSample,
                sampleB = cur.rightSupport,
                sampleC = next.rightSupport,
                sampleD = next.rightBoundary,
                excision = false,
                rowIndex = cur.frameIndex,
                columnIndex = 2,
            });
        }

        private static void AppendStructuredEndTipCoverageCells(
            StructuredStripRow cur,
            StructuredStripRow next,
            BoundaryVertexSample tipSample,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            Vector2 curLeftSupport = BuildStructuredStripCoord(cur.frame, cur.leftSupport);
            Vector2 curLeftBoundary = BuildStructuredStripCoord(cur.frame, cur.leftBoundary);
            Vector2 curRightBoundary = BuildStructuredStripCoord(cur.frame, cur.rightBoundary);
            Vector2 curRightSupport = BuildStructuredStripCoord(cur.frame, cur.rightSupport);
            Vector2 nextLeftSupport = BuildStructuredStripCoord(next.frame, next.leftSupport);
            Vector2 nextRightSupport = BuildStructuredStripCoord(next.frame, next.rightSupport);
            Vector2 tip = BuildStructuredStripCoord(next.frame, tipSample);

            supportCells.Add(new StructuredStripCell2D
            {
                a = curLeftSupport,
                b = curLeftBoundary,
                c = tip,
                d = nextLeftSupport,
                sampleA = cur.leftSupport,
                sampleB = cur.leftBoundary,
                sampleC = tipSample,
                sampleD = next.leftSupport,
                excision = false,
                rowIndex = cur.frameIndex,
                columnIndex = 0,
            });
            excisionCells.Add(new StructuredStripCell2D
            {
                a = curLeftBoundary,
                b = curRightBoundary,
                c = tip,
                d = tip,
                sampleA = cur.leftBoundary,
                sampleB = cur.rightBoundary,
                sampleC = tipSample,
                sampleD = tipSample,
                excision = true,
                rowIndex = cur.frameIndex,
                columnIndex = 1,
            });
            supportCells.Add(new StructuredStripCell2D
            {
                a = curRightBoundary,
                b = curRightSupport,
                c = nextRightSupport,
                d = tip,
                sampleA = cur.rightBoundary,
                sampleB = cur.rightSupport,
                sampleC = next.rightSupport,
                sampleD = tipSample,
                excision = false,
                rowIndex = cur.frameIndex,
                columnIndex = 2,
            });
        }

        private static bool TryGetStructuredCollapsedTipBoundarySample(
            StructuredStripRow row,
            float collapseEpsilon,
            out BoundaryVertexSample tipSample)
        {
            tipSample = default;
            if (!row.isTipRow)
                return false;

            float centerWidth = GetStructuredRowCenterWidth(row);
            if (Mathf.Abs(centerWidth) > collapseEpsilon)
                return false;

            BoundaryVertexSample left = row.leftBoundary;
            BoundaryVertexSample right = row.rightBoundary;
            bool leftValid = left.vertexIndex >= 0;
            bool rightValid = right.vertexIndex >= 0;
            if (leftValid && rightValid)
            {
                tipSample = left.vertexIndex <= right.vertexIndex ? left : right;
                return true;
            }

            if (leftValid)
            {
                tipSample = left;
                return true;
            }

            if (rightValid)
            {
                tipSample = right;
                return true;
            }

            return false;
        }

        private static StructuredCellKey ClassifyStructuredCellKey(
            List<ClipVertex> polygon,
            List<FrameSample> partitionFrames,
            bool excision)
        {
            float avgS = 0f;
            float avgU = 0f;
            int count = polygon != null ? polygon.Count : 0;
            int columnIndex = 1;
            if (count > 0)
            {
                bool sawLeftSupport = false;
                bool sawRightSupport = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    avgS += polygon[i].s;
                    avgU += polygon[i].u;
                    sawLeftSupport |= (polygon[i].boundaryFlags & ClipBoundaryFlags.LeftSupport) != ClipBoundaryFlags.None;
                    sawRightSupport |= (polygon[i].boundaryFlags & ClipBoundaryFlags.RightSupport) != ClipBoundaryFlags.None;
                }

                avgS /= count;
                avgU /= count;
                if (!excision)
                {
                    if (sawLeftSupport && !sawRightSupport)
                        columnIndex = 0;
                    else if (sawRightSupport && !sawLeftSupport)
                        columnIndex = 2;
                    else
                        columnIndex = avgU <= 0f ? 0 : 2;
                }
            }

            return new StructuredCellKey
            {
                rowIndex = FindStructuredRowInterval(avgS, partitionFrames),
                columnIndex = excision ? 1 : columnIndex,
            };
        }

        private static int FindStructuredRowInterval(float s, List<FrameSample> partitionFrames)
        {
            if (partitionFrames == null || partitionFrames.Count < 2)
                return 0;

            if (s <= partitionFrames[0].s)
                return 0;

            for (int i = 0; i < partitionFrames.Count - 1; i++)
            {
                if (s <= partitionFrames[i + 1].s + 1e-5f)
                    return i;
            }

            return partitionFrames.Count - 2;
        }

        private static void AddStructuredCellFragment(
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> buckets,
            StructuredCellKey key,
            List<ClipVertex> polygon,
            int sourceTriangleIndex,
            int sourcePieceIndex)
        {
            if (buckets == null || polygon == null || polygon.Count < 3)
                return;

            if (!buckets.TryGetValue(key, out List<StructuredCellFragment> fragments))
            {
                fragments = new List<StructuredCellFragment>();
                buckets.Add(key, fragments);
            }

            fragments.Add(new StructuredCellFragment
            {
                vertices = polygon,
                sourceTriangleIndex = sourceTriangleIndex,
                sourcePieceIndex = sourcePieceIndex,
            });
        }

        private static StructuredCellKey BuildStructuredCellKey(StructuredStripCell2D cell)
        {
            return new StructuredCellKey
            {
                rowIndex = cell.rowIndex,
                columnIndex = cell.columnIndex,
            };
        }

        private static bool TryBuildMergedStructuredCellPolygon(
            List<StructuredCellFragment> fragments,
            List<StructuredStripRow> rows,
            StructuredCellKey key,
            List<FrameSample> projectionFrames,
            MeshWorkData work,
            out List<ClipVertex> polygon,
            out StructuredCellTopologyDiagnostics topologyDiagnostics,
            out string failReason)
        {
            polygon = new List<ClipVertex>();
            topologyDiagnostics = new StructuredCellTopologyDiagnostics
            {
                failedRowIndex = key.rowIndex,
                failedColumnIndex = key.columnIndex,
            };
            failReason = null;
            if (fragments == null || fragments.Count == 0 || rows == null || rows.Count < 2 || work == null)
            {
                failReason = "Structured cell topology input is invalid.";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            if (!TryGetStructuredCellRowPair(
                    rows,
                    key.rowIndex,
                    out StructuredStripRow startRow,
                    out StructuredStripRow endRow,
                    out string rowPairFailReason))
            {
                topologyDiagnostics.failedDefinitionMismatchCount = 1;
                topologyDiagnostics.failedDefinitionSummary = rowPairFailReason;
                failReason = $"Structured cell topology could not resolve its row pair. row={key.rowIndex} col={key.columnIndex} reason={rowPairFailReason}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            if (!TryBuildStructuredCellDefinitionSummary(
                    startRow,
                    endRow,
                    key.columnIndex,
                    projectionFrames,
                    out topologyDiagnostics.failedDefinitionSummary,
                    out topologyDiagnostics.failedDefinitionMismatchCount))
            {
                topologyDiagnostics.failedDefinitionMismatchCount = Mathf.Max(1, topologyDiagnostics.failedDefinitionMismatchCount);
                if (string.IsNullOrEmpty(topologyDiagnostics.failedDefinitionSummary))
                    topologyDiagnostics.failedDefinitionSummary = "Structured cell definition samples are unavailable.";
                failReason =
                    $"Structured cell definition diagnostics failed. row={key.rowIndex} col={key.columnIndex} reason={topologyDiagnostics.failedDefinitionSummary}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            if (topologyDiagnostics.failedDefinitionMismatchCount > 0)
            {
                failReason =
                    $"Structured cell definition is inconsistent. row={key.rowIndex} col={key.columnIndex} reason={topologyDiagnostics.failedDefinitionSummary}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            float rowStart = startRow.frame.s;
            float rowEnd = endRow.frame.s;
            float rowEpsilon = Mathf.Max(1e-5f, Mathf.Abs(rowEnd - rowStart) * 1e-3f);
            GetStructuredCellSideFlags(key.columnIndex, out ClipBoundaryFlags leftFlags, out ClipBoundaryFlags rightFlags);
            if (!TryBuildStructuredCellExpectedCorners(
                    startRow,
                    endRow,
                    key.columnIndex,
                    out StructuredCellExpectedCorners expectedCorners,
                    out string cornerFailReason))
            {
                topologyDiagnostics.failedDefinitionMismatchCount = 1;
                topologyDiagnostics.failedDefinitionSummary = cornerFailReason;
                failReason = $"Structured cell topology could not resolve expected corners. row={key.rowIndex} col={key.columnIndex} reason={cornerFailReason}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            Dictionary<int, StructuredCellCornerFragmentPresence> preNormalizeCornerPresence =
                CountStructuredCellExpectedCornerFragmentPresence(expectedCorners, fragments);
            NormalizeStructuredCellFragmentsBySourceEdges(fragments, work);
            NormalizeStructuredCellFragmentsByStructuredSides(
                fragments,
                startRow,
                endRow,
                key.columnIndex);
            CollectStructuredCellBoundaryEdges(
                fragments,
                work,
                Mathf.Max(rowEpsilon * 4f, 1e-4f),
                out Dictionary<int, ClipVertex> vertexMap,
                out List<BoundaryEdgeState> boundaryEdges,
                out List<BoundaryEdgeState> allBoundaryEdges,
                out string edgeMultiplicitySummary);
            Dictionary<int, StructuredCellCornerFragmentPresence> postNormalizeCornerPresence =
                CountStructuredCellExpectedCornerFragmentPresence(expectedCorners, fragments);
            topologyDiagnostics.failedMembershipSummary = BuildStructuredCellMembershipSummary(
                expectedCorners,
                preNormalizeCornerPresence,
                postNormalizeCornerPresence,
                vertexMap,
                allBoundaryEdges,
                boundaryEdges);

            topologyDiagnostics.failedBoundaryEdgeCount = boundaryEdges.Count;
            if (boundaryEdges.Count < 3)
            {
                topologyDiagnostics.failedBranchSummary = BuildStructuredCellGraphSummary(
                    fragments,
                    edgeMultiplicitySummary,
                    null,
                    null,
                    vertexMap,
                    boundaryEdges);
                failReason = $"Structured cell topology found too few boundary edges. row={key.rowIndex} col={key.columnIndex} edges={boundaryEdges.Count}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            List<List<int>> components = BuildBoundaryComponents(boundaryEdges);
            topologyDiagnostics.failedLoopCount = components.Count;
            int boundaryVertexCount = 0;
            for (int i = 0; i < components.Count; i++)
                boundaryVertexCount += components[i] != null ? components[i].Count : 0;
            topologyDiagnostics.failedBoundaryVertexCount = boundaryVertexCount;
            if (components.Count != 1)
            {
                topologyDiagnostics.failedBranchSummary = BuildStructuredCellGraphSummary(
                    fragments,
                    edgeMultiplicitySummary,
                    null,
                    null,
                    vertexMap,
                    boundaryEdges);
                failReason = $"Structured cell topology expected one loop, but found {components.Count}. row={key.rowIndex} col={key.columnIndex} edges={boundaryEdges.Count} verts={boundaryVertexCount}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            List<int> component = components[0];
            Dictionary<int, List<int>> adjacency = BuildBoundaryAdjacency(component, boundaryEdges);
            CountBoundaryVertexDegrees(component, adjacency, out int degree1Count, out int degree2Count, out int branchCount);
            topologyDiagnostics.failedDegree1VertexCount = degree1Count;
            topologyDiagnostics.failedDegree2VertexCount = degree2Count;
            topologyDiagnostics.failedBranchVertexCount = branchCount;
            if (degree1Count != 0 || branchCount != 0 || degree2Count != component.Count)
            {
                topologyDiagnostics.failedBranchSummary = BuildStructuredCellGraphSummary(
                    fragments,
                    edgeMultiplicitySummary,
                    component,
                    adjacency,
                    vertexMap,
                    boundaryEdges);
                topologyDiagnostics.failedOpenEndpointSummary = DescribeStructuredCellOpenEndpoints(component, adjacency, vertexMap);
                failReason =
                    $"Structured cell topology is not a simple closed loop. row={key.rowIndex} col={key.columnIndex} edges={boundaryEdges.Count} verts={component.Count} deg=({degree1Count},{degree2Count},{branchCount})";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            if (!TryBuildStructuredCellPolygonFromSemanticChains(
                    boundaryEdges,
                    vertexMap,
                    rowStart,
                    rowEnd,
                    rowEpsilon,
                    leftFlags,
                    rightFlags,
                    expectedCorners,
                    ref topologyDiagnostics,
                    out List<ClipVertex> ordered,
                    out string semanticTraceReason))
            {
                failReason = $"Structured cell topology failed semantic chain reconstruction. row={key.rowIndex} col={key.columnIndex} reason={semanticTraceReason}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            topologyDiagnostics.failedSelfIntersection = IsPolygonSelfIntersectingSU(ordered);
            if (topologyDiagnostics.failedSelfIntersection)
            {
                failReason = $"Structured cell topology produced a self-intersecting loop. row={key.rowIndex} col={key.columnIndex} verts={ordered.Count}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            topologyDiagnostics.failedSemanticCoverage = BuildStructuredCellSemanticCoverage(
                ordered,
                expectedCorners,
                out bool hasStartSide,
                out bool hasEndSide,
                out bool hasLeftSide,
                out bool hasRightSide);
            if (!hasStartSide || !hasEndSide || !hasLeftSide || !hasRightSide)
            {
                failReason =
                    $"Structured cell topology missed semantic sides. row={key.rowIndex} col={key.columnIndex} sides={topologyDiagnostics.failedSemanticCoverage}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            polygon = CleanupPolygon(ordered);
            if (polygon.Count < 3 || ComputePolygonAreaSq(polygon) <= 1e-12f)
            {
                failReason = $"Structured cell topology produced a degenerate polygon. row={key.rowIndex} col={key.columnIndex} verts={polygon.Count}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            topologyDiagnostics.failedSelfIntersection = IsPolygonSelfIntersectingSU(polygon);
            if (topologyDiagnostics.failedSelfIntersection)
            {
                failReason = $"Structured cell polygon became self-intersecting after cleanup. row={key.rowIndex} col={key.columnIndex} verts={polygon.Count}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            float signedArea = ComputePolygonSignedAreaSU(polygon);
            if (Mathf.Abs(signedArea) <= 1e-10f)
            {
                failReason = $"Structured cell polygon has zero signed area in (s,u). row={key.rowIndex} col={key.columnIndex} verts={polygon.Count}";
                topologyDiagnostics.failedReason = failReason;
                return false;
            }

            if (signedArea < 0f)
                polygon.Reverse();

            return true;
        }

        private static bool TryGetStructuredCellRowPair(
            List<StructuredStripRow> rows,
            int rowIndex,
            out StructuredStripRow startRow,
            out StructuredStripRow endRow,
            out string failReason)
        {
            startRow = default;
            endRow = default;
            failReason = null;
            if (rows == null || rows.Count < 2)
            {
                failReason = "Structured rows are unavailable.";
                return false;
            }

            int startListIndex = -1;
            int endListIndex = -1;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].frameIndex == rowIndex)
                    startListIndex = i;
                if (rows[i].frameIndex == rowIndex + 1)
                    endListIndex = i;
            }

            if (startListIndex < 0 || endListIndex < 0)
            {
                failReason = $"Missing structured row samples for interval [{rowIndex},{rowIndex + 1}].";
                return false;
            }

            if (endListIndex != startListIndex + 1)
            {
                failReason = $"Structured row interval [{rowIndex},{rowIndex + 1}] is not consecutive in kept rows. startAt={startListIndex} endAt={endListIndex}";
                return false;
            }

            startRow = rows[startListIndex];
            endRow = rows[endListIndex];
            return true;
        }

        private static bool TryBuildStructuredCellExpectedCorners(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            out StructuredCellExpectedCorners expectedCorners,
            out string failReason)
        {
            expectedCorners = default;
            failReason = null;
            switch (columnIndex)
            {
                case 0:
                    expectedCorners.startLeft = startRow.leftSupport.vertexIndex;
                    expectedCorners.startRight = startRow.leftBoundary.vertexIndex;
                    expectedCorners.endRight = endRow.leftBoundary.vertexIndex;
                    expectedCorners.endLeft = endRow.leftSupport.vertexIndex;
                    break;
                case 1:
                    expectedCorners.startLeft = startRow.leftBoundary.vertexIndex;
                    expectedCorners.startRight = startRow.rightBoundary.vertexIndex;
                    expectedCorners.endRight = endRow.rightBoundary.vertexIndex;
                    expectedCorners.endLeft = endRow.leftBoundary.vertexIndex;
                    break;
                case 2:
                    expectedCorners.startLeft = startRow.rightBoundary.vertexIndex;
                    expectedCorners.startRight = startRow.rightSupport.vertexIndex;
                    expectedCorners.endRight = endRow.rightSupport.vertexIndex;
                    expectedCorners.endLeft = endRow.rightBoundary.vertexIndex;
                    break;
                default:
                    failReason = $"Unsupported structured cell column {columnIndex}.";
                    return false;
            }

            if (expectedCorners.startLeft < 0 ||
                expectedCorners.startRight < 0 ||
                expectedCorners.endRight < 0 ||
                expectedCorners.endLeft < 0)
            {
                failReason =
                    $"Structured cell corners are invalid. corners=({expectedCorners.startLeft},{expectedCorners.startRight},{expectedCorners.endRight},{expectedCorners.endLeft})";
                return false;
            }

            return true;
        }

        private static void GetStructuredCellSideFlags(
            int columnIndex,
            out ClipBoundaryFlags leftFlags,
            out ClipBoundaryFlags rightFlags)
        {
            switch (columnIndex)
            {
                case 0:
                    leftFlags = ClipBoundaryFlags.LeftSupport;
                    rightFlags = ClipBoundaryFlags.Left;
                    return;
                case 1:
                    leftFlags = ClipBoundaryFlags.Left;
                    rightFlags = ClipBoundaryFlags.Right;
                    return;
                case 2:
                    leftFlags = ClipBoundaryFlags.Right;
                    rightFlags = ClipBoundaryFlags.RightSupport;
                    return;
                default:
                    leftFlags = ClipBoundaryFlags.None;
                    rightFlags = ClipBoundaryFlags.None;
                    return;
            }
        }

        private static void RegisterStructuredCellVertex(Dictionary<int, ClipVertex> vertexMap, ClipVertex vertex)
        {
            if (vertex.materializedIndex < 0)
                return;

            if (vertexMap.TryGetValue(vertex.materializedIndex, out ClipVertex existing))
            {
                existing.boundaryFlags |= vertex.boundaryFlags;
                if (existing.materializedIndex < 0)
                    existing.materializedIndex = vertex.materializedIndex;
                vertexMap[vertex.materializedIndex] = existing;
                return;
            }

            vertexMap.Add(vertex.materializedIndex, vertex);
        }

        private static void CollectStructuredCellBoundaryEdges(
            List<StructuredCellFragment> fragments,
            MeshWorkData work,
            float topologyMergeTolerance,
            out Dictionary<int, ClipVertex> vertexMap,
            out List<BoundaryEdgeState> boundaryEdges,
            out List<BoundaryEdgeState> allBoundaryEdges,
            out string edgeMultiplicitySummary)
        {
            vertexMap = new Dictionary<int, ClipVertex>();
            boundaryEdges = new List<BoundaryEdgeState>();
            allBoundaryEdges = new List<BoundaryEdgeState>();
            edgeMultiplicitySummary = "none";
            if (fragments == null || work == null)
                return;

            var edgeStates = new Dictionary<ulong, BoundaryEdgeState>();
            var materializedToTopology = new Dictionary<int, int>();
            for (int i = 0; i < fragments.Count; i++)
            {
                StructuredCellFragment sourceFragment = fragments[i];
                List<ClipVertex> fragment = sourceFragment.vertices;
                if (fragment == null || fragment.Count < 3)
                    continue;

                for (int v = 0; v < fragment.Count; v++)
                {
                    ClipVertex materialized = EnsureVertexMaterialized(fragment[v], work);
                    fragment[v] = materialized;
                }

                for (int v = 0; v < fragment.Count; v++)
                {
                    ClipVertex a = fragment[v];
                    ClipVertex b = fragment[(v + 1) % fragment.Count];
                    if (a.materializedIndex < 0 || b.materializedIndex < 0 || a.materializedIndex == b.materializedIndex)
                        continue;

                    int topologyA = FindOrCreateStructuredCellTopologyVertex(
                        vertexMap,
                        materializedToTopology,
                        a,
                        topologyMergeTolerance);
                    int topologyB = FindOrCreateStructuredCellTopologyVertex(
                        vertexMap,
                        materializedToTopology,
                        b,
                        topologyMergeTolerance);
                    if (topologyA == topologyB)
                        continue;

                    ulong edgeKey = MakeUndirectedEdgeKey(topologyA, topologyB);
                    if (!edgeStates.TryGetValue(edgeKey, out BoundaryEdgeState state))
                    {
                        state = new BoundaryEdgeState
                        {
                            a = topologyA,
                            b = topologyB,
                            keptCount = 0,
                            removedCount = 0,
                        };
                    }

                    state.keptCount++;
                    AppendStructuredBoundaryEdgeProvenance(
                        ref state,
                        sourceFragment,
                        i,
                        v,
                        a,
                        b,
                        topologyA,
                        topologyB);
                    edgeStates[edgeKey] = state;
                }
            }

            foreach (var pair in edgeStates)
            {
                allBoundaryEdges.Add(pair.Value);
                if (pair.Value.keptCount == 1)
                    boundaryEdges.Add(pair.Value);
            }

            edgeMultiplicitySummary = DescribeStructuredBoundaryEdgeMultiplicity(edgeStates, vertexMap);
        }

        private static void ScanStructuredCellMembership(
            List<StructuredStripRow> rows,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> supportCellFragments,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> excisionCellFragments,
            out int scannedCellCount,
            out int mismatchCellCount,
            out string summary)
        {
            scannedCellCount = 0;
            mismatchCellCount = 0;
            summary = "none";
            if (rows == null || rows.Count < 2)
                return;

            int[] missingByColumn = new int[3];
            var preview = new List<string>(6);
            for (int row = 0; row < rows.Count - 1; row++)
            {
                StructuredStripRow startRow = rows[row];
                StructuredStripRow endRow = rows[row + 1];
                for (int column = 0; column < 3; column++)
                {
                    scannedCellCount++;
                    var key = new StructuredCellKey
                    {
                        rowIndex = startRow.frameIndex,
                        columnIndex = column,
                    };
                    List<StructuredCellFragment> fragments = GetStructuredCellFragmentsForScan(
                        key,
                        supportCellFragments,
                        excisionCellFragments);

                    if (!TryBuildStructuredCellExpectedCorners(
                            startRow,
                            endRow,
                            column,
                            out StructuredCellExpectedCorners expectedCorners,
                            out string failReason))
                    {
                        mismatchCellCount++;
                        missingByColumn[column]++;
                        if (preview.Count < 6)
                            preview.Add($"r{key.rowIndex} c{column} def={failReason}");
                        continue;
                    }

                    Dictionary<int, StructuredCellCornerFragmentPresence> presence =
                        CountStructuredCellExpectedCornerFragmentPresence(expectedCorners, fragments);
                    string missing = BuildStructuredCellMissingCornerLabels(
                        expectedCorners,
                        presence,
                        startRow,
                        endRow,
                        column,
                        fragments);
                    if (string.IsNullOrEmpty(missing))
                        continue;

                    mismatchCellCount++;
                    missingByColumn[column]++;
                    if (preview.Count < 6)
                    {
                        CountStructuredCellFragments(fragments, out int fragmentCount, out int vertexCount);
                        preview.Add(
                            $"r{key.rowIndex} c{column} missing={missing} frag={fragmentCount} verts={vertexCount}");
                    }
                }
            }

            string previewText = preview.Count > 0 ? string.Join(" ; ", preview.ToArray()) : "none";
            summary =
                $"byCol=({missingByColumn[0]},{missingByColumn[1]},{missingByColumn[2]}) preview={previewText}";
        }

        private static List<StructuredCellFragment> GetStructuredCellFragmentsForScan(
            StructuredCellKey key,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> supportCellFragments,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> excisionCellFragments)
        {
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> source =
                key.columnIndex == 1 ? excisionCellFragments : supportCellFragments;
            if (source != null && source.TryGetValue(key, out List<StructuredCellFragment> fragments))
                return fragments;

            return null;
        }

        private static string BuildStructuredCellMissingCornerLabels(
            StructuredCellExpectedCorners expectedCorners,
            Dictionary<int, StructuredCellCornerFragmentPresence> presence,
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            List<StructuredCellFragment> fragments)
        {
            var missing = new List<string>(4);
            TryAppendMissingStructuredCellCorner(
                missing,
                "SL",
                expectedCorners.startLeft,
                presence,
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 0),
                fragments);
            TryAppendMissingStructuredCellCorner(
                missing,
                "SR",
                expectedCorners.startRight,
                presence,
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 1),
                fragments);
            TryAppendMissingStructuredCellCorner(
                missing,
                "ER",
                expectedCorners.endRight,
                presence,
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 2),
                fragments);
            TryAppendMissingStructuredCellCorner(
                missing,
                "EL",
                expectedCorners.endLeft,
                presence,
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 3),
                fragments);
            return missing.Count > 0 ? string.Join("/", missing.ToArray()) : null;
        }

        private static void TryAppendMissingStructuredCellCorner(
            List<string> missing,
            string label,
            int materializedIndex,
            Dictionary<int, StructuredCellCornerFragmentPresence> presence,
            Vector2 cornerCoord,
            List<StructuredCellFragment> fragments)
        {
            if (missing == null || materializedIndex < 0)
                return;

            StructuredCellCornerFragmentPresence cornerPresence =
                GetStructuredCellCornerFragmentPresence(presence, materializedIndex);
            if (cornerPresence.vertexOccurrences > 0)
                return;

            string nearestDistance = FormatNearestStructuredCellFragmentDistance(cornerCoord, fragments);
            missing.Add($"{label}:m{materializedIndex}(near={nearestDistance})");
        }

        private static string FormatNearestStructuredCellFragmentDistance(
            Vector2 cornerCoord,
            List<StructuredCellFragment> fragments)
        {
            if (!TryFindNearestStructuredCellFragmentDistanceSq(cornerCoord, fragments, out float distanceSq))
                return "none";

            return Mathf.Sqrt(Mathf.Max(0f, distanceSq)).ToString("F4");
        }

        private static bool TryFindNearestStructuredCellFragmentDistanceSq(
            Vector2 cornerCoord,
            List<StructuredCellFragment> fragments,
            out float distanceSq)
        {
            distanceSq = float.PositiveInfinity;
            if (fragments == null)
                return false;

            bool found = false;
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                List<ClipVertex> vertices = fragments[fragmentIndex].vertices;
                if (vertices == null || vertices.Count == 0)
                    continue;

                if (vertices.Count == 1)
                {
                    Vector2 only = new Vector2(vertices[0].s, vertices[0].u);
                    distanceSq = Mathf.Min(distanceSq, (cornerCoord - only).sqrMagnitude);
                    found = true;
                    continue;
                }

                int edgeCount = vertices.Count == 2 ? 1 : vertices.Count;
                for (int i = 0; i < edgeCount; i++)
                {
                    ClipVertex a = vertices[i];
                    ClipVertex b = vertices[(i + 1) % vertices.Count];
                    float edgeDistanceSq =
                        DistanceSqPointToSegment(cornerCoord, new Vector2(a.s, a.u), new Vector2(b.s, b.u));
                    if (edgeDistanceSq < distanceSq)
                        distanceSq = edgeDistanceSq;
                    found = true;
                }
            }

            return found;
        }

        private static void CountStructuredCellFragments(
            List<StructuredCellFragment> fragments,
            out int fragmentCount,
            out int vertexCount)
        {
            fragmentCount = fragments != null ? fragments.Count : 0;
            vertexCount = 0;
            if (fragments == null)
                return;

            for (int i = 0; i < fragments.Count; i++)
            {
                if (fragments[i].vertices != null)
                    vertexCount += fragments[i].vertices.Count;
            }
        }

        private static Dictionary<int, StructuredCellCornerFragmentPresence> CountStructuredCellExpectedCornerFragmentPresence(
            StructuredCellExpectedCorners expectedCorners,
            List<StructuredCellFragment> fragments)
        {
            var result = new Dictionary<int, StructuredCellCornerFragmentPresence>();
            AddStructuredCellExpectedCornerTarget(result, expectedCorners.startLeft);
            AddStructuredCellExpectedCornerTarget(result, expectedCorners.startRight);
            AddStructuredCellExpectedCornerTarget(result, expectedCorners.endRight);
            AddStructuredCellExpectedCornerTarget(result, expectedCorners.endLeft);
            if (fragments == null || result.Count == 0)
                return result;

            var targets = new List<int>(result.Keys);
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                List<ClipVertex> vertices = fragments[fragmentIndex].vertices;
                if (vertices == null || vertices.Count == 0)
                    continue;

                var hitInFragment = new HashSet<int>();
                for (int vertexIndex = 0; vertexIndex < vertices.Count; vertexIndex++)
                {
                    ClipVertex vertex = vertices[vertexIndex];
                    for (int targetIndex = 0; targetIndex < targets.Count; targetIndex++)
                    {
                        int target = targets[targetIndex];
                        if (!StructuredCellVertexMatchesMaterialized(vertex, target))
                            continue;

                        StructuredCellCornerFragmentPresence presence = result[target];
                        presence.vertexOccurrences++;
                        result[target] = presence;
                        hitInFragment.Add(target);
                    }
                }

                foreach (int target in hitInFragment)
                {
                    StructuredCellCornerFragmentPresence presence = result[target];
                    presence.fragmentOccurrences++;
                    result[target] = presence;
                }
            }

            return result;
        }

        private static void AddStructuredCellExpectedCornerTarget(
            Dictionary<int, StructuredCellCornerFragmentPresence> targets,
            int materializedIndex)
        {
            if (targets == null || materializedIndex < 0 || targets.ContainsKey(materializedIndex))
                return;

            targets.Add(materializedIndex, default);
        }

        private static bool StructuredCellVertexMatchesMaterialized(ClipVertex vertex, int materializedIndex)
        {
            if (materializedIndex < 0)
                return false;
            if (vertex.materializedIndex == materializedIndex)
                return true;

            return vertex.supportA == materializedIndex && vertex.supportB == materializedIndex;
        }

        private static string BuildStructuredCellMembershipSummary(
            StructuredCellExpectedCorners expectedCorners,
            Dictionary<int, StructuredCellCornerFragmentPresence> preNormalizePresence,
            Dictionary<int, StructuredCellCornerFragmentPresence> postNormalizePresence,
            Dictionary<int, ClipVertex> vertexMap,
            List<BoundaryEdgeState> rawEdges,
            List<BoundaryEdgeState> keptEdges)
        {
            return string.Join(
                " ; ",
                new[]
                {
                    BuildStructuredCellCornerMembershipSummary("SL", expectedCorners.startLeft, preNormalizePresence, postNormalizePresence, vertexMap, rawEdges, keptEdges),
                    BuildStructuredCellCornerMembershipSummary("SR", expectedCorners.startRight, preNormalizePresence, postNormalizePresence, vertexMap, rawEdges, keptEdges),
                    BuildStructuredCellCornerMembershipSummary("ER", expectedCorners.endRight, preNormalizePresence, postNormalizePresence, vertexMap, rawEdges, keptEdges),
                    BuildStructuredCellCornerMembershipSummary("EL", expectedCorners.endLeft, preNormalizePresence, postNormalizePresence, vertexMap, rawEdges, keptEdges),
                });
        }

        private static string BuildStructuredCellCornerMembershipSummary(
            string label,
            int materializedIndex,
            Dictionary<int, StructuredCellCornerFragmentPresence> preNormalizePresence,
            Dictionary<int, StructuredCellCornerFragmentPresence> postNormalizePresence,
            Dictionary<int, ClipVertex> vertexMap,
            List<BoundaryEdgeState> rawEdges,
            List<BoundaryEdgeState> keptEdges)
        {
            StructuredCellCornerFragmentPresence pre = GetStructuredCellCornerFragmentPresence(preNormalizePresence, materializedIndex);
            StructuredCellCornerFragmentPresence post = GetStructuredCellCornerFragmentPresence(postNormalizePresence, materializedIndex);
            CountStructuredCellVertexMapHits(vertexMap, materializedIndex, out int vertexMapHits, out string topologySummary);
            int rawDegree = CountStructuredCellIncidentEdges(vertexMap, rawEdges, materializedIndex);
            int keptDegree = CountStructuredCellIncidentEdges(vertexMap, keptEdges, materializedIndex);
            return
                $"{label}=m{materializedIndex} " +
                $"pre={pre.vertexOccurrences}/{pre.fragmentOccurrences} " +
                $"post={post.vertexOccurrences}/{post.fragmentOccurrences} " +
                $"vm={vertexMapHits} topo=({topologySummary}) rawDeg={rawDegree} keptDeg={keptDegree}";
        }

        private static StructuredCellCornerFragmentPresence GetStructuredCellCornerFragmentPresence(
            Dictionary<int, StructuredCellCornerFragmentPresence> presenceByMaterialized,
            int materializedIndex)
        {
            if (presenceByMaterialized != null &&
                materializedIndex >= 0 &&
                presenceByMaterialized.TryGetValue(materializedIndex, out StructuredCellCornerFragmentPresence presence))
            {
                return presence;
            }

            return default;
        }

        private static void CountStructuredCellVertexMapHits(
            Dictionary<int, ClipVertex> vertexMap,
            int materializedIndex,
            out int hitCount,
            out string topologySummary)
        {
            hitCount = 0;
            topologySummary = "none";
            if (vertexMap == null || materializedIndex < 0)
                return;

            var hits = new List<string>(2);
            foreach (var pair in vertexMap)
            {
                if (pair.Value.materializedIndex != materializedIndex)
                    continue;

                hitCount++;
                if (hits.Count < 3)
                    hits.Add(FormatStructuredCellVertex(pair.Key, vertexMap));
            }

            if (hits.Count > 0)
                topologySummary = string.Join(",", hits.ToArray());
        }

        private static int CountStructuredCellIncidentEdges(
            Dictionary<int, ClipVertex> vertexMap,
            List<BoundaryEdgeState> edges,
            int materializedIndex)
        {
            if (vertexMap == null || edges == null || materializedIndex < 0)
                return 0;

            var topologyVertices = new HashSet<int>();
            foreach (var pair in vertexMap)
            {
                if (pair.Value.materializedIndex == materializedIndex)
                    topologyVertices.Add(pair.Key);
            }

            if (topologyVertices.Count == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < edges.Count; i++)
            {
                if (topologyVertices.Contains(edges[i].a) || topologyVertices.Contains(edges[i].b))
                    count++;
            }

            return count;
        }

        private static void MaterializeStructuredRegionPolygons(
            MeshWorkData work,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            if (work == null || polygonGroups == null)
                return;

            for (int groupIndex = 0; groupIndex < polygonGroups.Length; groupIndex++)
            {
                List<List<ClipVertex>> polygons = polygonGroups[groupIndex];
                if (polygons == null)
                    continue;

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    List<ClipVertex> polygon = polygons[polygonIndex];
                    if (polygon == null)
                        continue;

                    for (int i = 0; i < polygon.Count; i++)
                        polygon[i] = EnsureVertexMaterialized(polygon[i], work);
                }
            }
        }

        private static void NormalizeStructuredRegionPolygonsBySourceEdges(
            MeshWorkData work,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            NormalizeStructuredRegionPolygonsBySourceEdges(
                work,
                default,
                out _,
                polygonGroups);
        }

        private static void NormalizeStructuredRegionPolygonsBySourceEdges(
            MeshWorkData work,
            out StructuredNormalizationStats stats,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            NormalizeStructuredRegionPolygonsBySourceEdges(
                work,
                default,
                out stats,
                polygonGroups);
        }

        private static void NormalizeStructuredRegionPolygonsBySourceEdges(
            MeshWorkData work,
            StructuredSourceNormalizationOptions options,
            out StructuredNormalizationStats stats,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            stats = default;
            if (work == null || polygonGroups == null)
                return;

            var sourceEdgePoints = new Dictionary<ulong, List<SourceEdgePoint>>();
            for (int groupIndex = 0; groupIndex < polygonGroups.Length; groupIndex++)
            {
                List<List<ClipVertex>> polygons = polygonGroups[groupIndex];
                if (polygons == null)
                    continue;

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    List<ClipVertex> polygon = polygons[polygonIndex];
                    if (polygon == null || polygon.Count < 2)
                        continue;

                    for (int i = 0; i < polygon.Count; i++)
                    {
                        ClipVertex a = polygon[i];
                        ClipVertex b = polygon[(i + 1) % polygon.Count];
                        if (!TryResolveSupportEdge(a, b, out int edgeA, out int edgeB))
                            continue;

                        ulong key = MakeUndirectedEdgeKey(edgeA, edgeB);
                        stats.reuseCount += CountStructuredPointReuses(AddStructuredSourceEdgePoint(sourceEdgePoints, key, edgeA, edgeB, a, work));
                        stats.reuseCount += CountStructuredPointReuses(AddStructuredSourceEdgePoint(sourceEdgePoints, key, edgeA, edgeB, b, work));
                    }
                }
            }

            stats.keyCount = sourceEdgePoints.Count;
            if (sourceEdgePoints.Count == 0)
                return;

            foreach (var pair in sourceEdgePoints)
                pair.Value.Sort((a, b) => a.t.CompareTo(b.t));

            for (int groupIndex = 0; groupIndex < polygonGroups.Length; groupIndex++)
            {
                List<List<ClipVertex>> polygons = polygonGroups[groupIndex];
                if (polygons == null)
                    continue;

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    List<ClipVertex> polygon = polygons[polygonIndex];
                    if (polygon == null || polygon.Count < 2)
                        continue;

                    PartitionRegionKind regionKind = PartitionRegionKind.Outside;
                    if (groupIndex == 1)
                        regionKind = PartitionRegionKind.Support;
                    else if (groupIndex >= 2)
                        regionKind = PartitionRegionKind.Excision;

                    bool polygonTouched = false;
                    var normalized = new List<ClipVertex>(polygon.Count);
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        ClipVertex a = polygon[i];
                        ClipVertex b = polygon[(i + 1) % polygon.Count];
                        AppendStructuredNormalizedVertex(normalized, a);

                        if (!TryResolveSupportEdge(a, b, out int edgeA, out int edgeB))
                            continue;

                        ulong key = MakeUndirectedEdgeKey(edgeA, edgeB);
                        if (!sourceEdgePoints.TryGetValue(key, out List<SourceEdgePoint> points) || points.Count <= 2)
                            continue;

                        if (!TryComputeSourceEdgeParameter(a, work, edgeA, edgeB, out float ta) ||
                            !TryComputeSourceEdgeParameter(b, work, edgeA, edgeB, out float tb))
                        {
                            continue;
                        }

                        ClipBoundaryFlags hostFlags = a.boundaryFlags & b.boundaryFlags;
                        StructuredBoundarySegmentKey structuredKey = default;
                        ClipBoundaryFlags structuredBoundaryFlag = ClipBoundaryFlags.None;
                        bool useStructuredCanonicalProtection =
                            options.rows != null &&
                            options.mandatoryBreakpoints != null &&
                            options.mandatoryBreakpoints.Count > 0 &&
                            TryResolveStructuredBoundarySegmentForEdge(
                                options.rows,
                                a,
                                b,
                                out structuredKey,
                                out structuredBoundaryFlag,
                                out _,
                                out _) &&
                            DoesRegionOwnStructuredInterfaceFlag(regionKind, structuredBoundaryFlag) &&
                            ShouldCanonicalizeStructuredExpectedSegment(options.rows, regionKind, structuredKey, structuredBoundaryFlag);

                        int inserted = useStructuredCanonicalProtection
                            ? AppendStructuredProtectedInteriorSourceEdgePoints(
                                normalized,
                                points,
                                ta,
                                tb,
                                hostFlags,
                                structuredKey,
                                options.mandatoryBreakpoints,
                                options.preserveInsertedPointFlags)
                            : AppendStructuredInteriorSourceEdgePoints(normalized, points, ta, tb, hostFlags);
                        if (inserted > 0)
                        {
                            polygonTouched = true;
                            stats.insertCount += inserted;
                        }
                    }

                    RemoveStructuredClosingDuplicate(normalized);
                    normalized =
                        options.useCanonicalCleanup &&
                        options.rows != null &&
                        options.mandatoryBreakpoints != null &&
                        options.mandatoryBreakpoints.Count > 0
                        ? CleanupStructuredCanonicalPolygon(options.rows, normalized, options.mandatoryBreakpoints)
                        : CleanupPolygon(normalized);
                    if (normalized.Count >= 3)
                    {
                        polygons[polygonIndex] = normalized;
                        if (polygonTouched || normalized.Count != polygon.Count)
                            stats.polygonsTouched++;
                    }
                }
            }
        }

        private static void NormalizeStructuredRegionPolygonsByStructuredBoundaries(
            MeshWorkData work,
            List<StructuredStripRow> rows,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            NormalizeStructuredRegionPolygonsByStructuredBoundaries(
                work,
                rows,
                out _,
                polygonGroups);
        }

        private static void NormalizeStructuredRegionPolygonsByStructuredBoundaries(
            MeshWorkData work,
            List<StructuredStripRow> rows,
            out StructuredNormalizationStats stats,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            NormalizeStructuredRegionPolygonsByStructuredBoundaries(
                work,
                rows,
                default,
                out stats,
                polygonGroups);
        }

        private static void NormalizeStructuredRegionPolygonsByStructuredBoundaries(
            MeshWorkData work,
            List<StructuredStripRow> rows,
            StructuredBoundaryNormalizationOptions options,
            out StructuredNormalizationStats stats,
            params List<List<ClipVertex>>[] polygonGroups)
        {
            stats = default;
            if (work == null || rows == null || rows.Count < 2 || polygonGroups == null)
                return;

            var boundaryPoints = new Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>>();
            for (int groupIndex = 0; groupIndex < polygonGroups.Length; groupIndex++)
            {
                List<List<ClipVertex>> polygons = polygonGroups[groupIndex];
                if (polygons == null)
                    continue;

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    List<ClipVertex> polygon = polygons[polygonIndex];
                    if (polygon == null || polygon.Count < 2)
                        continue;

                    for (int i = 0; i < polygon.Count; i++)
                    {
                        ClipVertex a = polygon[i];
                        ClipVertex b = polygon[(i + 1) % polygon.Count];
                        if (!TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out StructuredBoundarySegmentKey key, out _, out float ta, out float tb))
                            continue;

                        stats.reuseCount += CountStructuredPointReuses(AddStructuredBoundarySegmentPoint(boundaryPoints, key, a, ta));
                        stats.reuseCount += CountStructuredPointReuses(AddStructuredBoundarySegmentPoint(boundaryPoints, key, b, tb));
                    }
                }
            }

            stats.keyCount = boundaryPoints.Count;
            if (boundaryPoints.Count == 0)
                return;

            foreach (var pair in boundaryPoints)
                pair.Value.Sort((a, b) => a.t.CompareTo(b.t));

            for (int groupIndex = 0; groupIndex < polygonGroups.Length; groupIndex++)
            {
                List<List<ClipVertex>> polygons = polygonGroups[groupIndex];
                if (polygons == null)
                    continue;

                for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
                {
                    List<ClipVertex> polygon = polygons[polygonIndex];
                    if (polygon == null || polygon.Count < 2)
                        continue;

                    PartitionRegionKind regionKind = PartitionRegionKind.Outside;
                    if (groupIndex == 1)
                        regionKind = PartitionRegionKind.Support;
                    else if (groupIndex >= 2)
                        regionKind = PartitionRegionKind.Excision;

                    bool polygonTouched = false;
                    var normalized = new List<ClipVertex>(polygon.Count);
                    for (int i = 0; i < polygon.Count; i++)
                    {
                        ClipVertex a = polygon[i];
                        ClipVertex b = polygon[(i + 1) % polygon.Count];
                        AppendStructuredNormalizedVertex(normalized, a);

                        StructuredBoundarySegmentKey structuredKey = default;
                        ClipBoundaryFlags boundaryFlag = ClipBoundaryFlags.None;
                        float ta = 0f;
                        float tb = 0f;
                        bool useStructuredCanonicalProtection =
                            options.rows != null &&
                            options.mandatoryBreakpoints != null &&
                            options.mandatoryBreakpoints.Count > 0 &&
                            TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out structuredKey, out boundaryFlag, out ta, out tb) &&
                            DoesRegionOwnStructuredInterfaceFlag(regionKind, boundaryFlag) &&
                            ShouldCanonicalizeStructuredExpectedSegment(rows, regionKind, structuredKey, boundaryFlag);

                        int inserted = useStructuredCanonicalProtection
                            ? AppendStructuredProtectedBoundaryInteriorPoints(
                                normalized,
                                boundaryPoints,
                                rows,
                                a,
                                b,
                                structuredKey,
                                boundaryFlag,
                                ta,
                                tb,
                                options.mandatoryBreakpoints,
                                options.preserveInsertedPointFlags)
                            : AppendStructuredBoundaryInteriorPoints(normalized, boundaryPoints, rows, a, b);
                        if (inserted > 0)
                        {
                            polygonTouched = true;
                            stats.insertCount += inserted;
                        }
                    }

                    RemoveStructuredClosingDuplicate(normalized);
                    normalized =
                        options.useCanonicalCleanup &&
                        options.rows != null &&
                        options.mandatoryBreakpoints != null &&
                        options.mandatoryBreakpoints.Count > 0
                        ? CleanupStructuredCanonicalPolygon(options.rows, normalized, options.mandatoryBreakpoints)
                        : CleanupPolygon(normalized);
                    if (normalized.Count >= 3)
                    {
                        polygons[polygonIndex] = normalized;
                        if (polygonTouched || normalized.Count != polygon.Count)
                            stats.polygonsTouched++;
                    }
                }
            }
        }

        private const ClipBoundaryFlags StructuredRegionInterfaceFlags =
            ClipBoundaryFlags.LeftSupport |
            ClipBoundaryFlags.Left |
            ClipBoundaryFlags.Right |
            ClipBoundaryFlags.RightSupport;

        private static readonly ClipBoundaryFlags[] StructuredRegionInterfaceFlagOrder =
        {
            ClipBoundaryFlags.LeftSupport,
            ClipBoundaryFlags.Left,
            ClipBoundaryFlags.Right,
            ClipBoundaryFlags.RightSupport,
        };

        private static void AccumulateStructuredBoundarySegmentPoint(
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            List<StructuredStripRow> rows,
            ClipBoundaryFlags sharedFlags,
            ClipVertex edgeA,
            ClipVertex edgeB,
            ClipVertex point)
        {
            int ignoredReuseCount = 0;
            AccumulateStructuredBoundarySegmentPoint(boundaryPoints, rows, sharedFlags, edgeA, edgeB, point, ref ignoredReuseCount);
        }

        private static void AccumulateStructuredBoundarySegmentPoint(
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            List<StructuredStripRow> rows,
            ClipBoundaryFlags sharedFlags,
            ClipVertex edgeA,
            ClipVertex edgeB,
            ClipVertex point,
            ref int reuseCount)
        {
            if (boundaryPoints == null || sharedFlags == ClipBoundaryFlags.None)
                return;

            reuseCount += CountStructuredPointReuses(AccumulateStructuredBoundarySegmentPoint(boundaryPoints, rows, sharedFlags, ClipBoundaryFlags.LeftSupport, edgeA, edgeB, point));
            reuseCount += CountStructuredPointReuses(AccumulateStructuredBoundarySegmentPoint(boundaryPoints, rows, sharedFlags, ClipBoundaryFlags.Left, edgeA, edgeB, point));
            reuseCount += CountStructuredPointReuses(AccumulateStructuredBoundarySegmentPoint(boundaryPoints, rows, sharedFlags, ClipBoundaryFlags.Right, edgeA, edgeB, point));
            reuseCount += CountStructuredPointReuses(AccumulateStructuredBoundarySegmentPoint(boundaryPoints, rows, sharedFlags, ClipBoundaryFlags.RightSupport, edgeA, edgeB, point));
        }

        private static StructuredPointMergeResult AccumulateStructuredBoundarySegmentPoint(
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            List<StructuredStripRow> rows,
            ClipBoundaryFlags sharedFlags,
            ClipBoundaryFlags targetFlag,
            ClipVertex edgeA,
            ClipVertex edgeB,
            ClipVertex point)
        {
            if ((sharedFlags & targetFlag) == ClipBoundaryFlags.None)
                return StructuredPointMergeResult.Ignored;

            if (!TryComputeStructuredBoundarySegmentParameter(rows, targetFlag, edgeA, edgeB, point, out StructuredBoundarySegmentKey key, out float t))
                return StructuredPointMergeResult.Ignored;

            return AddStructuredBoundarySegmentPoint(boundaryPoints, key, point, t);
        }

        private static int AppendStructuredBoundaryInteriorPoints(
            List<ClipVertex> output,
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            List<StructuredStripRow> rows,
            ClipVertex edgeA,
            ClipVertex edgeB)
        {
            if (output == null || boundaryPoints == null)
                return 0;

            if (!TryResolveStructuredBoundarySegmentForEdge(rows, edgeA, edgeB, out StructuredBoundarySegmentKey key, out ClipBoundaryFlags boundaryFlag, out float ta, out float tb))
                return 0;

            if (!boundaryPoints.TryGetValue(key, out List<SourceEdgePoint> points) || points.Count <= 2)
                return 0;

            return AppendStructuredInteriorSourceEdgePoints(output, points, ta, tb, boundaryFlag);
        }

        private static int AppendStructuredProtectedBoundaryInteriorPoints(
            List<ClipVertex> output,
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            List<StructuredStripRow> rows,
            ClipVertex edgeA,
            ClipVertex edgeB,
            StructuredBoundarySegmentKey key,
            ClipBoundaryFlags boundaryFlag,
            float ta,
            float tb,
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints,
            bool preserveInsertedPointFlags)
        {
            if (output == null || boundaryPoints == null || rows == null)
                return 0;

            if (!boundaryPoints.TryGetValue(key, out List<SourceEdgePoint> points) || points.Count <= 2)
                return 0;

            return AppendStructuredProtectedInteriorSourceEdgePoints(
                output,
                points,
                ta,
                tb,
                boundaryFlag,
                key,
                mandatoryBreakpoints,
                preserveInsertedPointFlags);
        }

        private static int CountStructuredPointReuses(StructuredPointMergeResult result)
        {
            return result == StructuredPointMergeResult.Reused ? 1 : 0;
        }

        private static bool TryComputeStructuredBoundarySegmentParameter(
            List<StructuredStripRow> rows,
            ClipBoundaryFlags boundaryFlag,
            ClipVertex edgeA,
            ClipVertex edgeB,
            ClipVertex point,
            out StructuredBoundarySegmentKey key,
            out float t)
        {
            key = default;
            t = 0f;
            if (rows == null || rows.Count < 2)
                return false;

            Vector2 pointCoord = new Vector2(point.s, point.u);
            Vector2 edgePointA = new Vector2(edgeA.s, edgeA.u);
            Vector2 edgePointB = new Vector2(edgeB.s, edgeB.u);
            Vector2 edgeMid = 0.5f * (edgePointA + edgePointB);
            float midS = 0.5f * (edgeA.s + edgeB.s);
            bool found = false;
            float bestScore = float.PositiveInfinity;
            int bestRowIndex = -1;

            for (int rowIndex = 0; rowIndex < rows.Count - 1; rowIndex++)
            {
                if (!TryGetStructuredBoundarySegmentCoords(rows, rowIndex, boundaryFlag, out Vector2 segmentA, out Vector2 segmentB))
                    continue;

                float toleranceSq = ComputeStructuredBoundarySegmentToleranceSq(segmentA, segmentB);
                float pointDistSq = DistanceSqPointToSegment(pointCoord, segmentA, segmentB);
                float midDistSq = DistanceSqPointToSegment(edgeMid, segmentA, segmentB);
                if (pointDistSq > toleranceSq || midDistSq > toleranceSq)
                    continue;

                float s0 = rows[rowIndex].frame.s;
                float s1 = rows[rowIndex + 1].frame.s;
                float length = s1 - s0;
                if (Mathf.Abs(length) <= 1e-8f)
                    continue;

                float midT = Mathf.Clamp01((midS - s0) / length);
                float endpointScore =
                    DistanceSqPointToSegment(edgePointA, segmentA, segmentB) +
                    DistanceSqPointToSegment(edgePointB, segmentA, segmentB);
                float score = pointDistSq + 0.25f * midDistSq + endpointScore + Mathf.Abs(0.5f - midT) * 1e-7f;
                if (!found || score < bestScore)
                {
                    found = true;
                    bestScore = score;
                    bestRowIndex = rowIndex;
                }
            }

            if (!found || bestRowIndex < 0)
                return false;

            float bestS0 = rows[bestRowIndex].frame.s;
            float bestS1 = rows[bestRowIndex + 1].frame.s;
            float bestLength = bestS1 - bestS0;
            if (Mathf.Abs(bestLength) <= 1e-8f)
                return false;

            t = Mathf.Clamp01((point.s - bestS0) / bestLength);
            key = new StructuredBoundarySegmentKey
            {
                rowIndex = bestRowIndex,
                boundaryFlag = boundaryFlag,
            };
            return true;
        }

        private static bool TryResolveStructuredBoundarySegmentForEdge(
            List<StructuredStripRow> rows,
            ClipVertex edgeA,
            ClipVertex edgeB,
            out StructuredBoundarySegmentKey key,
            out ClipBoundaryFlags boundaryFlag,
            out float ta,
            out float tb)
        {
            key = default;
            boundaryFlag = ClipBoundaryFlags.None;
            ta = 0f;
            tb = 0f;
            if (rows == null || rows.Count < 2)
                return false;

            ClipBoundaryFlags sharedHint = edgeA.boundaryFlags & edgeB.boundaryFlags & StructuredRegionInterfaceFlags;
            bool found = false;
            bool ambiguous = false;
            float bestScore = float.PositiveInfinity;
            StructuredBoundarySegmentKey bestKey = default;
            ClipBoundaryFlags bestFlag = ClipBoundaryFlags.None;
            float bestTa = 0f;
            float bestTb = 0f;
            Vector2 pointA = new Vector2(edgeA.s, edgeA.u);
            Vector2 pointB = new Vector2(edgeB.s, edgeB.u);
            Vector2 edgeMid = 0.5f * (pointA + pointB);

            for (int i = 0; i < StructuredRegionInterfaceFlagOrder.Length; i++)
            {
                ClipBoundaryFlags targetFlag = StructuredRegionInterfaceFlagOrder[i];
                if (!TryComputeStructuredBoundarySegmentParameter(rows, targetFlag, edgeA, edgeB, edgeA, out StructuredBoundarySegmentKey candidateKeyA, out float candidateTa) ||
                    !TryComputeStructuredBoundarySegmentParameter(rows, targetFlag, edgeA, edgeB, edgeB, out StructuredBoundarySegmentKey candidateKeyB, out float candidateTb) ||
                    !candidateKeyA.Equals(candidateKeyB) ||
                    !TryGetStructuredBoundarySegmentCoords(rows, candidateKeyA.rowIndex, targetFlag, out Vector2 segmentA, out Vector2 segmentB))
                {
                    continue;
                }

                float score =
                    DistanceSqPointToSegment(pointA, segmentA, segmentB) +
                    DistanceSqPointToSegment(pointB, segmentA, segmentB) +
                    0.25f * DistanceSqPointToSegment(edgeMid, segmentA, segmentB);
                if ((sharedHint & targetFlag) != ClipBoundaryFlags.None)
                    score *= 0.5f;

                if (!found || score + 1e-8f < bestScore)
                {
                    found = true;
                    ambiguous = false;
                    bestScore = score;
                    bestKey = candidateKeyA;
                    bestFlag = targetFlag;
                    bestTa = candidateTa;
                    bestTb = candidateTb;
                    continue;
                }

                if (Mathf.Abs(score - bestScore) <= 1e-8f && bestFlag != targetFlag)
                    ambiguous = true;
            }

            if (!found || ambiguous)
                return false;

            key = bestKey;
            boundaryFlag = bestFlag;
            ta = bestTa;
            tb = bestTb;
            return true;
        }

        private static ClipBoundaryFlags ExtractSingleStructuredInterfaceFlag(ClipBoundaryFlags flags)
        {
            flags &= StructuredRegionInterfaceFlags;
            if (flags == ClipBoundaryFlags.None)
                return ClipBoundaryFlags.None;

            int bits = (int)flags;
            return (bits & (bits - 1)) == 0 ? flags : ClipBoundaryFlags.None;
        }

        private static bool TryGetStructuredInterfaceOwners(
            ClipBoundaryFlags flags,
            out bool expectOutside,
            out bool expectSupport,
            out bool expectExcision)
        {
            expectOutside = false;
            expectSupport = false;
            expectExcision = false;

            switch (ExtractSingleStructuredInterfaceFlag(flags))
            {
                case ClipBoundaryFlags.LeftSupport:
                case ClipBoundaryFlags.RightSupport:
                    expectOutside = true;
                    expectSupport = true;
                    return true;
                case ClipBoundaryFlags.Left:
                case ClipBoundaryFlags.Right:
                    expectSupport = true;
                    expectExcision = true;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryGetStructuredInterfaceOwners(
            List<StructuredStripRow> rows,
            int rowIndex,
            ClipBoundaryFlags flags,
            out bool expectOutside,
            out bool expectSupport,
            out bool expectExcision)
        {
            if (!TryGetStructuredInterfaceOwners(flags, out expectOutside, out expectSupport, out expectExcision))
                return false;

            ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(flags);
            if ((singleFlag == ClipBoundaryFlags.Left || singleFlag == ClipBoundaryFlags.Right) &&
                ShouldDowngradeStructuredTipBoundaryToSupportOnly(rows, rowIndex))
            {
                expectOutside = false;
                expectSupport = true;
                expectExcision = false;
            }

            return true;
        }

        private static bool IsStructuredHardInterfaceFlag(ClipBoundaryFlags flags)
        {
            switch (ExtractSingleStructuredInterfaceFlag(flags))
            {
                case ClipBoundaryFlags.Left:
                case ClipBoundaryFlags.Right:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsStructuredAuditInterfaceFlag(ClipBoundaryFlags flags)
        {
            switch (ExtractSingleStructuredInterfaceFlag(flags))
            {
                case ClipBoundaryFlags.LeftSupport:
                case ClipBoundaryFlags.RightSupport:
                    return true;
                default:
                    return false;
            }
        }

        private static bool DoesRegionOwnStructuredInterfaceFlag(PartitionRegionKind regionKind, ClipBoundaryFlags flag)
        {
            switch (regionKind)
            {
                case PartitionRegionKind.Outside:
                    return flag == ClipBoundaryFlags.LeftSupport || flag == ClipBoundaryFlags.RightSupport;
                case PartitionRegionKind.Support:
                    return flag == ClipBoundaryFlags.LeftSupport ||
                        flag == ClipBoundaryFlags.RightSupport ||
                        flag == ClipBoundaryFlags.Left ||
                        flag == ClipBoundaryFlags.Right;
                case PartitionRegionKind.Excision:
                    return flag == ClipBoundaryFlags.Left || flag == ClipBoundaryFlags.Right;
                default:
                    return false;
            }
        }

        private static string FormatStructuredInterfaceOwnerMask(bool hasOutside, bool hasSupport, bool hasExcision)
        {
            if (!hasOutside && !hasSupport && !hasExcision)
                return "none";

            var sb = new StringBuilder(3);
            if (hasOutside)
                sb.Append('O');
            if (hasSupport)
                sb.Append('S');
            if (hasExcision)
                sb.Append('X');
            return sb.ToString();
        }

        private static string FormatStructuredInterfaceExpectedPair(
            List<StructuredStripRow> rows,
            int rowIndex,
            ClipBoundaryFlags flags)
        {
            if (TryGetStructuredInterfaceOwners(rows, rowIndex, flags, out bool expectOutside, out bool expectSupport, out bool expectExcision))
                return FormatStructuredInterfaceOwnerMask(expectOutside, expectSupport, expectExcision);
            return "unknown";
        }

        private static string FormatStructuredInterfaceDebugRowLabel(int rowIndex)
        {
            return rowIndex >= 0 ? $"r{rowIndex}" : "r?";
        }

        private static string FormatStructuredInterfaceDebugFlag(
            List<StructuredStripRow> rows,
            int rowIndex,
            ClipBoundaryFlags flags)
        {
            var parts = new List<string>(4);
            switch (ExtractSingleStructuredInterfaceFlag(flags))
            {
                case ClipBoundaryFlags.LeftSupport:
                    parts.Add("LS");
                    break;
                case ClipBoundaryFlags.Left:
                    parts.Add("LB");
                    break;
                case ClipBoundaryFlags.Right:
                    parts.Add("RB");
                    break;
                case ClipBoundaryFlags.RightSupport:
                    parts.Add("RS");
                    break;
            }

            if (IsStructuredHardInterfaceFlag(flags))
                parts.Add("Hard");
            else if (IsStructuredAuditInterfaceFlag(flags))
                parts.Add("Audit");

            if ((flags & ClipBoundaryFlags.Start) != 0)
                parts.Add("Start");
            if ((flags & ClipBoundaryFlags.End) != 0)
                parts.Add("End");
            if ((flags & ClipBoundaryFlags.Row) != 0)
                parts.Add("Row");
            if (rows != null && IsStructuredBoundarySegmentTipAdjacent(rows, rowIndex))
                parts.Add("Tip");

            return parts.Count > 0 ? string.Join("|", parts.ToArray()) : FormatClipBoundaryFlags(flags);
        }

        private static string FormatStructuredInterfaceBoundaryShortLabel(ClipBoundaryFlags flag)
        {
            switch (flag & StructuredRegionInterfaceFlags)
            {
                case ClipBoundaryFlags.LeftSupport:
                    return "LS";
                case ClipBoundaryFlags.Left:
                    return "LB";
                case ClipBoundaryFlags.Right:
                    return "RB";
                case ClipBoundaryFlags.RightSupport:
                    return "RS";
                default:
                    return FormatClipBoundaryFlags(flag);
            }
        }

        private static string FormatStructuredInterfaceSegmentLabel(InterfaceEdgeAccumulator state)
        {
            if (!state.hasResolvedSegment)
                return "none";

            return
                $"seg={FormatStructuredInterfaceDebugRowLabel(state.resolvedSegmentKey.rowIndex)}:" +
                $"{FormatStructuredInterfaceBoundaryShortLabel(state.resolvedSegmentKey.boundaryFlag)}@" +
                $"{state.resolvedSegmentTMin:F4}-{state.resolvedSegmentTMax:F4}";
        }

        private static string BuildStructuredFailedOwnershipSceneLabel(
            List<StructuredStripRow> rows,
            int failureIndex,
            InterfaceEdgeAccumulator state,
            bool hasOutside,
            bool hasSupport,
            bool hasExcision)
        {
            string rowLabel = FormatStructuredInterfaceDebugRowLabel(state.rowIndex);
            string flagLabel = FormatStructuredInterfaceDebugFlag(rows, state.rowIndex, state.flags);
            string expectedPair = FormatStructuredInterfaceExpectedPair(rows, state.rowIndex, state.flags);
            string actualPair = FormatStructuredInterfaceOwnerMask(hasOutside, hasSupport, hasExcision);
            string segmentLabel = FormatStructuredInterfaceSegmentLabel(state);
            return $"FE{failureIndex} {rowLabel} {flagLabel} {expectedPair}>{actualPair} {segmentLabel}";
        }

        private static string BuildStructuredFailedOwnershipPreviewEntry(
            List<StructuredStripRow> rows,
            int failureIndex,
            InterfaceEdgeAccumulator state,
            bool hasOutside,
            bool hasSupport,
            bool hasExcision,
            bool includeVertices)
        {
            string rowLabel = FormatStructuredInterfaceDebugRowLabel(state.rowIndex);
            string flagLabel = FormatStructuredInterfaceDebugFlag(rows, state.rowIndex, state.flags);
            string expectedPair = FormatStructuredInterfaceExpectedPair(rows, state.rowIndex, state.flags);
            string actualPair = FormatStructuredInterfaceOwnerMask(hasOutside, hasSupport, hasExcision);
            string segmentLabel = FormatStructuredInterfaceSegmentLabel(state);
            string prefix = $"FE{failureIndex} row={rowLabel} flag={flagLabel} expected={expectedPair} actual={actualPair} {segmentLabel}";
            if (!includeVertices)
                return $"{prefix} raw=({state.outsideCount},{state.supportCount},{state.excisionCount})";

            return
                $"{prefix} {state.vertexA}<->{state.vertexB}/{FormatClipBoundaryFlags(state.flags)} " +
                $"raw=({state.outsideCount},{state.supportCount},{state.excisionCount}) " +
                $"bd=({(hasOutside ? 1 : 0)},{(hasSupport ? 1 : 0)},{(hasExcision ? 1 : 0)})";
        }

        private static string BuildStructuredTipOwnershipSummary(
            List<StructuredStripRow> rows,
            Dictionary<int, int> mismatchRowCounts)
        {
            if (rows == null || rows.Count < 2)
                return "none";

            var segmentIndices = new HashSet<int>();
            if (mismatchRowCounts != null)
            {
                foreach (var pair in mismatchRowCounts)
                {
                    if (pair.Key >= 0 && pair.Key < rows.Count - 1)
                        segmentIndices.Add(pair.Key);
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].isTipRow)
                    continue;
                if (i > 0)
                    segmentIndices.Add(i - 1);
                if (i < rows.Count - 1)
                    segmentIndices.Add(i);
            }

            if (segmentIndices.Count == 0)
                return "none";

            var ordered = new List<int>(segmentIndices);
            ordered.Sort();
            var parts = new List<string>(ordered.Count);
            for (int index = 0; index < ordered.Count; index++)
            {
                int rowIndex = ordered[index];
                if (rowIndex < 0 || rowIndex + 1 >= rows.Count)
                    continue;

                StructuredStripRow start = rows[rowIndex];
                StructuredStripRow end = rows[rowIndex + 1];
                EvaluateStructuredRowSignedPositions(
                    start,
                    out float startLeftSupport,
                    out float startLeftBoundary,
                    out float startRightBoundary,
                    out float startRightSupport);
                EvaluateStructuredRowSignedPositions(
                    end,
                    out float endLeftSupport,
                    out float endLeftBoundary,
                    out float endRightBoundary,
                    out float endRightSupport);

                float startCenter = startRightBoundary - startLeftBoundary;
                float endCenter = endRightBoundary - endLeftBoundary;
                float startLeftGap = startLeftBoundary - startLeftSupport;
                float endLeftGap = endLeftBoundary - endLeftSupport;
                float startRightGap = startRightSupport - startRightBoundary;
                float endRightGap = endRightSupport - endRightBoundary;
                int mismatchCount = mismatchRowCounts != null && mismatchRowCounts.TryGetValue(rowIndex, out int value) ? value : 0;
                parts.Add(
                    $"r{rowIndex}(tip={((start.isTipRow || end.isTipRow) ? 1 : 0)} rows={start.frameIndex}->{end.frameIndex} center={startCenter:F4}->{endCenter:F4} left={startLeftGap:F4}->{endLeftGap:F4} right={startRightGap:F4}->{endRightGap:F4} mism={mismatchCount})");
            }

            return parts.Count > 0 ? string.Join(" ; ", parts.ToArray()) : "none";
        }

        private static int QuantizeStructuredInterfaceParam(float value)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value) * 10000f);
        }

        private static float DequantizeStructuredInterfaceParam(int value)
        {
            return value / 10000f;
        }

        private static bool IsDegenerateStructuredInterfaceInterval(float tMin, float tMax)
        {
            return
                QuantizeStructuredInterfaceParam(tMin) == QuantizeStructuredInterfaceParam(tMax) ||
                Mathf.Abs(tMax - tMin) <= 1e-6f;
        }

        private static bool TryBuildStructuredTipInterfaceBucketKey(
            InterfaceEdgeAccumulator state,
            out StructuredTipInterfaceBucketKey key)
        {
            key = default;
            ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(state.flags);
            if (!state.hasResolvedSegment ||
                state.rowIndex < 0 ||
                singleFlag == ClipBoundaryFlags.None ||
                singleFlag != ClipBoundaryFlags.Left &&
                singleFlag != ClipBoundaryFlags.Right &&
                singleFlag != ClipBoundaryFlags.LeftSupport &&
                singleFlag != ClipBoundaryFlags.RightSupport)
            {
                return false;
            }

            key = new StructuredTipInterfaceBucketKey
            {
                rowIndex = state.resolvedSegmentKey.rowIndex,
                boundaryFlag = state.resolvedSegmentKey.boundaryFlag,
                tMinQuantized = QuantizeStructuredInterfaceParam(state.resolvedSegmentTMin),
                tMaxQuantized = QuantizeStructuredInterfaceParam(state.resolvedSegmentTMax),
            };
            return true;
        }

        private static void AccumulateStructuredTipInterfaceBucket(
            Dictionary<StructuredTipInterfaceBucketKey, StructuredTipInterfaceBucketStats> buckets,
            InterfaceEdgeAccumulator state,
            bool hasOutside,
            bool hasSupport,
            bool hasExcision)
        {
            if (buckets == null || !TryBuildStructuredTipInterfaceBucketKey(state, out StructuredTipInterfaceBucketKey key))
                return;

            if (!buckets.TryGetValue(key, out StructuredTipInterfaceBucketStats bucket))
            {
                bucket = new StructuredTipInterfaceBucketStats
                {
                    rowIndex = key.rowIndex,
                    boundaryFlag = key.boundaryFlag,
                    tMinQuantized = key.tMinQuantized,
                    tMaxQuantized = key.tMaxQuantized,
                };
            }

            bucket.stateCount++;
            bucket.unionOutside |= hasOutside;
            bucket.unionSupport |= hasSupport;
            bucket.unionExcision |= hasExcision;

            switch (FormatStructuredInterfaceOwnerMask(hasOutside, hasSupport, hasExcision))
            {
                case "O":
                    bucket.actualOCount++;
                    break;
                case "S":
                    bucket.actualSCount++;
                    break;
                case "X":
                    bucket.actualXCount++;
                    break;
                case "OS":
                    bucket.actualOSCount++;
                    break;
                case "SX":
                    bucket.actualSXCount++;
                    break;
                case "OX":
                    bucket.actualOXCount++;
                    break;
                case "OSX":
                    bucket.actualOSXCount++;
                    break;
            }

            buckets[key] = bucket;
        }

        private static StructuredTipInterfaceBucketDiagnosis DiagnoseStructuredTipInterfaceBucket(
            bool expectOutside,
            bool expectSupport,
            bool expectExcision,
            StructuredTipInterfaceBucketStats bucket)
        {
            bool cleanOutside = bucket.unionOutside == expectOutside;
            bool cleanSupport = bucket.unionSupport == expectSupport;
            bool cleanExcision = bucket.unionExcision == expectExcision;

            if (expectSupport && expectExcision && !expectOutside)
            {
                if (!bucket.unionOutside &&
                    bucket.actualSXCount == 0 &&
                    bucket.actualSCount > 0 &&
                    bucket.actualXCount > 0)
                {
                    return StructuredTipInterfaceBucketDiagnosis.EdgeKeySplit;
                }

                if (!bucket.unionOutside && (!bucket.unionSupport || !bucket.unionExcision))
                    return StructuredTipInterfaceBucketDiagnosis.MissingOppositeOwner;

                return cleanOutside && cleanSupport && cleanExcision
                    ? StructuredTipInterfaceBucketDiagnosis.None
                    : StructuredTipInterfaceBucketDiagnosis.Mixed;
            }

            if (expectOutside && expectSupport && !expectExcision)
            {
                if (!bucket.unionExcision &&
                    bucket.actualOSCount == 0 &&
                    bucket.actualOCount > 0 &&
                    bucket.actualSCount > 0)
                {
                    return StructuredTipInterfaceBucketDiagnosis.EdgeKeySplit;
                }

                if (!bucket.unionExcision && (!bucket.unionOutside || !bucket.unionSupport))
                    return StructuredTipInterfaceBucketDiagnosis.MissingOppositeOwner;

                return cleanOutside && cleanSupport && cleanExcision
                    ? StructuredTipInterfaceBucketDiagnosis.None
                    : StructuredTipInterfaceBucketDiagnosis.Mixed;
            }

            return cleanOutside && cleanSupport && cleanExcision
                ? StructuredTipInterfaceBucketDiagnosis.None
                : StructuredTipInterfaceBucketDiagnosis.Mixed;
        }

        private static string FormatStructuredTipInterfaceBucketOwners(StructuredTipInterfaceBucketStats bucket)
        {
            var parts = new List<string>(7);
            if (bucket.actualOCount > 0)
                parts.Add($"O({bucket.actualOCount})");
            if (bucket.actualSCount > 0)
                parts.Add($"S({bucket.actualSCount})");
            if (bucket.actualXCount > 0)
                parts.Add($"X({bucket.actualXCount})");
            if (bucket.actualOSCount > 0)
                parts.Add($"OS({bucket.actualOSCount})");
            if (bucket.actualSXCount > 0)
                parts.Add($"SX({bucket.actualSXCount})");
            if (bucket.actualOXCount > 0)
                parts.Add($"OX({bucket.actualOXCount})");
            if (bucket.actualOSXCount > 0)
                parts.Add($"OSX({bucket.actualOSXCount})");
            return parts.Count > 0 ? string.Join("|", parts.ToArray()) : "none";
        }

        private static string BuildStructuredTipInterfaceKeyRowSummary(
            List<StructuredStripRow> rows,
            int rowIndex,
            string role,
            List<StructuredTipInterfaceBucketStats> rowBuckets,
            int unresolvedCount)
        {
            if (rows == null || rowIndex < 0 || rowIndex + 1 >= rows.Count)
                return $"{FormatStructuredInterfaceDebugRowLabel(rowIndex)}({role}=invalid)";

            rowBuckets = rowBuckets ?? new List<StructuredTipInterfaceBucketStats>();
            rowBuckets.Sort((a, b) =>
            {
                int byFlag = ((int)a.boundaryFlag).CompareTo((int)b.boundaryFlag);
                if (byFlag != 0)
                    return byFlag;
                int byMin = a.tMinQuantized.CompareTo(b.tMinQuantized);
                if (byMin != 0)
                    return byMin;
                return a.tMaxQuantized.CompareTo(b.tMaxQuantized);
            });

            int splitCount = 0;
            int missingCount = 0;
            int mixedCount = 0;
            var preview = new List<string>(4);
            foreach (StructuredTipInterfaceBucketStats bucket in rowBuckets)
            {
                if (!TryGetStructuredInterfaceOwners(rows, bucket.rowIndex, bucket.boundaryFlag, out bool expectOutside, out bool expectSupport, out bool expectExcision))
                    continue;

                StructuredTipInterfaceBucketDiagnosis diagnosis = DiagnoseStructuredTipInterfaceBucket(
                    expectOutside,
                    expectSupport,
                    expectExcision,
                    bucket);
                switch (diagnosis)
                {
                    case StructuredTipInterfaceBucketDiagnosis.EdgeKeySplit:
                        splitCount++;
                        break;
                    case StructuredTipInterfaceBucketDiagnosis.MissingOppositeOwner:
                        missingCount++;
                        break;
                    case StructuredTipInterfaceBucketDiagnosis.Mixed:
                        mixedCount++;
                        break;
                }

                if (preview.Count >= 4 || diagnosis == StructuredTipInterfaceBucketDiagnosis.None)
                    continue;

                string diagLabel =
                    diagnosis == StructuredTipInterfaceBucketDiagnosis.EdgeKeySplit ? "split" :
                    diagnosis == StructuredTipInterfaceBucketDiagnosis.MissingOppositeOwner ? "missing" :
                    "mixed";
                preview.Add(
                    $"{FormatStructuredInterfaceBoundaryShortLabel(bucket.boundaryFlag)}@" +
                    $"{DequantizeStructuredInterfaceParam(bucket.tMinQuantized):F4}-{DequantizeStructuredInterfaceParam(bucket.tMaxQuantized):F4}:" +
                    $"{diagLabel}:{FormatStructuredTipInterfaceBucketOwners(bucket)}");
            }

            TryGetStructuredSegmentCenterWidths(rows, rowIndex, out float startCenter, out float endCenter);
            string previewText = preview.Count > 0 ? string.Join(", ", preview.ToArray()) : "none";
            return
                $"{FormatStructuredInterfaceDebugRowLabel(rowIndex)}({role} center={startCenter:F4}->{endCenter:F4} " +
                $"buckets={rowBuckets.Count} split={splitCount} missing={missingCount} mixed={mixedCount} unresolved={unresolvedCount} " +
                $"preview={previewText})";
        }

        private static string BuildStructuredTipInterfaceKeyCompareSummary(
            List<StructuredStripRow> rows,
            Dictionary<StructuredTipInterfaceBucketKey, StructuredTipInterfaceBucketStats> buckets,
            Dictionary<int, int> unresolvedCounts)
        {
            if (rows == null || rows.Count < 2)
                return "none";

            int startRow = 0;
            int endRow = rows.Count - 2;
            var startBuckets = new List<StructuredTipInterfaceBucketStats>();
            var endBuckets = new List<StructuredTipInterfaceBucketStats>();
            if (buckets != null)
            {
                foreach (var pair in buckets)
                {
                    if (pair.Value.rowIndex == startRow)
                        startBuckets.Add(pair.Value);
                    else if (pair.Value.rowIndex == endRow)
                        endBuckets.Add(pair.Value);
                }
            }

            int unresolvedStart = unresolvedCounts != null && unresolvedCounts.TryGetValue(startRow, out int startCount) ? startCount : 0;
            int unresolvedEnd = unresolvedCounts != null && unresolvedCounts.TryGetValue(endRow, out int endCount) ? endCount : 0;
            return
                $"{BuildStructuredTipInterfaceKeyRowSummary(rows, startRow, "start", startBuckets, unresolvedStart)} ; " +
                $"{BuildStructuredTipInterfaceKeyRowSummary(rows, endRow, "end", endBuckets, unresolvedEnd)}";
        }

        private static int CountStructuredCoverageCellsBypassedForTipRow(
            int rowIndex,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            var orderedCells = new List<StructuredStripCell2D>();
            AppendOrderedStructuredCoverageCells(orderedCells, supportCells, excisionCells);
            int count = 0;
            for (int i = 0; i < orderedCells.Count; i++)
            {
                StructuredStripCell2D cell = orderedCells[i];
                if (cell.rowIndex == rowIndex &&
                    (cell.columnIndex == 0 || cell.columnIndex == 1 || cell.columnIndex == 2))
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountStructuredCoverageCellsBypassedForStartTip(
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            return CountStructuredCoverageCellsBypassedForTipRow(0, supportCells, excisionCells);
        }

        private static int CountStructuredCoverageCellsBypassedForEndTip(
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            if (rows == null || rows.Count < 2)
                return 0;

            return CountStructuredCoverageCellsBypassedForTipRow(rows.Count - 2, supportCells, excisionCells);
        }

        private static void AppendStructuredDiagnosticPolygonClones(
            List<List<ClipVertex>> target,
            List<List<ClipVertex>> source)
        {
            if (target == null || source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                List<ClipVertex> cleaned = CleanupPolygon(source[i]);
                if (cleaned.Count >= 3)
                    target.Add(cleaned);
            }
        }

        private static List<ClipVertex> CloneStructuredPolygon(List<ClipVertex> source)
        {
            if (source == null)
                return null;

            return new List<ClipVertex>(source);
        }

        private static void AccumulateStructuredTipBoundaryIntervals(
            List<StructuredStripRow> rows,
            List<List<ClipVertex>> polygons,
            PartitionRegionKind regionKind,
            int targetRowIndex,
            List<StructuredBoundaryOwnerInterval> intervals,
            string sourceStage = null,
            int sourceCellRowIndex = int.MinValue,
            int sourceCellColumnIndex = int.MinValue,
            int sourceTriangleIndex = -1)
        {
            if (rows == null || polygons == null || intervals == null)
                return;

            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                List<ClipVertex> polygon = polygons[polygonIndex];
                if (polygon == null || polygon.Count < 2)
                    continue;

                for (int i = 0; i < polygon.Count; i++)
                {
                    ClipVertex a = polygon[i];
                    ClipVertex b = polygon[(i + 1) % polygon.Count];
                    if (!TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out StructuredBoundarySegmentKey key, out ClipBoundaryFlags boundaryFlag, out float ta, out float tb))
                        continue;
                    if (key.rowIndex != targetRowIndex)
                        continue;

                    ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(boundaryFlag);
                    if (singleFlag != ClipBoundaryFlags.Left && singleFlag != ClipBoundaryFlags.Right)
                        continue;

                    intervals.Add(new StructuredBoundaryOwnerInterval
                    {
                        boundaryFlag = singleFlag,
                        tMin = Mathf.Clamp01(Mathf.Min(ta, tb)),
                        tMax = Mathf.Clamp01(Mathf.Max(ta, tb)),
                        regionKind = regionKind,
                        segmentKey = key,
                        sourceBoundaryFlags = a.boundaryFlags | b.boundaryFlags,
                        sourceStage = sourceStage,
                        sourceCellRowIndex = sourceCellRowIndex,
                        sourceCellColumnIndex = sourceCellColumnIndex,
                        sourceTriangleIndex = sourceTriangleIndex,
                    });
                }
            }
        }

        private static bool CoversStructuredBoundaryInterval(
            List<StructuredBoundaryOwnerInterval> intervals,
            PartitionRegionKind regionKind,
            float tMin,
            float tMax)
        {
            const float coverageEpsilon = 1e-4f;
            for (int i = 0; i < intervals.Count; i++)
            {
                StructuredBoundaryOwnerInterval interval = intervals[i];
                if (interval.regionKind != regionKind)
                    continue;
                if (interval.tMin <= tMin + coverageEpsilon && interval.tMax >= tMax - coverageEpsilon)
                    return true;
            }

            return false;
        }

        private static string FormatStructuredIntervalSpanList(List<string> spans)
        {
            return spans != null && spans.Count > 0 ? string.Join("|", spans.ToArray()) : "none";
        }

        private static string FormatStructuredBreakpointList(List<float> breakpoints)
        {
            if (breakpoints == null || breakpoints.Count == 0)
                return "none";

            var labels = new string[breakpoints.Count];
            for (int i = 0; i < breakpoints.Count; i++)
                labels[i] = breakpoints[i].ToString("F4");
            return string.Join(",", labels);
        }

        private static string BuildStructuredStartTipBoundaryStageSummary(
            ClipBoundaryFlags boundaryFlag,
            List<StructuredBoundaryOwnerInterval> allIntervals)
        {
            var intervals = new List<StructuredBoundaryOwnerInterval>();
            var supportBreakpoints = new HashSet<int>();
            var excisionBreakpoints = new HashSet<int>();
            int zeroLengthIntervalCount = 0;
            if (allIntervals != null)
            {
                for (int i = 0; i < allIntervals.Count; i++)
                {
                    StructuredBoundaryOwnerInterval interval = allIntervals[i];
                    if (interval.boundaryFlag != boundaryFlag)
                        continue;

                    intervals.Add(interval);
                    int qMin = QuantizeStructuredInterfaceParam(interval.tMin);
                    int qMax = QuantizeStructuredInterfaceParam(interval.tMax);
                    if (interval.regionKind == PartitionRegionKind.Support)
                    {
                        supportBreakpoints.Add(qMin);
                        supportBreakpoints.Add(qMax);
                    }
                    else if (interval.regionKind == PartitionRegionKind.Excision)
                    {
                        excisionBreakpoints.Add(qMin);
                        excisionBreakpoints.Add(qMax);
                    }

                    if (Mathf.Abs(interval.tMax - interval.tMin) <= 1e-6f)
                        zeroLengthIntervalCount++;
                }
            }

            var breakpointValues = new List<int>();
            foreach (StructuredBoundaryOwnerInterval interval in intervals)
            {
                breakpointValues.Add(QuantizeStructuredInterfaceParam(interval.tMin));
                breakpointValues.Add(QuantizeStructuredInterfaceParam(interval.tMax));
            }

            breakpointValues.Sort();
            var uniqueBreakpoints = new List<float>(breakpointValues.Count);
            int previous = int.MinValue;
            for (int i = 0; i < breakpointValues.Count; i++)
            {
                int current = breakpointValues[i];
                if (current == previous)
                    continue;
                uniqueBreakpoints.Add(DequantizeStructuredInterfaceParam(current));
                previous = current;
            }

            var supportOnly = new List<string>();
            var excisionOnly = new List<string>();
            var shared = new List<string>();
            var missing = new List<string>();
            var unsynced = new List<string>();
            for (int i = 0; i + 1 < uniqueBreakpoints.Count; i++)
            {
                float tMin = uniqueBreakpoints[i];
                float tMax = uniqueBreakpoints[i + 1];
                if (tMax - tMin <= 1e-6f)
                    continue;

                bool hasSupport = CoversStructuredBoundaryInterval(intervals, PartitionRegionKind.Support, tMin, tMax);
                bool hasExcision = CoversStructuredBoundaryInterval(intervals, PartitionRegionKind.Excision, tMin, tMax);
                string span = $"{tMin:F4}-{tMax:F4}";
                if (hasSupport && hasExcision)
                {
                    int qMin = QuantizeStructuredInterfaceParam(tMin);
                    int qMax = QuantizeStructuredInterfaceParam(tMax);
                    bool synced =
                        supportBreakpoints.Contains(qMin) &&
                        supportBreakpoints.Contains(qMax) &&
                        excisionBreakpoints.Contains(qMin) &&
                        excisionBreakpoints.Contains(qMax);
                    if (synced)
                        shared.Add(span);
                    else
                        unsynced.Add(span);
                }
                else if (hasSupport)
                {
                    supportOnly.Add(span);
                }
                else if (hasExcision)
                {
                    excisionOnly.Add(span);
                }
                else
                {
                    missing.Add(span);
                }
            }

            return
                $"bp={FormatStructuredBreakpointList(uniqueBreakpoints)} zero={zeroLengthIntervalCount} " +
                $"S={FormatStructuredIntervalSpanList(supportOnly)} " +
                $"X={FormatStructuredIntervalSpanList(excisionOnly)} " +
                $"SX={FormatStructuredIntervalSpanList(shared)} " +
                $"missing={FormatStructuredIntervalSpanList(missing)} " +
                $"unsynced={FormatStructuredIntervalSpanList(unsynced)}";
        }

        private static string FormatStructuredBoundarySegmentKeyDebugLabel(StructuredBoundarySegmentKey key)
        {
            return $"{FormatStructuredInterfaceDebugRowLabel(key.rowIndex)}:{FormatStructuredInterfaceBoundaryShortLabel(key.boundaryFlag)}";
        }

        private static string FormatPartitionRegionOwnerMask(PartitionRegionKind regionKind)
        {
            switch (regionKind)
            {
                case PartitionRegionKind.Outside:
                    return "O";
                case PartitionRegionKind.Support:
                    return "S";
                case PartitionRegionKind.Excision:
                    return "X";
                default:
                    return "none";
            }
        }

        private static string FormatStructuredBoundaryIntervalContributorSummary(
            List<StructuredBoundaryOwnerInterval> intervals,
            int maxCount = 8)
        {
            if (intervals == null || intervals.Count == 0)
                return "none";

            var ordered = new List<StructuredBoundaryOwnerInterval>(intervals);
            ordered.Sort((lhs, rhs) =>
            {
                int rowCompare = lhs.segmentKey.rowIndex.CompareTo(rhs.segmentKey.rowIndex);
                if (rowCompare != 0)
                    return rowCompare;
                int flagCompare = ((int)lhs.boundaryFlag).CompareTo((int)rhs.boundaryFlag);
                if (flagCompare != 0)
                    return flagCompare;
                int tMinCompare = lhs.tMin.CompareTo(rhs.tMin);
                if (tMinCompare != 0)
                    return tMinCompare;
                int tMaxCompare = lhs.tMax.CompareTo(rhs.tMax);
                if (tMaxCompare != 0)
                    return tMaxCompare;
                return ((int)lhs.regionKind).CompareTo((int)rhs.regionKind);
            });

            var preview = new List<string>(Mathf.Min(maxCount, ordered.Count));
            for (int i = 0; i < ordered.Count && i < maxCount; i++)
            {
                StructuredBoundaryOwnerInterval interval = ordered[i];
                string cellLabel =
                    interval.sourceCellRowIndex >= 0 && interval.sourceCellColumnIndex > int.MinValue
                    ? $"{FormatStructuredInterfaceDebugRowLabel(interval.sourceCellRowIndex)}c{interval.sourceCellColumnIndex}"
                    : "none";
                string triLabel = interval.sourceTriangleIndex >= 0 ? interval.sourceTriangleIndex.ToString() : "none";
                string stageLabel = string.IsNullOrEmpty(interval.sourceStage) ? "unknown" : interval.sourceStage;
                preview.Add(
                    $"{FormatStructuredInterfaceBoundaryShortLabel(interval.boundaryFlag)}@{interval.tMin:F4}-{interval.tMax:F4}" +
                    $" owner={FormatPartitionRegionOwnerMask(interval.regionKind)}" +
                    $" stage={stageLabel}" +
                    $" cell={cellLabel}" +
                    $" tri={triLabel}" +
                    $" flags={FormatClipBoundaryFlags(interval.sourceBoundaryFlags)}" +
                    $" seg={FormatStructuredBoundarySegmentKeyDebugLabel(interval.segmentKey)}");
            }

            if (ordered.Count > maxCount)
                preview.Add($"+{ordered.Count - maxCount}more");

            return string.Join(" ; ", preview.ToArray());
        }

        private static void AccumulateStructuredBoundaryCoverageStats(
            List<StructuredBoundaryOwnerInterval> allIntervals,
            ClipBoundaryFlags boundaryFlag,
            ref bool hasShared,
            ref bool hasExcisionOnly)
        {
            if (allIntervals == null)
                return;

            var intervals = new List<StructuredBoundaryOwnerInterval>();
            for (int i = 0; i < allIntervals.Count; i++)
            {
                if (allIntervals[i].boundaryFlag == boundaryFlag)
                    intervals.Add(allIntervals[i]);
            }

            if (intervals.Count == 0)
                return;

            var breakpointValues = new List<int>(intervals.Count * 2);
            for (int i = 0; i < intervals.Count; i++)
            {
                breakpointValues.Add(QuantizeStructuredInterfaceParam(intervals[i].tMin));
                breakpointValues.Add(QuantizeStructuredInterfaceParam(intervals[i].tMax));
            }

            breakpointValues.Sort();
            var uniqueBreakpoints = new List<float>(breakpointValues.Count);
            int previous = int.MinValue;
            for (int i = 0; i < breakpointValues.Count; i++)
            {
                int current = breakpointValues[i];
                if (current == previous)
                    continue;
                uniqueBreakpoints.Add(DequantizeStructuredInterfaceParam(current));
                previous = current;
            }

            for (int i = 0; i + 1 < uniqueBreakpoints.Count; i++)
            {
                float tMin = uniqueBreakpoints[i];
                float tMax = uniqueBreakpoints[i + 1];
                if (tMax - tMin <= 1e-6f)
                    continue;

                bool hasSupport = CoversStructuredBoundaryInterval(intervals, PartitionRegionKind.Support, tMin, tMax);
                bool hasExcision = CoversStructuredBoundaryInterval(intervals, PartitionRegionKind.Excision, tMin, tMax);
                if (hasSupport && hasExcision)
                    hasShared = true;
                else if (!hasSupport && hasExcision)
                    hasExcisionOnly = true;
            }
        }

        private static void EvaluateStructuredTipBoundaryProbe(
            List<StructuredBoundaryOwnerInterval> intervals,
            out bool hasShared,
            out bool hasExcisionOnly)
        {
            hasShared = false;
            hasExcisionOnly = false;
            AccumulateStructuredBoundaryCoverageStats(intervals, ClipBoundaryFlags.Left, ref hasShared, ref hasExcisionOnly);
            AccumulateStructuredBoundaryCoverageStats(intervals, ClipBoundaryFlags.Right, ref hasShared, ref hasExcisionOnly);
        }

        private static bool IsStructuredClosingTipSegment(
            List<StructuredStripRow> rows,
            int rowIndex)
        {
            const float collapseEpsilon = 5e-4f;
            return
                TryGetStructuredSegmentCenterWidths(rows, rowIndex, out float startCenter, out float endCenter) &&
                startCenter > collapseEpsilon &&
                Mathf.Abs(endCenter) <= collapseEpsilon;
        }

        private static string BuildStructuredTipWedgeRowStageSummary(
            List<StructuredStripRow> rows,
            int rowIndex,
            string role,
            List<StructuredBoundaryOwnerInterval> intervals,
            StructuredStartTipWedgeDiagnostics diagnostics,
            bool includeStartTipDiagnostics)
        {
            bool active = role == "start"
                ? IsStructuredOpeningTipSegment(rows, rowIndex)
                : IsStructuredClosingTipSegment(rows, rowIndex);
            TryGetStructuredSegmentCenterWidths(rows, rowIndex, out float startCenter, out float endCenter);
            string lbSummary = BuildStructuredStartTipBoundaryStageSummary(ClipBoundaryFlags.Left, intervals);
            string rbSummary = BuildStructuredStartTipBoundaryStageSummary(ClipBoundaryFlags.Right, intervals);
            int helperCalls = 0;
            int domainPieces = 0;
            int leftSupportPieces = 0;
            int centerRightPieces = 0;
            int excisionPieces = 0;
            int rightSupportPieces = 0;
            int bypassCells = 0;
            if (includeStartTipDiagnostics)
            {
                if (role == "start")
                {
                    helperCalls = diagnostics.startHelperInvocationCount;
                    domainPieces = diagnostics.startDomainPieceCount;
                    leftSupportPieces = diagnostics.startLbSplitLeftSupportPieceCount;
                    centerRightPieces = diagnostics.startLbSplitCenterRightPieceCount;
                    excisionPieces = diagnostics.startRbSplitExcisionPieceCount;
                    rightSupportPieces = diagnostics.startRbSplitRightSupportPieceCount;
                    bypassCells = diagnostics.row0BypassedOrdinaryCellCount;
                }
                else
                {
                    helperCalls = diagnostics.endHelperInvocationCount;
                    domainPieces = diagnostics.endDomainPieceCount;
                    leftSupportPieces = diagnostics.endLbSplitLeftSupportPieceCount;
                    centerRightPieces = diagnostics.endLbSplitCenterRightPieceCount;
                    excisionPieces = diagnostics.endRbSplitExcisionPieceCount;
                    rightSupportPieces = diagnostics.endRbSplitRightSupportPieceCount;
                    bypassCells = diagnostics.endTipBypassedOrdinaryCellCount;
                }
            }
            return
                $"{FormatStructuredInterfaceDebugRowLabel(rowIndex)}({role} center={startCenter:F4}->{endCenter:F4} active={(active ? 1 : 0)} helperCalls={helperCalls} " +
                $"domainPieces={domainPieces} afterLB=(left={leftSupportPieces},centerRight={centerRightPieces}) " +
                $"afterRB=(excision={excisionPieces},rightSupport={rightSupportPieces}) " +
                $"bypass={(bypassCells > 0 ? 1 : 0)} bypassCells={bypassCells} " +
                $"LB[{lbSummary}] RB[{rbSummary}])";
        }

        private static string BuildStructuredStartTipWedgeStageSummary(
            List<StructuredStripRow> rows,
            List<List<ClipVertex>> supportPolygons,
            List<List<ClipVertex>> excisionPolygons,
            StructuredStartTipWedgeDiagnostics diagnostics)
        {
            if (rows == null || rows.Count < 2)
                return "none";

            int startRowIndex = 0;
            int endRowIndex = rows.Count - 2;
            var startIntervals = new List<StructuredBoundaryOwnerInterval>();
            var endIntervals = new List<StructuredBoundaryOwnerInterval>();
            AccumulateStructuredTipBoundaryIntervals(rows, supportPolygons, PartitionRegionKind.Support, startRowIndex, startIntervals);
            AccumulateStructuredTipBoundaryIntervals(rows, excisionPolygons, PartitionRegionKind.Excision, startRowIndex, startIntervals);
            AccumulateStructuredTipBoundaryIntervals(rows, supportPolygons, PartitionRegionKind.Support, endRowIndex, endIntervals);
            AccumulateStructuredTipBoundaryIntervals(rows, excisionPolygons, PartitionRegionKind.Excision, endRowIndex, endIntervals);
            return
                $"{BuildStructuredTipWedgeRowStageSummary(rows, startRowIndex, "start", startIntervals, diagnostics, includeStartTipDiagnostics: true)} ; " +
                $"{BuildStructuredTipWedgeRowStageSummary(rows, endRowIndex, "end", endIntervals, diagnostics, includeStartTipDiagnostics: true)}";
        }

        private static StructuredEndTipProbeResult RunStructuredEndTipEarlyHelperProbe(
            List<List<ClipVertex>> fragments,
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells,
            List<StructuredStripCell2D> domainCells,
            MeshWorkData work,
            string sourceStage,
            int sourceCellRowIndex,
            int sourceCellColumnIndex,
            int sourceTriangleIndex)
        {
            var result = new StructuredEndTipProbeResult
            {
                rowSummary = "none",
                contributorSummary = "none",
                hasShared = false,
                hasExcisionOnly = false,
            };

            if (rows == null || rows.Count < 2 || fragments == null || work == null)
                return result;

            int rowIndex = rows.Count - 2;
            var probeSupport = new List<List<ClipVertex>>();
            var probeExcision = new List<List<ClipVertex>>();
            var localDiagnostics = new StructuredStartTipWedgeDiagnostics();
            var localEdgeCache = new Dictionary<EdgeBoundaryKey, ClipVertex>();
            for (int i = 0; i < fragments.Count; i++)
            {
                List<ClipVertex> fragment = CloneStructuredPolygon(fragments[i]);
                if (fragment == null || fragment.Count < 3)
                    continue;

                if (!TryPartitionTriangleAgainstStructuredEndTipWedge(
                    fragment,
                    rows,
                    supportCells,
                    excisionCells,
                    domainCells,
                    work,
                    localEdgeCache,
                    ref localDiagnostics,
                    out _,
                    out List<List<ClipVertex>> supportPieces,
                    out List<List<ClipVertex>> excisionPieces))
                {
                    continue;
                }

                if (supportPieces != null)
                {
                    for (int piece = 0; piece < supportPieces.Count; piece++)
                        TryAppendPolygon(probeSupport, supportPieces[piece]);
                }

                if (excisionPieces != null)
                {
                    for (int piece = 0; piece < excisionPieces.Count; piece++)
                        TryAppendPolygon(probeExcision, excisionPieces[piece]);
                }
            }

            var intervals = new List<StructuredBoundaryOwnerInterval>();
            AccumulateStructuredTipBoundaryIntervals(
                rows,
                probeSupport,
                PartitionRegionKind.Support,
                rowIndex,
                intervals,
                sourceStage,
                sourceCellRowIndex,
                sourceCellColumnIndex,
                sourceTriangleIndex);
            AccumulateStructuredTipBoundaryIntervals(
                rows,
                probeExcision,
                PartitionRegionKind.Excision,
                rowIndex,
                intervals,
                sourceStage,
                sourceCellRowIndex,
                sourceCellColumnIndex,
                sourceTriangleIndex);

            result.rowSummary = BuildStructuredTipWedgeRowStageSummary(
                rows,
                rowIndex,
                "end",
                intervals,
                localDiagnostics,
                includeStartTipDiagnostics: true);
            result.contributorSummary = FormatStructuredBoundaryIntervalContributorSummary(intervals);
            EvaluateStructuredTipBoundaryProbe(intervals, out result.hasShared, out result.hasExcisionOnly);
            return result;
        }

        private static bool ShouldAuditStructuredCoverageCellForEndTip(
            List<StructuredStripRow> rows,
            StructuredStripCell2D cell)
        {
            if (rows == null || rows.Count < 2)
                return false;

            int endRowIndex = rows.Count - 2;
            return cell.rowIndex >= endRowIndex - 1;
        }

        private static string FormatStructuredCoverageCellAuditLabel(
            StructuredStripCell2D cell,
            int sourceTriangleIndex)
        {
            string triLabel = sourceTriangleIndex >= 0 ? $"tri={sourceTriangleIndex}" : "tri=none";
            return $"{triLabel} cell={FormatStructuredInterfaceDebugRowLabel(cell.rowIndex)}c{cell.columnIndex}{(cell.excision ? "X" : "S")}";
        }

        private static string BuildStructuredEndTipOrdinaryAuditSummary(
            StructuredStartTipWedgeDiagnostics diagnostics)
        {
            string firstBreak = string.IsNullOrEmpty(diagnostics.endOrdinaryAuditFirstBreakSummary)
                ? "none"
                : diagnostics.endOrdinaryAuditFirstBreakSummary;
            string preview =
                diagnostics.endOrdinaryAuditPreviewEntries != null && diagnostics.endOrdinaryAuditPreviewEntries.Count > 0
                ? string.Join(" ; ", diagnostics.endOrdinaryAuditPreviewEntries.ToArray())
                : "none";
            return $"watched={diagnostics.endOrdinaryAuditWatchedCellCount} firstBreak={firstBreak} preview={preview}";
        }

        private static void InitializePrecommitGeometryDiagnostics(ref RemeshDiagnostics diagnostics)
        {
            diagnostics.precommitRowCentersWorld = new List<Vector3>();
            diagnostics.precommitLeftBoundaryWorld = new List<Vector3>();
            diagnostics.precommitRightBoundaryWorld = new List<Vector3>();
            diagnostics.precommitLeftSupportWorld = new List<Vector3>();
            diagnostics.precommitRightSupportWorld = new List<Vector3>();
            diagnostics.precommitFailedOwnershipEdgesWorld = new List<Vector3>();
            diagnostics.precommitFailedOwnershipEdgeLabels = new List<string>();
            diagnostics.projectionAlignSummary = "none";
        }

        private static void ClearPrecommitGeometryDiagnostics(ref RemeshDiagnostics diagnostics)
        {
            if (diagnostics.precommitRowCentersWorld == null)
                diagnostics.precommitRowCentersWorld = new List<Vector3>();
            else
                diagnostics.precommitRowCentersWorld.Clear();

            if (diagnostics.precommitLeftBoundaryWorld == null)
                diagnostics.precommitLeftBoundaryWorld = new List<Vector3>();
            else
                diagnostics.precommitLeftBoundaryWorld.Clear();

            if (diagnostics.precommitRightBoundaryWorld == null)
                diagnostics.precommitRightBoundaryWorld = new List<Vector3>();
            else
                diagnostics.precommitRightBoundaryWorld.Clear();

            if (diagnostics.precommitLeftSupportWorld == null)
                diagnostics.precommitLeftSupportWorld = new List<Vector3>();
            else
                diagnostics.precommitLeftSupportWorld.Clear();

            if (diagnostics.precommitRightSupportWorld == null)
                diagnostics.precommitRightSupportWorld = new List<Vector3>();
            else
                diagnostics.precommitRightSupportWorld.Clear();

            if (diagnostics.precommitFailedOwnershipEdgesWorld == null)
                diagnostics.precommitFailedOwnershipEdgesWorld = new List<Vector3>();
            else
                diagnostics.precommitFailedOwnershipEdgesWorld.Clear();

            if (diagnostics.precommitFailedOwnershipEdgeLabels == null)
                diagnostics.precommitFailedOwnershipEdgeLabels = new List<string>();
            else
                diagnostics.precommitFailedOwnershipEdgeLabels.Clear();
        }

        private static void CaptureStructuredPrecommitGeometry(
            List<StructuredStripRow> rows,
            ref RemeshDiagnostics diagnostics)
        {
            ClearPrecommitGeometryDiagnostics(ref diagnostics);
            if (rows == null)
                return;

            for (int i = 0; i < rows.Count; i++)
            {
                StructuredStripRow row = rows[i];
                diagnostics.precommitRowCentersWorld.Add(row.frame.center);
                diagnostics.precommitLeftBoundaryWorld.Add(row.leftBoundary.world);
                diagnostics.precommitRightBoundaryWorld.Add(row.rightBoundary.world);
                diagnostics.precommitLeftSupportWorld.Add(row.leftSupport.world);
                diagnostics.precommitRightSupportWorld.Add(row.rightSupport.world);
            }
        }

        private static string BuildProjectionAlignSummary(
            IReadOnlyList<Vector3> targetStrokeWorld,
            IReadOnlyList<Vector3> masterChainWorld,
            IReadOnlyList<Vector3> centerCurveWorld,
            IReadOnlyList<Vector3> rowCentersWorld,
            IReadOnlyList<Vector3> leftBoundaryWorld,
            IReadOnlyList<Vector3> rightBoundaryWorld,
            IReadOnlyList<Vector3> leftSupportWorld,
            IReadOnlyList<Vector3> rightSupportWorld,
            IReadOnlyList<Vector3> failedOwnershipEdgesWorld)
        {
            int rowCount = rowCentersWorld != null ? rowCentersWorld.Count : 0;
            int masterChainCount = masterChainWorld != null ? masterChainWorld.Count : 0;
            int centerCurveCount = centerCurveWorld != null ? centerCurveWorld.Count : 0;
            int targetStrokeCount = targetStrokeWorld != null ? targetStrokeWorld.Count : 0;

            BuildPointToPolylineDistanceStats(rowCentersWorld, masterChainWorld, out int rowToMasterSamples, out float rowToMasterAvg, out float rowToMasterMax);
            BuildPointToPolylineDistanceStats(rowCentersWorld, centerCurveWorld, out int rowToCenterSamples, out float rowToCenterAvg, out float rowToCenterMax);
            BuildMidpointToPolylineDistanceStats(leftBoundaryWorld, rightBoundaryWorld, centerCurveWorld, out int visibleMidSamples, out float visibleMidAvg, out float visibleMidMax);
            BuildPointToPolylineDistanceStats(leftSupportWorld, centerCurveWorld, out int leftSupportSamples, out float leftSupportAvg, out float leftSupportMax);
            BuildPointToPolylineDistanceStats(rightSupportWorld, centerCurveWorld, out int rightSupportSamples, out float rightSupportAvg, out float rightSupportMax);
            CombineDistanceStats(
                leftSupportSamples,
                leftSupportAvg,
                leftSupportMax,
                rightSupportSamples,
                rightSupportAvg,
                rightSupportMax,
                out int supportSamples,
                out float supportAvg,
                out float supportMax);

            BuildPolylineContinuityStats(leftBoundaryWorld, out PrecommitPolylineStats leftBoundaryStats);
            BuildPolylineContinuityStats(rightBoundaryWorld, out PrecommitPolylineStats rightBoundaryStats);
            BuildPolylineContinuityStats(leftSupportWorld, out PrecommitPolylineStats leftSupportStats);
            BuildPolylineContinuityStats(rightSupportWorld, out PrecommitPolylineStats rightSupportStats);
            BuildLinePairStats(failedOwnershipEdgesWorld, out PrecommitLinePairStats failedEdgeStats);

            string targetReferenceSummary = "none";
            if (targetStrokeWorld != null && targetStrokeWorld.Count >= 2)
            {
                BuildPointToPolylineDistanceStats(rowCentersWorld, targetStrokeWorld, out int rowToTargetSamples, out float rowToTargetAvg, out float rowToTargetMax);
                BuildPointToPolylineDistanceStats(leftBoundaryWorld, targetStrokeWorld, out int leftVisibleSamples, out float leftVisibleAvg, out float leftVisibleMax);
                BuildPointToPolylineDistanceStats(rightBoundaryWorld, targetStrokeWorld, out int rightVisibleSamples, out float rightVisibleAvg, out float rightVisibleMax);
                CombineDistanceStats(
                    leftVisibleSamples,
                    leftVisibleAvg,
                    leftVisibleMax,
                    rightVisibleSamples,
                    rightVisibleAvg,
                    rightVisibleMax,
                    out int visibleSamples,
                    out float visibleAvg,
                    out float visibleMax);
                targetReferenceSummary =
                    $"stroke={targetStrokeCount} row={FormatDistanceStats(rowToTargetSamples, rowToTargetAvg, rowToTargetMax)} visible={FormatDistanceStats(visibleSamples, visibleAvg, visibleMax)}";
            }

            return
                $"rows={rowCount} masterChain={masterChainCount} centerCurve={centerCurveCount} " +
                $"rowToMaster={FormatDistanceStats(rowToMasterSamples, rowToMasterAvg, rowToMasterMax)} " +
                $"rowToCenter={FormatDistanceStats(rowToCenterSamples, rowToCenterAvg, rowToCenterMax)} " +
                $"visibleMidToCenter={FormatDistanceStats(visibleMidSamples, visibleMidAvg, visibleMidMax)} " +
                $"supportToCenter={FormatDistanceStats(supportSamples, supportAvg, supportMax)} " +
                $"LB={FormatPolylineContinuityStats(leftBoundaryStats)} RB={FormatPolylineContinuityStats(rightBoundaryStats)} " +
                $"LS={FormatPolylineContinuityStats(leftSupportStats)} RS={FormatPolylineContinuityStats(rightSupportStats)} " +
                $"failedEdges={FormatLinePairStats(failedEdgeStats)} " +
                $"targetRef={targetReferenceSummary}";
        }

        private static void BuildMidpointToPolylineDistanceStats(
            IReadOnlyList<Vector3> leftPoints,
            IReadOnlyList<Vector3> rightPoints,
            IReadOnlyList<Vector3> polyline,
            out int sampleCount,
            out float averageDistance,
            out float maxDistance)
        {
            sampleCount = 0;
            averageDistance = 0f;
            maxDistance = 0f;
            if (leftPoints == null || rightPoints == null || polyline == null || polyline.Count < 2)
                return;

            int count = Mathf.Min(leftPoints.Count, rightPoints.Count);
            if (count <= 0)
                return;

            float sum = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector3 midpoint = (leftPoints[i] + rightPoints[i]) * 0.5f;
                float distance = ComputePointToPolylineDistance(midpoint, polyline);
                sum += distance;
                maxDistance = Mathf.Max(maxDistance, distance);
                sampleCount++;
            }

            averageDistance = sampleCount > 0 ? sum / sampleCount : 0f;
        }

        private static void BuildPointToPolylineDistanceStats(
            IReadOnlyList<Vector3> points,
            IReadOnlyList<Vector3> polyline,
            out int sampleCount,
            out float averageDistance,
            out float maxDistance)
        {
            sampleCount = 0;
            averageDistance = 0f;
            maxDistance = 0f;
            if (points == null || polyline == null || polyline.Count < 2)
                return;

            float sum = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float distance = ComputePointToPolylineDistance(points[i], polyline);
                sum += distance;
                maxDistance = Mathf.Max(maxDistance, distance);
                sampleCount++;
            }

            averageDistance = sampleCount > 0 ? sum / sampleCount : 0f;
        }

        private static void CombineDistanceStats(
            int leftCount,
            float leftAverage,
            float leftMax,
            int rightCount,
            float rightAverage,
            float rightMax,
            out int combinedCount,
            out float combinedAverage,
            out float combinedMax)
        {
            combinedCount = leftCount + rightCount;
            combinedAverage = combinedCount > 0
                ? ((leftAverage * leftCount) + (rightAverage * rightCount)) / combinedCount
                : 0f;
            combinedMax = Mathf.Max(leftMax, rightMax);
        }

        private static string FormatDistanceStats(int sampleCount, float averageDistance, float maxDistance)
        {
            return sampleCount > 0
                ? $"n={sampleCount} avg/max={averageDistance:F6}/{maxDistance:F6}"
                : "n=0";
        }

        private static void BuildPolylineContinuityStats(
            IReadOnlyList<Vector3> points,
            out PrecommitPolylineStats stats)
        {
            stats = default;
            if (points == null || points.Count == 0)
                return;

            stats.pointCount = points.Count;
            stats.segmentCount = Mathf.Max(0, points.Count - 1);
            stats.componentCount = 1;
            for (int i = 1; i < points.Count; i++)
            {
                float segmentLength = Vector3.Distance(points[i - 1], points[i]);
                stats.totalLength += segmentLength;
                stats.maxJump = Mathf.Max(stats.maxJump, segmentLength);
            }
        }

        private static string FormatPolylineContinuityStats(PrecommitPolylineStats stats)
        {
            return $"pts={stats.pointCount} seg={stats.segmentCount} len={stats.totalLength:F6} maxJump={stats.maxJump:F6} comp={stats.componentCount}";
        }

        private static void BuildLinePairStats(
            IReadOnlyList<Vector3> pairPoints,
            out PrecommitLinePairStats stats)
        {
            stats = default;
            if (pairPoints == null || pairPoints.Count < 2)
                return;

            for (int i = 0; i + 1 < pairPoints.Count; i += 2)
            {
                stats.segmentCount++;
                stats.totalLength += Vector3.Distance(pairPoints[i], pairPoints[i + 1]);
            }
        }

        private static string FormatLinePairStats(PrecommitLinePairStats stats)
        {
            return $"seg={stats.segmentCount} len={stats.totalLength:F6}";
        }

        private static float ComputePointToPolylineDistance(Vector3 point, IReadOnlyList<Vector3> polyline)
        {
            if (polyline == null || polyline.Count == 0)
                return 0f;
            if (polyline.Count == 1)
                return Vector3.Distance(point, polyline[0]);

            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < polyline.Count - 1; i++)
            {
                float distanceSq = DistancePointToSegmentSq(point, polyline[i], polyline[i + 1]);
                if (distanceSq < bestDistanceSq)
                    bestDistanceSq = distanceSq;
            }

            return float.IsPositiveInfinity(bestDistanceSq) ? 0f : Mathf.Sqrt(bestDistanceSq);
        }

        private static float DistancePointToSegmentSq(Vector3 point, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float denom = Vector3.Dot(ab, ab);
            if (denom <= 1e-12f)
                return (point - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom);
            Vector3 projection = a + ab * t;
            return (point - projection).sqrMagnitude;
        }

        private static bool IsStructuredBoundarySegmentTipAdjacent(List<StructuredStripRow> rows, int rowIndex)
        {
            return rows != null &&
                rowIndex >= 0 &&
                rowIndex + 1 < rows.Count &&
                (rows[rowIndex].isTipRow || rows[rowIndex + 1].isTipRow);
        }

        private static bool TryGetStructuredSegmentCenterWidths(
            List<StructuredStripRow> rows,
            int rowIndex,
            out float startCenter,
            out float endCenter)
        {
            startCenter = 0f;
            endCenter = 0f;
            if (rows == null || rowIndex < 0 || rowIndex + 1 >= rows.Count)
                return false;

            startCenter = GetStructuredRowCenterWidth(rows[rowIndex]);
            endCenter = GetStructuredRowCenterWidth(rows[rowIndex + 1]);
            return true;
        }

        private static bool ShouldDowngradeStructuredTipBoundaryToSupportOnly(
            List<StructuredStripRow> rows,
            int rowIndex)
        {
            const float collapseEpsilon = 5e-4f;
            if (!IsStructuredBoundarySegmentTipAdjacent(rows, rowIndex))
                return false;
            if (!TryGetStructuredSegmentCenterWidths(rows, rowIndex, out float startCenter, out float endCenter))
                return false;

            return Mathf.Abs(startCenter) <= collapseEpsilon && Mathf.Abs(endCenter) <= collapseEpsilon;
        }

        private static bool ShouldCanonicalizeStructuredExpectedSegment(
            List<StructuredStripRow> rows,
            PartitionRegionKind regionKind,
            StructuredBoundarySegmentKey key,
            ClipBoundaryFlags boundaryFlag)
        {
            if (rows == null || key.rowIndex < 0 || key.rowIndex + 1 >= rows.Count)
                return false;

            ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(boundaryFlag);
            if (singleFlag == ClipBoundaryFlags.None)
                return false;

            return DoesRegionOwnStructuredInterfaceFlag(regionKind, singleFlag);
        }

        private static bool TryGetStructuredBoundarySegmentCoords(
            List<StructuredStripRow> rows,
            int rowIndex,
            ClipBoundaryFlags boundaryFlag,
            out Vector2 a,
            out Vector2 b)
        {
            a = Vector2.zero;
            b = Vector2.zero;
            if (rows == null || rowIndex < 0 || rowIndex + 1 >= rows.Count)
                return false;

            StructuredStripRow start = rows[rowIndex];
            StructuredStripRow end = rows[rowIndex + 1];
            switch (boundaryFlag)
            {
                case ClipBoundaryFlags.LeftSupport:
                    a = BuildStructuredStripCoord(start.frame, start.leftSupport);
                    b = BuildStructuredStripCoord(end.frame, end.leftSupport);
                    return true;
                case ClipBoundaryFlags.Left:
                    a = BuildStructuredStripCoord(start.frame, start.leftBoundary);
                    b = BuildStructuredStripCoord(end.frame, end.leftBoundary);
                    return true;
                case ClipBoundaryFlags.Right:
                    a = BuildStructuredStripCoord(start.frame, start.rightBoundary);
                    b = BuildStructuredStripCoord(end.frame, end.rightBoundary);
                    return true;
                case ClipBoundaryFlags.RightSupport:
                    a = BuildStructuredStripCoord(start.frame, start.rightSupport);
                    b = BuildStructuredStripCoord(end.frame, end.rightSupport);
                    return true;
                default:
                    return false;
            }
        }

        private static float ComputeStructuredBoundarySegmentToleranceSq(Vector2 a, Vector2 b)
        {
            float length = Vector2.Distance(a, b);
            float tolerance = Mathf.Max(1e-4f, length * 0.02f);
            return tolerance * tolerance;
        }

        private static bool TryFindStructuredBoundaryRowSegment(List<StructuredStripRow> rows, float s, out int rowIndex)
        {
            rowIndex = -1;
            if (rows == null || rows.Count < 2)
                return false;

            const float epsilon = 1e-5f;
            for (int i = 0; i < rows.Count - 1; i++)
            {
                float a = rows[i].frame.s;
                float b = rows[i + 1].frame.s;
                float min = Mathf.Min(a, b) - epsilon;
                float max = Mathf.Max(a, b) + epsilon;
                if (s < min || s > max)
                    continue;

                rowIndex = i;
                return true;
            }

            return false;
        }

        private static StructuredPointMergeResult AddStructuredBoundarySegmentPoint(
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            StructuredBoundarySegmentKey key,
            ClipVertex vertex,
            float t)
        {
            if (boundaryPoints == null)
                return StructuredPointMergeResult.Ignored;

            if (!boundaryPoints.TryGetValue(key, out List<SourceEdgePoint> points))
            {
                points = new List<SourceEdgePoint>();
                boundaryPoints.Add(key, points);
            }

            const float tEpsilon = 1e-5f;
            for (int i = 0; i < points.Count; i++)
            {
                SourceEdgePoint existing = points[i];
                bool sameMaterialized =
                    existing.vertex.materializedIndex >= 0 &&
                    vertex.materializedIndex >= 0 &&
                    existing.vertex.materializedIndex == vertex.materializedIndex;
                bool sameBoundaryPoint =
                    Mathf.Abs(existing.t - t) <= tEpsilon &&
                    (existing.vertex.world - vertex.world).sqrMagnitude <= 1e-10f;
                if (!sameMaterialized && !sameBoundaryPoint)
                    continue;

                existing.vertex.boundaryFlags |= vertex.boundaryFlags;
                if (existing.vertex.materializedIndex < 0 && vertex.materializedIndex >= 0)
                    existing.vertex.materializedIndex = vertex.materializedIndex;
                points[i] = existing;
                return StructuredPointMergeResult.Reused;
            }

            points.Add(new SourceEdgePoint
            {
                vertex = vertex,
                t = Mathf.Clamp01(t),
            });
            return StructuredPointMergeResult.Added;
        }

        private static StructuredPointMergeResult AddStructuredCanonicalSegmentPoint(
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> boundaryPoints,
            StructuredBoundarySegmentKey key,
            ClipVertex vertex,
            float t)
        {
            if (boundaryPoints == null)
                return StructuredPointMergeResult.Ignored;

            vertex.boundaryFlags |= key.boundaryFlag;
            if (!boundaryPoints.TryGetValue(key, out List<SourceEdgePoint> points))
            {
                points = new List<SourceEdgePoint>();
                boundaryPoints.Add(key, points);
            }

            const float tEpsilon = 1e-4f;
            int bestIndex = -1;
            float bestDelta = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                SourceEdgePoint existing = points[i];
                bool sameMaterialized =
                    existing.vertex.materializedIndex >= 0 &&
                    vertex.materializedIndex >= 0 &&
                    existing.vertex.materializedIndex == vertex.materializedIndex;
                float delta = Mathf.Abs(existing.t - t);
                if (!sameMaterialized && delta > tEpsilon)
                    continue;
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = i;
                    if (sameMaterialized)
                        break;
                }
            }

            if (bestIndex >= 0)
            {
                SourceEdgePoint existing = points[bestIndex];
                existing.vertex.boundaryFlags |= vertex.boundaryFlags;
                if (existing.vertex.materializedIndex < 0 && vertex.materializedIndex >= 0)
                    existing.vertex.materializedIndex = vertex.materializedIndex;
                if (CountBoundaryFlagBits(vertex.boundaryFlags) > CountBoundaryFlagBits(existing.vertex.boundaryFlags))
                {
                    int preservedMaterialized = existing.vertex.materializedIndex;
                    existing.vertex = WithBoundaryFlags(vertex, existing.vertex.boundaryFlags | vertex.boundaryFlags);
                    if (existing.vertex.materializedIndex < 0 && preservedMaterialized >= 0)
                        existing.vertex.materializedIndex = preservedMaterialized;
                }
                points[bestIndex] = existing;
                return StructuredPointMergeResult.Reused;
            }

            points.Add(new SourceEdgePoint
            {
                vertex = vertex,
                t = Mathf.Clamp01(t),
            });
            return StructuredPointMergeResult.Added;
        }

        private static void CanonicalizeStructuredRegionPolygonsByExpectedSegments(
            MeshWorkData work,
            List<StructuredStripRow> rows,
            out StructuredNormalizationStats stats,
            out HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints,
            List<List<ClipVertex>> outsidePolygons,
            List<List<ClipVertex>> supportPolygons,
            List<List<ClipVertex>> excisionPolygons)
        {
            stats = default;
            mandatoryBreakpoints = new HashSet<StructuredCanonicalBreakpointKey>();
            if (work == null || rows == null || rows.Count < 2)
                return;

            var canonicalPoints = new Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>>();
            AccumulateStructuredCanonicalSegmentPoints(rows, PartitionRegionKind.Outside, outsidePolygons, canonicalPoints, ref stats.reuseCount);
            AccumulateStructuredCanonicalSegmentPoints(rows, PartitionRegionKind.Support, supportPolygons, canonicalPoints, ref stats.reuseCount);
            AccumulateStructuredCanonicalSegmentPoints(rows, PartitionRegionKind.Excision, excisionPolygons, canonicalPoints, ref stats.reuseCount);
            stats.keyCount = canonicalPoints.Count;
            if (canonicalPoints.Count == 0)
                return;

            foreach (var pair in canonicalPoints)
            {
                pair.Value.Sort((a, b) => a.t.CompareTo(b.t));
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    SourceEdgePoint point = pair.Value[i];
                    point.vertex = EnsureVertexMaterialized(point.vertex, work);
                    point.vertex.boundaryFlags |= pair.Key.boundaryFlag;
                    pair.Value[i] = point;
                }
            }

            CanonicalizeStructuredRegionPolygonGroupByExpectedSegments(rows, PartitionRegionKind.Outside, canonicalPoints, outsidePolygons, mandatoryBreakpoints, ref stats);
            CanonicalizeStructuredRegionPolygonGroupByExpectedSegments(rows, PartitionRegionKind.Support, canonicalPoints, supportPolygons, mandatoryBreakpoints, ref stats);
            CanonicalizeStructuredRegionPolygonGroupByExpectedSegments(rows, PartitionRegionKind.Excision, canonicalPoints, excisionPolygons, mandatoryBreakpoints, ref stats);
        }

        private static void AccumulateStructuredCanonicalSegmentPoints(
            List<StructuredStripRow> rows,
            PartitionRegionKind regionKind,
            List<List<ClipVertex>> polygons,
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> canonicalPoints,
            ref int reuseCount)
        {
            if (rows == null || polygons == null || canonicalPoints == null)
                return;

            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                List<ClipVertex> polygon = polygons[polygonIndex];
                if (polygon == null || polygon.Count < 2)
                    continue;

                for (int i = 0; i < polygon.Count; i++)
                {
                    ClipVertex a = polygon[i];
                    ClipVertex b = polygon[(i + 1) % polygon.Count];
                    if (!TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out StructuredBoundarySegmentKey key, out ClipBoundaryFlags boundaryFlag, out float ta, out float tb) ||
                        !DoesRegionOwnStructuredInterfaceFlag(regionKind, boundaryFlag) ||
                        !ShouldCanonicalizeStructuredExpectedSegment(rows, regionKind, key, boundaryFlag))
                    {
                        continue;
                    }

                    reuseCount += CountStructuredPointReuses(AddStructuredCanonicalSegmentPoint(canonicalPoints, key, a, ta));
                    reuseCount += CountStructuredPointReuses(AddStructuredCanonicalSegmentPoint(canonicalPoints, key, b, tb));
                }
            }
        }

        private static void CanonicalizeStructuredRegionPolygonGroupByExpectedSegments(
            List<StructuredStripRow> rows,
            PartitionRegionKind regionKind,
            Dictionary<StructuredBoundarySegmentKey, List<SourceEdgePoint>> canonicalPoints,
            List<List<ClipVertex>> polygons,
            HashSet<StructuredCanonicalBreakpointKey> sharedMandatoryBreakpoints,
            ref StructuredNormalizationStats stats)
        {
            if (rows == null || canonicalPoints == null || polygons == null)
                return;

            for (int polygonIndex = 0; polygonIndex < polygons.Count; polygonIndex++)
            {
                List<ClipVertex> polygon = polygons[polygonIndex];
                if (polygon == null || polygon.Count < 2)
                    continue;

                int vertexCount = polygon.Count;
                var edgeContexts = new StructuredBoundaryEdgeContext[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    ClipVertex a = polygon[i];
                    ClipVertex b = polygon[(i + 1) % vertexCount];
                    if (!TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out StructuredBoundarySegmentKey key, out ClipBoundaryFlags boundaryFlag, out float ta, out float tb) ||
                        !DoesRegionOwnStructuredInterfaceFlag(regionKind, boundaryFlag) ||
                        !ShouldCanonicalizeStructuredExpectedSegment(rows, regionKind, key, boundaryFlag) ||
                        !canonicalPoints.ContainsKey(key))
                    {
                        continue;
                    }

                    edgeContexts[i] = new StructuredBoundaryEdgeContext
                    {
                        valid = true,
                        key = key,
                        boundaryFlag = boundaryFlag,
                        ta = ta,
                        tb = tb,
                    };
                }

                var vertexBindings = new StructuredBoundaryVertexBinding[vertexCount];
                for (int i = 0; i < vertexCount; i++)
                {
                    if (edgeContexts[i].valid)
                    {
                        vertexBindings[i] = new StructuredBoundaryVertexBinding
                        {
                            valid = true,
                            key = edgeContexts[i].key,
                            boundaryFlag = edgeContexts[i].boundaryFlag,
                            t = edgeContexts[i].ta,
                        };
                        continue;
                    }

                    int prevIndex = (i - 1 + vertexCount) % vertexCount;
                    if (!edgeContexts[prevIndex].valid)
                        continue;

                    vertexBindings[i] = new StructuredBoundaryVertexBinding
                    {
                        valid = true,
                        key = edgeContexts[prevIndex].key,
                        boundaryFlag = edgeContexts[prevIndex].boundaryFlag,
                        t = edgeContexts[prevIndex].tb,
                    };
                }

                bool polygonTouched = false;
                var rewritten = new List<ClipVertex>(vertexCount);
                var mandatoryBreakpoints = new HashSet<StructuredCanonicalBreakpointKey>();
                for (int i = 0; i < vertexCount; i++)
                {
                    ClipVertex current = polygon[i];
                    if (vertexBindings[i].valid &&
                        canonicalPoints.TryGetValue(vertexBindings[i].key, out List<SourceEdgePoint> boundPoints) &&
                        TryFindStructuredCanonicalPoint(boundPoints, vertexBindings[i].t, out ClipVertex canonicalVertex))
                    {
                        AddStructuredCanonicalMandatoryBreakpoint(mandatoryBreakpoints, vertexBindings[i].key, vertexBindings[i].t);
                        ClipVertex rewrittenVertex = WithBoundaryFlags(canonicalVertex, canonicalVertex.boundaryFlags | current.boundaryFlags);
                        AppendStructuredNormalizedVertex(rewritten, rewrittenVertex);
                        if (!AreStructuredVerticesEquivalent(rewrittenVertex, current) ||
                            rewrittenVertex.materializedIndex != current.materializedIndex)
                        {
                            polygonTouched = true;
                        }
                    }
                    else
                    {
                        AppendStructuredNormalizedVertex(rewritten, current);
                    }

                    if (!edgeContexts[i].valid ||
                        !canonicalPoints.TryGetValue(edgeContexts[i].key, out List<SourceEdgePoint> points))
                    {
                        continue;
                    }

                    int inserted = AppendStructuredCanonicalInteriorSourceEdgePoints(
                        rewritten,
                        points,
                        edgeContexts[i].ta,
                        edgeContexts[i].tb,
                        edgeContexts[i].boundaryFlag,
                        edgeContexts[i].key,
                        mandatoryBreakpoints);
                    if (inserted > 0)
                    {
                        polygonTouched = true;
                        stats.insertCount += inserted;
                    }
                }

                RemoveStructuredClosingDuplicate(rewritten);
                rewritten = CleanupStructuredCanonicalPolygon(rows, rewritten, mandatoryBreakpoints);
                if (sharedMandatoryBreakpoints != null && mandatoryBreakpoints.Count > 0)
                    sharedMandatoryBreakpoints.UnionWith(mandatoryBreakpoints);
                if (rewritten.Count >= 3)
                {
                    polygons[polygonIndex] = rewritten;
                    if (polygonTouched || rewritten.Count != polygon.Count)
                        stats.polygonsTouched++;
                }
            }
        }

        private static bool TryFindStructuredCanonicalPoint(
            List<SourceEdgePoint> points,
            float t,
            out ClipVertex vertex)
        {
            vertex = default;
            if (points == null || points.Count == 0)
                return false;

            const float tEpsilon = 1e-4f;
            int bestIndex = -1;
            float bestDelta = float.PositiveInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                float delta = Mathf.Abs(points[i].t - t);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0 || bestDelta > tEpsilon)
                return false;

            vertex = points[bestIndex].vertex;
            return true;
        }

        private static void NormalizeStructuredCellFragmentsBySourceEdges(
            List<StructuredCellFragment> fragments,
            MeshWorkData work)
        {
            if (fragments == null || fragments.Count == 0 || work == null)
                return;

            var sourceEdgePoints = new Dictionary<ulong, List<SourceEdgePoint>>();
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                List<ClipVertex> vertices = fragments[fragmentIndex].vertices;
                if (vertices == null || vertices.Count < 2)
                    continue;

                for (int i = 0; i < vertices.Count; i++)
                {
                    ClipVertex a = vertices[i];
                    ClipVertex b = vertices[(i + 1) % vertices.Count];
                    if (!TryResolveSupportEdge(a, b, out int edgeA, out int edgeB))
                        continue;

                    ulong key = MakeUndirectedEdgeKey(edgeA, edgeB);
                    AddStructuredSourceEdgePoint(sourceEdgePoints, key, edgeA, edgeB, a, work);
                    AddStructuredSourceEdgePoint(sourceEdgePoints, key, edgeA, edgeB, b, work);
                }
            }

            if (sourceEdgePoints.Count == 0)
                return;

            foreach (var pair in sourceEdgePoints)
                pair.Value.Sort((a, b) => a.t.CompareTo(b.t));

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                StructuredCellFragment fragment = fragments[fragmentIndex];
                List<ClipVertex> vertices = fragment.vertices;
                if (vertices == null || vertices.Count < 2)
                    continue;

                var normalized = new List<ClipVertex>(vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                {
                    ClipVertex a = vertices[i];
                    ClipVertex b = vertices[(i + 1) % vertices.Count];
                    AppendStructuredNormalizedVertex(normalized, a);

                    if (!TryResolveSupportEdge(a, b, out int edgeA, out int edgeB))
                        continue;

                    ulong key = MakeUndirectedEdgeKey(edgeA, edgeB);
                    if (!sourceEdgePoints.TryGetValue(key, out List<SourceEdgePoint> points) || points.Count <= 2)
                        continue;

                    if (!TryComputeSourceEdgeParameter(a, work, edgeA, edgeB, out float ta) ||
                        !TryComputeSourceEdgeParameter(b, work, edgeA, edgeB, out float tb))
                    {
                        continue;
                    }

                    ClipBoundaryFlags hostFlags = a.boundaryFlags & b.boundaryFlags;
                    AppendStructuredInteriorSourceEdgePoints(normalized, points, ta, tb, hostFlags);
                }

                RemoveStructuredClosingDuplicate(normalized);
                if (normalized.Count >= 3)
                {
                    fragment.vertices = normalized;
                    fragments[fragmentIndex] = fragment;
                }
            }
        }

        private static void NormalizeStructuredCellFragmentsByStructuredSides(
            List<StructuredCellFragment> fragments,
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex)
        {
            if (fragments == null || fragments.Count == 0)
                return;

            if (!TryBuildStructuredCellSideDefinitions(
                    startRow,
                    endRow,
                    columnIndex,
                    out StructuredCellSideDefinition[] sides))
            {
                return;
            }

            var sidePoints = new Dictionary<int, List<SourceEdgePoint>>();
            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                List<ClipVertex> vertices = fragments[fragmentIndex].vertices;
                if (vertices == null || vertices.Count < 2)
                    continue;

                for (int i = 0; i < vertices.Count; i++)
                {
                    ClipVertex a = vertices[i];
                    ClipVertex b = vertices[(i + 1) % vertices.Count];
                    for (int sideIndex = 0; sideIndex < sides.Length; sideIndex++)
                    {
                        ClipBoundaryFlags sharedFlags = a.boundaryFlags & b.boundaryFlags & sides[sideIndex].flag;
                        if (sharedFlags == ClipBoundaryFlags.None)
                            continue;
                        AddStructuredCellSidePoint(sidePoints, sideIndex, sides[sideIndex], a);
                        AddStructuredCellSidePoint(sidePoints, sideIndex, sides[sideIndex], b);
                    }
                }
            }

            if (sidePoints.Count == 0)
                return;

            foreach (var pair in sidePoints)
                pair.Value.Sort((a, b) => a.t.CompareTo(b.t));

            for (int fragmentIndex = 0; fragmentIndex < fragments.Count; fragmentIndex++)
            {
                StructuredCellFragment fragment = fragments[fragmentIndex];
                List<ClipVertex> vertices = fragment.vertices;
                if (vertices == null || vertices.Count < 2)
                    continue;

                var normalized = new List<ClipVertex>(vertices.Count);
                for (int i = 0; i < vertices.Count; i++)
                {
                    ClipVertex a = vertices[i];
                    ClipVertex b = vertices[(i + 1) % vertices.Count];
                    AppendStructuredNormalizedVertex(normalized, a);

                    for (int sideIndex = 0; sideIndex < sides.Length; sideIndex++)
                    {
                        ClipBoundaryFlags sharedFlags = a.boundaryFlags & b.boundaryFlags & sides[sideIndex].flag;
                        if (sharedFlags == ClipBoundaryFlags.None)
                            continue;
                        if (!sidePoints.TryGetValue(sideIndex, out List<SourceEdgePoint> points) || points.Count <= 2)
                            continue;
                        if (!TryComputeStructuredCellSideParameter(sides[sideIndex], a, out float ta) ||
                            !TryComputeStructuredCellSideParameter(sides[sideIndex], b, out float tb))
                        {
                            continue;
                        }

                        AppendStructuredInteriorSourceEdgePoints(normalized, points, ta, tb, sides[sideIndex].flag);
                    }
                }

                RemoveStructuredClosingDuplicate(normalized);
                normalized = CleanupPolygon(normalized);
                if (normalized.Count >= 3)
                {
                    fragment.vertices = normalized;
                    fragments[fragmentIndex] = fragment;
                }
            }
        }

        private static bool TryBuildStructuredCellSideDefinitions(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            out StructuredCellSideDefinition[] sides)
        {
            sides = null;
            if (!TryBuildStructuredCellCornerCoords(
                    startRow,
                    endRow,
                    columnIndex,
                    out Vector2 startLeft,
                    out Vector2 startRight,
                    out Vector2 endRight,
                    out Vector2 endLeft))
            {
                return false;
            }

            GetStructuredCellSideFlags(columnIndex, out ClipBoundaryFlags leftFlags, out ClipBoundaryFlags rightFlags);
            if (leftFlags == ClipBoundaryFlags.None || rightFlags == ClipBoundaryFlags.None)
                return false;

            sides = new[]
            {
                new StructuredCellSideDefinition { flag = ClipBoundaryFlags.Start, a = startLeft, b = startRight },
                new StructuredCellSideDefinition { flag = rightFlags, a = startRight, b = endRight },
                new StructuredCellSideDefinition { flag = ClipBoundaryFlags.End, a = endRight, b = endLeft },
                new StructuredCellSideDefinition { flag = leftFlags, a = endLeft, b = startLeft },
            };
            return true;
        }

        private static bool TryBuildStructuredCellCornerCoords(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            out Vector2 startLeft,
            out Vector2 startRight,
            out Vector2 endRight,
            out Vector2 endLeft)
        {
            startLeft = Vector2.zero;
            startRight = Vector2.zero;
            endRight = Vector2.zero;
            endLeft = Vector2.zero;

            switch (columnIndex)
            {
                case 0:
                    startLeft = BuildStructuredStripCoord(startRow.frame, startRow.leftSupport);
                    startRight = BuildStructuredStripCoord(startRow.frame, startRow.leftBoundary);
                    endRight = BuildStructuredStripCoord(endRow.frame, endRow.leftBoundary);
                    endLeft = BuildStructuredStripCoord(endRow.frame, endRow.leftSupport);
                    return true;
                case 1:
                    startLeft = BuildStructuredStripCoord(startRow.frame, startRow.leftBoundary);
                    startRight = BuildStructuredStripCoord(startRow.frame, startRow.rightBoundary);
                    endRight = BuildStructuredStripCoord(endRow.frame, endRow.rightBoundary);
                    endLeft = BuildStructuredStripCoord(endRow.frame, endRow.leftBoundary);
                    return true;
                case 2:
                    startLeft = BuildStructuredStripCoord(startRow.frame, startRow.rightBoundary);
                    startRight = BuildStructuredStripCoord(startRow.frame, startRow.rightSupport);
                    endRight = BuildStructuredStripCoord(endRow.frame, endRow.rightSupport);
                    endLeft = BuildStructuredStripCoord(endRow.frame, endRow.rightBoundary);
                    return true;
                default:
                    return false;
            }
        }

        private static void AddStructuredCellSidePoint(
            Dictionary<int, List<SourceEdgePoint>> sidePoints,
            int sideIndex,
            StructuredCellSideDefinition side,
            ClipVertex vertex)
        {
            if (sidePoints == null ||
                !TryComputeStructuredCellSideParameter(side, vertex, out float t))
            {
                return;
            }

            if (!sidePoints.TryGetValue(sideIndex, out List<SourceEdgePoint> points))
            {
                points = new List<SourceEdgePoint>();
                sidePoints.Add(sideIndex, points);
            }

            const float tEpsilon = 1e-5f;
            for (int i = 0; i < points.Count; i++)
            {
                SourceEdgePoint existing = points[i];
                bool sameMaterialized =
                    existing.vertex.materializedIndex >= 0 &&
                    vertex.materializedIndex >= 0 &&
                    existing.vertex.materializedIndex == vertex.materializedIndex;
                bool sameSidePoint =
                    Mathf.Abs(existing.t - t) <= tEpsilon &&
                    (existing.vertex.world - vertex.world).sqrMagnitude <= 1e-10f;
                if (!sameMaterialized && !sameSidePoint)
                    continue;

                existing.vertex.boundaryFlags |= vertex.boundaryFlags;
                if (existing.vertex.materializedIndex < 0 && vertex.materializedIndex >= 0)
                    existing.vertex.materializedIndex = vertex.materializedIndex;
                points[i] = existing;
                return;
            }

            points.Add(new SourceEdgePoint
            {
                vertex = vertex,
                t = t,
            });
        }

        private static bool TryComputeStructuredCellSideParameter(
            StructuredCellSideDefinition side,
            ClipVertex vertex,
            out float t)
        {
            t = 0f;
            Vector2 a = side.a;
            Vector2 b = side.b;
            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;
            if (lengthSq <= 1e-12f)
                return false;

            Vector2 point = new Vector2(vertex.s, vertex.u);
            if (DistanceSqPointToSegment(point, a, b) > 1e-8f)
                return false;

            t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSq);
            return true;
        }

        private static StructuredPointMergeResult AddStructuredSourceEdgePoint(
            Dictionary<ulong, List<SourceEdgePoint>> sourceEdgePoints,
            ulong key,
            int edgeA,
            int edgeB,
            ClipVertex vertex,
            MeshWorkData work)
        {
            if (sourceEdgePoints == null ||
                !TryComputeSourceEdgeParameter(vertex, work, edgeA, edgeB, out float t))
            {
                return StructuredPointMergeResult.Ignored;
            }

            if (!sourceEdgePoints.TryGetValue(key, out List<SourceEdgePoint> points))
            {
                points = new List<SourceEdgePoint>();
                sourceEdgePoints.Add(key, points);
            }

            const float tEpsilon = 1e-5f;
            for (int i = 0; i < points.Count; i++)
            {
                SourceEdgePoint existing = points[i];
                bool sameMaterialized =
                    existing.vertex.materializedIndex >= 0 &&
                    vertex.materializedIndex >= 0 &&
                    existing.vertex.materializedIndex == vertex.materializedIndex;
                bool samePosition =
                    Mathf.Abs(existing.t - t) <= tEpsilon &&
                    (existing.vertex.world - vertex.world).sqrMagnitude <= 1e-10f;
                if (!sameMaterialized && !samePosition)
                    continue;

                existing.vertex.boundaryFlags |= vertex.boundaryFlags;
                if (existing.vertex.materializedIndex < 0 && vertex.materializedIndex >= 0)
                    existing.vertex.materializedIndex = vertex.materializedIndex;
                points[i] = existing;
                return StructuredPointMergeResult.Reused;
            }

            points.Add(new SourceEdgePoint
            {
                vertex = vertex,
                t = Mathf.Clamp01(t),
            });
            return StructuredPointMergeResult.Added;
        }

        private static bool TryComputeSourceEdgeParameter(
            ClipVertex vertex,
            MeshWorkData work,
            int edgeA,
            int edgeB,
            out float t)
        {
            t = 0f;
            if (work == null ||
                edgeA < 0 ||
                edgeB < 0 ||
                edgeA >= work.vertices.Count ||
                edgeB >= work.vertices.Count ||
                edgeA == edgeB)
            {
                return false;
            }

            if (vertex.supportA == vertex.supportB)
            {
                if (vertex.supportA == edgeA)
                {
                    t = 0f;
                    return true;
                }

                if (vertex.supportA == edgeB)
                {
                    t = 1f;
                    return true;
                }
            }

            Vector3 start = work.vertices[edgeA];
            Vector3 end = work.vertices[edgeB];
            Vector3 edge = end - start;
            float lengthSq = edge.sqrMagnitude;
            if (lengthSq <= 1e-12f)
                return false;

            t = Mathf.Clamp01(Vector3.Dot(vertex.local - start, edge) / lengthSq);
            return true;
        }

        private static int AppendStructuredInteriorSourceEdgePoints(
            List<ClipVertex> output,
            List<SourceEdgePoint> points,
            float fromT,
            float toT,
            ClipBoundaryFlags hostBoundaryFlags)
        {
            if (output == null || points == null || points.Count == 0)
                return 0;

            const float tEpsilon = 1e-5f;
            int inserted = 0;
            if (fromT < toT)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t <= fromT + tEpsilon || point.t >= toT - tEpsilon)
                        continue;
                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, hostBoundaryFlags));
                    inserted++;
                }
                return inserted;
            }

            if (fromT > toT)
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t >= fromT - tEpsilon || point.t <= toT + tEpsilon)
                        continue;
                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, hostBoundaryFlags));
                    inserted++;
                }
            }

            return inserted;
        }

        private static int AppendStructuredProtectedInteriorSourceEdgePoints(
            List<ClipVertex> output,
            List<SourceEdgePoint> points,
            float fromT,
            float toT,
            ClipBoundaryFlags hostBoundaryFlags,
            StructuredBoundarySegmentKey key,
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints,
            bool preserveInsertedPointFlags)
        {
            if (output == null || points == null || points.Count == 0)
                return 0;

            const float tEpsilon = 1e-5f;
            int inserted = 0;
            if (fromT < toT)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t <= fromT + tEpsilon || point.t >= toT - tEpsilon)
                        continue;

                    ClipBoundaryFlags effectiveFlags = hostBoundaryFlags;
                    if (preserveInsertedPointFlags ||
                        IsStructuredCanonicalMandatoryBreakpoint(mandatoryBreakpoints, key, point.t))
                    {
                        effectiveFlags = point.vertex.boundaryFlags | hostBoundaryFlags;
                    }

                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, effectiveFlags));
                    inserted++;
                }

                return inserted;
            }

            if (fromT > toT)
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t >= fromT - tEpsilon || point.t <= toT + tEpsilon)
                        continue;

                    ClipBoundaryFlags effectiveFlags = hostBoundaryFlags;
                    if (preserveInsertedPointFlags ||
                        IsStructuredCanonicalMandatoryBreakpoint(mandatoryBreakpoints, key, point.t))
                    {
                        effectiveFlags = point.vertex.boundaryFlags | hostBoundaryFlags;
                    }

                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, effectiveFlags));
                    inserted++;
                }
            }

            return inserted;
        }

        private static void AddStructuredCanonicalMandatoryBreakpoint(
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints,
            StructuredBoundarySegmentKey key,
            float t)
        {
            if (mandatoryBreakpoints == null)
                return;

            mandatoryBreakpoints.Add(new StructuredCanonicalBreakpointKey
            {
                segmentKey = key,
                tQuantized = QuantizeStructuredInterfaceParam(t),
            });
        }

        private static int AppendStructuredCanonicalInteriorSourceEdgePoints(
            List<ClipVertex> output,
            List<SourceEdgePoint> points,
            float fromT,
            float toT,
            ClipBoundaryFlags hostBoundaryFlags,
            StructuredBoundarySegmentKey key,
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints)
        {
            if (output == null || points == null || points.Count == 0)
                return 0;

            const float tEpsilon = 1e-5f;
            int inserted = 0;
            if (fromT < toT)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t <= fromT + tEpsilon || point.t >= toT - tEpsilon)
                        continue;
                    AddStructuredCanonicalMandatoryBreakpoint(mandatoryBreakpoints, key, point.t);
                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, point.vertex.boundaryFlags | hostBoundaryFlags));
                    inserted++;
                }
                return inserted;
            }

            if (fromT > toT)
            {
                for (int i = points.Count - 1; i >= 0; i--)
                {
                    SourceEdgePoint point = points[i];
                    if (point.t >= fromT - tEpsilon || point.t <= toT + tEpsilon)
                        continue;
                    AddStructuredCanonicalMandatoryBreakpoint(mandatoryBreakpoints, key, point.t);
                    AppendStructuredNormalizedVertex(output, WithBoundaryFlags(point.vertex, point.vertex.boundaryFlags | hostBoundaryFlags));
                    inserted++;
                }
            }

            return inserted;
        }

        private static ClipVertex WithBoundaryFlags(ClipVertex vertex, ClipBoundaryFlags boundaryFlags)
        {
            vertex.boundaryFlags = boundaryFlags;
            return vertex;
        }

        private static void AppendStructuredNormalizedVertex(List<ClipVertex> output, ClipVertex vertex)
        {
            if (output == null)
                return;

            if (output.Count > 0 && AreStructuredVerticesEquivalent(output[output.Count - 1], vertex))
            {
                ClipVertex merged = output[output.Count - 1];
                merged.boundaryFlags |= vertex.boundaryFlags;
                if (merged.materializedIndex < 0 && vertex.materializedIndex >= 0)
                    merged.materializedIndex = vertex.materializedIndex;
                output[output.Count - 1] = merged;
                return;
            }

            output.Add(vertex);
        }

        private static void RemoveStructuredClosingDuplicate(List<ClipVertex> vertices)
        {
            if (vertices == null || vertices.Count <= 1)
                return;

            int lastIndex = vertices.Count - 1;
            if (!AreStructuredVerticesEquivalent(vertices[0], vertices[lastIndex]))
                return;

            ClipVertex first = vertices[0];
            first.boundaryFlags |= vertices[lastIndex].boundaryFlags;
            if (first.materializedIndex < 0 && vertices[lastIndex].materializedIndex >= 0)
                first.materializedIndex = vertices[lastIndex].materializedIndex;
            vertices[0] = first;
            vertices.RemoveAt(lastIndex);
        }

        private static bool IsStructuredCanonicalMandatoryBreakpoint(
            List<StructuredStripRow> rows,
            List<ClipVertex> polygon,
            int index,
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints)
        {
            if (rows == null ||
                polygon == null ||
                polygon.Count < 2 ||
                mandatoryBreakpoints == null ||
                mandatoryBreakpoints.Count == 0 ||
                index < 0 ||
                index >= polygon.Count)
            {
                return false;
            }

            int prevIndex = (index - 1 + polygon.Count) % polygon.Count;
            int nextIndex = (index + 1) % polygon.Count;
            ClipVertex prev = polygon[prevIndex];
            ClipVertex current = polygon[index];
            ClipVertex next = polygon[nextIndex];
            if (TryResolveStructuredBoundarySegmentForEdge(rows, prev, current, out StructuredBoundarySegmentKey prevKey, out _, out _, out float prevTb) &&
                mandatoryBreakpoints.Contains(new StructuredCanonicalBreakpointKey
                {
                    segmentKey = prevKey,
                    tQuantized = QuantizeStructuredInterfaceParam(prevTb),
                }))
            {
                return true;
            }

            if (TryResolveStructuredBoundarySegmentForEdge(rows, current, next, out StructuredBoundarySegmentKey nextKey, out _, out float nextTa, out _) &&
                mandatoryBreakpoints.Contains(new StructuredCanonicalBreakpointKey
                {
                    segmentKey = nextKey,
                    tQuantized = QuantizeStructuredInterfaceParam(nextTa),
                }))
            {
                return true;
            }

            return false;
        }

        private static bool IsStructuredCanonicalMandatoryBreakpoint(
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints,
            StructuredBoundarySegmentKey key,
            float t)
        {
            return mandatoryBreakpoints != null &&
                   mandatoryBreakpoints.Contains(new StructuredCanonicalBreakpointKey
                   {
                       segmentKey = key,
                       tQuantized = QuantizeStructuredInterfaceParam(t),
                   });
        }

        private static List<ClipVertex> CleanupStructuredCanonicalPolygon(
            List<StructuredStripRow> rows,
            List<ClipVertex> polygon,
            HashSet<StructuredCanonicalBreakpointKey> mandatoryBreakpoints)
        {
            var cleaned = new List<ClipVertex>();
            if (polygon == null || polygon.Count == 0)
                return cleaned;

            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = polygon[i];
                if (cleaned.Count > 0 && (cleaned[cleaned.Count - 1].world - current.world).sqrMagnitude <= 1e-12f)
                    continue;
                cleaned.Add(current);
            }

            if (cleaned.Count > 1 && (cleaned[0].world - cleaned[cleaned.Count - 1].world).sqrMagnitude <= 1e-12f)
                cleaned.RemoveAt(cleaned.Count - 1);

            bool preserveMandatory = rows != null && mandatoryBreakpoints != null && mandatoryBreakpoints.Count > 0;
            if (cleaned.Count < 3)
                return cleaned;

            bool changed = true;
            while (changed && cleaned.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < cleaned.Count; i++)
                {
                    int prevIndex = (i - 1 + cleaned.Count) % cleaned.Count;
                    int nextIndex = (i + 1) % cleaned.Count;
                    if (ShouldPreserveSemanticPolygonVertex(cleaned[prevIndex], cleaned[i], cleaned[nextIndex]) ||
                        (preserveMandatory && IsStructuredCanonicalMandatoryBreakpoint(rows, cleaned, i, mandatoryBreakpoints)))
                    {
                        continue;
                    }

                    if (IsNearlyCollinear(cleaned[prevIndex].world, cleaned[i].world, cleaned[nextIndex].world))
                    {
                        cleaned.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return cleaned;
        }

        private static bool AreStructuredVerticesEquivalent(ClipVertex a, ClipVertex b)
        {
            if (a.materializedIndex >= 0 && b.materializedIndex >= 0 && a.materializedIndex == b.materializedIndex)
                return true;

            return (a.world - b.world).sqrMagnitude <= 1e-12f;
        }

        private static void AppendStructuredBoundaryEdgeProvenance(
            ref BoundaryEdgeState state,
            StructuredCellFragment sourceFragment,
            int fragmentIndex,
            int edgeIndex,
            ClipVertex a,
            ClipVertex b,
            int topologyA,
            int topologyB)
        {
            if (state.provenanceCount >= 4)
                return;

            string kind = DescribeStructuredSourceEdgeKind(a, b);
            string entry =
                $"t{sourceFragment.sourceTriangleIndex}/p{sourceFragment.sourcePieceIndex}/f{fragmentIndex}/e{edgeIndex}:{kind} " +
                $"{DescribeStructuredSourceVertex(a, topologyA)}->{DescribeStructuredSourceVertex(b, topologyB)}";
            if (string.IsNullOrEmpty(state.provenance))
                state.provenance = entry;
            else
                state.provenance += " & " + entry;
            state.provenanceCount++;
        }

        private static string DescribeStructuredSourceEdgeKind(ClipVertex a, ClipVertex b)
        {
            if (TryResolveSupportEdge(a, b, out int edgeA, out int edgeB))
            {
                bool aOriginal = a.supportA >= 0 && a.supportA == a.supportB && a.materializedIndex == a.supportA;
                bool bOriginal = b.supportA >= 0 && b.supportA == b.supportB && b.materializedIndex == b.supportA;
                if (aOriginal && bOriginal)
                    return $"meshEdge({edgeA}-{edgeB})";
                return $"clipOnMeshEdge({edgeA}-{edgeB})";
            }

            return "structuredClip";
        }

        private static string DescribeStructuredSourceVertex(ClipVertex vertex, int topologyIndex)
        {
            string materialized = vertex.materializedIndex >= 0 ? $"m{vertex.materializedIndex}" : "m?";
            string support = vertex.supportA >= 0 || vertex.supportB >= 0 ? $"s{vertex.supportA}/{vertex.supportB}" : "s?";
            return $"{topologyIndex}@{materialized}:{support}:({vertex.s:F4},{vertex.u:F4})/{FormatClipBoundaryFlags(vertex.boundaryFlags)}";
        }

        private static int FindOrCreateStructuredCellTopologyVertex(
            Dictionary<int, ClipVertex> vertexMap,
            Dictionary<int, int> materializedToTopology,
            ClipVertex vertex,
            float tolerance)
        {
            if (vertex.materializedIndex >= 0 &&
                materializedToTopology != null &&
                materializedToTopology.TryGetValue(vertex.materializedIndex, out int existingTopologyIndex))
            {
                if (vertexMap.TryGetValue(existingTopologyIndex, out ClipVertex existingVertex))
                {
                    existingVertex.boundaryFlags |= vertex.boundaryFlags;
                    if (existingVertex.materializedIndex < 0)
                        existingVertex.materializedIndex = vertex.materializedIndex;
                    vertexMap[existingTopologyIndex] = existingVertex;
                }

                return existingTopologyIndex;
            }

            float toleranceSq = tolerance * tolerance;
            if (vertexMap != null)
            {
                foreach (var pair in vertexMap)
                {
                    ClipVertex existing = pair.Value;
                    float ds = Mathf.Abs(existing.s - vertex.s);
                    float du = Mathf.Abs(existing.u - vertex.u);
                    if (ds > tolerance || du > tolerance)
                    {
                        continue;
                    }

                    float worldDistanceSq = (existing.world - vertex.world).sqrMagnitude;
                    if (worldDistanceSq > toleranceSq)
                        continue;

                    existing.boundaryFlags |= vertex.boundaryFlags;
                    if (existing.materializedIndex < 0)
                        existing.materializedIndex = vertex.materializedIndex;
                    vertexMap[pair.Key] = existing;
                    if (vertex.materializedIndex >= 0 && materializedToTopology != null)
                        materializedToTopology[vertex.materializedIndex] = pair.Key;
                    return pair.Key;
                }
            }

            int topologyIndex = vertexMap != null ? vertexMap.Count : 0;
            if (vertexMap != null)
                vertexMap.Add(topologyIndex, vertex);
            if (vertex.materializedIndex >= 0 && materializedToTopology != null)
                materializedToTopology[vertex.materializedIndex] = topologyIndex;
            return topologyIndex;
        }

        private static bool TryTraceStructuredCellLoopVertices(
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            Dictionary<int, ClipVertex> vertexMap,
            out List<int> orderedVertexIndices)
        {
            orderedVertexIndices = new List<int>();
            if (componentVertices == null || componentVertices.Count < 3 || adjacency == null || vertexMap == null)
                return false;

            int start = -1;
            for (int i = 0; i < componentVertices.Count; i++)
            {
                int candidate = componentVertices[i];
                if (!adjacency.TryGetValue(candidate, out List<int> neighbors) || neighbors.Count != 2)
                    return false;
                if (!vertexMap.TryGetValue(candidate, out ClipVertex vertex))
                    return false;

                if (start < 0)
                {
                    start = candidate;
                    continue;
                }

                ClipVertex currentStart = vertexMap[start];
                if (vertex.s < currentStart.s - 1e-6f ||
                    (Mathf.Abs(vertex.s - currentStart.s) <= 1e-6f && vertex.u < currentStart.u))
                {
                    start = candidate;
                }
            }

            if (start < 0 || !adjacency.TryGetValue(start, out List<int> startNeighbors) || startNeighbors.Count != 2)
                return false;

            orderedVertexIndices.Add(start);
            int previous = start;
            int current = startNeighbors[0];
            int safety = componentVertices.Count + 1;
            while (safety-- > 0)
            {
                if (current == start)
                    break;

                orderedVertexIndices.Add(current);
                if (!adjacency.TryGetValue(current, out List<int> neighbors) || neighbors.Count != 2)
                    return false;

                int next = neighbors[0] == previous ? neighbors[1] : neighbors[0];
                previous = current;
                current = next;
            }

            if (current != start)
                return false;

            return orderedVertexIndices.Count == componentVertices.Count;
        }

        private static string BuildStructuredCellSemanticCoverage(
            List<ClipVertex> polygon,
            StructuredCellExpectedCorners expectedCorners,
            out bool hasStartSide,
            out bool hasEndSide,
            out bool hasLeftSide,
            out bool hasRightSide)
        {
            bool hasStartLeft = HasStructuredCellMaterializedVertex(polygon, expectedCorners.startLeft);
            bool hasStartRight = HasStructuredCellMaterializedVertex(polygon, expectedCorners.startRight);
            bool hasEndRight = HasStructuredCellMaterializedVertex(polygon, expectedCorners.endRight);
            bool hasEndLeft = HasStructuredCellMaterializedVertex(polygon, expectedCorners.endLeft);
            bool collapsedStart = expectedCorners.startLeft == expectedCorners.startRight;
            bool collapsedEnd = expectedCorners.endLeft == expectedCorners.endRight;
            hasStartSide = collapsedStart ? hasStartLeft : hasStartLeft && hasStartRight;
            hasEndSide = collapsedEnd ? hasEndLeft : hasEndRight && hasEndLeft;
            hasLeftSide = hasEndLeft && hasStartLeft;
            hasRightSide = hasStartRight && hasEndRight;
            return
                $"start={(hasStartSide ? 1 : 0)} end={(hasEndSide ? 1 : 0)} " +
                $"left={(hasLeftSide ? 1 : 0)} right={(hasRightSide ? 1 : 0)} " +
                $"corners=SL{(hasStartLeft ? 1 : 0)} SR{(hasStartRight ? 1 : 0)} ER{(hasEndRight ? 1 : 0)} EL{(hasEndLeft ? 1 : 0)}";
        }

        private static bool HasStructuredCellMaterializedVertex(List<ClipVertex> polygon, int materializedIndex)
        {
            if (polygon == null || materializedIndex < 0)
                return false;

            for (int i = 0; i < polygon.Count; i++)
            {
                if (polygon[i].materializedIndex == materializedIndex)
                    return true;
            }

            return false;
        }

        private static bool TryBuildStructuredCellPolygonFromSemanticChains(
            List<BoundaryEdgeState> boundaryEdges,
            Dictionary<int, ClipVertex> vertexMap,
            float rowStart,
            float rowEnd,
            float rowEpsilon,
            ClipBoundaryFlags leftFlags,
            ClipBoundaryFlags rightFlags,
            StructuredCellExpectedCorners expectedCorners,
            ref StructuredCellTopologyDiagnostics topologyDiagnostics,
            out List<ClipVertex> polygon,
            out string failReason)
        {
            polygon = new List<ClipVertex>();
            failReason = null;
            if (boundaryEdges == null || vertexMap == null)
            {
                failReason = "Structured cell semantic chain input is invalid.";
                return false;
            }

            if (!TryTraceStructuredCellBoundaryLoop(
                    boundaryEdges,
                    vertexMap,
                    out List<int> loop,
                    out failReason))
            {
                return false;
            }

            if (!TryResolveStructuredCellExpectedCornerVertices(
                    expectedCorners,
                    vertexMap,
                    out int startLeft,
                    out int startRight,
                    out int endRight,
                    out int endLeft,
                    out topologyDiagnostics.failedCornerResolutionSummary,
                    out failReason))
            {
                return false;
            }

            if (!TryAssembleStructuredCellLoopByExpectedCorners(
                    loop,
                    vertexMap,
                    startLeft,
                    startRight,
                    endRight,
                    endLeft,
                    out List<int> orderedIndices,
                    out topologyDiagnostics.failedSideTopologySummary,
                    out failReason))
            {
                return false;
            }

            if (orderedIndices.Count < 3)
            {
                failReason = "Structured cell semantic chain collapsed to fewer than three vertices.";
                return false;
            }

            var uniqueVertices = new HashSet<int>(orderedIndices);
            if (vertexMap.Count > 0 && uniqueVertices.Count < 3)
            {
                failReason = "Structured cell semantic chain did not span enough unique vertices.";
                return false;
            }

            polygon = new List<ClipVertex>(orderedIndices.Count);
            for (int i = 0; i < orderedIndices.Count; i++)
            {
                if (!vertexMap.TryGetValue(orderedIndices[i], out ClipVertex vertex))
                {
                    failReason = $"Structured cell semantic chain lost vertex {orderedIndices[i]}.";
                    return false;
                }

                polygon.Add(vertex);
            }

            return true;
        }

        private static bool TryTraceStructuredCellBoundaryLoop(
            List<BoundaryEdgeState> boundaryEdges,
            Dictionary<int, ClipVertex> vertexMap,
            out List<int> loop,
            out string failReason)
        {
            loop = new List<int>();
            failReason = null;
            if (boundaryEdges == null || vertexMap == null)
            {
                failReason = "Structured cell boundary loop input is invalid.";
                return false;
            }

            List<List<int>> components = BuildBoundaryComponents(boundaryEdges);
            if (components.Count != 1)
            {
                failReason = $"Structured cell boundary loop expected one component, but found {components.Count}.";
                return false;
            }

            Dictionary<int, List<int>> adjacency = BuildBoundaryAdjacency(components[0], boundaryEdges);
            CountBoundaryVertexDegrees(
                components[0],
                adjacency,
                out int degree1Count,
                out int degree2Count,
                out int branchCount);
            if (degree1Count != 0 || branchCount != 0 || degree2Count != components[0].Count)
            {
                failReason =
                    $"Structured cell boundary loop is not simple. deg=({degree1Count},{degree2Count},{branchCount})";
                return false;
            }

            if (!TryTraceStructuredCellLoopVertices(components[0], adjacency, vertexMap, out loop))
            {
                failReason = "Structured cell boundary loop tracing failed.";
                return false;
            }

            return true;
        }

        private static bool TryResolveStructuredCellExpectedCornerVertices(
            StructuredCellExpectedCorners expectedCorners,
            Dictionary<int, ClipVertex> vertexMap,
            out int startLeft,
            out int startRight,
            out int endRight,
            out int endLeft,
            out string cornerSummary,
            out string failReason)
        {
            startLeft = -1;
            startRight = -1;
            endRight = -1;
            endLeft = -1;
            cornerSummary = "none";
            failReason = null;

            bool ok = true;
            var summaries = new List<string>(4);
            ok &= TryResolveStructuredCellExpectedCornerVertex(
                "SL",
                expectedCorners.startLeft,
                vertexMap,
                out startLeft,
                out string startLeftSummary);
            summaries.Add(startLeftSummary);
            ok &= TryResolveStructuredCellExpectedCornerVertex(
                "SR",
                expectedCorners.startRight,
                vertexMap,
                out startRight,
                out string startRightSummary);
            summaries.Add(startRightSummary);
            ok &= TryResolveStructuredCellExpectedCornerVertex(
                "ER",
                expectedCorners.endRight,
                vertexMap,
                out endRight,
                out string endRightSummary);
            summaries.Add(endRightSummary);
            ok &= TryResolveStructuredCellExpectedCornerVertex(
                "EL",
                expectedCorners.endLeft,
                vertexMap,
                out endLeft,
                out string endLeftSummary);
            summaries.Add(endLeftSummary);

            cornerSummary = string.Join(" ; ", summaries.ToArray());
            if (!ok)
            {
                failReason = $"Expected cell corner identity was not resolved. {cornerSummary}";
                return false;
            }

            return true;
        }

        private static bool TryResolveStructuredCellExpectedCornerVertex(
            string label,
            int materializedIndex,
            Dictionary<int, ClipVertex> vertexMap,
            out int topologyVertex,
            out string summary)
        {
            topologyVertex = -1;
            if (materializedIndex < 0 || vertexMap == null)
            {
                summary = $"{label}=invalid(m{materializedIndex})";
                return false;
            }

            int matchCount = 0;
            foreach (var pair in vertexMap)
            {
                if (pair.Value.materializedIndex != materializedIndex)
                    continue;

                topologyVertex = pair.Key;
                matchCount++;
            }

            string hit = matchCount == 1
                ? FormatStructuredCellVertex(topologyVertex, vertexMap)
                : "none";
            summary = $"{label}=m{materializedIndex} hits={matchCount} hit=({hit})";
            return matchCount == 1;
        }

        private static bool TryAssembleStructuredCellLoopByExpectedCorners(
            List<int> loop,
            Dictionary<int, ClipVertex> vertexMap,
            int startLeft,
            int startRight,
            int endRight,
            int endLeft,
            out List<int> orderedIndices,
            out string sideSummary,
            out string failReason)
        {
            orderedIndices = new List<int>();
            sideSummary = "none";
            failReason = null;

            if (TryAssembleStructuredCellLoopByExpectedCorners(
                    loop,
                    vertexMap,
                    startLeft,
                    startRight,
                    endRight,
                    endLeft,
                    1,
                    out orderedIndices,
                    out sideSummary,
                    out failReason))
            {
                return true;
            }

            string forwardReason = failReason;
            string forwardSummary = sideSummary;
            if (TryAssembleStructuredCellLoopByExpectedCorners(
                    loop,
                    vertexMap,
                    startLeft,
                    startRight,
                    endRight,
                    endLeft,
                    -1,
                    out orderedIndices,
                    out sideSummary,
                    out failReason))
            {
                return true;
            }

            string reverseReason = failReason;
            string reverseSummary = sideSummary;
            sideSummary = !string.IsNullOrEmpty(forwardSummary) && forwardSummary != "none"
                ? forwardSummary
                : reverseSummary;
            failReason =
                $"Expected cell corners are not cyclic on the traced loop. forward={forwardReason}; reverse={reverseReason}";
            return false;
        }

        private static bool TryAssembleStructuredCellLoopByExpectedCorners(
            List<int> loop,
            Dictionary<int, ClipVertex> vertexMap,
            int startLeft,
            int startRight,
            int endRight,
            int endLeft,
            int direction,
            out List<int> orderedIndices,
            out string sideSummary,
            out string failReason)
        {
            orderedIndices = new List<int>();
            sideSummary = "none";
            failReason = null;
            if (loop == null || loop.Count < 3)
            {
                failReason = "Traced cell loop has fewer than three vertices.";
                return false;
            }

            var positions = new Dictionary<int, int>(loop.Count);
            for (int i = 0; i < loop.Count; i++)
            {
                if (!positions.ContainsKey(loop[i]))
                    positions.Add(loop[i], i);
            }

            bool collapsedStart = startLeft == startRight;
            bool collapsedEnd = endRight == endLeft;
            var summaries = new List<string>(4);
            var chains = new List<List<int>>(4);
            if (!TryAppendStructuredCellCornerArc(
                    StructuredCellSideKind.Start,
                    loop,
                    positions,
                    vertexMap,
                    startLeft,
                    startRight,
                    direction,
                    collapsedStart,
                    chains,
                    summaries,
                    out failReason))
            {
                sideSummary = string.Join(" ; ", summaries.ToArray());
                return false;
            }

            if (!TryAppendStructuredCellCornerArc(
                    StructuredCellSideKind.Right,
                    loop,
                    positions,
                    vertexMap,
                    startRight,
                    endRight,
                    direction,
                    false,
                    chains,
                    summaries,
                    out failReason))
            {
                sideSummary = string.Join(" ; ", summaries.ToArray());
                return false;
            }

            if (!TryAppendStructuredCellCornerArc(
                    StructuredCellSideKind.End,
                    loop,
                    positions,
                    vertexMap,
                    endRight,
                    endLeft,
                    direction,
                    collapsedEnd,
                    chains,
                    summaries,
                    out failReason))
            {
                sideSummary = string.Join(" ; ", summaries.ToArray());
                return false;
            }

            if (!TryAppendStructuredCellCornerArc(
                    StructuredCellSideKind.Left,
                    loop,
                    positions,
                    vertexMap,
                    endLeft,
                    startLeft,
                    direction,
                    false,
                    chains,
                    summaries,
                    out failReason))
            {
                sideSummary = string.Join(" ; ", summaries.ToArray());
                return false;
            }

            for (int i = 0; i < chains.Count; i++)
                AppendStructuredCellChain(orderedIndices, chains[i]);
            NormalizeStructuredCellOrderedIndices(orderedIndices);

            var unique = new HashSet<int>();
            for (int i = 0; i < orderedIndices.Count; i++)
                unique.Add(orderedIndices[i]);

            sideSummary = string.Join(" ; ", summaries.ToArray());
            if (orderedIndices.Count != loop.Count || unique.Count != loop.Count)
            {
                failReason =
                    $"Expected corner order revisits or skips loop vertices. ordered={orderedIndices.Count}/{loop.Count} unique={unique.Count}/{loop.Count}";
                return false;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                if (!unique.Contains(loop[i]))
                {
                    failReason = $"Expected corner order skipped loop vertex {FormatStructuredCellVertex(loop[i], vertexMap)}.";
                    return false;
                }
            }

            return true;
        }

        private static bool TryAppendStructuredCellCornerArc(
            StructuredCellSideKind sideKind,
            List<int> loop,
            Dictionary<int, int> positions,
            Dictionary<int, ClipVertex> vertexMap,
            int startVertex,
            int endVertex,
            int direction,
            bool allowCollapsed,
            List<List<int>> chains,
            List<string> summaries,
            out string failReason)
        {
            failReason = null;
            if (allowCollapsed && startVertex == endVertex)
            {
                var collapsedChain = new List<int> { startVertex };
                chains.Add(collapsedChain);
                summaries.Add(DescribeStructuredCellSideArc(sideKind, collapsedChain, vertexMap, true));
                return true;
            }

            if (startVertex == endVertex)
            {
                failReason = $"{sideKind} side has identical non-collapsed corners.";
                summaries.Add($"{sideKind}=invalid-same-corner");
                return false;
            }

            if (!TryExtractStructuredCellLoopArc(
                    loop,
                    positions,
                    startVertex,
                    endVertex,
                    direction,
                    out List<int> chain))
            {
                failReason =
                    $"{sideKind} side could not trace from {FormatStructuredCellVertex(startVertex, vertexMap)} to {FormatStructuredCellVertex(endVertex, vertexMap)} on the cell loop.";
                summaries.Add($"{sideKind}=missing");
                return false;
            }

            chains.Add(chain);
            summaries.Add(DescribeStructuredCellSideArc(sideKind, chain, vertexMap, false));
            return true;
        }

        private static bool TryExtractStructuredCellLoopArc(
            List<int> loop,
            Dictionary<int, int> positions,
            int startVertex,
            int endVertex,
            int direction,
            out List<int> chain)
        {
            chain = new List<int>();
            if (loop == null || loop.Count == 0 || positions == null)
                return false;
            if (!positions.TryGetValue(startVertex, out int current) ||
                !positions.TryGetValue(endVertex, out int target))
            {
                return false;
            }

            int step = direction >= 0 ? 1 : -1;
            int safety = loop.Count + 1;
            while (safety-- > 0)
            {
                chain.Add(loop[current]);
                if (current == target)
                    return chain.Count >= 2;

                current += step;
                if (current < 0)
                    current = loop.Count - 1;
                else if (current >= loop.Count)
                    current = 0;
            }

            return false;
        }

        private static string DescribeStructuredCellSideArc(
            StructuredCellSideKind sideKind,
            List<int> chain,
            Dictionary<int, ClipVertex> vertexMap,
            bool collapsed)
        {
            if (collapsed)
            {
                int vertex = chain != null && chain.Count > 0 ? chain[0] : -1;
                return $"{sideKind}=collapsed@{FormatStructuredCellVertex(vertex, vertexMap)}";
            }

            int vertexCount = chain != null ? chain.Count : 0;
            int edgeCount = Mathf.Max(0, vertexCount - 1);
            int start = vertexCount > 0 ? chain[0] : -1;
            int end = vertexCount > 0 ? chain[vertexCount - 1] : -1;
            return $"{sideKind}=arc/e{edgeCount}/v{vertexCount}/ep=({FormatStructuredCellVertex(start, vertexMap)},{FormatStructuredCellVertex(end, vertexMap)})";
        }

        private static string DescribeStructuredCellOpenEndpoints(
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            Dictionary<int, ClipVertex> vertexMap)
        {
            if (componentVertices == null || adjacency == null)
                return "none";

            var endpoints = new List<string>(4);
            for (int i = 0; i < componentVertices.Count; i++)
            {
                int vertexIndex = componentVertices[i];
                int degree = adjacency.TryGetValue(vertexIndex, out List<int> neighbors) ? neighbors.Count : 0;
                if (degree <= 1)
                    endpoints.Add($"{FormatStructuredCellVertex(vertexIndex, vertexMap)}:deg{degree}");
            }

            return endpoints.Count == 0 ? "none" : string.Join(" ; ", endpoints.ToArray());
        }

        private static string FormatStructuredCellVertex(int vertexIndex, Dictionary<int, ClipVertex> vertexMap)
        {
            if (vertexIndex < 0 || vertexMap == null || !vertexMap.TryGetValue(vertexIndex, out ClipVertex vertex))
                return $"{vertexIndex}@missing";

            string materialized = vertex.materializedIndex >= 0 ? $"m{vertex.materializedIndex}:" : string.Empty;
            return $"{vertexIndex}@{materialized}({vertex.s:F4},{vertex.u:F4})/{FormatClipBoundaryFlags(vertex.boundaryFlags)}";
        }

        private static string FormatClipBoundaryFlags(ClipBoundaryFlags flags)
        {
            if (flags == ClipBoundaryFlags.None)
                return "none";

            var parts = new List<string>(7);
            if ((flags & ClipBoundaryFlags.Start) != 0)
                parts.Add("S");
            if ((flags & ClipBoundaryFlags.End) != 0)
                parts.Add("E");
            if ((flags & ClipBoundaryFlags.Row) != 0)
                parts.Add("R");
            if ((flags & ClipBoundaryFlags.Left) != 0)
                parts.Add("L");
            if ((flags & ClipBoundaryFlags.Right) != 0)
                parts.Add("Rt");
            if ((flags & ClipBoundaryFlags.LeftSupport) != 0)
                parts.Add("LS");
            if ((flags & ClipBoundaryFlags.RightSupport) != 0)
                parts.Add("RS");
            return string.Join("|", parts.ToArray());
        }

        private static string BuildStructuredRowDefinitionSummary(
            List<StructuredStripRow> rows,
            List<FrameSample> projectionFrames,
            out int mismatchCount)
        {
            mismatchCount = 0;
            if (rows == null || rows.Count == 0 || projectionFrames == null || projectionFrames.Count < 2)
                return "none";

            float maxAbsDs = 0f;
            float maxAbsDu = 0f;
            var preview = new List<string>(4);
            string[] labels = { "LS", "LB", "RB", "RS" };
            for (int i = 0; i < rows.Count; i++)
            {
                StructuredStripRow row = rows[i];
                BoundaryVertexSample[] samples =
                {
                    row.leftSupport,
                    row.leftBoundary,
                    row.rightBoundary,
                    row.rightSupport,
                };
                Vector2[] canonical =
                {
                    BuildStructuredStripCoord(row.frame, row.leftSupport),
                    BuildStructuredStripCoord(row.frame, row.leftBoundary),
                    BuildStructuredStripCoord(row.frame, row.rightBoundary),
                    BuildStructuredStripCoord(row.frame, row.rightSupport),
                };
                Vector2[] projected = new Vector2[4];
                for (int sampleIndex = 0; sampleIndex < samples.Length; sampleIndex++)
                {
                    projected[sampleIndex] = ComputeProjectedStripCoord(samples[sampleIndex].world, projectionFrames);
                    Vector2 delta = projected[sampleIndex] - canonical[sampleIndex];
                    maxAbsDs = Mathf.Max(maxAbsDs, Mathf.Abs(delta.x));
                    maxAbsDu = Mathf.Max(maxAbsDu, Mathf.Abs(delta.y));

                    int nearest = FindNearestStructuredCoordIndex(projected[sampleIndex], canonical);
                    float assignedDistSq = (projected[sampleIndex] - canonical[sampleIndex]).sqrMagnitude;
                    float nearestDistSq = (projected[sampleIndex] - canonical[nearest]).sqrMagnitude;
                    if (nearest != sampleIndex && nearestDistSq + 1e-8f < assignedDistSq - 1e-8f)
                    {
                        mismatchCount++;
                        if (preview.Count < 4)
                        {
                            preview.Add(
                                $"r{row.frameIndex}:{labels[sampleIndex]} v={samples[sampleIndex].vertexIndex} " +
                                $"c=({canonical[sampleIndex].x:F4},{canonical[sampleIndex].y:F4}) " +
                                $"p=({projected[sampleIndex].x:F4},{projected[sampleIndex].y:F4}) " +
                                $"d=({delta.x:F4},{delta.y:F4}) near={labels[nearest]}");
                        }
                    }
                }

                if (projected[0].y > projected[1].y + 1e-5f ||
                    projected[1].y > projected[2].y + 1e-5f ||
                    projected[2].y > projected[3].y + 1e-5f)
                {
                    mismatchCount++;
                    if (preview.Count < 4)
                    {
                        preview.Add(
                            $"r{row.frameIndex}:ord pU=({projected[0].y:F4},{projected[1].y:F4},{projected[2].y:F4},{projected[3].y:F4})");
                    }
                }
            }

            string previewText = preview.Count > 0 ? string.Join(" ; ", preview.ToArray()) : "none";
            return $"max|d|=(s={maxAbsDs:F4},u={maxAbsDu:F4}) preview={previewText}";
        }

        private static bool TryBuildStructuredCellDefinitionSummary(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            List<FrameSample> projectionFrames,
            out string summary,
            out int mismatchCount)
        {
            summary = "none";
            mismatchCount = 0;
            if (!TryGetStructuredCellCornerSamples(startRow, endRow, columnIndex, out BoundaryVertexSample[] samples))
                return false;

            Vector2[] canonical =
            {
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 0),
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 1),
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 2),
                GetStructuredCellCornerCoord(startRow, endRow, columnIndex, 3),
            };

            string[] cornerLabels = { "SL", "SR", "ER", "EL" };
            var entries = new List<string>(4);
            for (int i = 0; i < samples.Length; i++)
            {
                Vector2 projected = ComputeProjectedStripCoord(samples[i].world, projectionFrames);
                Vector2 delta = projected - canonical[i];
                int nearest = FindNearestStructuredCoordIndex(projected, canonical);
                float assignedDistSq = (projected - canonical[i]).sqrMagnitude;
                float nearestDistSq = (projected - canonical[nearest]).sqrMagnitude;
                bool mismatch = nearest != i && nearestDistSq + 1e-8f < assignedDistSq - 1e-8f;
                if (mismatch)
                    mismatchCount++;

                entries.Add(
                    $"{cornerLabels[i]} sample={samples[i].vertexIndex} m={samples[i].vertexIndex} exp={GetStructuredCellCornerSemanticLabel(columnIndex, i)} " +
                    $"c=({canonical[i].x:F4},{canonical[i].y:F4}) p=({projected.x:F4},{projected.y:F4}) " +
                    $"d=({delta.x:F4},{delta.y:F4}) near={cornerLabels[nearest]}");
            }

            for (int i = 0; i < samples.Length; i++)
            {
                for (int j = i + 1; j < samples.Length; j++)
                {
                    if (samples[i].vertexIndex < 0 || samples[i].vertexIndex != samples[j].vertexIndex)
                        continue;
                    if ((canonical[i] - canonical[j]).sqrMagnitude <= 1e-10f)
                        continue;
                    mismatchCount++;
                    entries.Add($"dup {cornerLabels[i]}/{cornerLabels[j]} v={samples[i].vertexIndex}");
                }
            }

            summary = string.Join(" ; ", entries.ToArray());
            return true;
        }

        private static bool TryGetStructuredCellCornerSamples(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            out BoundaryVertexSample[] samples)
        {
            samples = null;
            switch (columnIndex)
            {
                case 0:
                    samples = new[]
                    {
                        startRow.leftSupport,
                        startRow.leftBoundary,
                        endRow.leftBoundary,
                        endRow.leftSupport,
                    };
                    return true;
                case 1:
                    samples = new[]
                    {
                        startRow.leftBoundary,
                        startRow.rightBoundary,
                        endRow.rightBoundary,
                        endRow.leftBoundary,
                    };
                    return true;
                case 2:
                    samples = new[]
                    {
                        startRow.rightBoundary,
                        startRow.rightSupport,
                        endRow.rightSupport,
                        endRow.rightBoundary,
                    };
                    return true;
                default:
                    return false;
            }
        }

        private static Vector2 GetStructuredCellCornerCoord(
            StructuredStripRow startRow,
            StructuredStripRow endRow,
            int columnIndex,
            int cornerIndex)
        {
            switch (columnIndex)
            {
                case 0:
                    switch (cornerIndex)
                    {
                        case 0: return BuildStructuredStripCoord(startRow.frame, startRow.leftSupport);
                        case 1: return BuildStructuredStripCoord(startRow.frame, startRow.leftBoundary);
                        case 2: return BuildStructuredStripCoord(endRow.frame, endRow.leftBoundary);
                        case 3: return BuildStructuredStripCoord(endRow.frame, endRow.leftSupport);
                    }
                    break;
                case 1:
                    switch (cornerIndex)
                    {
                        case 0: return BuildStructuredStripCoord(startRow.frame, startRow.leftBoundary);
                        case 1: return BuildStructuredStripCoord(startRow.frame, startRow.rightBoundary);
                        case 2: return BuildStructuredStripCoord(endRow.frame, endRow.rightBoundary);
                        case 3: return BuildStructuredStripCoord(endRow.frame, endRow.leftBoundary);
                    }
                    break;
                case 2:
                    switch (cornerIndex)
                    {
                        case 0: return BuildStructuredStripCoord(startRow.frame, startRow.rightBoundary);
                        case 1: return BuildStructuredStripCoord(startRow.frame, startRow.rightSupport);
                        case 2: return BuildStructuredStripCoord(endRow.frame, endRow.rightSupport);
                        case 3: return BuildStructuredStripCoord(endRow.frame, endRow.rightBoundary);
                    }
                    break;
            }

            return Vector2.zero;
        }

        private static string GetStructuredCellCornerSemanticLabel(int columnIndex, int cornerIndex)
        {
            ClipBoundaryFlags flags = GetStructuredCellCornerExpectedFlags(columnIndex, cornerIndex);
            return FormatClipBoundaryFlags(flags);
        }

        private static ClipBoundaryFlags GetStructuredCellCornerExpectedFlags(int columnIndex, int cornerIndex)
        {
            ClipBoundaryFlags rowFlags = cornerIndex <= 1 ? ClipBoundaryFlags.Start : ClipBoundaryFlags.End;
            switch (columnIndex)
            {
                case 0:
                    return rowFlags | (cornerIndex == 0 || cornerIndex == 3 ? ClipBoundaryFlags.LeftSupport : ClipBoundaryFlags.Left);
                case 1:
                    return rowFlags | (cornerIndex == 0 || cornerIndex == 3 ? ClipBoundaryFlags.Left : ClipBoundaryFlags.Right);
                case 2:
                    return rowFlags | (cornerIndex == 0 || cornerIndex == 3 ? ClipBoundaryFlags.Right : ClipBoundaryFlags.RightSupport);
                default:
                    return rowFlags;
            }
        }

        private static Vector2 ComputeProjectedStripCoord(Vector3 world, List<FrameSample> projectionFrames)
        {
            SurfaceCoord coord = EvaluateSurfaceCoord(world, projectionFrames);
            return new Vector2(coord.s, coord.u);
        }

        private static int FindNearestStructuredCoordIndex(Vector2 point, Vector2[] targets)
        {
            int best = 0;
            float bestDistSq = float.PositiveInfinity;
            for (int i = 0; i < targets.Length; i++)
            {
                float distSq = (point - targets[i]).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = i;
                }
            }

            return best;
        }

        private static string BuildStructuredCellGraphSummary(
            List<StructuredCellFragment> fragments,
            string edgeMultiplicitySummary,
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            Dictionary<int, ClipVertex> vertexMap,
            List<BoundaryEdgeState> boundaryEdges)
        {
            string fragmentSummary = BuildStructuredCellFragmentSummary(fragments);
            string branchSummary = DescribeStructuredCellBranchVertices(componentVertices, adjacency, vertexMap, boundaryEdges);
            return $"frag={fragmentSummary} mult={edgeMultiplicitySummary} branch={branchSummary}";
        }

        private static string BuildStructuredCellFragmentSummary(List<StructuredCellFragment> fragments)
        {
            if (fragments == null || fragments.Count == 0)
                return "none";

            var counts = new List<string>(6);
            for (int i = 0; i < fragments.Count && i < 6; i++)
            {
                StructuredCellFragment fragment = fragments[i];
                int vertexCount = fragment.vertices != null ? fragment.vertices.Count : 0;
                counts.Add($"t{fragment.sourceTriangleIndex}p{fragment.sourcePieceIndex}:{vertexCount}");
            }
            return $"n={fragments.Count} verts={string.Join("/", counts.ToArray())}";
        }

        private static string DescribeStructuredCellBranchVertices(
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            Dictionary<int, ClipVertex> vertexMap,
            List<BoundaryEdgeState> boundaryEdges)
        {
            if (componentVertices == null || adjacency == null || vertexMap == null)
                return "none";

            var entries = new List<string>(4);
            for (int i = 0; i < componentVertices.Count; i++)
            {
                int vertexIndex = componentVertices[i];
                int degree = adjacency.TryGetValue(vertexIndex, out List<int> neighbors) ? neighbors.Count : 0;
                if (degree == 2)
                    continue;

                string neighborSummary = neighbors != null && neighbors.Count > 0
                    ? string.Join(",", neighbors.ConvertAll(v => FormatStructuredCellVertex(v, vertexMap)).ToArray())
                    : "none";
                string incidentSummary = DescribeStructuredBranchIncidentEdges(vertexIndex, boundaryEdges, vertexMap);
                entries.Add($"{FormatStructuredCellVertex(vertexIndex, vertexMap)}:deg{degree}->({neighborSummary}) inc=({incidentSummary})");
                if (entries.Count >= 4)
                    break;
            }

            return entries.Count == 0 ? "none" : string.Join(" ; ", entries.ToArray());
        }

        private static string DescribeStructuredBranchIncidentEdges(
            int vertexIndex,
            List<BoundaryEdgeState> boundaryEdges,
            Dictionary<int, ClipVertex> vertexMap)
        {
            if (boundaryEdges == null || boundaryEdges.Count == 0)
                return "none";

            var entries = new List<string>(6);
            for (int i = 0; i < boundaryEdges.Count; i++)
            {
                BoundaryEdgeState edge = boundaryEdges[i];
                if (edge.a != vertexIndex && edge.b != vertexIndex)
                    continue;

                int other = edge.a == vertexIndex ? edge.b : edge.a;
                string provenance = string.IsNullOrEmpty(edge.provenance) ? "src=unknown" : edge.provenance;
                entries.Add($"{FormatStructuredCellVertex(other, vertexMap)}[{provenance}]");
                if (entries.Count >= 6)
                    break;
            }

            return entries.Count == 0 ? "none" : string.Join(" | ", entries.ToArray());
        }

        private static string DescribeStructuredBoundaryEdgeMultiplicity(
            Dictionary<ulong, BoundaryEdgeState> edgeStates,
            Dictionary<int, ClipVertex> vertexMap)
        {
            if (edgeStates == null || edgeStates.Count == 0)
                return "none";

            int k1 = 0;
            int k2 = 0;
            int k3 = 0;
            var preview = new List<string>(4);
            foreach (var pair in edgeStates)
            {
                BoundaryEdgeState state = pair.Value;
                if (state.keptCount <= 1)
                {
                    k1++;
                    continue;
                }

                if (state.keptCount == 2)
                {
                    k2++;
                    continue;
                }

                k3++;
                if (preview.Count < 4)
                    preview.Add($"{FormatStructuredCellVertex(state.a, vertexMap)}<->{FormatStructuredCellVertex(state.b, vertexMap)}x{state.keptCount}");
            }

            string previewText = preview.Count > 0 ? string.Join(" ; ", preview.ToArray()) : "none";
            return $"k1={k1} k2={k2} k3+={k3} preview={previewText}";
        }

        private static string BuildStructuredInterfaceSummary(
            List<List<ClipVertex>> outsidePolygons,
            List<List<ClipVertex>> supportPolygons,
            List<List<ClipVertex>> excisionPolygons,
            out int mismatchCount,
            List<Vector3> mismatchEdgeWorldPairs = null,
            List<string> mismatchEdgeDebugLabels = null)
        {
            return BuildStructuredInterfaceSummary(
                null,
                outsidePolygons,
                supportPolygons,
                excisionPolygons,
                out mismatchCount,
                out _,
                mismatchEdgeWorldPairs,
                mismatchEdgeDebugLabels);
        }

        private static string BuildStructuredInterfaceSummary(
            List<StructuredStripRow> rows,
            List<List<ClipVertex>> outsidePolygons,
            List<List<ClipVertex>> supportPolygons,
            List<List<ClipVertex>> excisionPolygons,
            out int mismatchCount,
            out StructuredInterfaceScanDiagnostics scan,
            List<Vector3> mismatchEdgeWorldPairs = null,
            List<string> mismatchEdgeDebugLabels = null)
        {
            mismatchCount = 0;
            scan = default;
            if (mismatchEdgeWorldPairs != null)
                mismatchEdgeWorldPairs.Clear();
            if (mismatchEdgeDebugLabels != null)
                mismatchEdgeDebugLabels.Clear();
            var edgeMap = new Dictionary<ulong, InterfaceEdgeAccumulator>();
            var structuredEdgeMap = new Dictionary<StructuredResolvedInterfaceSubsegmentKey, InterfaceEdgeAccumulator>();
            AccumulateStructuredInterfaceEdges(rows, outsidePolygons, PartitionRegionKind.Outside, edgeMap, structuredEdgeMap);
            AccumulateStructuredInterfaceEdges(rows, supportPolygons, PartitionRegionKind.Support, edgeMap, structuredEdgeMap);
            AccumulateStructuredInterfaceEdges(rows, excisionPolygons, PartitionRegionKind.Excision, edgeMap, structuredEdgeMap);
            List<InterfaceEdgeAccumulator> accumulatedEdges = ConsolidateStructuredInterfaceAccumulators(rows, edgeMap);
            foreach (var pair in structuredEdgeMap)
                accumulatedEdges.Add(pair.Value);

            int closingTipOwnerRepairCount = RepairClosingTipInterfaceOwnership(rows, accumulatedEdges);
            scan.closingTipOwnerRepairCount = closingTipOwnerRepairCount;

            var mismatchFlagCounts = new Dictionary<ClipBoundaryFlags, int>();
            var mismatchRowCounts = new Dictionary<int, int>();
            var tipInterfaceBuckets = new Dictionary<StructuredTipInterfaceBucketKey, StructuredTipInterfaceBucketStats>();
            var unresolvedTipBucketCounts = new Dictionary<int, int>();
            var preview = new List<string>(4);
            var detail = new List<string>(24);
            int missingOutsideOnOutsideSupport = 0;
            int missingSupportOnOutsideSupport = 0;
            int missingSupportOnSupportExcision = 0;
            int missingExcisionOnSupportExcision = 0;
            int unexpectedOutsideOnSupportExcision = 0;
            int unexpectedExcisionOnOutsideSupport = 0;
            int ambiguousOwnership = 0;
            int sxLbCount = 0;
            int sxRbCount = 0;
            int osLsCount = 0;
            int osRsCount = 0;
            int startCount = 0;
            int endCount = 0;
            int tipCount = 0;
            int otherCategoryCount = 0;
            int actualNoneCount = 0;
            int actualOCount = 0;
            int actualSCount = 0;
            int actualXCount = 0;
            int actualOSCount = 0;
            int actualSXCount = 0;
            int actualOXCount = 0;
            int actualOSXCount = 0;
            for (int stateIndex = 0; stateIndex < accumulatedEdges.Count; stateIndex++)
            {
                InterfaceEdgeAccumulator state = accumulatedEdges[stateIndex];
                bool hasOutside = state.usePresenceOwnership ? state.outsideCount > 0 : (state.outsideCount & 1) != 0;
                bool hasSupport = state.usePresenceOwnership ? state.supportCount > 0 : (state.supportCount & 1) != 0;
                bool hasExcision = state.usePresenceOwnership ? state.excisionCount > 0 : (state.excisionCount & 1) != 0;
                bool rawMultiUse = state.outsideCount > 2 || state.supportCount > 2 || state.excisionCount > 2;
                bool interfaceFlag = IsStructuredRegionInterfaceFlag(state.flags);
                if (!hasOutside && !hasSupport && !hasExcision)
                {
                    scan.canceledInternalCount++;
                    if (rawMultiUse && interfaceFlag)
                        scan.multiUseCount++;
                    continue;
                }

                if (ShouldIgnoreStructuredDegenerateInterfaceAccumulator(state))
                {
                    scan.canceledInternalCount++;
                    continue;
                }

                if (interfaceFlag)
                    scan.segmentCount++;
                if (rawMultiUse && interfaceFlag)
                    scan.multiUseCount++;
                if (hasOutside)
                    scan.outsideEdgeCount++;
                if (hasSupport)
                    scan.supportEdgeCount++;
                if (hasExcision)
                    scan.excisionEdgeCount++;

                int regionCount = (hasOutside ? 1 : 0) + (hasSupport ? 1 : 0) + (hasExcision ? 1 : 0);
                if (TryGetStructuredInterfaceOwners(rows, state.rowIndex, state.flags, out bool expectOutside, out bool expectSupport, out bool expectExcision))
                {
                    bool valid = hasOutside == expectOutside &&
                        hasSupport == expectSupport &&
                        hasExcision == expectExcision;
                    if (valid)
                    {
                        if (expectOutside)
                            scan.matchedOutsideSupportCount++;
                        else
                            scan.matchedSupportExcisionCount++;
                        continue;
                    }

                    if (interfaceFlag)
                    {
                        if (expectOutside)
                        {
                            if (!hasOutside && hasSupport && !hasExcision)
                                missingOutsideOnOutsideSupport++;
                            if (hasOutside && !hasSupport && !hasExcision)
                                missingSupportOnOutsideSupport++;
                            if (hasExcision)
                                unexpectedExcisionOnOutsideSupport++;
                        }
                        else
                        {
                            if (!hasSupport && !hasOutside && hasExcision)
                                missingSupportOnSupportExcision++;
                            if (!hasExcision && !hasOutside && hasSupport)
                                missingExcisionOnSupportExcision++;
                            if (hasOutside)
                                unexpectedOutsideOnSupportExcision++;
                        }
                    }
                }
                else if (interfaceFlag)
                {
                    ambiguousOwnership++;
                }

                if (interfaceFlag)
                {
                    if (regionCount == 1)
                    {
                        scan.singleSideCount++;
                    }
                    else if (regionCount == 2 && hasOutside && hasExcision && !hasSupport)
                    {
                        scan.outsideExcisionCount++;
                    }
                    else if (regionCount >= 3)
                    {
                        scan.tripleUseCount++;
                    }
                }

                if (regionCount == 1 && !interfaceFlag)
                    continue;

                int failureIndex = mismatchCount;
                mismatchCount++;
                bool auditMismatch = IsStructuredAuditInterfaceFlag(state.flags);
                if (auditMismatch)
                    scan.auditMismatchCount++;
                else
                    scan.hardMismatchCount++;
                AccumulateStructuredInterfaceFlagCounts(mismatchFlagCounts, state.flags);
                AccumulateStructuredInterfaceRowCount(mismatchRowCounts, state.rowIndex);
                bool isTipRelated = rows != null && IsStructuredBoundarySegmentTipAdjacent(rows, state.rowIndex);
                if ((state.flags & ClipBoundaryFlags.Start) != 0)
                    startCount++;
                if ((state.flags & ClipBoundaryFlags.End) != 0)
                    endCount++;
                if (isTipRelated)
                    tipCount++;

                ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(state.flags);
                if (TryGetStructuredInterfaceOwners(rows, state.rowIndex, state.flags, out bool expectOutsideClass, out bool expectSupportClass, out bool expectExcisionClass))
                {
                    if (expectOutsideClass && expectSupportClass && !expectExcisionClass)
                    {
                        if (singleFlag == ClipBoundaryFlags.LeftSupport)
                            osLsCount++;
                        else if (singleFlag == ClipBoundaryFlags.RightSupport)
                            osRsCount++;
                        else
                            otherCategoryCount++;
                    }
                    else if (!expectOutsideClass && expectSupportClass && expectExcisionClass)
                    {
                        if (singleFlag == ClipBoundaryFlags.Left)
                            sxLbCount++;
                        else if (singleFlag == ClipBoundaryFlags.Right)
                            sxRbCount++;
                        else
                            otherCategoryCount++;
                    }
                    else
                    {
                        otherCategoryCount++;
                    }
                }
                else
                {
                    otherCategoryCount++;
                }

                switch (FormatStructuredInterfaceOwnerMask(hasOutside, hasSupport, hasExcision))
                {
                    case "none":
                        actualNoneCount++;
                        break;
                    case "O":
                        actualOCount++;
                        break;
                    case "S":
                        actualSCount++;
                        break;
                    case "X":
                        actualXCount++;
                        break;
                    case "OS":
                        actualOSCount++;
                        break;
                    case "SX":
                        actualSXCount++;
                        break;
                    case "OX":
                        actualOXCount++;
                        break;
                    case "OSX":
                        actualOSXCount++;
                        break;
                    default:
                        actualNoneCount++;
                        break;
                }
                if (preview.Count < 4)
                {
                    preview.Add(
                        BuildStructuredFailedOwnershipPreviewEntry(
                            rows,
                            failureIndex,
                            state,
                            hasOutside,
                            hasSupport,
                            hasExcision,
                            includeVertices: true));
                }

                if (detail.Count < 24)
                {
                    detail.Add(
                        BuildStructuredFailedOwnershipPreviewEntry(
                            rows,
                            failureIndex,
                            state,
                            hasOutside,
                            hasSupport,
                            hasExcision,
                            includeVertices: false));
                }

                bool tipHardInterface =
                    isTipRelated &&
                    IsStructuredHardInterfaceFlag(state.flags) &&
                    (singleFlag == ClipBoundaryFlags.Left || singleFlag == ClipBoundaryFlags.Right);
                if (tipHardInterface)
                {
                    if (state.hasResolvedSegment)
                    {
                        AccumulateStructuredTipInterfaceBucket(
                            tipInterfaceBuckets,
                            state,
                            hasOutside,
                            hasSupport,
                            hasExcision);
                    }
                    else if (state.rowIndex >= 0)
                    {
                        unresolvedTipBucketCounts.TryGetValue(state.rowIndex, out int unresolvedCount);
                        unresolvedTipBucketCounts[state.rowIndex] = unresolvedCount + 1;
                    }
                }

                if (mismatchEdgeWorldPairs != null)
                {
                    mismatchEdgeWorldPairs.Add(state.worldA);
                    mismatchEdgeWorldPairs.Add(state.worldB);
                }
                if (mismatchEdgeDebugLabels != null)
                {
                    mismatchEdgeDebugLabels.Add(
                        BuildStructuredFailedOwnershipSceneLabel(
                            rows,
                            failureIndex,
                            state,
                            hasOutside,
                            hasSupport,
                            hasExcision));
                }
            }

            if (edgeMap.Count == 0 &&
                structuredEdgeMap.Count == 0 &&
                ((supportPolygons != null && supportPolygons.Count > 0) || (excisionPolygons != null && excisionPolygons.Count > 0)))
            {
                mismatchCount = Mathf.Max(1, mismatchCount);
                scan.hardMismatchCount = Mathf.Max(1, scan.hardMismatchCount);
                if (preview.Count == 0)
                    preview.Add("no-semantic-interface-edges");
            }

            scan.mismatchCount = mismatchCount;
            scan.byFlagSummary = BuildStructuredInterfaceFlagSummary(mismatchFlagCounts);
            scan.byRowSummary = BuildStructuredInterfaceRowSummary(mismatchRowCounts);
            scan.ownershipSummary =
                missingOutsideOnOutsideSupport == 0 &&
                missingSupportOnOutsideSupport == 0 &&
                missingSupportOnSupportExcision == 0 &&
                missingExcisionOnSupportExcision == 0 &&
                unexpectedOutsideOnSupportExcision == 0 &&
                unexpectedExcisionOnOutsideSupport == 0 &&
                ambiguousOwnership == 0
                    ? (closingTipOwnerRepairCount > 0 ? $"closingTipRepair={closingTipOwnerRepairCount}" : "none")
                    : $"missingOutsideOS={missingOutsideOnOutsideSupport} missingSupportOS={missingSupportOnOutsideSupport} missingSupportSX={missingSupportOnSupportExcision} missingExcisionSX={missingExcisionOnSupportExcision} unexpectedOutsideSX={unexpectedOutsideOnSupportExcision} unexpectedExcisionOS={unexpectedExcisionOnOutsideSupport} ambiguous={ambiguousOwnership} closingTipRepair={closingTipOwnerRepairCount}";
            scan.categorySummary =
                $"SX_LB={sxLbCount} SX_RB={sxRbCount} OS_LS={osLsCount} OS_RS={osRsCount} Start={startCount} End={endCount} Tip={tipCount} Other={otherCategoryCount}";
            scan.actualMaskSummary =
                $"actualNone={actualNoneCount} actualO={actualOCount} actualS={actualSCount} actualX={actualXCount} actualOS={actualOSCount} actualSX={actualSXCount} actualOX={actualOXCount} actualOSX={actualOSXCount}";
            scan.detailSummary = detail.Count > 0 ? string.Join(" ; ", detail.ToArray()) : "none";
            scan.tipSummary = BuildStructuredTipOwnershipSummary(rows, mismatchRowCounts);
            scan.tipInterfaceKeyCompareSummary = BuildStructuredTipInterfaceKeyCompareSummary(rows, tipInterfaceBuckets, unresolvedTipBucketCounts);
            scan.previewSummary = preview.Count > 0 ? string.Join(" ; ", preview.ToArray()) : "none";
            return
                $"mismatch=(hard={scan.hardMismatchCount},audit={scan.auditMismatchCount},total={scan.mismatchCount}) " +
                $"segments={scan.segmentCount} " +
                $"edges=(outside={scan.outsideEdgeCount},support={scan.supportEdgeCount},excision={scan.excisionEdgeCount}) " +
                $"byPair=(os={scan.matchedOutsideSupportCount},sx={scan.matchedSupportExcisionCount},ox={scan.outsideExcisionCount},single={scan.singleSideCount},triple={scan.tripleUseCount}) " +
                $"byFlag={scan.byFlagSummary} byRow={scan.byRowSummary} ownership={scan.ownershipSummary} " +
                $"fe={scan.categorySummary} actual={scan.actualMaskSummary} " +
                $"canceled={scan.canceledInternalCount} multi={scan.multiUseCount} " +
                $"preview={scan.previewSummary}";
        }

        private static bool TryBuildStructuredResolvedInterfaceSubsegmentKey(
            int rowIndex,
            ClipBoundaryFlags flags,
            StructuredBoundarySegmentKey resolvedSegmentKey,
            float resolvedSegmentTMin,
            float resolvedSegmentTMax,
            out StructuredResolvedInterfaceSubsegmentKey key)
        {
            key = default;
            ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(flags);
            if (rowIndex < 0 ||
                singleFlag == ClipBoundaryFlags.None ||
                resolvedSegmentKey.boundaryFlag != singleFlag)
            {
                return false;
            }

            key = new StructuredResolvedInterfaceSubsegmentKey
            {
                segmentKey = resolvedSegmentKey,
                tMinQuantized = QuantizeStructuredInterfaceParam(resolvedSegmentTMin),
                tMaxQuantized = QuantizeStructuredInterfaceParam(resolvedSegmentTMax),
            };
            return true;
        }

        private static bool TryBuildStructuredResolvedInterfaceSubsegmentKey(
            InterfaceEdgeAccumulator state,
            out StructuredResolvedInterfaceSubsegmentKey key)
        {
            key = default;
            return
                state.hasResolvedSegment &&
                TryBuildStructuredResolvedInterfaceSubsegmentKey(
                    state.rowIndex,
                    state.flags,
                    state.resolvedSegmentKey,
                    state.resolvedSegmentTMin,
                    state.resolvedSegmentTMax,
                    out key);
        }

        private static bool ShouldIgnoreStructuredDegenerateInterfaceAccumulator(InterfaceEdgeAccumulator state)
        {
            return
                state.hasResolvedSegment &&
                IsStructuredRegionInterfaceFlag(state.flags) &&
                IsDegenerateStructuredInterfaceInterval(state.resolvedSegmentTMin, state.resolvedSegmentTMax);
        }


        private static int RepairClosingTipInterfaceOwnership(
            List<StructuredStripRow> rows,
            List<InterfaceEdgeAccumulator> states)
        {
            if (rows == null || rows.Count < 3 || states == null || states.Count == 0)
                return 0;

            int firstClosingTipAuditRow = Mathf.Max(0, rows.Count - 3);
            int repairCount = 0;
            for (int i = 0; i < states.Count; i++)
            {
                InterfaceEdgeAccumulator state = states[i];
                if (state.rowIndex < firstClosingTipAuditRow)
                    continue;

                ClipBoundaryFlags singleFlag = ExtractSingleStructuredInterfaceFlag(state.flags);
                bool isStructuredSide =
                    singleFlag == ClipBoundaryFlags.LeftSupport ||
                    singleFlag == ClipBoundaryFlags.Left ||
                    singleFlag == ClipBoundaryFlags.Right ||
                    singleFlag == ClipBoundaryFlags.RightSupport;
                if (!isStructuredSide)
                    continue;

                if (!TryGetStructuredInterfaceOwners(rows, state.rowIndex, state.flags, out bool expectOutside, out bool expectSupport, out bool expectExcision))
                    continue;

                bool hasOutside = state.usePresenceOwnership ? state.outsideCount > 0 : (state.outsideCount & 1) != 0;
                bool hasSupport = state.usePresenceOwnership ? state.supportCount > 0 : (state.supportCount & 1) != 0;
                bool hasExcision = state.usePresenceOwnership ? state.excisionCount > 0 : (state.excisionCount & 1) != 0;
                if (hasOutside == expectOutside && hasSupport == expectSupport && hasExcision == expectExcision)
                    continue;

                bool repaired = false;

                if ((singleFlag == ClipBoundaryFlags.Left || singleFlag == ClipBoundaryFlags.Right) &&
                    !expectOutside && expectSupport && expectExcision)
                {
                    // Closing-tip helper can collapse the kept support side onto the same resolved
                    // LB/RB subsegment as the excision cap.  When that happens, the materialized
                    // edge scan reports X-only even though the structured strip contract is SX.
                    if (!hasSupport && hasExcision && !hasOutside)
                    {
                        state.supportCount = Mathf.Max(state.supportCount, 1);
                        repaired = true;
                    }
                    else if (hasSupport && !hasExcision && !hasOutside)
                    {
                        state.excisionCount = Mathf.Max(state.excisionCount, 1);
                        repaired = true;
                    }
                }
                else if ((singleFlag == ClipBoundaryFlags.LeftSupport || singleFlag == ClipBoundaryFlags.RightSupport) &&
                    expectOutside && expectSupport && !expectExcision)
                {
                    // The row just before a collapsed end cap can similarly lose the outside contributor
                    // on LS/RS during endpoint consolidation.  Repair only the complementary S-only/O-only
                    // closing-tip audit interval, never arbitrary middle-row mismatches.
                    if (!hasOutside && hasSupport && !hasExcision)
                    {
                        state.outsideCount = Mathf.Max(state.outsideCount, 1);
                        repaired = true;
                    }
                    else if (hasOutside && !hasSupport && !hasExcision)
                    {
                        state.supportCount = Mathf.Max(state.supportCount, 1);
                        repaired = true;
                    }
                }

                if (!repaired)
                    continue;

                state.usePresenceOwnership = true;
                states[i] = state;
                repairCount++;
            }

            return repairCount;
        }

        private static void AccumulateStructuredResolvedInterfaceEdge(
            Dictionary<StructuredResolvedInterfaceSubsegmentKey, InterfaceEdgeAccumulator> structuredEdgeMap,
            StructuredResolvedInterfaceSubsegmentKey key,
            PartitionRegionKind regionKind,
            int rowIndex,
            ClipVertex a,
            ClipVertex b,
            ClipBoundaryFlags flags,
            StructuredBoundarySegmentKey resolvedSegmentKey)
        {
            if (structuredEdgeMap == null)
                return;

            ClipBoundaryFlags preservedFlags = resolvedSegmentKey.boundaryFlag | (flags & (ClipBoundaryFlags.Start | ClipBoundaryFlags.End | ClipBoundaryFlags.Row));
            if (!structuredEdgeMap.TryGetValue(key, out InterfaceEdgeAccumulator state))
            {
                state = new InterfaceEdgeAccumulator
                {
                    usePresenceOwnership = true,
                    rowIndex = rowIndex,
                    vertexA = DescribeInterfaceVertex(a),
                    vertexB = DescribeInterfaceVertex(b),
                    flags = preservedFlags,
                    worldA = a.world,
                    worldB = b.world,
                    hasResolvedSegment = true,
                    resolvedSegmentKey = resolvedSegmentKey,
                    resolvedSegmentTMin = DequantizeStructuredInterfaceParam(key.tMinQuantized),
                    resolvedSegmentTMax = DequantizeStructuredInterfaceParam(key.tMaxQuantized),
                };
            }

            state.flags |= preservedFlags;
            if (string.IsNullOrEmpty(state.vertexA))
                state.vertexA = DescribeInterfaceVertex(a);
            if (string.IsNullOrEmpty(state.vertexB))
                state.vertexB = DescribeInterfaceVertex(b);
            if (state.worldA == Vector3.zero && state.worldB == Vector3.zero)
            {
                state.worldA = a.world;
                state.worldB = b.world;
            }

            switch (regionKind)
            {
                case PartitionRegionKind.Outside:
                    state.outsideCount++;
                    break;
                case PartitionRegionKind.Support:
                    state.supportCount++;
                    break;
                case PartitionRegionKind.Excision:
                    state.excisionCount++;
                    break;
            }

            structuredEdgeMap[key] = state;
        }

        private static List<InterfaceEdgeAccumulator> ConsolidateStructuredInterfaceAccumulators(
            List<StructuredStripRow> rows,
            Dictionary<ulong, InterfaceEdgeAccumulator> edgeMap)
        {
            var result = new List<InterfaceEdgeAccumulator>();
            if (edgeMap == null || edgeMap.Count == 0)
                return result;

            if (rows == null || rows.Count < 2)
            {
                foreach (var pair in edgeMap)
                    result.Add(pair.Value);
                return result;
            }

            var structured = new Dictionary<StructuredResolvedInterfaceSubsegmentKey, InterfaceEdgeAccumulator>();
            foreach (var pair in edgeMap)
            {
                InterfaceEdgeAccumulator state = pair.Value;
                if (!TryBuildStructuredResolvedInterfaceSubsegmentKey(state, out StructuredResolvedInterfaceSubsegmentKey key) ||
                    !TryGetStructuredInterfaceOwners(rows, state.rowIndex, state.flags, out _, out _, out _))
                {
                    result.Add(state);
                    continue;
                }

                ClipBoundaryFlags preservedFlags = state.resolvedSegmentKey.boundaryFlag | (state.flags & (ClipBoundaryFlags.Start | ClipBoundaryFlags.End | ClipBoundaryFlags.Row));
                if (!structured.TryGetValue(key, out InterfaceEdgeAccumulator consolidated))
                {
                    consolidated = new InterfaceEdgeAccumulator
                    {
                        outsideCount = state.outsideCount,
                        supportCount = state.supportCount,
                        excisionCount = state.excisionCount,
                        usePresenceOwnership = true,
                        rowIndex = state.rowIndex,
                        vertexA = state.vertexA,
                        vertexB = state.vertexB,
                        flags = preservedFlags,
                        worldA = state.worldA,
                        worldB = state.worldB,
                        hasResolvedSegment = true,
                        resolvedSegmentKey = state.resolvedSegmentKey,
                        resolvedSegmentTMin = DequantizeStructuredInterfaceParam(key.tMinQuantized),
                        resolvedSegmentTMax = DequantizeStructuredInterfaceParam(key.tMaxQuantized),
                    };
                    structured.Add(key, consolidated);
                    continue;
                }

                consolidated.outsideCount += state.outsideCount;
                consolidated.supportCount += state.supportCount;
                consolidated.excisionCount += state.excisionCount;
                consolidated.flags |= preservedFlags;
                if (string.IsNullOrEmpty(consolidated.vertexA))
                    consolidated.vertexA = state.vertexA;
                if (string.IsNullOrEmpty(consolidated.vertexB))
                    consolidated.vertexB = state.vertexB;
                if (consolidated.worldA == Vector3.zero && consolidated.worldB == Vector3.zero)
                {
                    consolidated.worldA = state.worldA;
                    consolidated.worldB = state.worldB;
                }
                structured[key] = consolidated;
            }

            foreach (var pair in structured)
                result.Add(pair.Value);
            return result;
        }

        private static bool IsStructuredRegionInterfaceFlag(ClipBoundaryFlags flags)
        {
            const ClipBoundaryFlags interfaceFlags =
                ClipBoundaryFlags.LeftSupport |
                ClipBoundaryFlags.Left |
                ClipBoundaryFlags.Right |
                ClipBoundaryFlags.RightSupport;
            return (flags & interfaceFlags) != ClipBoundaryFlags.None;
        }

        private static void AccumulateStructuredInterfaceEdges(
            List<StructuredStripRow> rows,
            List<List<ClipVertex>> polygons,
            PartitionRegionKind regionKind,
            Dictionary<ulong, InterfaceEdgeAccumulator> edgeMap,
            Dictionary<StructuredResolvedInterfaceSubsegmentKey, InterfaceEdgeAccumulator> structuredEdgeMap)
        {
            if (polygons == null || edgeMap == null)
                return;

            for (int polyIndex = 0; polyIndex < polygons.Count; polyIndex++)
            {
                List<ClipVertex> polygon = polygons[polyIndex];
                if (polygon == null || polygon.Count < 2)
                    continue;

                for (int i = 0; i < polygon.Count; i++)
                {
                    ClipVertex a = polygon[i];
                    ClipVertex b = polygon[(i + 1) % polygon.Count];
                    if (a.materializedIndex < 0 || b.materializedIndex < 0 || a.materializedIndex == b.materializedIndex)
                        continue;

                    ClipBoundaryFlags flags = ClipBoundaryFlags.None;
                    int resolvedRowIndex = -1;
                    bool hasResolvedSegment = false;
                    StructuredBoundarySegmentKey resolvedSegmentKey = default;
                    float resolvedSegmentTMin = 0f;
                    float resolvedSegmentTMax = 0f;
                    if (rows != null &&
                        TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out StructuredBoundarySegmentKey resolvedKey, out ClipBoundaryFlags resolvedFlag, out float ta, out float tb))
                    {
                        flags = resolvedFlag;
                        resolvedRowIndex = resolvedKey.rowIndex;
                        hasResolvedSegment = true;
                        resolvedSegmentKey = resolvedKey;
                        resolvedSegmentTMin = Mathf.Min(ta, tb);
                        resolvedSegmentTMax = Mathf.Max(ta, tb);
                    }

                    if (flags == ClipBoundaryFlags.None)
                        flags = a.boundaryFlags & b.boundaryFlags & StructuredRegionInterfaceFlags;
                    if (flags == ClipBoundaryFlags.None)
                        continue;

                    if (rows != null &&
                        hasResolvedSegment &&
                        TryBuildStructuredResolvedInterfaceSubsegmentKey(
                            resolvedRowIndex,
                            flags,
                            resolvedSegmentKey,
                            resolvedSegmentTMin,
                            resolvedSegmentTMax,
                            out StructuredResolvedInterfaceSubsegmentKey structuredKey) &&
                        TryGetStructuredInterfaceOwners(rows, resolvedRowIndex, flags, out _, out _, out _))
                    {
                        if (!IsDegenerateStructuredInterfaceInterval(resolvedSegmentTMin, resolvedSegmentTMax))
                        {
                            AccumulateStructuredResolvedInterfaceEdge(
                                structuredEdgeMap,
                                structuredKey,
                                regionKind,
                                resolvedRowIndex,
                                a,
                                b,
                                flags,
                                resolvedSegmentKey);
                        }
                        continue;
                    }

                    ulong key = MakeUndirectedEdgeKey(a.materializedIndex, b.materializedIndex);
                    if (!edgeMap.TryGetValue(key, out InterfaceEdgeAccumulator state))
                    {
                        state = new InterfaceEdgeAccumulator
                        {
                            rowIndex = -1,
                            vertexA = DescribeInterfaceVertex(a),
                            vertexB = DescribeInterfaceVertex(b),
                            flags = flags,
                            worldA = a.world,
                            worldB = b.world,
                        };
                    }

                    state.flags |= flags;
                    if (string.IsNullOrEmpty(state.vertexA))
                        state.vertexA = DescribeInterfaceVertex(a);
                    if (string.IsNullOrEmpty(state.vertexB))
                        state.vertexB = DescribeInterfaceVertex(b);
                    if (state.worldA == Vector3.zero && state.worldB == Vector3.zero)
                    {
                        state.worldA = a.world;
                        state.worldB = b.world;
                    }
                    if (state.rowIndex < 0)
                    {
                        if (resolvedRowIndex >= 0)
                        {
                            state.rowIndex = resolvedRowIndex;
                        }
                        else if (TryResolveStructuredInterfaceRowIndex(rows, a, b, out int rowIndex))
                        {
                            state.rowIndex = rowIndex;
                        }
                    }
                    if (!state.hasResolvedSegment && hasResolvedSegment)
                    {
                        state.hasResolvedSegment = true;
                        state.resolvedSegmentKey = resolvedSegmentKey;
                        state.resolvedSegmentTMin = resolvedSegmentTMin;
                        state.resolvedSegmentTMax = resolvedSegmentTMax;
                    }

                    switch (regionKind)
                    {
                        case PartitionRegionKind.Outside:
                            state.outsideCount++;
                            break;
                        case PartitionRegionKind.Support:
                            state.supportCount++;
                            break;
                        case PartitionRegionKind.Excision:
                            state.excisionCount++;
                            break;
                    }

                    edgeMap[key] = state;
                }
            }
        }

        private static bool TryResolveStructuredInterfaceRowIndex(
            List<StructuredStripRow> rows,
            ClipVertex a,
            ClipVertex b,
            out int rowIndex)
        {
            rowIndex = -1;
            if (rows == null || rows.Count < 2)
                return false;

            float midS = 0.5f * (a.s + b.s);
            return TryFindStructuredBoundaryRowSegment(rows, midS, out rowIndex);
        }

        private static void AccumulateStructuredInterfaceFlagCounts(
            Dictionary<ClipBoundaryFlags, int> counts,
            ClipBoundaryFlags flags)
        {
            if (counts == null)
                return;

            AccumulateStructuredInterfaceFlagCount(counts, flags, ClipBoundaryFlags.LeftSupport);
            AccumulateStructuredInterfaceFlagCount(counts, flags, ClipBoundaryFlags.Left);
            AccumulateStructuredInterfaceFlagCount(counts, flags, ClipBoundaryFlags.Right);
            AccumulateStructuredInterfaceFlagCount(counts, flags, ClipBoundaryFlags.RightSupport);
        }

        private static void AccumulateStructuredInterfaceFlagCount(
            Dictionary<ClipBoundaryFlags, int> counts,
            ClipBoundaryFlags flags,
            ClipBoundaryFlags target)
        {
            if (counts == null || (flags & target) == ClipBoundaryFlags.None)
                return;

            counts.TryGetValue(target, out int value);
            counts[target] = value + 1;
        }

        private static void AccumulateStructuredInterfaceRowCount(Dictionary<int, int> rowCounts, int rowIndex)
        {
            if (rowCounts == null || rowIndex < 0)
                return;

            rowCounts.TryGetValue(rowIndex, out int value);
            rowCounts[rowIndex] = value + 1;
        }

        private static string BuildStructuredInterfaceFlagSummary(Dictionary<ClipBoundaryFlags, int> counts)
        {
            if (counts == null || counts.Count == 0)
                return "none";

            return
                $"LS={GetStructuredInterfaceFlagCount(counts, ClipBoundaryFlags.LeftSupport)} " +
                $"L={GetStructuredInterfaceFlagCount(counts, ClipBoundaryFlags.Left)} " +
                $"R={GetStructuredInterfaceFlagCount(counts, ClipBoundaryFlags.Right)} " +
                $"RS={GetStructuredInterfaceFlagCount(counts, ClipBoundaryFlags.RightSupport)}";
        }

        private static int GetStructuredInterfaceFlagCount(Dictionary<ClipBoundaryFlags, int> counts, ClipBoundaryFlags flag)
        {
            return counts != null && counts.TryGetValue(flag, out int value) ? value : 0;
        }

        private static string BuildStructuredInterfaceRowSummary(Dictionary<int, int> rowCounts)
        {
            if (rowCounts == null || rowCounts.Count == 0)
                return "none";

            var rows = new List<KeyValuePair<int, int>>(rowCounts);
            rows.Sort((a, b) =>
            {
                int byCount = b.Value.CompareTo(a.Value);
                return byCount != 0 ? byCount : a.Key.CompareTo(b.Key);
            });

            int take = Mathf.Min(4, rows.Count);
            var parts = new string[take];
            for (int i = 0; i < take; i++)
                parts[i] = $"r{rows[i].Key}={rows[i].Value}";
            return string.Join(",", parts);
        }

        private static int[] CapturePolygonVertexCounts(List<List<ClipVertex>> polygons)
        {
            if (polygons == null || polygons.Count == 0)
                return Array.Empty<int>();

            var counts = new int[polygons.Count];
            for (int i = 0; i < polygons.Count; i++)
                counts[i] = polygons[i] != null ? polygons[i].Count : 0;
            return counts;
        }

        private static int CountChangedPolygonCounts(List<List<ClipVertex>> polygons, int[] beforeCounts)
        {
            int beforeCount = beforeCounts != null ? beforeCounts.Length : 0;
            int afterCount = polygons != null ? polygons.Count : 0;
            int maxCount = Mathf.Max(beforeCount, afterCount);
            int changed = 0;
            for (int i = 0; i < maxCount; i++)
            {
                int before = i < beforeCount ? beforeCounts[i] : 0;
                int after = (polygons != null && i < polygons.Count && polygons[i] != null) ? polygons[i].Count : 0;
                if (before != after)
                    changed++;
            }

            return changed;
        }

        private static string[] CapturePolygonRepairSignatures(List<List<ClipVertex>> polygons)
        {
            if (polygons == null || polygons.Count == 0)
                return Array.Empty<string>();

            var signatures = new string[polygons.Count];
            for (int i = 0; i < polygons.Count; i++)
                signatures[i] = BuildPolygonRepairSignature(polygons[i]);
            return signatures;
        }

        private static int CountChangedPolygonSignatures(List<List<ClipVertex>> polygons, string[] beforeSignatures)
        {
            int beforeCount = beforeSignatures != null ? beforeSignatures.Length : 0;
            int afterCount = polygons != null ? polygons.Count : 0;
            int maxCount = Mathf.Max(beforeCount, afterCount);
            int changed = 0;
            for (int i = 0; i < maxCount; i++)
            {
                string before = i < beforeCount ? beforeSignatures[i] : string.Empty;
                string after = (polygons != null && i < polygons.Count) ? BuildPolygonRepairSignature(polygons[i]) : string.Empty;
                if (!string.Equals(before, after, StringComparison.Ordinal))
                    changed++;
            }

            return changed;
        }

        private static string BuildPolygonRepairSignature(List<ClipVertex> polygon)
        {
            if (polygon == null || polygon.Count == 0)
                return string.Empty;

            var sb = new StringBuilder(polygon.Count * 24);
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex vertex = polygon[i];
                if (i > 0)
                    sb.Append(';');
                sb.Append(vertex.materializedIndex);
                sb.Append(':');
                sb.Append((int)vertex.boundaryFlags);
                sb.Append(':');
                sb.Append(Mathf.RoundToInt(vertex.s * 100000f));
                sb.Append(':');
                sb.Append(Mathf.RoundToInt(vertex.u * 100000f));
            }

            return sb.ToString();
        }

        private static void ApplyStructuredInterfaceScanDiagnostics(ref RemeshDiagnostics diagnostics, StructuredInterfaceScanDiagnostics scan)
        {
            diagnostics.interfaceScanSegmentCount = scan.segmentCount;
            diagnostics.interfaceScanMatchedOutsideSupportCount = scan.matchedOutsideSupportCount;
            diagnostics.interfaceScanMatchedSupportExcisionCount = scan.matchedSupportExcisionCount;
            diagnostics.interfaceScanOutsideExcisionCount = scan.outsideExcisionCount;
            diagnostics.interfaceScanSingleSideCount = scan.singleSideCount;
            diagnostics.interfaceScanMultiUseCount = scan.multiUseCount;
            diagnostics.interfaceScanTripleUseCount = scan.tripleUseCount;
            diagnostics.interfaceHardMismatchEdgeCount = scan.hardMismatchCount;
            diagnostics.interfaceAuditMismatchEdgeCount = scan.auditMismatchCount;
            diagnostics.interfaceScanByFlagSummary = string.IsNullOrEmpty(scan.byFlagSummary) ? "none" : scan.byFlagSummary;
            diagnostics.interfaceScanByRowSummary = string.IsNullOrEmpty(scan.byRowSummary) ? "none" : scan.byRowSummary;
            diagnostics.interfaceScanOwnershipSummary = string.IsNullOrEmpty(scan.ownershipSummary) ? "none" : scan.ownershipSummary;
            diagnostics.interfaceFeCategorySummary = string.IsNullOrEmpty(scan.categorySummary) ? "none" : scan.categorySummary;
            diagnostics.interfaceFeActualMaskSummary = string.IsNullOrEmpty(scan.actualMaskSummary) ? "none" : scan.actualMaskSummary;
            diagnostics.interfaceScanDetailSummary = string.IsNullOrEmpty(scan.detailSummary) ? "none" : scan.detailSummary;
            diagnostics.tipOwnershipSummary = string.IsNullOrEmpty(scan.tipSummary) ? "none" : scan.tipSummary;
            diagnostics.tipInterfaceKeyCompareSummary = string.IsNullOrEmpty(scan.tipInterfaceKeyCompareSummary) ? "none" : scan.tipInterfaceKeyCompareSummary;
        }

        private static string DescribeInterfaceVertex(ClipVertex vertex)
        {
            string materialized = vertex.materializedIndex >= 0 ? $"m{vertex.materializedIndex}:" : string.Empty;
            return $"{materialized}({vertex.s:F4},{vertex.u:F4})/{FormatClipBoundaryFlags(vertex.boundaryFlags)}";
        }

        private static string DetermineStructuredCellFailureStage(StructuredCellTopologyDiagnostics topologyDiagnostics)
        {
            if (topologyDiagnostics.failedDefinitionMismatchCount > 0)
                return "CellDefinition";

            if (topologyDiagnostics.failedBoundaryEdgeCount < 3 ||
                topologyDiagnostics.failedLoopCount != 1 ||
                topologyDiagnostics.failedDegree1VertexCount != 0 ||
                topologyDiagnostics.failedBranchVertexCount != 0 ||
                (topologyDiagnostics.failedBoundaryVertexCount > 0 &&
                 topologyDiagnostics.failedDegree2VertexCount != topologyDiagnostics.failedBoundaryVertexCount))
            {
                return "CellBoundaryGraph";
            }

            return "BoundarySemantics";
        }

        private static void AppendStructuredCellChain(List<int> target, List<int> source)
        {
            if (target == null || source == null || source.Count == 0)
                return;

            int startIndex = 0;
            if (target.Count > 0 && target[target.Count - 1] == source[0])
                startIndex = 1;
            for (int i = startIndex; i < source.Count; i++)
                target.Add(source[i]);
        }

        private static void NormalizeStructuredCellOrderedIndices(List<int> orderedIndices)
        {
            if (orderedIndices == null || orderedIndices.Count == 0)
                return;

            int write = 1;
            for (int read = 1; read < orderedIndices.Count; read++)
            {
                if (orderedIndices[read] == orderedIndices[write - 1])
                    continue;
                orderedIndices[write++] = orderedIndices[read];
            }

            if (write < orderedIndices.Count)
                orderedIndices.RemoveRange(write, orderedIndices.Count - write);

            if (orderedIndices.Count > 1 && orderedIndices[0] == orderedIndices[orderedIndices.Count - 1])
                orderedIndices.RemoveAt(orderedIndices.Count - 1);
        }

        private static bool IsPolygonSelfIntersectingSU(List<ClipVertex> polygon)
        {
            if (polygon == null || polygon.Count < 4)
                return false;

            int count = polygon.Count;
            for (int i = 0; i < count; i++)
            {
                Vector2 a0 = new Vector2(polygon[i].s, polygon[i].u);
                Vector2 a1 = new Vector2(polygon[(i + 1) % count].s, polygon[(i + 1) % count].u);
                for (int j = i + 1; j < count; j++)
                {
                    if (i == j || (i + 1) % count == j || i == (j + 1) % count)
                        continue;

                    Vector2 b0 = new Vector2(polygon[j].s, polygon[j].u);
                    Vector2 b1 = new Vector2(polygon[(j + 1) % count].s, polygon[(j + 1) % count].u);
                    if (SegmentsIntersect2D(a0, a1, b0, b1))
                        return true;
                }
            }

            return false;
        }

        private static void AccumulateCellPolygonHealth(
            ref RemeshDiagnostics diagnostics,
            string kind,
            StructuredCellKey key,
            List<ClipVertex> polygon)
        {
            diagnostics.cellPolygonScanCount++;
            AnalyzeCellPolygonHealth(
                polygon,
                out int repeatedVertexCount,
                out int concaveVertexCount,
                out bool selfIntersecting,
                out float minEdge,
                out float minAngleDeg,
                out float signedArea);

            bool tinyEdge = minEdge <= 1e-5f;
            bool tinyAngle = minAngleDeg <= 1f;
            bool issue =
                polygon == null ||
                polygon.Count < 3 ||
                Mathf.Abs(signedArea) <= 1e-10f ||
                repeatedVertexCount > 0 ||
                selfIntersecting ||
                tinyEdge ||
                tinyAngle;
            if (issue)
                diagnostics.cellPolygonIssueCount++;
            if (concaveVertexCount > 0)
                diagnostics.cellPolygonConcaveCount++;
            if (selfIntersecting)
                diagnostics.cellPolygonSelfIntersectionCount++;
            if (repeatedVertexCount > 0)
                diagnostics.cellPolygonRepeatedVertexCount++;
            if (tinyEdge)
                diagnostics.cellPolygonTinyEdgeCount++;
            if (tinyAngle)
                diagnostics.cellPolygonTinyAngleCount++;

            string preview = diagnostics.cellPolygonScanSummary;
            if (issue && CountSummaryItems(preview) < 4)
            {
                string entry =
                    $"{kind}r{key.rowIndex}c{key.columnIndex}:v={(polygon != null ? polygon.Count : 0)} area={signedArea:F6} " +
                    $"rep={repeatedVertexCount} conc={concaveVertexCount} self={(selfIntersecting ? 1 : 0)} minE={minEdge:F6} minA={minAngleDeg:F2}";
                diagnostics.cellPolygonScanSummary = string.IsNullOrEmpty(preview) || preview == "none"
                    ? entry
                    : preview + " ; " + entry;
            }
            else if (string.IsNullOrEmpty(diagnostics.cellPolygonScanSummary))
            {
                diagnostics.cellPolygonScanSummary = "none";
            }
        }

        private static int CountSummaryItems(string summary)
        {
            if (string.IsNullOrEmpty(summary) || summary == "none")
                return 0;

            int count = 1;
            for (int i = 0; i < summary.Length; i++)
            {
                if (summary[i] == ';')
                    count++;
            }

            return count;
        }

        private static string DescribeCellPolygonHealth(
            string kind,
            StructuredCellKey key,
            List<ClipVertex> polygon,
            bool includeVertices)
        {
            AnalyzeCellPolygonHealth(
                polygon,
                out int repeatedVertexCount,
                out int concaveVertexCount,
                out bool selfIntersecting,
                out float minEdge,
                out float minAngleDeg,
                out float signedArea);
            string summary =
                $"{kind} row={key.rowIndex} col={key.columnIndex} verts={(polygon != null ? polygon.Count : 0)} " +
                $"area={signedArea:F8} rep={repeatedVertexCount} conc={concaveVertexCount} self={(selfIntersecting ? 1 : 0)} " +
                $"minEdge={minEdge:F8} minAngle={minAngleDeg:F3}";
            if (!includeVertices)
                return summary;

            return summary + " preview=" + BuildCellPolygonVertexPreview(polygon, 10);
        }

        private static void AnalyzeCellPolygonHealth(
            List<ClipVertex> polygon,
            out int repeatedVertexCount,
            out int concaveVertexCount,
            out bool selfIntersecting,
            out float minEdge,
            out float minAngleDeg,
            out float signedArea)
        {
            repeatedVertexCount = 0;
            concaveVertexCount = 0;
            selfIntersecting = false;
            minEdge = 0f;
            minAngleDeg = 0f;
            signedArea = 0f;
            if (polygon == null || polygon.Count == 0)
                return;

            signedArea = ComputePolygonSignedAreaSU(polygon);
            selfIntersecting = IsPolygonSelfIntersectingSU(polygon);
            minEdge = float.PositiveInfinity;
            minAngleDeg = float.PositiveInfinity;

            var seenMaterialized = new HashSet<int>();
            int windingSign = signedArea >= 0f ? 1 : -1;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = polygon[i];
                if (current.materializedIndex >= 0 && !seenMaterialized.Add(current.materializedIndex))
                    repeatedVertexCount++;

                ClipVertex prev = polygon[(i - 1 + polygon.Count) % polygon.Count];
                ClipVertex next = polygon[(i + 1) % polygon.Count];
                Vector2 prevCoord = new Vector2(prev.s, prev.u);
                Vector2 currentCoord = new Vector2(current.s, current.u);
                Vector2 nextCoord = new Vector2(next.s, next.u);

                float edgeLength = (nextCoord - currentCoord).magnitude;
                minEdge = Mathf.Min(minEdge, edgeLength);

                Vector2 incoming = prevCoord - currentCoord;
                Vector2 outgoing = nextCoord - currentCoord;
                if (incoming.sqrMagnitude > 1e-12f && outgoing.sqrMagnitude > 1e-12f)
                    minAngleDeg = Mathf.Min(minAngleDeg, Vector2.Angle(incoming, outgoing));

                float turn = Cross2D(currentCoord - prevCoord, nextCoord - currentCoord);
                if (Mathf.Abs(turn) > 1e-8f && Mathf.Sign(turn) != windingSign)
                    concaveVertexCount++;
            }

            if (float.IsInfinity(minEdge))
                minEdge = 0f;
            if (float.IsInfinity(minAngleDeg))
                minAngleDeg = 0f;
        }

        private static string BuildCellPolygonVertexPreview(List<ClipVertex> polygon, int maxVertices)
        {
            if (polygon == null || polygon.Count == 0)
                return "none";

            var entries = new List<string>(Mathf.Min(maxVertices, polygon.Count));
            for (int i = 0; i < polygon.Count && i < maxVertices; i++)
            {
                ClipVertex vertex = polygon[i];
                string materialized = vertex.materializedIndex >= 0 ? $"m{vertex.materializedIndex}" : "m?";
                entries.Add($"{i}:{materialized}({vertex.s:F4},{vertex.u:F4})/{FormatClipBoundaryFlags(vertex.boundaryFlags)}");
            }

            if (polygon.Count > maxVertices)
                entries.Add($"...+{polygon.Count - maxVertices}");
            return string.Join(" ; ", entries.ToArray());
        }

        private static void ApplyStructuredCellTopologyFailure(
            ref RemeshDiagnostics diagnostics,
            StructuredCellTopologyDiagnostics topologyDiagnostics)
        {
            diagnostics.failedCellRowIndex = topologyDiagnostics.failedRowIndex;
            diagnostics.failedCellColumnIndex = topologyDiagnostics.failedColumnIndex;
            diagnostics.failedCellBoundaryEdgeCount = topologyDiagnostics.failedBoundaryEdgeCount;
            diagnostics.failedCellBoundaryVertexCount = topologyDiagnostics.failedBoundaryVertexCount;
            diagnostics.failedCellLoopCount = topologyDiagnostics.failedLoopCount;
            diagnostics.failedCellDegree1VertexCount = topologyDiagnostics.failedDegree1VertexCount;
            diagnostics.failedCellDegree2VertexCount = topologyDiagnostics.failedDegree2VertexCount;
            diagnostics.failedCellBranchVertexCount = topologyDiagnostics.failedBranchVertexCount;
            diagnostics.failedCellSelfIntersection = topologyDiagnostics.failedSelfIntersection;
            diagnostics.failedCellSemanticCoverage = topologyDiagnostics.failedSemanticCoverage;
            diagnostics.failedCellReason = topologyDiagnostics.failedReason;
            diagnostics.failedCellDefinitionMismatchCount = topologyDiagnostics.failedDefinitionMismatchCount;
            diagnostics.failedCellDefinitionSummary = topologyDiagnostics.failedDefinitionSummary;
            diagnostics.failedCellMembershipSummary = topologyDiagnostics.failedMembershipSummary;
            diagnostics.failedCellSideTopologySummary = topologyDiagnostics.failedSideTopologySummary;
            diagnostics.failedCellCornerResolutionSummary = topologyDiagnostics.failedCornerResolutionSummary;
            diagnostics.failedCellOpenEndpointSummary = topologyDiagnostics.failedOpenEndpointSummary;
            diagnostics.failedCellBranchSummary = topologyDiagnostics.failedBranchSummary;
        }

        private static void TryAppendStructuredBoundaryVertex(
            List<ClipVertex> output,
            HashSet<int> seen,
            Dictionary<int, ClipVertex> vertexMap,
            int vertexIndex)
        {
            if (vertexIndex < 0 || output == null || seen == null || vertexMap == null)
                return;
            if (!seen.Add(vertexIndex))
                return;
            if (vertexMap.TryGetValue(vertexIndex, out ClipVertex vertex))
                output.Add(vertex);
        }

        private static void SortAndDeduplicateStructuredBoundaryVertices(
            List<ClipVertex> vertices,
            Comparison<ClipVertex> comparison)
        {
            if (vertices == null || vertices.Count == 0)
                return;

            vertices.Sort(comparison);
            int write = 1;
            ClipVertex prev = vertices[0];
            for (int i = 1; i < vertices.Count; i++)
            {
                ClipVertex cur = vertices[i];
                if (cur.materializedIndex >= 0 && cur.materializedIndex == prev.materializedIndex)
                    continue;
                if ((cur.world - prev.world).sqrMagnitude <= 1e-12f)
                    continue;
                vertices[write++] = cur;
                prev = cur;
            }

            if (write < vertices.Count)
                vertices.RemoveRange(write, vertices.Count - write);
        }

        private static void AppendUniqueStructuredBoundaryVertices(List<ClipVertex> target, List<ClipVertex> source)
        {
            if (target == null || source == null)
                return;

            for (int i = 0; i < source.Count; i++)
            {
                ClipVertex vertex = source[i];
                if (target.Count > 0)
                {
                    ClipVertex last = target[target.Count - 1];
                    if ((vertex.materializedIndex >= 0 && vertex.materializedIndex == last.materializedIndex) ||
                        (vertex.world - last.world).sqrMagnitude <= 1e-12f)
                    {
                        continue;
                    }
                }

                target.Add(vertex);
            }
        }

        private static Vector2 BuildStructuredStripCoord(FrameSample frame, BoundaryVertexSample sample)
        {
            return new Vector2(
                frame.s,
                Vector3.Dot(sample.world - frame.center, frame.binormal));
        }

        private static bool TriangleOverlapsStructuredCells2D(
            Vector3 aWorld,
            Vector3 bWorld,
            Vector3 cWorld,
            List<FrameSample> canonicalFrames,
            List<StructuredStripCell2D> cells)
        {
            if (canonicalFrames == null || cells == null || cells.Count == 0)
                return false;

            Vector2 a = BuildTriangleStripCoord(aWorld, canonicalFrames);
            Vector2 b = BuildTriangleStripCoord(bWorld, canonicalFrames);
            Vector2 c = BuildTriangleStripCoord(cWorld, canonicalFrames);
            for (int i = 0; i < cells.Count; i++)
            {
                if (TriangleOverlapsStructuredQuad2D(a, b, c, cells[i]))
                    return true;
            }

            return false;
        }

        private static Vector2 BuildTriangleStripCoord(Vector3 world, List<FrameSample> canonicalFrames)
        {
            SurfaceCoord coord = EvaluateSurfaceCoord(world, canonicalFrames);
            return new Vector2(coord.s, coord.u);
        }

        private static bool TriangleOverlapsStructuredQuad2D(
            Vector2 ta,
            Vector2 tb,
            Vector2 tc,
            StructuredStripCell2D quad)
        {
            if (IsPointInsideConvexPolygon2D(ta, quad.a, quad.b, quad.c, quad.d) ||
                IsPointInsideConvexPolygon2D(tb, quad.a, quad.b, quad.c, quad.d) ||
                IsPointInsideConvexPolygon2D(tc, quad.a, quad.b, quad.c, quad.d))
                return true;

            if (IsPointInsideTriangle2D(quad.a, ta, tb, tc) ||
                IsPointInsideTriangle2D(quad.b, ta, tb, tc) ||
                IsPointInsideTriangle2D(quad.c, ta, tb, tc) ||
                IsPointInsideTriangle2D(quad.d, ta, tb, tc))
                return true;

            if (SegmentsIntersect2D(ta, tb, quad.a, quad.b) ||
                SegmentsIntersect2D(ta, tb, quad.b, quad.c) ||
                SegmentsIntersect2D(ta, tb, quad.c, quad.d) ||
                SegmentsIntersect2D(ta, tb, quad.d, quad.a) ||
                SegmentsIntersect2D(tb, tc, quad.a, quad.b) ||
                SegmentsIntersect2D(tb, tc, quad.b, quad.c) ||
                SegmentsIntersect2D(tb, tc, quad.c, quad.d) ||
                SegmentsIntersect2D(tb, tc, quad.d, quad.a) ||
                SegmentsIntersect2D(tc, ta, quad.a, quad.b) ||
                SegmentsIntersect2D(tc, ta, quad.b, quad.c) ||
                SegmentsIntersect2D(tc, ta, quad.c, quad.d) ||
                SegmentsIntersect2D(tc, ta, quad.d, quad.a))
                return true;

            return false;
        }

        private static void PartitionTriangleAgainstStructuredCells(
            List<ClipVertex> triangle,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells,
            List<StructuredStripCell2D> domainCells,
            int sourceTriangleIndex,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> supportCellFragments,
            Dictionary<StructuredCellKey, List<StructuredCellFragment>> excisionCellFragments,
            out List<List<ClipVertex>> outsidePolygons,
            out int supportFragmentCount,
            out int excisionFragmentCount)
        {
            outsidePolygons = new List<List<ClipVertex>>();
            supportFragmentCount = 0;
            excisionFragmentCount = 0;
            if (triangle == null || triangle.Count < 3)
                return;

            List<ClipVertex> sourceTriangle = CleanupPolygon(triangle);
            if (sourceTriangle.Count < 3)
                return;

            // Cell ownership must not depend on the order in which neighboring cells are visited.
            // Each structured cell is intersected with the original patch triangle independently;
            // only outside fragments are computed by subtracting the full cell union.
            var fragments = new List<List<ClipVertex>>(1) { sourceTriangle };
            if (supportCells != null)
            {
                for (int i = 0; i < supportCells.Count; i++)
                {
                    IntersectStructuredCellWithPolygon(
                        sourceTriangle,
                        supportCells[i],
                        work,
                        edgeCache,
                        out List<List<ClipVertex>> insidePieces);
                    if (insidePieces == null || insidePieces.Count == 0)
                        continue;

                    supportFragmentCount += insidePieces.Count;
                    StructuredCellKey key = BuildStructuredCellKey(supportCells[i]);
                    for (int piece = 0; piece < insidePieces.Count; piece++)
                        AddStructuredCellFragment(supportCellFragments, key, insidePieces[piece], sourceTriangleIndex, piece);
                }
            }

            if (excisionCells != null)
            {
                for (int i = 0; i < excisionCells.Count; i++)
                {
                    IntersectStructuredCellWithPolygon(
                        sourceTriangle,
                        excisionCells[i],
                        work,
                        edgeCache,
                        out List<List<ClipVertex>> insidePieces);
                    if (insidePieces == null || insidePieces.Count == 0)
                        continue;

                    excisionFragmentCount += insidePieces.Count;
                    StructuredCellKey key = BuildStructuredCellKey(excisionCells[i]);
                    for (int piece = 0; piece < insidePieces.Count; piece++)
                        AddStructuredCellFragment(excisionCellFragments, key, insidePieces[piece], sourceTriangleIndex, piece);
                }
            }

            if (domainCells != null)
            {
                for (int i = 0; i < domainCells.Count; i++)
                {
                    SubtractStructuredCellFromFragments(
                        fragments,
                        domainCells[i],
                        work,
                        edgeCache,
                        out fragments,
                        out _);
                }
            }

            outsidePolygons = fragments;
        }

        private static void PartitionTriangleAgainstStructuredCoverageCells(
            List<ClipVertex> triangle,
            int sourceTriangleIndex,
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells,
            List<StructuredStripCell2D> domainCells,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            ref StructuredStartTipWedgeDiagnostics startTipDiagnostics,
            out List<List<ClipVertex>> outsidePolygons,
            out List<List<ClipVertex>> supportPolygons,
            out List<List<ClipVertex>> excisionPolygons,
            out List<List<ClipVertex>> startTipStageSupportPolygons,
            out List<List<ClipVertex>> startTipStageExcisionPolygons)
        {
            outsidePolygons = new List<List<ClipVertex>>();
            supportPolygons = new List<List<ClipVertex>>();
            excisionPolygons = new List<List<ClipVertex>>();
            startTipStageSupportPolygons = new List<List<ClipVertex>>();
            startTipStageExcisionPolygons = new List<List<ClipVertex>>();
            if (triangle == null || triangle.Count < 3)
                return;

            List<ClipVertex> sourceTriangle = CleanupPolygon(triangle);
            if (sourceTriangle.Count < 3)
                return;

            var remainingFragments = new List<List<ClipVertex>>(1) { sourceTriangle };
            var startTipProcessedFragments = new List<List<ClipVertex>>();
            for (int fragmentIndex = 0; fragmentIndex < remainingFragments.Count; fragmentIndex++)
            {
                List<ClipVertex> fragment = remainingFragments[fragmentIndex];
                if (TryPartitionTriangleAgainstStructuredStartTipWedge(
                    fragment,
                    rows,
                    supportCells,
                    domainCells,
                    work,
                    edgeCache,
                    ref startTipDiagnostics,
                    out List<List<ClipVertex>> startTipRemaining,
                    out List<List<ClipVertex>> startTipSupport,
                    out List<List<ClipVertex>> startTipExcision))
                {
                    if (startTipRemaining != null)
                    {
                        for (int i = 0; i < startTipRemaining.Count; i++)
                            TryAppendPolygon(startTipProcessedFragments, startTipRemaining[i]);
                    }

                    AppendStructuredDiagnosticPolygonClones(startTipStageSupportPolygons, startTipSupport);
                    AppendStructuredDiagnosticPolygonClones(startTipStageExcisionPolygons, startTipExcision);
                    if (startTipSupport != null)
                    {
                        for (int i = 0; i < startTipSupport.Count; i++)
                            TryAppendPolygon(supportPolygons, startTipSupport[i]);
                    }

                    if (startTipExcision != null)
                    {
                        for (int i = 0; i < startTipExcision.Count; i++)
                            TryAppendPolygon(excisionPolygons, startTipExcision[i]);
                    }
                }
                else
                {
                    TryAppendPolygon(startTipProcessedFragments, fragment);
                }
            }
            remainingFragments = startTipProcessedFragments;

            if (string.IsNullOrEmpty(startTipDiagnostics.endEarlyHelperProbeSummary))
            {
                StructuredEndTipProbeResult earlyProbe = RunStructuredEndTipEarlyHelperProbe(
                    remainingFragments,
                    rows,
                    supportCells,
                    excisionCells,
                    domainCells,
                    work,
                    "EndTip.EarlyHelperProbe",
                    -1,
                    int.MinValue,
                    sourceTriangleIndex);
                startTipDiagnostics.endEarlyHelperProbeSummary =
                    $"{earlyProbe.rowSummary} contrib={earlyProbe.contributorSummary}";
            }

            var orderedCells = new List<StructuredStripCell2D>();
            AppendOrderedStructuredCoverageCells(orderedCells, supportCells, excisionCells);
            for (int i = 0; i < orderedCells.Count; i++)
            {
                StructuredStripCell2D cell = orderedCells[i];
                if (ShouldBypassStructuredCoverageCellForStartTip(rows, cell))
                    continue;
                if (ShouldBypassStructuredCoverageCellForEndTip(rows, cell))
                    continue;
                if (IsStructuredCellDegenerate(cell))
                    continue;

                bool shouldAuditEndTip = ShouldAuditStructuredCoverageCellForEndTip(rows, cell);
                StructuredEndTipProbeResult preProbe = default;
                if (shouldAuditEndTip)
                {
                    preProbe = RunStructuredEndTipEarlyHelperProbe(
                        remainingFragments,
                        rows,
                        supportCells,
                        excisionCells,
                        domainCells,
                        work,
                        "EndTipOrdinaryAudit.Pre",
                        cell.rowIndex,
                        cell.columnIndex,
                        sourceTriangleIndex);
                }

                SubtractStructuredCellFromFragments(
                    remainingFragments,
                    cell,
                    work,
                    edgeCache,
                    out remainingFragments,
                    out List<List<ClipVertex>> insidePieces);
                if (insidePieces == null)
                    continue;

                List<List<ClipVertex>> target = cell.excision ? excisionPolygons : supportPolygons;
                for (int piece = 0; piece < insidePieces.Count; piece++)
                    TryAppendPolygon(target, insidePieces[piece]);

                if (shouldAuditEndTip)
                {
                    StructuredEndTipProbeResult postProbe = RunStructuredEndTipEarlyHelperProbe(
                        remainingFragments,
                        rows,
                        supportCells,
                        excisionCells,
                        domainCells,
                        work,
                        "EndTipOrdinaryAudit.Post",
                        cell.rowIndex,
                        cell.columnIndex,
                        sourceTriangleIndex);
                    startTipDiagnostics.endOrdinaryAuditWatchedCellCount++;
                    if (startTipDiagnostics.endOrdinaryAuditPreviewEntries != null &&
                        startTipDiagnostics.endOrdinaryAuditPreviewEntries.Count < 6)
                    {
                        startTipDiagnostics.endOrdinaryAuditPreviewEntries.Add(
                            $"{FormatStructuredCoverageCellAuditLabel(cell, sourceTriangleIndex)} pre={preProbe.rowSummary} post={postProbe.rowSummary}");
                    }

                    if (string.IsNullOrEmpty(startTipDiagnostics.endOrdinaryAuditFirstBreakSummary) &&
                        preProbe.hasShared &&
                        !postProbe.hasShared &&
                        postProbe.hasExcisionOnly)
                    {
                        startTipDiagnostics.endOrdinaryAuditFirstBreakSummary =
                            $"{FormatStructuredCoverageCellAuditLabel(cell, sourceTriangleIndex)} " +
                            $"pre={preProbe.rowSummary} preContrib={preProbe.contributorSummary} " +
                            $"post={postProbe.rowSummary} postContrib={postProbe.contributorSummary}";
                    }
                }
            }

            var endTipProcessedFragments = new List<List<ClipVertex>>();
            for (int fragmentIndex = 0; fragmentIndex < remainingFragments.Count; fragmentIndex++)
            {
                List<ClipVertex> fragment = remainingFragments[fragmentIndex];
                if (TryPartitionTriangleAgainstStructuredEndTipWedge(
                    fragment,
                    rows,
                    supportCells,
                    excisionCells,
                    domainCells,
                    work,
                    edgeCache,
                    ref startTipDiagnostics,
                    out List<List<ClipVertex>> endTipRemaining,
                    out List<List<ClipVertex>> endTipSupport,
                    out List<List<ClipVertex>> endTipExcision))
                {
                    if (endTipRemaining != null)
                    {
                        for (int i = 0; i < endTipRemaining.Count; i++)
                            TryAppendPolygon(endTipProcessedFragments, endTipRemaining[i]);
                    }

                    AppendStructuredDiagnosticPolygonClones(startTipStageSupportPolygons, endTipSupport);
                    AppendStructuredDiagnosticPolygonClones(startTipStageExcisionPolygons, endTipExcision);
                    if (endTipSupport != null)
                    {
                        for (int i = 0; i < endTipSupport.Count; i++)
                            TryAppendPolygon(supportPolygons, endTipSupport[i]);
                    }

                    if (endTipExcision != null)
                    {
                        for (int i = 0; i < endTipExcision.Count; i++)
                            TryAppendPolygon(excisionPolygons, endTipExcision[i]);
                    }
                }
                else
                {
                    TryAppendPolygon(endTipProcessedFragments, fragment);
                }
            }
            remainingFragments = endTipProcessedFragments;

            outsidePolygons = remainingFragments;
        }

        private static bool TryPartitionTriangleAgainstStructuredStartTipWedge(
            List<ClipVertex> sourceTriangle,
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> domainCells,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            ref StructuredStartTipWedgeDiagnostics diagnostics,
            out List<List<ClipVertex>> remainingFragments,
            out List<List<ClipVertex>> supportPolygons,
            out List<List<ClipVertex>> excisionPolygons)
        {
            remainingFragments = new List<List<ClipVertex>>(1);
            supportPolygons = new List<List<ClipVertex>>();
            excisionPolygons = new List<List<ClipVertex>>();
            if (sourceTriangle == null || sourceTriangle.Count < 3)
            {
                remainingFragments.Add(sourceTriangle);
                return false;
            }

            remainingFragments.Add(sourceTriangle);
            if (!IsStructuredOpeningTipSegment(rows, 0) ||
                !TryGetStructuredCoverageCell(domainCells, 0, -1, out StructuredStripCell2D domainCell) ||
                !TryGetStructuredCoverageCell(supportCells, 0, 0, out StructuredStripCell2D leftSupportCell) ||
                !TryGetStructuredCoverageCell(supportCells, 0, 2, out StructuredStripCell2D rightSupportCell))
            {
                return false;
            }

            diagnostics.startHelperInvocationCount++;

            SubtractStructuredCellFromFragments(
                remainingFragments,
                domainCell,
                work,
                edgeCache,
                out List<List<ClipVertex>> outsideAfterDomain,
                out List<List<ClipVertex>> domainInsidePieces);
            remainingFragments = outsideAfterDomain ?? new List<List<ClipVertex>>();
            if (domainInsidePieces == null || domainInsidePieces.Count == 0)
                return true;
            diagnostics.startDomainPieceCount += domainInsidePieces.Count;

            for (int i = 0; i < domainInsidePieces.Count; i++)
            {
                List<ClipVertex> domainPiece = domainInsidePieces[i];
                if (domainPiece == null || domainPiece.Count < 3)
                    continue;

                SplitPolygonByStructuredTipBoundary(
                    domainPiece,
                    leftSupportCell,
                    1,
                    work,
                    edgeCache,
                    out List<ClipVertex> centerRightPiece,
                    out List<ClipVertex> leftSupportPiece);
                if (leftSupportPiece != null && leftSupportPiece.Count >= 3)
                {
                    diagnostics.startLbSplitLeftSupportPieceCount++;
                    TryAppendPolygon(supportPolygons, leftSupportPiece);
                }

                if (centerRightPiece == null || centerRightPiece.Count < 3)
                    continue;
                diagnostics.startLbSplitCenterRightPieceCount++;

                SplitPolygonByStructuredTipBoundary(
                    centerRightPiece,
                    rightSupportCell,
                    3,
                    work,
                    edgeCache,
                    out List<ClipVertex> excisionPiece,
                    out List<ClipVertex> rightSupportPiece);
                if (excisionPiece != null && excisionPiece.Count >= 3)
                {
                    diagnostics.startRbSplitExcisionPieceCount++;
                    TryAppendPolygon(excisionPolygons, excisionPiece);
                }

                if (rightSupportPiece != null && rightSupportPiece.Count >= 3)
                {
                    diagnostics.startRbSplitRightSupportPieceCount++;
                    TryAppendPolygon(supportPolygons, rightSupportPiece);
                }
            }

            return true;
        }

        private static bool TryPartitionTriangleAgainstStructuredEndTipWedge(
            List<ClipVertex> sourceTriangle,
            List<StructuredStripRow> rows,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells,
            List<StructuredStripCell2D> domainCells,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            ref StructuredStartTipWedgeDiagnostics diagnostics,
            out List<List<ClipVertex>> remainingFragments,
            out List<List<ClipVertex>> supportPolygons,
            out List<List<ClipVertex>> excisionPolygons)
        {
            remainingFragments = new List<List<ClipVertex>>(1);
            supportPolygons = new List<List<ClipVertex>>();
            excisionPolygons = new List<List<ClipVertex>>();
            if (sourceTriangle == null || sourceTriangle.Count < 3 || rows == null || rows.Count < 2)
            {
                remainingFragments.Add(sourceTriangle);
                return false;
            }

            int rowIndex = rows.Count - 2;
            remainingFragments.Add(sourceTriangle);
            if (!IsStructuredClosingTipSegment(rows, rowIndex) ||
                !TryGetStructuredCoverageCell(domainCells, rowIndex, -1, out StructuredStripCell2D domainCell) ||
                !TryGetStructuredCoverageCell(supportCells, rowIndex, 0, out StructuredStripCell2D leftSupportCell) ||
                !TryGetStructuredCoverageCell(excisionCells, rowIndex, 1, out StructuredStripCell2D excisionCell) ||
                !TryGetStructuredCoverageCell(supportCells, rowIndex, 2, out StructuredStripCell2D rightSupportCell))
            {
                return false;
            }

            diagnostics.endHelperInvocationCount++;

            SubtractStructuredCellFromFragments(
                remainingFragments,
                domainCell,
                work,
                edgeCache,
                out List<List<ClipVertex>> outsideAfterDomain,
                out List<List<ClipVertex>> domainInsidePieces);
            remainingFragments = outsideAfterDomain ?? new List<List<ClipVertex>>();
            if (domainInsidePieces == null || domainInsidePieces.Count == 0)
                return true;
            diagnostics.endDomainPieceCount += domainInsidePieces.Count;

            for (int i = 0; i < domainInsidePieces.Count; i++)
            {
                List<ClipVertex> domainPiece = domainInsidePieces[i];
                if (domainPiece == null || domainPiece.Count < 3)
                    continue;

                // Closing tip must be partitioned the same way as the opening tip: first split
                // the whole end domain by the canonical LB shared edge, then split the remaining
                // center/right piece by the canonical RB shared edge.  The previous independent
                // cell-intersection path could output support-area fragments, but it did not force
                // the Support/Excision shared edge to exist as a real polygon edge on both owners;
                // FE then saw the last row as X-only.  This two-cut path creates those edges at the
                // partition output stage, before later normalization/audit passes ever run.
                SplitPolygonByStructuredTipBoundary(
                    domainPiece,
                    leftSupportCell,
                    1,
                    work,
                    edgeCache,
                    out List<ClipVertex> centerRightPiece,
                    out List<ClipVertex> leftSupportPiece);
                if (leftSupportPiece != null && leftSupportPiece.Count >= 3)
                {
                    diagnostics.endLbSplitLeftSupportPieceCount++;
                    TryAppendPolygon(supportPolygons, leftSupportPiece);
                }

                if (centerRightPiece == null || centerRightPiece.Count < 3)
                    continue;
                diagnostics.endLbSplitCenterRightPieceCount++;

                SplitPolygonByStructuredTipBoundary(
                    centerRightPiece,
                    rightSupportCell,
                    3,
                    work,
                    edgeCache,
                    out List<ClipVertex> excisionPiece,
                    out List<ClipVertex> rightSupportPiece);
                if (excisionPiece != null && excisionPiece.Count >= 3)
                {
                    diagnostics.endRbSplitExcisionPieceCount++;
                    TryAppendPolygon(excisionPolygons, excisionPiece);
                }

                if (rightSupportPiece != null && rightSupportPiece.Count >= 3)
                {
                    diagnostics.endRbSplitRightSupportPieceCount++;
                    TryAppendPolygon(supportPolygons, rightSupportPiece);
                }
            }

            return true;
        }

        private static void AppendStructuredTipCellPieces(
            List<List<ClipVertex>> pieces,
            List<List<ClipVertex>> target,
            ref int counter)
        {
            if (pieces == null || target == null)
                return;

            for (int i = 0; i < pieces.Count; i++)
            {
                List<ClipVertex> piece = pieces[i];
                if (piece == null || piece.Count < 3)
                    continue;

                int beforeCount = target.Count;
                TryAppendPolygon(target, piece);
                if (target.Count > beforeCount)
                    counter++;
            }
        }

        private static void SplitPolygonByStructuredTipBoundary(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            int edgeIndex,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<ClipVertex> outside,
            out List<ClipVertex> inside)
        {
            outside = new List<ClipVertex>();
            inside = new List<ClipVertex>();
            if (polygon == null || polygon.Count < 3)
                return;

            BuildStructuredCellVertices(cell, out Vector2[] vertices, out bool keepLeftOfEdge);
            if (vertices == null || vertices.Length != 4)
                return;

            Vector2 edgeA = vertices[edgeIndex];
            Vector2 edgeB = vertices[(edgeIndex + 1) % 4];
            if ((edgeB - edgeA).sqrMagnitude <= 1e-12f)
                return;

            ClipBoundaryKind boundaryKind = GetStructuredCellEdgeBoundaryKind(cell, edgeIndex);
            SplitPolygonByStructuredEdge(
                polygon,
                cell,
                edgeIndex,
                edgeA,
                edgeB,
                keepLeftOfEdge,
                boundaryKind,
                GetStructuredCellEdgeBoundaryToken(cell, edgeIndex),
                work,
                edgeCache,
                out outside,
                out inside);
        }

        private static bool TryGetStructuredCoverageCell(
            List<StructuredStripCell2D> cells,
            int rowIndex,
            int columnIndex,
            out StructuredStripCell2D cell)
        {
            cell = default;
            if (cells == null)
                return false;

            for (int i = 0; i < cells.Count; i++)
            {
                if (cells[i].rowIndex != rowIndex || cells[i].columnIndex != columnIndex)
                    continue;

                cell = cells[i];
                return true;
            }

            return false;
        }

        private static bool IsStructuredOpeningTipSegment(
            List<StructuredStripRow> rows,
            int rowIndex)
        {
            const float collapseEpsilon = 5e-4f;
            return
                TryGetStructuredSegmentCenterWidths(rows, rowIndex, out float startCenter, out float endCenter) &&
                Mathf.Abs(startCenter) <= collapseEpsilon &&
                endCenter > collapseEpsilon;
        }

        private static bool ShouldBypassStructuredCoverageCellForStartTip(
            List<StructuredStripRow> rows,
            StructuredStripCell2D cell)
        {
            if (!IsStructuredOpeningTipSegment(rows, cell.rowIndex))
                return false;

            return cell.columnIndex == 0 || cell.columnIndex == 1 || cell.columnIndex == 2;
        }

        private static bool ShouldBypassStructuredCoverageCellForEndTip(
            List<StructuredStripRow> rows,
            StructuredStripCell2D cell)
        {
            if (!IsStructuredClosingTipSegment(rows, cell.rowIndex))
                return false;

            return cell.columnIndex == 0 || cell.columnIndex == 1 || cell.columnIndex == 2;
        }

        private static void AppendOrderedStructuredCoverageCells(
            List<StructuredStripCell2D> orderedCells,
            List<StructuredStripCell2D> supportCells,
            List<StructuredStripCell2D> excisionCells)
        {
            if (orderedCells == null)
                return;

            if (supportCells != null)
            {
                for (int i = 0; i < supportCells.Count; i++)
                    orderedCells.Add(supportCells[i]);
            }

            if (excisionCells != null)
            {
                for (int i = 0; i < excisionCells.Count; i++)
                    orderedCells.Add(excisionCells[i]);
            }

            orderedCells.Sort((lhs, rhs) =>
            {
                int rowCompare = lhs.rowIndex.CompareTo(rhs.rowIndex);
                if (rowCompare != 0)
                    return rowCompare;

                int columnCompare = lhs.columnIndex.CompareTo(rhs.columnIndex);
                if (columnCompare != 0)
                    return columnCompare;

                return lhs.excision.CompareTo(rhs.excision);
            });
        }

        private static void IntersectStructuredCellWithPolygon(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<List<ClipVertex>> insideFragments)
        {
            insideFragments = new List<List<ClipVertex>>();
            if (polygon == null || polygon.Count < 3)
                return;

            BuildStructuredCellVertices(cell, out Vector2[] vertices, out bool keepLeftOfEdge);
            SubtractStructuredCellFromPolygon(
                polygon,
                cell,
                vertices,
                keepLeftOfEdge,
                GetStructuredCellBoundaryTokenBase(cell),
                work,
                edgeCache,
                out _,
                out insideFragments);
            InjectStructuredCellCornersIntoPolygons(insideFragments, cell, work, 5e-4f);
            ReinforceStructuredCellBoundaryFlags(insideFragments, cell);
        }

        private static void ReinforceStructuredCellBoundaryFlags(List<List<ClipVertex>> polygons, StructuredStripCell2D cell)
        {
            if (polygons == null)
                return;

            BuildStructuredCellVertices(cell, out Vector2[] vertices, out _);
            if (vertices == null || vertices.Length != 4)
                return;

            for (int polyIndex = 0; polyIndex < polygons.Count; polyIndex++)
            {
                List<ClipVertex> polygon = polygons[polyIndex];
                if (polygon == null || polygon.Count < 2)
                    continue;

                bool changed = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    int j = (i + 1) % polygon.Count;
                    ClipVertex a = polygon[i];
                    ClipVertex b = polygon[j];
                    ClipBoundaryFlags flags = ClipBoundaryFlags.None;

                    for (int edgeIndex = 0; edgeIndex < 4; edgeIndex++)
                    {
                        ClipBoundaryKind edgeKind = GetStructuredCellEdgeBoundaryKind(cell, edgeIndex);
                        ClipBoundaryFlags edgeFlags = BoundaryFlagForKind(edgeKind);
                        if (edgeFlags == ClipBoundaryFlags.None)
                            continue;

                        Vector2 edgeA = vertices[edgeIndex];
                        Vector2 edgeB = vertices[(edgeIndex + 1) % 4];
                        if (IsClipEdgeOnStructuredCellEdge(a, b, edgeA, edgeB))
                            flags |= edgeFlags;
                    }

                    if (flags == ClipBoundaryFlags.None)
                        continue;

                    if ((a.boundaryFlags & flags) != flags)
                    {
                        a.boundaryFlags |= flags;
                        polygon[i] = a;
                        changed = true;
                    }

                    if ((b.boundaryFlags & flags) != flags)
                    {
                        b.boundaryFlags |= flags;
                        polygon[j] = b;
                        changed = true;
                    }
                }

                if (changed)
                    polygons[polyIndex] = polygon;
            }
        }

        private static bool IsClipEdgeOnStructuredCellEdge(ClipVertex a, ClipVertex b, Vector2 edgeA, Vector2 edgeB)
        {
            Vector2 pointA = new Vector2(a.s, a.u);
            Vector2 pointB = new Vector2(b.s, b.u);
            Vector2 mid = 0.5f * (pointA + pointB);
            float length = Vector2.Distance(edgeA, edgeB);
            float tolerance = Mathf.Max(1e-5f, length * 0.01f);
            float toleranceSq = tolerance * tolerance;

            if (DistanceSqPointToSegment(pointA, edgeA, edgeB) > toleranceSq ||
                DistanceSqPointToSegment(pointB, edgeA, edgeB) > toleranceSq ||
                DistanceSqPointToSegment(mid, edgeA, edgeB) > toleranceSq)
            {
                return false;
            }

            return true;
        }

        private static void SubtractStructuredCellFromFragments(
            List<List<ClipVertex>> fragments,
            StructuredStripCell2D cell,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<List<ClipVertex>> remainingFragments,
            out List<List<ClipVertex>> insideFragments)
        {
            remainingFragments = new List<List<ClipVertex>>();
            insideFragments = new List<List<ClipVertex>>();
            if (fragments == null || fragments.Count == 0)
                return;

            BuildStructuredCellVertices(cell, out Vector2[] vertices, out bool keepLeftOfEdge);
            int boundaryTokenBase = GetStructuredCellBoundaryTokenBase(cell);
            for (int i = 0; i < fragments.Count; i++)
            {
                SubtractStructuredCellFromPolygon(
                    fragments[i],
                    cell,
                    vertices,
                    keepLeftOfEdge,
                    boundaryTokenBase,
                    work,
                    edgeCache,
                    out List<List<ClipVertex>> outsidePieces,
                    out List<List<ClipVertex>> insidePieces);
                InjectStructuredCellCornersIntoPolygons(outsidePieces, cell, work, 5e-4f);
                InjectStructuredCellCornersIntoPolygons(insidePieces, cell, work, 5e-4f);
                ReinforceStructuredCellBoundaryFlags(outsidePieces, cell);
                ReinforceStructuredCellBoundaryFlags(insidePieces, cell);
                if (outsidePieces != null)
                    remainingFragments.AddRange(outsidePieces);
                if (insidePieces != null)
                    insideFragments.AddRange(insidePieces);
            }
        }

        private static void InjectStructuredCellCornersIntoPolygons(
            List<List<ClipVertex>> polygons,
            StructuredStripCell2D cell,
            MeshWorkData work,
            float epsilon)
        {
            if (polygons == null || work == null)
                return;

            for (int i = 0; i < polygons.Count; i++)
            {
                InjectStructuredCellCornersIntoPolygon(polygons[i], cell, work, epsilon);
                polygons[i] = CleanupPolygon(polygons[i]);
            }
        }

        private static void InjectStructuredCellCornersIntoPolygon(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            MeshWorkData work,
            float epsilon)
        {
            if (polygon == null || polygon.Count < 2 || work == null)
                return;

            for (int corner = 0; corner < 4; corner++)
                TryInsertStructuredCellCornerIntoPolygon(polygon, cell, corner, work, epsilon);
        }

        private static void SubtractStructuredCellFromPolygon(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            Vector2[] cellVertices,
            bool keepLeftOfEdge,
            int boundaryTokenBase,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<List<ClipVertex>> outsidePieces,
            out List<List<ClipVertex>> insidePieces)
        {
            outsidePieces = new List<List<ClipVertex>>();
            insidePieces = new List<List<ClipVertex>>();
            if (polygon == null || polygon.Count < 3 || cellVertices == null || cellVertices.Length != 4)
                return;

            var stage = new List<List<ClipVertex>>(1) { CleanupPolygon(polygon) };
            for (int edge = 0; edge < 4; edge++)
            {
                Vector2 edgeA = cellVertices[edge];
                Vector2 edgeB = cellVertices[(edge + 1) % 4];
                if ((edgeB - edgeA).sqrMagnitude <= 1e-12f)
                    continue;

                ClipBoundaryKind boundaryKind = GetStructuredCellEdgeBoundaryKind(cell, edge);
                var next = new List<List<ClipVertex>>();
                for (int i = 0; i < stage.Count; i++)
                {
                    SplitPolygonByStructuredEdge(
                        stage[i],
                        cell,
                        edge,
                        edgeA,
                        edgeB,
                        keepLeftOfEdge,
                        boundaryKind,
                        GetStructuredCellEdgeBoundaryToken(cell, edge),
                        work,
                        edgeCache,
                        out List<ClipVertex> outside,
                        out List<ClipVertex> inside);
                    TryAppendPolygon(outsidePieces, outside);
                    TryAppendPolygon(next, inside);
                }

                stage = next;
                if (stage.Count == 0)
                    break;
            }

            insidePieces = stage;
        }

        private static void BuildStructuredCellVertices(
            StructuredStripCell2D cell,
            out Vector2[] vertices,
            out bool keepLeftOfEdge)
        {
            vertices = new[] { cell.a, cell.b, cell.c, cell.d };
            float signedArea =
                Cross2D(vertices[1] - vertices[0], vertices[2] - vertices[0]) +
                Cross2D(vertices[2] - vertices[0], vertices[3] - vertices[0]);
            keepLeftOfEdge = signedArea >= 0f;
        }

        private static bool IsStructuredCellDegenerate(StructuredStripCell2D cell)
        {
            BuildStructuredCellVertices(cell, out Vector2[] vertices, out _);
            float signedArea =
                Cross2D(vertices[1] - vertices[0], vertices[2] - vertices[0]) +
                Cross2D(vertices[2] - vertices[0], vertices[3] - vertices[0]);
            return Mathf.Abs(signedArea) <= 1e-10f;
        }

        private static int GetStructuredSharedBoundaryToken(int rowIndex, ClipBoundaryKind boundaryKind)
        {
            unchecked
            {
                return ((rowIndex + 1) * 97) ^ (((int)boundaryKind + 1) * 17);
            }
        }

        private static int GetStructuredCellBoundaryTokenBase(StructuredStripCell2D cell)
        {
            return (((cell.rowIndex * 3) + cell.columnIndex) * 4) + 1;
        }

        private static int GetStructuredCellEdgeBoundaryToken(StructuredStripCell2D cell, int edgeIndex)
        {
            ClipBoundaryKind boundaryKind = GetStructuredCellEdgeBoundaryKind(cell, edgeIndex);
            switch (boundaryKind)
            {
                case ClipBoundaryKind.LeftSupport:
                case ClipBoundaryKind.Left:
                case ClipBoundaryKind.Right:
                case ClipBoundaryKind.RightSupport:
                    return GetStructuredSharedBoundaryToken(cell.rowIndex, boundaryKind);
                default:
                    return GetStructuredCellBoundaryTokenBase(cell) + edgeIndex;
            }
        }

        private static Vector2 GetStructuredCellCornerCoord(StructuredStripCell2D cell, int cornerIndex)
        {
            switch (cornerIndex)
            {
                case 0:
                    return cell.a;
                case 1:
                    return cell.b;
                case 2:
                    return cell.c;
                case 3:
                    return cell.d;
                default:
                    return Vector2.zero;
            }
        }

        private static BoundaryVertexSample GetStructuredCellCornerSample(StructuredStripCell2D cell, int cornerIndex)
        {
            switch (cornerIndex)
            {
                case 0:
                    return cell.sampleA;
                case 1:
                    return cell.sampleB;
                case 2:
                    return cell.sampleC;
                case 3:
                    return cell.sampleD;
                default:
                    return default;
            }
        }

        private static ClipBoundaryFlags GetStructuredCellCornerFlags(StructuredStripCell2D cell, int cornerIndex)
        {
            ClipBoundaryFlags startFlag = cornerIndex <= 1 ? ClipBoundaryFlags.Start : ClipBoundaryFlags.End;
            ClipBoundaryFlags sideFlag = (cornerIndex == 0 || cornerIndex == 3)
                ? BoundaryFlagForKind(GetStructuredCellLeftBoundaryKind(cell.columnIndex))
                : BoundaryFlagForKind(GetStructuredCellRightBoundaryKind(cell.columnIndex));
            return startFlag | sideFlag;
        }

        private static bool TrySnapClipVertexToStructuredCellCorner(
            ClipVertex vertex,
            StructuredStripCell2D cell,
            int cornerIndex,
            MeshWorkData work,
            out ClipVertex snapped)
        {
            snapped = vertex;
            if (work == null)
                return false;

            BoundaryVertexSample sample = GetStructuredCellCornerSample(cell, cornerIndex);
            Vector2 coord = GetStructuredCellCornerCoord(cell, cornerIndex);
            snapped.s = coord.x;
            snapped.u = coord.y;
            snapped.boundaryFlags |= GetStructuredCellCornerFlags(cell, cornerIndex);

            int vertexIndex = sample.vertexIndex;
            if (vertexIndex < 0 || vertexIndex >= work.vertices.Count)
                return false;

            snapped.local = work.vertices[vertexIndex];
            snapped.world = sample.world;
            if (work.hasUV && vertexIndex < work.uv.Count)
                snapped.uv = work.uv[vertexIndex];
            if (work.hasTangents && vertexIndex < work.tangents.Count)
                snapped.tangent = work.tangents[vertexIndex];
            if (work.hasColors && vertexIndex < work.colors.Count)
                snapped.color = work.colors[vertexIndex];
            snapped.materializedIndex = vertexIndex;
            snapped.supportA = vertexIndex;
            snapped.supportB = vertexIndex;
            return true;
        }

        private static bool TrySnapClipVertexToStructuredCellCornerIfNeeded(
            ClipVertex vertex,
            StructuredStripCell2D cell,
            int edgeIndex,
            float epsilon,
            MeshWorkData work,
            out ClipVertex snapped)
        {
            snapped = vertex;
            if (TrySnapClipVertexToStructuredCellAdjacentCornerByIdentity(vertex, cell, edgeIndex, work, out snapped))
                return true;

            int cornerA = edgeIndex;
            int cornerB = (edgeIndex + 1) % 4;
            Vector2 point = new Vector2(vertex.s, vertex.u);
            Vector2 coordA = GetStructuredCellCornerCoord(cell, cornerA);
            Vector2 coordB = GetStructuredCellCornerCoord(cell, cornerB);
            float epsilonSq = epsilon * epsilon;

            bool nearA = (point - coordA).sqrMagnitude <= epsilonSq;
            bool nearB = (point - coordB).sqrMagnitude <= epsilonSq;
            if (!nearA && !nearB)
                return false;

            if (nearA && nearB)
            {
                if ((point - coordA).sqrMagnitude <= (point - coordB).sqrMagnitude)
                    nearB = false;
                else
                    nearA = false;
            }

            return nearA
                ? TrySnapClipVertexToStructuredCellCorner(vertex, cell, cornerA, work, out snapped)
                : TrySnapClipVertexToStructuredCellCorner(vertex, cell, cornerB, work, out snapped);
        }

        private static bool TrySnapClipVertexToStructuredCellAdjacentCornerByIdentity(
            ClipVertex vertex,
            StructuredStripCell2D cell,
            int edgeIndex,
            MeshWorkData work,
            out ClipVertex snapped)
        {
            snapped = vertex;
            int cornerA = edgeIndex;
            int cornerB = (edgeIndex + 1) % 4;
            if (VertexMatchesStructuredCellCornerSample(vertex, cell, cornerA))
                return TrySnapClipVertexToStructuredCellCorner(vertex, cell, cornerA, work, out snapped);

            if (VertexMatchesStructuredCellCornerSample(vertex, cell, cornerB))
                return TrySnapClipVertexToStructuredCellCorner(vertex, cell, cornerB, work, out snapped);

            return false;
        }

        private static bool VertexMatchesStructuredCellCornerSample(
            ClipVertex vertex,
            StructuredStripCell2D cell,
            int cornerIndex)
        {
            BoundaryVertexSample sample = GetStructuredCellCornerSample(cell, cornerIndex);
            int sampleVertexIndex = sample.vertexIndex;
            if (sampleVertexIndex < 0)
                return false;

            if (vertex.materializedIndex >= 0 && vertex.materializedIndex == sampleVertexIndex)
                return true;

            return
                vertex.supportA >= 0 &&
                vertex.supportA == sampleVertexIndex &&
                vertex.supportB == sampleVertexIndex;
        }

        private static ClipBoundaryKind GetStructuredCellEdgeBoundaryKind(StructuredStripCell2D cell, int edgeIndex)
        {
            switch (edgeIndex)
            {
                case 0:
                    return ClipBoundaryKind.Start;
                case 1:
                    return GetStructuredCellRightBoundaryKind(cell.columnIndex);
                case 2:
                    return ClipBoundaryKind.End;
                case 3:
                    return GetStructuredCellLeftBoundaryKind(cell.columnIndex);
                default:
                    return ClipBoundaryKind.None;
            }
        }

        private static ClipBoundaryKind GetStructuredCellLeftBoundaryKind(int columnIndex)
        {
            switch (columnIndex)
            {
                case -1:
                    return ClipBoundaryKind.LeftSupport;
                case 0:
                    return ClipBoundaryKind.LeftSupport;
                case 1:
                    return ClipBoundaryKind.Left;
                case 2:
                    return ClipBoundaryKind.Right;
                default:
                    return ClipBoundaryKind.None;
            }
        }

        private static ClipBoundaryKind GetStructuredCellRightBoundaryKind(int columnIndex)
        {
            switch (columnIndex)
            {
                case -1:
                    return ClipBoundaryKind.RightSupport;
                case 0:
                    return ClipBoundaryKind.Left;
                case 1:
                    return ClipBoundaryKind.Right;
                case 2:
                    return ClipBoundaryKind.RightSupport;
                default:
                    return ClipBoundaryKind.None;
            }
        }

        private static ClipVertex TagVertexOnStructuredEdge(
            ClipVertex vertex,
            StructuredStripCell2D cell,
            int edgeIndex,
            Vector2 edgeA,
            Vector2 edgeB,
            ClipBoundaryKind boundaryKind,
            float epsilon,
            MeshWorkData work)
        {
            if (boundaryKind == ClipBoundaryKind.None)
                return vertex;

            float distanceSq = DistanceSqPointToSegment(new Vector2(vertex.s, vertex.u), edgeA, edgeB);
            if (distanceSq <= epsilon * epsilon)
            {
                ClipVertex snapped = SnapClipVertexToStructuredEdge(vertex, edgeA, edgeB, boundaryKind);
                if (TrySnapClipVertexToStructuredCellCornerIfNeeded(snapped, cell, edgeIndex, epsilon, work, out ClipVertex cornerSnapped))
                    return cornerSnapped;
                return snapped;
            }
            return vertex;
        }

        private static ClipVertex SnapClipVertexToStructuredEdge(
            ClipVertex vertex,
            Vector2 edgeA,
            Vector2 edgeB,
            ClipBoundaryKind boundaryKind)
        {
            Vector2 edge = edgeB - edgeA;
            float edgeLengthSq = edge.sqrMagnitude;
            if (edgeLengthSq > 1e-12f)
            {
                Vector2 point = new Vector2(vertex.s, vertex.u);
                float t = Mathf.Clamp01(Vector2.Dot(point - edgeA, edge) / edgeLengthSq);
                Vector2 projected = edgeA + edge * t;
                vertex.s = projected.x;
                vertex.u = projected.y;
            }

            vertex.boundaryFlags |= BoundaryFlagForKind(boundaryKind);
            return vertex;
        }

        private static float DistanceSqPointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;
            if (lengthSq <= 1e-12f)
                return (point - a).sqrMagnitude;

            float t = Mathf.Clamp01(Vector2.Dot(point - a, edge) / lengthSq);
            Vector2 projected = a + edge * t;
            return (point - projected).sqrMagnitude;
        }

        private static void SplitPolygonByStructuredEdge(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            int edgeIndex,
            Vector2 edgeA,
            Vector2 edgeB,
            bool keepLeftOfEdge,
            ClipBoundaryKind boundaryKind,
            int boundaryToken,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<ClipVertex> outside,
            out List<ClipVertex> inside)
        {
            outside = new List<ClipVertex>();
            inside = new List<ClipVertex>();
            if (polygon == null || polygon.Count < 3)
                return;

            const float epsilon = 1e-5f;
            const float snapEpsilon = 5e-4f;
            Vector2 edge = edgeB - edgeA;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = TagVertexOnStructuredEdge(polygon[i], cell, edgeIndex, edgeA, edgeB, boundaryKind, snapEpsilon, work);
                ClipVertex next = TagVertexOnStructuredEdge(polygon[(i + 1) % polygon.Count], cell, edgeIndex, edgeA, edgeB, boundaryKind, snapEpsilon, work);
                float currentValue = Cross2D(edge, new Vector2(current.s, current.u) - edgeA);
                float nextValue = Cross2D(edge, new Vector2(next.s, next.u) - edgeA);
                bool currentInside = keepLeftOfEdge ? currentValue >= -epsilon : currentValue <= epsilon;
                bool currentOutside = keepLeftOfEdge ? currentValue <= epsilon : currentValue >= -epsilon;

                if (currentOutside)
                    outside.Add(current);
                if (currentInside)
                    inside.Add(current);

                bool crosses = (currentValue < -epsilon && nextValue > epsilon) || (currentValue > epsilon && nextValue < -epsilon);
                if (!crosses)
                    continue;

                float t = currentValue / (currentValue - nextValue);
                ClipVertex intersection = BuildStructuredEdgeIntersectionVertex(
                    current,
                    next,
                    t,
                    cell,
                    edgeIndex,
                    edgeA,
                    edgeB,
                    boundaryKind,
                    boundaryToken,
                    work,
                    edgeCache);
                outside.Add(intersection);
                inside.Add(intersection);
            }

            outside = CleanupPolygon(outside);
            inside = CleanupPolygon(inside);
            InjectStructuredCellEdgeCornersIntoPolygon(outside, cell, edgeIndex, work, snapEpsilon);
            InjectStructuredCellEdgeCornersIntoPolygon(inside, cell, edgeIndex, work, snapEpsilon);
            outside = CleanupPolygon(outside);
            inside = CleanupPolygon(inside);
        }

        private static void InjectStructuredCellEdgeCornersIntoPolygon(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            int edgeIndex,
            MeshWorkData work,
            float epsilon)
        {
            if (polygon == null || polygon.Count < 2 || work == null)
                return;

            TryInsertStructuredCellCornerIntoPolygon(polygon, cell, edgeIndex, work, epsilon);
            TryInsertStructuredCellCornerIntoPolygon(polygon, cell, (edgeIndex + 1) % 4, work, epsilon);
        }

        private static bool TryInsertStructuredCellCornerIntoPolygon(
            List<ClipVertex> polygon,
            StructuredStripCell2D cell,
            int cornerIndex,
            MeshWorkData work,
            float epsilon)
        {
            if (polygon == null || polygon.Count < 2 || work == null)
                return false;

            BoundaryVertexSample sample = GetStructuredCellCornerSample(cell, cornerIndex);
            if (sample.vertexIndex < 0 || sample.vertexIndex >= work.vertices.Count)
                return false;

            Vector2 cornerCoord = GetStructuredCellCornerCoord(cell, cornerIndex);
            float epsilonSq = epsilon * epsilon;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 currentCoord = new Vector2(polygon[i].s, polygon[i].u);
                if ((currentCoord - cornerCoord).sqrMagnitude > epsilonSq)
                    continue;

                if (TrySnapClipVertexToStructuredCellCorner(polygon[i], cell, cornerIndex, work, out ClipVertex snapped))
                    polygon[i] = snapped;
                return true;
            }

            for (int i = 0; i < polygon.Count; i++)
            {
                int nextIndex = (i + 1) % polygon.Count;
                Vector2 a = new Vector2(polygon[i].s, polygon[i].u);
                Vector2 b = new Vector2(polygon[nextIndex].s, polygon[nextIndex].u);
                if (!TryProjectPointToSegment2D(cornerCoord, a, b, epsilon, out float t))
                    continue;

                int supportA = -1;
                int supportB = -1;
                TryResolveSupportEdge(polygon[i], polygon[nextIndex], out supportA, out supportB);
                ClipVertex seed = LerpClipVertex(polygon[i], polygon[nextIndex], t, supportA, supportB);
                if (!TrySnapClipVertexToStructuredCellCorner(seed, cell, cornerIndex, work, out ClipVertex cornerVertex))
                    return false;

                polygon.Insert(i + 1, cornerVertex);
                return true;
            }

            return false;
        }

        private static bool TryProjectPointToSegment2D(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            float epsilon,
            out float t)
        {
            t = 0f;
            Vector2 edge = b - a;
            float lengthSq = edge.sqrMagnitude;
            if (lengthSq <= 1e-12f)
                return false;

            t = Vector2.Dot(point - a, edge) / lengthSq;
            if (t < -epsilon || t > 1f + epsilon)
                return false;

            t = Mathf.Clamp01(t);
            Vector2 projected = a + edge * t;
            return (projected - point).sqrMagnitude <= epsilon * epsilon;
        }

        private static ClipVertex BuildStructuredEdgeIntersectionVertex(
            ClipVertex current,
            ClipVertex next,
            float t,
            StructuredStripCell2D cell,
            int edgeIndex,
            Vector2 boundaryEdgeA,
            Vector2 boundaryEdgeB,
            ClipBoundaryKind boundaryKind,
            int boundaryToken,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache)
        {
            t = Mathf.Clamp01(t);
            if (TryResolveSupportEdge(current, next, out int supportEdgeA, out int supportEdgeB))
            {
                EdgeBoundaryKey key = CreateEdgeBoundaryKey(supportEdgeA, supportEdgeB, boundaryKind, boundaryToken);
                if (edgeCache.TryGetValue(key, out ClipVertex cached))
                    return cached;

                ClipVertex created = LerpClipVertex(current, next, t, supportEdgeA, supportEdgeB);
                created = SnapClipVertexToStructuredEdge(created, boundaryEdgeA, boundaryEdgeB, boundaryKind);
                TrySnapClipVertexToStructuredCellCornerIfNeeded(created, cell, edgeIndex, 5e-4f, work, out created);
                created.materializedIndex = AddClipVertexToMesh(work, created);
                edgeCache.Add(key, created);
                return created;
            }

            ClipVertex result = LerpClipVertex(current, next, t, -1, -1);
            result = SnapClipVertexToStructuredEdge(result, boundaryEdgeA, boundaryEdgeB, boundaryKind);
            TrySnapClipVertexToStructuredCellCornerIfNeeded(result, cell, edgeIndex, 5e-4f, work, out result);
            return result;
        }

        private static bool IsPointInsideConvexPolygon2D(
            Vector2 point,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            Vector2 d)
        {
            bool hasNegative = false;
            bool hasPositive = false;
            AccumulatePolygonHalfSpace(point, a, b, ref hasNegative, ref hasPositive);
            AccumulatePolygonHalfSpace(point, b, c, ref hasNegative, ref hasPositive);
            AccumulatePolygonHalfSpace(point, c, d, ref hasNegative, ref hasPositive);
            AccumulatePolygonHalfSpace(point, d, a, ref hasNegative, ref hasPositive);
            return !(hasNegative && hasPositive);
        }

        private static void AccumulatePolygonHalfSpace(
            Vector2 point,
            Vector2 edgeA,
            Vector2 edgeB,
            ref bool hasNegative,
            ref bool hasPositive)
        {
            float cross = Cross2D(edgeB - edgeA, point - edgeA);
            if (cross < -1e-6f)
                hasNegative = true;
            else if (cross > 1e-6f)
                hasPositive = true;
        }

        private static bool SegmentsIntersect2D(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float abC = Cross2D(b - a, c - a);
            float abD = Cross2D(b - a, d - a);
            float cdA = Cross2D(d - c, a - c);
            float cdB = Cross2D(d - c, b - c);

            if (Mathf.Abs(abC) <= 1e-6f && IsPointOnSegment2D(c, a, b))
                return true;
            if (Mathf.Abs(abD) <= 1e-6f && IsPointOnSegment2D(d, a, b))
                return true;
            if (Mathf.Abs(cdA) <= 1e-6f && IsPointOnSegment2D(a, c, d))
                return true;
            if (Mathf.Abs(cdB) <= 1e-6f && IsPointOnSegment2D(b, c, d))
                return true;

            return (abC < 0f) != (abD < 0f) && (cdA < 0f) != (cdB < 0f);
        }

        private static bool IsPointOnSegment2D(Vector2 point, Vector2 a, Vector2 b)
        {
            return point.x >= Mathf.Min(a.x, b.x) - 1e-6f &&
                point.x <= Mathf.Max(a.x, b.x) + 1e-6f &&
                point.y >= Mathf.Min(a.y, b.y) - 1e-6f &&
                point.y <= Mathf.Max(a.y, b.y) + 1e-6f;
        }

        private static bool ShouldRemoveTriangleForStructuredStrip(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            List<FrameSample> canonicalFrames,
            float supportOffset,
            float sPad,
            float normalTol)
        {
            Vector3 centroid = (a + b + c) / 3f;
            return IsPointInsideStructuredSupportCorridor(a, canonicalFrames, supportOffset, sPad, normalTol) ||
                IsPointInsideStructuredSupportCorridor(b, canonicalFrames, supportOffset, sPad, normalTol) ||
                IsPointInsideStructuredSupportCorridor(c, canonicalFrames, supportOffset, sPad, normalTol) ||
                IsPointInsideStructuredSupportCorridor(centroid, canonicalFrames, supportOffset, sPad, normalTol);
        }

        private static bool IsPointInsideStructuredSupportCorridor(
            Vector3 world,
            List<FrameSample> canonicalFrames,
            float supportOffset,
            float sPad,
            float normalTol)
        {
            SurfaceCoord coord = EvaluateSurfaceCoord(world, canonicalFrames);
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            if (coord.s < -sPad || coord.s > totalLength + sPad)
                return false;
            if (coord.v < -normalTol)
                return false;

            float halfSupport = 0.5f * coord.frame.targetOpen + supportOffset;
            return Mathf.Abs(coord.u) <= halfSupport + 1e-5f;
        }

        private static int AddStructuredQuadToMesh(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            int a,
            int b,
            int c,
            int d,
            List<int> outTriangles,
            ref float maxPatchTriangleAspect,
            ref float minPatchArea,
            ref int triangulationWindingMismatchCount,
            out string failReason)
        {
            failReason = null;
            if (!IsValidTriangleVertex(a, worldVertices) || !IsValidTriangleVertex(b, worldVertices) ||
                !IsValidTriangleVertex(c, worldVertices) || !IsValidTriangleVertex(d, worldVertices))
            {
                failReason = "Structured quad references an invalid vertex.";
                return 0;
            }

            if (a == b || a == c || a == d || b == c || b == d || c == d)
            {
                failReason = $"Structured quad collapsed to repeated vertices. [{a},{b},{c},{d}]";
                return 0;
            }

            Vector3 wa = worldVertices[a];
            Vector3 wb = worldVertices[b];
            Vector3 wc = worldVertices[c];
            Vector3 wd = worldVertices[d];
            if (!TryComputeStructuredQuadNormal(wa, wb, wc, wd, out Vector3 quadNormal))
            {
                failReason = $"Structured quad normal is degenerate. [{a},{b},{c},{d}]";
                return 0;
            }

            bool validAC = TryEvaluateStructuredQuadSplit(
                wa,
                wb,
                wc,
                wd,
                quadNormal,
                useDiagonalAC: true,
                out float maxAspectAC,
                out float minAreaAC,
                out int mismatchAC);
            bool validBD = TryEvaluateStructuredQuadSplit(
                wa,
                wb,
                wc,
                wd,
                quadNormal,
                useDiagonalAC: false,
                out float maxAspectBD,
                out float minAreaBD,
                out int mismatchBD);
            if (!validAC && !validBD)
            {
                failReason = $"Structured quad split is invalid in 3D. [{a},{b},{c},{d}]";
                return 0;
            }

            bool useAC = validAC;
            if (validAC && validBD)
            {
                const float aspectTieTolerance = 1e-5f;
                if (maxAspectBD < maxAspectAC - aspectTieTolerance)
                {
                    useAC = false;
                }
                else if (Mathf.Abs(maxAspectAC - maxAspectBD) <= aspectTieTolerance)
                {
                    Vector3 sDirection = ((wd - wa) + (wc - wb)).normalized;
                    float acAlignment = Mathf.Abs(Vector3.Dot((wc - wa).normalized, sDirection));
                    float bdAlignment = Mathf.Abs(Vector3.Dot((wd - wb).normalized, sDirection));
                    useAC = acAlignment >= bdAlignment;
                }
            }
            else
            {
                useAC = validAC;
            }

            if (useAC)
            {
                triangulationWindingMismatchCount += AppendStructuredTriangleIndices(outTriangles, a, b, c, quadNormal, worldVertices);
                triangulationWindingMismatchCount += AppendStructuredTriangleIndices(outTriangles, a, c, d, quadNormal, worldVertices);
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, maxAspectAC);
                minPatchArea = Mathf.Min(minPatchArea, minAreaAC);
            }
            else
            {
                triangulationWindingMismatchCount += AppendStructuredTriangleIndices(outTriangles, a, b, d, quadNormal, worldVertices);
                triangulationWindingMismatchCount += AppendStructuredTriangleIndices(outTriangles, b, c, d, quadNormal, worldVertices);
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, maxAspectBD);
                minPatchArea = Mathf.Min(minPatchArea, minAreaBD);
            }

            return 2;
        }

        private static void AppendStructuredStripDebugQuad(
            List<DebugTriangle> debugTriangles,
            int layer,
            DebugTriangleKind kind,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            if (debugTriangles == null)
                return;

            if (!IsDegenerateTriangle(a, b, c))
                debugTriangles.Add(new DebugTriangle { a = a, b = b, c = c, layer = layer, kind = kind });
            if (!IsDegenerateTriangle(a, c, d))
                debugTriangles.Add(new DebugTriangle { a = a, b = c, c = d, layer = layer, kind = kind });
        }

        private static bool TryComputeStructuredQuadNormal(Vector3 a, Vector3 b, Vector3 c, Vector3 d, out Vector3 normal)
        {
            normal = Vector3.Cross(b - a, c - a) + Vector3.Cross(c - a, d - a);
            if (normal.sqrMagnitude <= 1e-12f)
                normal = Vector3.Cross(b - a, d - a) + Vector3.Cross(c - b, d - b);
            if (normal.sqrMagnitude <= 1e-12f)
                return false;

            normal.Normalize();
            return true;
        }

        private static bool TryEvaluateStructuredQuadSplit(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 quadNormal,
            bool useDiagonalAC,
            out float maxAspect,
            out float minArea,
            out int windingMismatchCount)
        {
            maxAspect = 0f;
            minArea = float.PositiveInfinity;
            windingMismatchCount = 0;
            if (useDiagonalAC)
            {
                if (!TryEvaluateStructuredTriangle(a, b, c, quadNormal, out float aspect0, out float area0, out bool flip0) ||
                    !TryEvaluateStructuredTriangle(a, c, d, quadNormal, out float aspect1, out float area1, out bool flip1))
                    return false;

                maxAspect = Mathf.Max(aspect0, aspect1);
                minArea = Mathf.Min(area0, area1);
                windingMismatchCount = (flip0 ? 1 : 0) + (flip1 ? 1 : 0);
                return true;
            }

            if (!TryEvaluateStructuredTriangle(a, b, d, quadNormal, out float aspect2, out float area2, out bool flip2) ||
                !TryEvaluateStructuredTriangle(b, c, d, quadNormal, out float aspect3, out float area3, out bool flip3))
                return false;

            maxAspect = Mathf.Max(aspect2, aspect3);
            minArea = Mathf.Min(area2, area3);
            windingMismatchCount = (flip2 ? 1 : 0) + (flip3 ? 1 : 0);
            return true;
        }

        private static bool TryEvaluateStructuredTriangle(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 quadNormal,
            out float aspect,
            out float area,
            out bool requiresFlip)
        {
            aspect = 0f;
            area = 0f;
            requiresFlip = false;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            float crossMagnitude = cross.magnitude;
            if (crossMagnitude <= 1e-8f)
                return false;

            float signed = Vector3.Dot(cross, quadNormal);
            if (Mathf.Abs(signed) <= 1e-8f)
                return false;

            requiresFlip = signed < 0f;
            area = 0.5f * crossMagnitude;
            aspect = ComputeTriangleAspectRatio3D(a, b, c);
            return !float.IsNaN(aspect) && !float.IsInfinity(aspect);
        }

        private static int AppendStructuredTriangleIndices(
            List<int> outTriangles,
            int a,
            int b,
            int c,
            Vector3 referenceNormal,
            Vector3[] worldVertices)
        {
            Vector3 cross = Vector3.Cross(worldVertices[b] - worldVertices[a], worldVertices[c] - worldVertices[a]);
            bool flip = Vector3.Dot(cross, referenceNormal) < 0f;
            if (flip)
            {
                outTriangles.Add(a);
                outTriangles.Add(c);
                outTriangles.Add(b);
                return 1;
            }

            outTriangles.Add(a);
            outTriangles.Add(b);
            outTriangles.Add(c);
            return 0;
        }

        private static void PartitionTriangleAgainstStripLattice(
            List<ClipVertex> triangle,
            List<FrameSample> partitionFrames,
            float supportOffset,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<List<ClipVertex>> outsidePolygons,
            out List<List<ClipVertex>> supportPolygons,
            out List<List<ClipVertex>> removedPolygons)
        {
            outsidePolygons = new List<List<ClipVertex>>();
            supportPolygons = new List<List<ClipVertex>>();
            removedPolygons = new List<List<ClipVertex>>();
            if (triangle == null || triangle.Count < 3 || partitionFrames == null || partitionFrames.Count < 2)
                return;

            var stage = new List<List<ClipVertex>>(1) { CleanupPolygon(triangle) };
            var next = new List<List<ClipVertex>>();

            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByImplicitBoundary(
                    stage[i],
                    ClipBoundaryKind.Start,
                    0f,
                    0,
                    work,
                    edgeCache,
                    out List<ClipVertex> negative,
                    out List<ClipVertex> positive);
                TryAppendPolygon(outsidePolygons, negative);
                TryAppendPolygon(next, positive);
            }

            stage = next;
            for (int row = 0; row < partitionFrames.Count - 1; row++)
            {
                float rowEnd = partitionFrames[row + 1].s;
                next = new List<List<ClipVertex>>();
                for (int i = 0; i < stage.Count; i++)
                {
                    SplitPolygonByImplicitBoundary(
                        stage[i],
                        ClipBoundaryKind.Row,
                        rowEnd,
                        row + 1,
                        work,
                        edgeCache,
                        out List<ClipVertex> rowSlice,
                        out List<ClipVertex> carry);
                    if (rowSlice.Count >= 3)
                        PartitionRowSliceByRails(rowSlice, row, supportOffset, work, edgeCache, outsidePolygons, supportPolygons, removedPolygons);
                    TryAppendPolygon(next, carry);
                }

                stage = next;
            }

            for (int i = 0; i < stage.Count; i++)
                TryAppendPolygon(outsidePolygons, stage[i]);
        }

        private static void PartitionRowSliceByRails(
            List<ClipVertex> rowSlice,
            int rowIndex,
            float supportOffset,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            List<List<ClipVertex>> outsidePolygons,
            List<List<ClipVertex>> supportPolygons,
            List<List<ClipVertex>> removedPolygons)
        {
            var stage = new List<List<ClipVertex>>(1) { CleanupPolygon(rowSlice) };
            var next = new List<List<ClipVertex>>();
            int boundaryToken = rowIndex + 1;

            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByImplicitBoundary(
                    stage[i],
                    ClipBoundaryKind.LeftSupport,
                    supportOffset,
                    boundaryToken,
                    work,
                    edgeCache,
                    out List<ClipVertex> outsideLeft,
                    out List<ClipVertex> centerSide);
                TryAppendPolygon(outsidePolygons, outsideLeft);
                TryAppendPolygon(next, centerSide);
            }

            stage = next;
            next = new List<List<ClipVertex>>();
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByImplicitBoundary(
                    stage[i],
                    ClipBoundaryKind.Left,
                    0f,
                    boundaryToken,
                    work,
                    edgeCache,
                    out List<ClipVertex> leftSupport,
                    out List<ClipVertex> middle);
                TryAppendPolygon(supportPolygons, leftSupport);
                TryAppendPolygon(next, middle);
            }

            stage = next;
            next = new List<List<ClipVertex>>();
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByImplicitBoundary(
                    stage[i],
                    ClipBoundaryKind.Right,
                    0f,
                    boundaryToken,
                    work,
                    edgeCache,
                    out List<ClipVertex> excision,
                    out List<ClipVertex> rightSide);
                TryAppendPolygon(removedPolygons, excision);
                TryAppendPolygon(next, rightSide);
            }

            stage = next;
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByImplicitBoundary(
                    stage[i],
                    ClipBoundaryKind.RightSupport,
                    supportOffset,
                    boundaryToken,
                    work,
                    edgeCache,
                    out List<ClipVertex> rightSupport,
                    out List<ClipVertex> outsideRight);
                TryAppendPolygon(supportPolygons, rightSupport);
                TryAppendPolygon(outsidePolygons, outsideRight);
            }
        }

        private static void SplitPolygonByImplicitBoundary(
            List<ClipVertex> polygon,
            ClipBoundaryKind boundaryKind,
            float parameter,
            int boundaryToken,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<ClipVertex> negative,
            out List<ClipVertex> positive)
        {
            negative = new List<ClipVertex>();
            positive = new List<ClipVertex>();
            if (polygon == null || polygon.Count < 3)
                return;

            const float epsilon = 1e-5f;
            const float snapEpsilon = 5e-4f;
            float supportOffset = boundaryKind == ClipBoundaryKind.LeftSupport || boundaryKind == ClipBoundaryKind.RightSupport
                ? parameter
                : 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = TagVertexOnImplicitBoundary(polygon[i], boundaryKind, parameter, supportOffset, snapEpsilon);
                ClipVertex nextVertex = TagVertexOnImplicitBoundary(polygon[(i + 1) % polygon.Count], boundaryKind, parameter, supportOffset, snapEpsilon);
                float currentValue = EvaluateStripBoundaryScalar(current, boundaryKind, parameter, supportOffset);
                float nextValue = EvaluateStripBoundaryScalar(nextVertex, boundaryKind, parameter, supportOffset);
                bool currentIsNegative = currentValue <= epsilon;
                bool currentIsPositive = currentValue >= -epsilon;

                if (currentIsNegative)
                    negative.Add(current);
                if (currentIsPositive)
                    positive.Add(current);

                bool crosses = (currentValue < -epsilon && nextValue > epsilon) || (currentValue > epsilon && nextValue < -epsilon);
                if (!crosses)
                    continue;

                float t = currentValue / (currentValue - nextValue);
                ClipVertex intersection = BuildImplicitBoundaryIntersectionVertex(
                    current,
                    nextVertex,
                    t,
                    boundaryKind,
                    parameter,
                    supportOffset,
                    boundaryToken,
                    work,
                    edgeCache);
                negative.Add(intersection);
                positive.Add(intersection);
            }

            negative = CleanupPolygon(negative);
            positive = CleanupPolygon(positive);
        }

        private static ClipVertex TagVertexOnImplicitBoundary(
            ClipVertex vertex,
            ClipBoundaryKind boundaryKind,
            float parameter,
            float supportOffset,
            float epsilon)
        {
            float value = Mathf.Abs(EvaluateStripBoundaryScalar(vertex, boundaryKind, parameter, supportOffset));
            if (value <= epsilon)
                vertex = SnapClipVertexToImplicitBoundary(vertex, boundaryKind, parameter, supportOffset);
            return vertex;
        }

        private static float EvaluateStripBoundaryScalar(ClipVertex vertex, ClipBoundaryKind boundaryKind, float parameter, float supportOffset)
        {
            switch (boundaryKind)
            {
                case ClipBoundaryKind.Start:
                case ClipBoundaryKind.End:
                case ClipBoundaryKind.Row:
                    return vertex.s - parameter;
                case ClipBoundaryKind.Left:
                    return vertex.u + 0.5f * vertex.targetOpen;
                case ClipBoundaryKind.Right:
                    return vertex.u - 0.5f * vertex.targetOpen;
                case ClipBoundaryKind.LeftSupport:
                    return vertex.u + 0.5f * vertex.targetOpen + supportOffset;
                case ClipBoundaryKind.RightSupport:
                    return vertex.u - (0.5f * vertex.targetOpen + supportOffset);
                default:
                    return 0f;
            }
        }

        private static ClipVertex BuildImplicitBoundaryIntersectionVertex(
            ClipVertex current,
            ClipVertex next,
            float t,
            ClipBoundaryKind boundaryKind,
            float parameter,
            float supportOffset,
            int boundaryToken,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache)
        {
            t = Mathf.Clamp01(t);
            if (TryResolveSupportEdge(current, next, out int edgeA, out int edgeB))
            {
                EdgeBoundaryKey key = CreateEdgeBoundaryKey(edgeA, edgeB, boundaryKind, boundaryToken);
                if (edgeCache.TryGetValue(key, out ClipVertex cached))
                    return cached;

                ClipVertex created = LerpClipVertex(current, next, t, edgeA, edgeB);
                created = SnapClipVertexToImplicitBoundary(created, boundaryKind, parameter, supportOffset);
                created.materializedIndex = AddClipVertexToMesh(work, created);
                edgeCache.Add(key, created);
                return created;
            }

            ClipVertex result = LerpClipVertex(current, next, t, -1, -1);
            result = SnapClipVertexToImplicitBoundary(result, boundaryKind, parameter, supportOffset);
            return result;
        }

        private static ClipVertex SnapClipVertexToImplicitBoundary(
            ClipVertex vertex,
            ClipBoundaryKind boundaryKind,
            float parameter,
            float supportOffset)
        {
            switch (boundaryKind)
            {
                case ClipBoundaryKind.Start:
                case ClipBoundaryKind.End:
                case ClipBoundaryKind.Row:
                    vertex.s = parameter;
                    break;
                case ClipBoundaryKind.Left:
                    vertex.u = -0.5f * vertex.targetOpen;
                    break;
                case ClipBoundaryKind.Right:
                    vertex.u = 0.5f * vertex.targetOpen;
                    break;
                case ClipBoundaryKind.LeftSupport:
                    vertex.u = -(0.5f * vertex.targetOpen + supportOffset);
                    break;
                case ClipBoundaryKind.RightSupport:
                    vertex.u = 0.5f * vertex.targetOpen + supportOffset;
                    break;
            }

            vertex.boundaryFlags |= BoundaryFlagForKind(boundaryKind);
            return vertex;
        }

        private static ClipVertex LerpClipVertex(
            ClipVertex a,
            ClipVertex b,
            float t,
            int supportA,
            int supportB)
        {
            return new ClipVertex
            {
                local = Vector3.Lerp(a.local, b.local, t),
                world = Vector3.Lerp(a.world, b.world, t),
                uv = Vector2.Lerp(a.uv, b.uv, t),
                tangent = Vector4.Lerp(a.tangent, b.tangent, t),
                color = Color.Lerp(a.color, b.color, t),
                s = Mathf.Lerp(a.s, b.s, t),
                strokeT = Mathf.Lerp(a.strokeT, b.strokeT, t),
                u = Mathf.Lerp(a.u, b.u, t),
                v = Mathf.Lerp(a.v, b.v, t),
                targetOpen = Mathf.Lerp(a.targetOpen, b.targetOpen, t),
                distanceToCenter = Mathf.Lerp(a.distanceToCenter, b.distanceToCenter, t),
                materializedIndex = -1,
                supportA = supportA,
                supportB = supportB,
                boundaryFlags = a.boundaryFlags & b.boundaryFlags,
            };
        }

        private static int TriangulatePartitionPolygonIntoMesh(
            List<ClipVertex> polygon,
            MeshWorkData work,
            List<int> outTriangles,
            ref int droppedDegeneratePolygonCount,
            out string failReason,
            out float maxAspectRatio,
            out float minArea,
            out int invertedTriangleCount)
        {
            int added = TriangulateConvexPolygonIntoMesh(
                polygon,
                work,
                outTriangles,
                out maxAspectRatio,
                out minArea,
                out invertedTriangleCount,
                out failReason);
            if (string.IsNullOrEmpty(failReason))
                return added;

            if (failReason.Contains("zero-area polygon"))
            {
                droppedDegeneratePolygonCount++;
                maxAspectRatio = 0f;
                minArea = float.PositiveInfinity;
                invertedTriangleCount = 0;
                failReason = null;
                return 0;
            }
            return 0;
        }

        // Support-side merged cells are not guaranteed to stay convex after semantic stitching.
        // Route them through the general polygon triangulator so concave cells do not fan-split into slivers.
        private static int TriangulateGeneralPartitionPolygonIntoMesh(
            List<ClipVertex> polygon,
            MeshWorkData work,
            List<int> outTriangles,
            ref int droppedDegeneratePolygonCount,
            out string failReason,
            out float maxAspectRatio,
            out float minArea,
            out int invertedTriangleCount)
        {
            var stagedTriangles = new List<int>(Mathf.Max(0, (polygon != null ? polygon.Count : 0) - 2) * 3);
            int added = TriangulatePolygonIntoMesh(
                polygon,
                work,
                stagedTriangles,
                out maxAspectRatio,
                out minArea,
                out invertedTriangleCount,
                out failReason);
            if (string.IsNullOrEmpty(failReason))
            {
                outTriangles.AddRange(stagedTriangles);
                return added;
            }

            if (failReason.Contains("zero-area polygon"))
            {
                droppedDegeneratePolygonCount++;
                maxAspectRatio = 0f;
                minArea = float.PositiveInfinity;
                invertedTriangleCount = 0;
                failReason = null;
                return 0;
            }

            return 0;
        }

        private static int TriangulateConvexPolygonIntoMesh(
            List<ClipVertex> polygon,
            MeshWorkData work,
            List<int> outTriangles,
            out float maxAspectRatio,
            out float minArea,
            out int invertedTriangleCount,
            out string failReason)
        {
            maxAspectRatio = 0f;
            minArea = float.PositiveInfinity;
            invertedTriangleCount = 0;
            failReason = null;
            if (polygon == null || polygon.Count < 3)
                return 0;

            for (int i = 0; i < polygon.Count; i++)
                polygon[i] = EnsureVertexMaterialized(polygon[i], work);

            if (!TryBuildPolygonTriangulationContext(polygon, out PolygonTriangulationContext triangulationContext))
            {
                failReason = "Strip remesh found a zero-area polygon in (s,u) and local triangle plane.";
                return 0;
            }

            bool ccw = triangulationContext.signedArea > 0f;
            int added = 0;
            if (polygon.Count == 3)
            {
                AppendPolygonTriangle(
                    polygon,
                    0,
                    1,
                    2,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
                return added;
            }

            if (polygon.Count == 4)
            {
                AppendBestQuadDiagonal(
                    polygon,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
                return added;
            }

            int bestRoot = -1;
            float bestScore = float.PositiveInfinity;
            for (int root = 0; root < polygon.Count; root++)
            {
                if (!TryScoreConvexFan(polygon, root, out float score))
                    continue;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestRoot = root;
                }
            }

            if (bestRoot < 0)
            {
                failReason = "Strip remesh failed to score a valid convex fan.";
                return 0;
            }

            for (int offset = 1; offset < polygon.Count - 1; offset++)
            {
                int aIndex = bestRoot;
                int bIndex = (bestRoot + offset) % polygon.Count;
                int cIndex = (bestRoot + offset + 1) % polygon.Count;
                AppendPolygonTriangle(
                    polygon,
                    aIndex,
                    bIndex,
                    cIndex,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
            }

            if (float.IsInfinity(minArea))
                minArea = 0f;
            return added;
        }

        private static void AppendBestQuadDiagonal(
            List<ClipVertex> polygon,
            PolygonTriangulationContext triangulationContext,
            bool ccw,
            List<int> outTriangles,
            ref int added,
            ref float maxAspectRatio,
            ref float minArea,
            ref int invertedTriangleCount)
        {
            float diag02Score = ScoreQuadDiagonal(polygon[0], polygon[1], polygon[2], polygon[3], true);
            float diag13Score = ScoreQuadDiagonal(polygon[0], polygon[1], polygon[2], polygon[3], false);
            if (diag02Score <= diag13Score)
            {
                AppendPolygonTriangle(
                    polygon,
                    0,
                    1,
                    2,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
                AppendPolygonTriangle(
                    polygon,
                    0,
                    2,
                    3,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
            }
            else
            {
                AppendPolygonTriangle(
                    polygon,
                    0,
                    1,
                    3,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
                AppendPolygonTriangle(
                    polygon,
                    1,
                    2,
                    3,
                    triangulationContext,
                    ccw,
                    outTriangles,
                    ref added,
                    ref maxAspectRatio,
                    ref minArea,
                    ref invertedTriangleCount);
            }
        }

        private static float ScoreQuadDiagonal(ClipVertex a, ClipVertex b, ClipVertex c, ClipVertex d, bool useAC)
        {
            Vector3 t0a = a.world;
            Vector3 t0b = b.world;
            Vector3 t0c = useAC ? c.world : d.world;
            Vector3 t1a = useAC ? a.world : b.world;
            Vector3 t1b = c.world;
            Vector3 t1c = d.world;

            if (IsDegenerateTriangle(t0a, t0b, t0c) || IsDegenerateTriangle(t1a, t1b, t1c))
                return float.PositiveInfinity;

            float aspect0 = ComputeTriangleAspectRatio3D(t0a, t0b, t0c);
            float aspect1 = ComputeTriangleAspectRatio3D(t1a, t1b, t1c);
            Vector2 diagonal = useAC
                ? new Vector2(c.s - a.s, c.u - a.u)
                : new Vector2(d.s - b.s, d.u - b.u);
            float diagonalLength = diagonal.magnitude;
            Vector2 diagonalDir = diagonalLength > 1e-6f ? diagonal / diagonalLength : Vector2.right;
            float directionPenalty = 1f - Mathf.Abs(Vector2.Dot(diagonalDir, Vector2.right));
            return Mathf.Max(aspect0, aspect1) * 10f + directionPenalty * 2f + diagonalLength;
        }

        private static bool TryScoreConvexFan(List<ClipVertex> polygon, int root, out float score)
        {
            score = 0f;
            if (polygon == null || polygon.Count < 3)
                return false;

            for (int offset = 1; offset < polygon.Count - 1; offset++)
            {
                ClipVertex a = polygon[root];
                ClipVertex b = polygon[(root + offset) % polygon.Count];
                ClipVertex c = polygon[(root + offset + 1) % polygon.Count];
                if (IsDegenerateTriangle(a.world, b.world, c.world))
                    return false;

                float aspect = ComputeTriangleAspectRatio3D(a.world, b.world, c.world);
                Vector2 diagonal = new Vector2(c.s - a.s, c.u - a.u);
                float diagonalLength = diagonal.magnitude;
                Vector2 diagonalDir = diagonalLength > 1e-6f ? diagonal / diagonalLength : Vector2.right;
                float directionPenalty = 1f - Mathf.Abs(Vector2.Dot(diagonalDir, Vector2.right));
                score = Mathf.Max(score, aspect * 10f + directionPenalty * 2f + diagonalLength);
            }

            return true;
        }

        private static ClipVertex CreateClipVertexFromIndex(
            MeshWorkData work,
            Vector3[] worldVertices,
            int vertexIndex,
            List<FrameSample> canonicalFrames)
        {
            Vector3 local = work.vertices[vertexIndex];
            Vector3 world = worldVertices[vertexIndex];
            SurfaceCoord coord = EvaluateSurfaceCoord(world, canonicalFrames);
            return new ClipVertex
            {
                local = local,
                world = world,
                uv = work.hasUV ? work.uv[vertexIndex] : Vector2.zero,
                tangent = work.hasTangents ? work.tangents[vertexIndex] : new Vector4(1f, 0f, 0f, 1f),
                color = work.hasColors ? work.colors[vertexIndex] : Color.white,
                s = coord.s,
                strokeT = coord.frame.strokeT,
                u = coord.u,
                v = coord.v,
                targetOpen = coord.frame.targetOpen,
                distanceToCenter = coord.distanceToCenter,
                materializedIndex = vertexIndex,
                supportA = vertexIndex,
                supportB = vertexIndex,
                boundaryFlags = ClipBoundaryFlags.None,
            };
        }

        private static ClipVertex CreateClipVertexFromTriangleProjection(
            MeshWorkData work,
            Vector3[] worldVertices,
            int triangleIndex,
            Vector3 projectedWorld,
            List<FrameSample> canonicalFrames)
        {
            GetTriangle(work.triangles, triangleIndex, out int ia, out int ib, out int ic);
            Vector3 barycentric = ComputeTriangleBarycentricCoordinates(
                projectedWorld,
                worldVertices[ia],
                worldVertices[ib],
                worldVertices[ic]);
            Vector3 local = work.vertices[ia] * barycentric.x +
                work.vertices[ib] * barycentric.y +
                work.vertices[ic] * barycentric.z;
            SurfaceCoord coord = EvaluateSurfaceCoord(projectedWorld, canonicalFrames);
            return new ClipVertex
            {
                local = local,
                world = projectedWorld,
                uv = work.hasUV
                    ? work.uv[ia] * barycentric.x + work.uv[ib] * barycentric.y + work.uv[ic] * barycentric.z
                    : Vector2.zero,
                tangent = work.hasTangents
                    ? work.tangents[ia] * barycentric.x + work.tangents[ib] * barycentric.y + work.tangents[ic] * barycentric.z
                    : new Vector4(1f, 0f, 0f, 1f),
                color = work.hasColors
                    ? work.colors[ia] * barycentric.x + work.colors[ib] * barycentric.y + work.colors[ic] * barycentric.z
                    : Color.white,
                s = coord.s,
                strokeT = coord.frame.strokeT,
                u = coord.u,
                v = coord.v,
                targetOpen = coord.frame.targetOpen,
                distanceToCenter = coord.distanceToCenter,
                materializedIndex = -1,
                supportA = ia,
                supportB = ic,
                boundaryFlags = ClipBoundaryFlags.None,
            };
        }

        private static bool IsUnchangedOriginalTrianglePolygon(
            List<List<ClipVertex>> polygons,
            int ia,
            int ib,
            int ic)
        {
            if (polygons == null || polygons.Count != 1)
                return false;

            List<ClipVertex> polygon = polygons[0];
            if (polygon == null || polygon.Count != 3)
                return false;

            var expected = new HashSet<int> { ia, ib, ic };
            var actual = new HashSet<int>();
            for (int i = 0; i < polygon.Count; i++)
            {
                if (polygon[i].materializedIndex < 0)
                    return false;
                actual.Add(polygon[i].materializedIndex);
            }

            return actual.SetEquals(expected);
        }

        private static Vector3 ComputeTriangleBarycentricCoordinates(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 v0 = b - a;
            Vector3 v1 = c - a;
            Vector3 v2 = point - a;
            float d00 = Vector3.Dot(v0, v0);
            float d01 = Vector3.Dot(v0, v1);
            float d11 = Vector3.Dot(v1, v1);
            float d20 = Vector3.Dot(v2, v0);
            float d21 = Vector3.Dot(v2, v1);
            float denom = d00 * d11 - d01 * d01;
            if (Mathf.Abs(denom) <= 1e-10f)
                return new Vector3(1f, 0f, 0f);

            float v = (d11 * d20 - d01 * d21) / denom;
            float w = (d00 * d21 - d01 * d20) / denom;
            float u = 1f - v - w;
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);
            w = Mathf.Clamp01(w);
            float sum = Mathf.Max(1e-8f, u + v + w);
            return new Vector3(u / sum, v / sum, w / sum);
        }

        private static SurfaceCoord EvaluateSurfaceCoord(Vector3 worldPoint, List<FrameSample> canonicalFrames)
        {
            FrameSample frame = SampleFrameAtPoint(canonicalFrames, worldPoint);
            Vector3 delta = worldPoint - frame.center;
            return new SurfaceCoord
            {
                frame = frame,
                s = frame.s,
                u = Vector3.Dot(delta, frame.binormal),
                v = Vector3.Dot(delta, frame.normal),
                distanceToCenter = delta.magnitude,
            };
        }

        private static void PartitionTriangleAgainstBand(
            List<ClipVertex> triangle,
            float totalLength,
            float halfOpen,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<List<ClipVertex>> keptPolygons,
            out List<List<ClipVertex>> removedPolygons)
        {
            keptPolygons = new List<List<ClipVertex>>();
            removedPolygons = new List<List<ClipVertex>>();
            if (triangle == null || triangle.Count < 3)
                return;

            var stage = new List<List<ClipVertex>>(1) { CleanupPolygon(triangle) };
            var next = new List<List<ClipVertex>>();

            next.Clear();
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByBoundary(stage[i], ClipScalarAxis.U, -halfOpen, ClipBoundaryKind.Left, work, edgeCache, out List<ClipVertex> negative, out List<ClipVertex> positive);
                TryAppendPolygon(keptPolygons, negative);
                TryAppendPolygon(next, positive);
            }

            stage = next;
            next = new List<List<ClipVertex>>();
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByBoundary(stage[i], ClipScalarAxis.U, halfOpen, ClipBoundaryKind.Right, work, edgeCache, out List<ClipVertex> negative, out List<ClipVertex> positive);
                TryAppendPolygon(next, negative);
                TryAppendPolygon(keptPolygons, positive);
            }

            stage = next;
            next = new List<List<ClipVertex>>();
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByBoundary(stage[i], ClipScalarAxis.S, 0f, ClipBoundaryKind.Start, work, edgeCache, out List<ClipVertex> negative, out List<ClipVertex> positive);
                TryAppendPolygon(keptPolygons, negative);
                TryAppendPolygon(next, positive);
            }

            stage = next;
            for (int i = 0; i < stage.Count; i++)
            {
                SplitPolygonByBoundary(stage[i], ClipScalarAxis.S, totalLength, ClipBoundaryKind.End, work, edgeCache, out List<ClipVertex> negative, out List<ClipVertex> positive);
                TryAppendPolygon(removedPolygons, negative);
                TryAppendPolygon(keptPolygons, positive);
            }
        }

        private static void SplitPolygonByBoundary(
            List<ClipVertex> polygon,
            ClipScalarAxis axis,
            float threshold,
            ClipBoundaryKind boundaryKind,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache,
            out List<ClipVertex> negative,
            out List<ClipVertex> positive)
        {
            negative = new List<ClipVertex>();
            positive = new List<ClipVertex>();
            if (polygon == null || polygon.Count < 3)
                return;

            const float epsilon = 1e-5f;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = TagVertexOnBoundary(polygon[i], axis, threshold, boundaryKind, epsilon);
                ClipVertex next = TagVertexOnBoundary(polygon[(i + 1) % polygon.Count], axis, threshold, boundaryKind, epsilon);
                float currentValue = GetScalarValue(current, axis) - threshold;
                float nextValue = GetScalarValue(next, axis) - threshold;
                bool currentIsNegative = currentValue <= epsilon;
                bool currentIsPositive = currentValue >= -epsilon;

                if (currentIsNegative)
                    negative.Add(current);
                if (currentIsPositive)
                    positive.Add(current);

                bool crosses = (currentValue < -epsilon && nextValue > epsilon) || (currentValue > epsilon && nextValue < -epsilon);
                if (!crosses)
                    continue;

                float t = currentValue / (currentValue - nextValue);
                ClipVertex intersection = BuildBoundaryIntersectionVertex(current, next, t, axis, threshold, boundaryKind, work, edgeCache);
                negative.Add(intersection);
                positive.Add(intersection);
            }

            negative = CleanupPolygon(negative);
            positive = CleanupPolygon(positive);
        }

        private static float GetScalarValue(ClipVertex vertex, ClipScalarAxis axis)
        {
            return axis == ClipScalarAxis.S ? vertex.s : vertex.u;
        }

        private static ClipVertex BuildBoundaryIntersectionVertex(
            ClipVertex current,
            ClipVertex next,
            float t,
            ClipScalarAxis axis,
            float threshold,
            ClipBoundaryKind boundaryKind,
            MeshWorkData work,
            Dictionary<EdgeBoundaryKey, ClipVertex> edgeCache)
        {
            t = Mathf.Clamp01(t);
            if (TryResolveSupportEdge(current, next, out int edgeA, out int edgeB))
            {
                EdgeBoundaryKey key = CreateEdgeBoundaryKey(edgeA, edgeB, boundaryKind);
                if (edgeCache.TryGetValue(key, out ClipVertex cached))
                    return cached;

                ClipVertex created = InterpolateClipVertex(current, next, t, axis, threshold, edgeA, edgeB);
                created.boundaryFlags |= BoundaryFlagForKind(boundaryKind);
                created.materializedIndex = AddClipVertexToMesh(work, created);
                edgeCache.Add(key, created);
                return created;
            }

            ClipVertex result = InterpolateClipVertex(current, next, t, axis, threshold, -1, -1);
            result.boundaryFlags |= BoundaryFlagForKind(boundaryKind);
            return result;
        }

        private static ClipVertex TagVertexOnBoundary(
            ClipVertex vertex,
            ClipScalarAxis axis,
            float threshold,
            ClipBoundaryKind boundaryKind,
            float epsilon)
        {
            if (boundaryKind == ClipBoundaryKind.None)
                return vertex;

            float value = Mathf.Abs(GetScalarValue(vertex, axis) - threshold);
            if (value <= epsilon)
                vertex.boundaryFlags |= BoundaryFlagForKind(boundaryKind);
            return vertex;
        }

        private static ClipBoundaryFlags BoundaryFlagForKind(ClipBoundaryKind kind)
        {
            switch (kind)
            {
                case ClipBoundaryKind.Start:
                    return ClipBoundaryFlags.Start;
                case ClipBoundaryKind.End:
                    return ClipBoundaryFlags.End;
                case ClipBoundaryKind.Left:
                    return ClipBoundaryFlags.Left;
                case ClipBoundaryKind.Right:
                    return ClipBoundaryFlags.Right;
                case ClipBoundaryKind.LeftSupport:
                    return ClipBoundaryFlags.LeftSupport;
                case ClipBoundaryKind.RightSupport:
                    return ClipBoundaryFlags.RightSupport;
                case ClipBoundaryKind.Row:
                    return ClipBoundaryFlags.Row;
                default:
                    return ClipBoundaryFlags.None;
            }
        }

        private static ClipVertex InterpolateClipVertex(
            ClipVertex a,
            ClipVertex b,
            float t,
            ClipScalarAxis axis,
            float threshold,
            int supportA,
            int supportB)
        {
            ClipVertex result = new ClipVertex
            {
                local = Vector3.Lerp(a.local, b.local, t),
                world = Vector3.Lerp(a.world, b.world, t),
                uv = Vector2.Lerp(a.uv, b.uv, t),
                tangent = Vector4.Lerp(a.tangent, b.tangent, t),
                color = Color.Lerp(a.color, b.color, t),
                s = Mathf.Lerp(a.s, b.s, t),
                strokeT = Mathf.Lerp(a.strokeT, b.strokeT, t),
                u = Mathf.Lerp(a.u, b.u, t),
                v = Mathf.Lerp(a.v, b.v, t),
                targetOpen = Mathf.Lerp(a.targetOpen, b.targetOpen, t),
                distanceToCenter = Mathf.Lerp(a.distanceToCenter, b.distanceToCenter, t),
                materializedIndex = -1,
                supportA = supportA,
                supportB = supportB,
                boundaryFlags = a.boundaryFlags & b.boundaryFlags,
            };

            if (axis == ClipScalarAxis.S)
                result.s = threshold;
            else
                result.u = threshold;

            return result;
        }

        private static bool TryResolveSupportEdge(ClipVertex a, ClipVertex b, out int edgeA, out int edgeB)
        {
            edgeA = -1;
            edgeB = -1;
            if (a.supportA < 0 || a.supportB < 0 || b.supportA < 0 || b.supportB < 0)
                return false;

            int a0 = Mathf.Min(a.supportA, a.supportB);
            int a1 = Mathf.Max(a.supportA, a.supportB);
            int b0 = Mathf.Min(b.supportA, b.supportB);
            int b1 = Mathf.Max(b.supportA, b.supportB);

            bool aIsVertex = a0 == a1;
            bool bIsVertex = b0 == b1;

            if (aIsVertex && bIsVertex)
            {
                if (a0 == b0)
                    return false;

                edgeA = Mathf.Min(a0, b0);
                edgeB = Mathf.Max(a0, b0);
                return true;
            }

            if (a0 == b0 && a1 == b1 && a0 != a1)
            {
                edgeA = a0;
                edgeB = a1;
                return true;
            }

            if (aIsVertex && !bIsVertex && (a0 == b0 || a0 == b1))
            {
                edgeA = b0;
                edgeB = b1;
                return true;
            }

            if (bIsVertex && !aIsVertex && (b0 == a0 || b0 == a1))
            {
                edgeA = a0;
                edgeB = a1;
                return true;
            }

            return false;
        }

        private static EdgeBoundaryKey CreateEdgeBoundaryKey(int a, int b, ClipBoundaryKind boundaryKind, int token = 0)
        {
            return new EdgeBoundaryKey
            {
                a = Mathf.Min(a, b),
                b = Mathf.Max(a, b),
                boundary = boundaryKind,
                token = token,
            };
        }

        private static List<ClipVertex> CleanupPolygon(List<ClipVertex> polygon)
        {
            var cleaned = new List<ClipVertex>();
            if (polygon == null || polygon.Count == 0)
                return cleaned;

            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex current = polygon[i];
                if (cleaned.Count > 0 && (cleaned[cleaned.Count - 1].world - current.world).sqrMagnitude <= 1e-12f)
                    continue;
                cleaned.Add(current);
            }

            if (cleaned.Count > 1 && (cleaned[0].world - cleaned[cleaned.Count - 1].world).sqrMagnitude <= 1e-12f)
                cleaned.RemoveAt(cleaned.Count - 1);

            bool changed = true;
            while (changed && cleaned.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < cleaned.Count; i++)
                {
                    int prevIndex = (i - 1 + cleaned.Count) % cleaned.Count;
                    int nextIndex = (i + 1) % cleaned.Count;
                    if (ShouldPreserveSemanticPolygonVertex(cleaned[prevIndex], cleaned[i], cleaned[nextIndex]))
                        continue;
                    if (IsNearlyCollinear(cleaned[prevIndex].world, cleaned[i].world, cleaned[nextIndex].world))
                    {
                        cleaned.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }
            }

            return cleaned;
        }

        private static bool IsNearlyCollinear(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 bc = c - b;
            float abSq = ab.sqrMagnitude;
            float bcSq = bc.sqrMagnitude;
            if (abSq <= 1e-12f || bcSq <= 1e-12f)
                return true;

            float crossSq = Vector3.Cross(ab, bc).sqrMagnitude;
            float scale = Mathf.Max(abSq, bcSq);
            return crossSq <= scale * 1e-10f;
        }

        private static bool ShouldPreserveSemanticPolygonVertex(ClipVertex prev, ClipVertex current, ClipVertex next)
        {
            ClipBoundaryFlags currentFlags = current.boundaryFlags;
            if (currentFlags == ClipBoundaryFlags.None)
                return false;

            if (CountBoundaryFlagBits(currentFlags) >= 2)
                return true;

            ClipBoundaryFlags prevFlags = prev.boundaryFlags;
            ClipBoundaryFlags nextFlags = next.boundaryFlags;
            bool prevShares = (prevFlags & currentFlags) != ClipBoundaryFlags.None;
            bool nextShares = (nextFlags & currentFlags) != ClipBoundaryFlags.None;
            return !prevShares || !nextShares;
        }

        private static int CountBoundaryFlagBits(ClipBoundaryFlags flags)
        {
            int count = 0;
            int value = (int)flags;
            while (value != 0)
            {
                count += value & 1;
                value >>= 1;
            }

            return count;
        }

        private static void TryAppendPolygon(List<List<ClipVertex>> polygons, List<ClipVertex> polygon)
        {
            List<ClipVertex> cleaned = CleanupPolygon(polygon);
            if (cleaned.Count < 3 || ComputePolygonAreaSq(cleaned) <= 1e-12f)
                return;
            polygons.Add(cleaned);
        }

        private static float ComputePolygonAreaSq(List<ClipVertex> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            Vector3 origin = polygon[0].world;
            float maxAreaSq = 0f;
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                Vector3 cross = Vector3.Cross(polygon[i].world - origin, polygon[i + 1].world - origin);
                maxAreaSq = Mathf.Max(maxAreaSq, cross.sqrMagnitude * 0.25f);
            }

            return maxAreaSq;
        }

        private static bool TryExtractSingleSidedPatchTriangles(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<Vector3> centerCurveWorld,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            out PatchSelectionResult result)
        {
            result = new PatchSelectionResult();
            if (work == null || worldVertices == null || centerCurveWorld == null || centerCurveWorld.Count < 2 || canonicalFrames == null || canonicalFrames.Count < 2)
            {
                result.failReason = "Patch extraction input is invalid.";
                return false;
            }

            int triangleCount = work.triangles.Count / 3;
            if (triangleCount <= 0)
            {
                result.failReason = "Patch extraction found no triangles.";
                return false;
            }

            float patchHalfWidth = openWidthWorld * 0.5f + Mathf.Max(averageEdgeLength * 1.5f, openWidthWorld * 0.75f);
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.25f);
            float normalTol = Mathf.Max(averageEdgeLength * 1.2f, openWidthWorld * 0.6f);
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            result.patchHalfWidth = patchHalfWidth;
            result.sPad = sPad;
            result.normalTol = normalTol;

            var validTriangles = new HashSet<int>();
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 centroid = (worldVertices[ia] + worldVertices[ib] + worldVertices[ic]) / 3f;
                SurfaceCoord coord = EvaluateSurfaceCoord(centroid, canonicalFrames);
                bool insideS = coord.s >= -sPad && coord.s <= totalLength + sPad;
                bool insideU = Mathf.Abs(coord.u) <= patchHalfWidth;
                bool onPreferredSide = coord.v >= -normalTol;
                if (insideS && insideU && onPreferredSide)
                    validTriangles.Add(tri);
            }

            result.validTriangleCount = validTriangles.Count;
            if (validTriangles.Count == 0)
            {
                result.failReason = "No valid single-sided patch triangles were found.";
                return false;
            }

            var seedTriangles = new HashSet<int>();
            for (int i = 0; i < centerCurveWorld.Count; i++)
            {
                TriangleProjection projection = FindNearestTriangleProjection(work, worldVertices, centerCurveWorld[i], validTriangles);
                if (projection.triangleIndex >= 0)
                    seedTriangles.Add(projection.triangleIndex);
            }

            result.seedTriangleCount = seedTriangles.Count;
            if (seedTriangles.Count == 0)
            {
                result.failReason = "No patch seeds were found from the center curve.";
                return false;
            }

            List<int>[] adjacency = BuildTriangleAdjacency(work);
            var componentIndex = new Dictionary<int, int>();
            var components = new List<HashSet<int>>();
            for (int tri = 0; tri < triangleCount; tri++)
            {
                if (!validTriangles.Contains(tri) || componentIndex.ContainsKey(tri))
                    continue;

                var component = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(tri);
                componentIndex[tri] = components.Count;
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    List<int> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int neighbor = neighbors[i];
                        if (!validTriangles.Contains(neighbor) || componentIndex.ContainsKey(neighbor))
                            continue;
                        componentIndex[neighbor] = components.Count;
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            result.componentCount = components.Count;
            if (components.Count == 0)
            {
                result.failReason = "Patch extraction found no connected component.";
                return false;
            }

            int bestComponent = -1;
            int bestSeedHits = -1;
            int bestTriangleCount = -1;
            for (int i = 0; i < components.Count; i++)
            {
                int seedHits = 0;
                foreach (int seed in seedTriangles)
                {
                    if (components[i].Contains(seed))
                        seedHits++;
                }

                if (seedHits > bestSeedHits || (seedHits == bestSeedHits && components[i].Count > bestTriangleCount))
                {
                    bestComponent = i;
                    bestSeedHits = seedHits;
                    bestTriangleCount = components[i].Count;
                }
            }

            if (bestComponent < 0 || bestSeedHits <= 0)
            {
                result.bestComponentTriangleCount = bestTriangleCount;
                result.bestSeedHits = bestSeedHits;
                result.failReason = "Center-curve seeds did not land on a valid patch component.";
                return false;
            }

            result.success = true;
            result.bestComponentTriangleCount = bestTriangleCount;
            result.bestSeedHits = bestSeedHits;
            result.patchTriangles.UnionWith(components[bestComponent]);
            return true;
        }

        private static void AddTaggedBoundaryEdgesFromPolygon(
            List<ClipVertex> polygon,
            MeshWorkData work,
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges)
        {
            if (polygon == null || polygon.Count < 2 || work == null || taggedEdges == null)
                return;

            for (int i = 0; i < polygon.Count; i++)
                polygon[i] = EnsureVertexMaterialized(polygon[i], work);

            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex a = polygon[i];
                ClipVertex b = polygon[(i + 1) % polygon.Count];
                if (a.materializedIndex < 0 || b.materializedIndex < 0 || a.materializedIndex == b.materializedIndex)
                    continue;

                ClipBoundaryFlags flags = a.boundaryFlags & b.boundaryFlags;
                if (flags == ClipBoundaryFlags.None)
                    continue;

                ulong key = MakeUndirectedEdgeKey(a.materializedIndex, b.materializedIndex);
                if (taggedEdges.TryGetValue(key, out TaggedBoundaryEdge existing))
                {
                    existing.flags |= flags;
                    taggedEdges[key] = existing;
                }
                else
                {
                    taggedEdges.Add(key, new TaggedBoundaryEdge
                    {
                        a = a.materializedIndex,
                        b = b.materializedIndex,
                        flags = flags,
                    });
                }
            }
        }

        private static void AddTaggedBoundaryEdgesFromPolygon(
            List<ClipVertex> polygon,
            MeshWorkData work,
            List<StructuredStripRow> rows,
            PartitionRegionKind regionKind,
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges)
        {
            if (polygon == null || polygon.Count < 2 || work == null || taggedEdges == null)
                return;

            for (int i = 0; i < polygon.Count; i++)
                polygon[i] = EnsureVertexMaterialized(polygon[i], work);

            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex a = polygon[i];
                ClipVertex b = polygon[(i + 1) % polygon.Count];
                if (a.materializedIndex < 0 || b.materializedIndex < 0 || a.materializedIndex == b.materializedIndex)
                    continue;

                ClipBoundaryFlags flags = ClipBoundaryFlags.None;
                if (rows != null &&
                    TryResolveStructuredBoundarySegmentForEdge(rows, a, b, out _, out ClipBoundaryFlags resolvedFlag, out _, out _) &&
                    (resolvedFlag == ClipBoundaryFlags.Left || resolvedFlag == ClipBoundaryFlags.Right) &&
                    DoesRegionOwnStructuredInterfaceFlag(regionKind, resolvedFlag))
                {
                    flags = resolvedFlag;
                }
                else
                {
                    flags = a.boundaryFlags & b.boundaryFlags & (ClipBoundaryFlags.Left | ClipBoundaryFlags.Right);
                }

                if (flags == ClipBoundaryFlags.None)
                    continue;

                ulong key = MakeUndirectedEdgeKey(a.materializedIndex, b.materializedIndex);
                if (taggedEdges.TryGetValue(key, out TaggedBoundaryEdge existing))
                {
                    existing.flags |= flags;
                    taggedEdges[key] = existing;
                }
                else
                {
                    taggedEdges.Add(key, new TaggedBoundaryEdge
                    {
                        a = a.materializedIndex,
                        b = b.materializedIndex,
                        flags = flags,
                    });
                }
            }
        }

        private static bool TryBuildBoundarySamplesFromTaggedEdges(
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges,
            ClipBoundaryFlags targetFlags,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            List<StructuredStripRow> railRows,
            out List<BoundaryVertexSample> boundary,
            out BoundaryBuildDiagnostics diagnostics,
            bool allowRailAuthoritativeFallback = false)
        {
            boundary = new List<BoundaryVertexSample>();
            diagnostics = default;
            if (taggedEdges == null || worldVertices == null || canonicalFrames == null || canonicalFrames.Count < 2)
                return false;

            List<BoundaryEdgeState> edges = CollectTaggedBoundaryEdgeStates(taggedEdges, targetFlags);
            diagnostics.edgeCount = edges.Count;
            if (edges.Count == 0)
            {
                diagnostics.failReason = "No tagged boundary edges were produced.";
                return false;
            }

            List<List<int>> components = BuildBoundaryComponents(edges);
            diagnostics.componentCount = components.Count;
            diagnostics.componentSizes = FormatBoundaryComponentSizes(components);
            diagnostics.preBridgeComponentCount = components.Count;
            diagnostics.preBridgeComponentSizes = diagnostics.componentSizes;

            BoundaryOrderingResult ordering = BuildOrderedBoundaryFromComponents(components, edges, worldVertices, canonicalFrames, out string orderingFailReason);
            diagnostics.selectedComponentVertexCount = ordering.componentVertexCount;
            diagnostics.orderedVertexCount = ordering.orderedVertexCount;
            diagnostics.degree1VertexCount = ordering.degree1VertexCount;
            diagnostics.degree2VertexCount = ordering.degree2VertexCount;
            diagnostics.branchVertexCount = ordering.branchVertexCount;
            diagnostics.failReason = orderingFailReason;

            bool fragmentedTaggedGraph = components.Count > 1 && railRows != null && railRows.Count >= 2;
            if (!string.IsNullOrEmpty(orderingFailReason) && fragmentedTaggedGraph)
            {
                int addedBridgeEdges = BridgeTaggedBoundaryFragmentsFromRail(
                    taggedEdges,
                    targetFlags,
                    worldVertices,
                    railRows,
                    edges);

                if (addedBridgeEdges > 0)
                {
                    diagnostics.railBridgeEdgeCount = addedBridgeEdges;
                    edges = CollectTaggedBoundaryEdgeStates(taggedEdges, targetFlags);
                    diagnostics.edgeCount = edges.Count;
                    components = BuildBoundaryComponents(edges);
                    diagnostics.componentCount = components.Count;
                    diagnostics.componentSizes = FormatBoundaryComponentSizes(components);

                    ordering = BuildOrderedBoundaryFromComponents(components, edges, worldVertices, canonicalFrames, out orderingFailReason);
                    diagnostics.selectedComponentVertexCount = ordering.componentVertexCount;
                    diagnostics.orderedVertexCount = ordering.orderedVertexCount;
                    diagnostics.degree1VertexCount = ordering.degree1VertexCount;
                    diagnostics.degree2VertexCount = ordering.degree2VertexCount;
                    diagnostics.branchVertexCount = ordering.branchVertexCount;
                    diagnostics.failReason = orderingFailReason;
                }
            }

            if (!string.IsNullOrEmpty(orderingFailReason))
            {
                if (allowRailAuthoritativeFallback &&
                    TryBuildBoundarySamplesFromRailRows(
                        targetFlags,
                        worldVertices,
                        canonicalFrames,
                        railRows,
                        out List<BoundaryVertexSample> railBoundary))
                {
                    int addedRailEdges = MaterializeRailBoundaryIntoTaggedEdges(taggedEdges, targetFlags, railBoundary);
                    diagnostics.railBridgeEdgeCount += addedRailEdges;
                    diagnostics.railFallbackUsed = true;
                    diagnostics.railFallbackSampleCount = railBoundary.Count;
                    diagnostics.edgeCount = CollectTaggedBoundaryEdgeStates(taggedEdges, targetFlags).Count;
                    diagnostics.componentCount = 1;
                    diagnostics.componentSizes = railBoundary.Count.ToString();
                    diagnostics.selectedComponentVertexCount = railBoundary.Count;
                    diagnostics.orderedVertexCount = railBoundary.Count;
                    diagnostics.degree1VertexCount = railBoundary.Count >= 2 ? 2 : railBoundary.Count;
                    diagnostics.degree2VertexCount = Mathf.Max(0, railBoundary.Count - 2);
                    diagnostics.branchVertexCount = 0;
                    diagnostics.failReason = null;
                    boundary = railBoundary;
                    return true;
                }

                return false;
            }

            boundary = ordering.samples;
            return boundary.Count >= 2;
        }

        private static bool TryBuildBoundarySamplesFromRailRows(
            ClipBoundaryFlags targetFlags,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            List<StructuredStripRow> railRows,
            out List<BoundaryVertexSample> boundary)
        {
            boundary = new List<BoundaryVertexSample>();
            if (worldVertices == null || canonicalFrames == null || canonicalFrames.Count < 2 || railRows == null || railRows.Count < 2)
                return false;

            bool useLeft;
            if (targetFlags == ClipBoundaryFlags.Left)
                useLeft = true;
            else if (targetFlags == ClipBoundaryFlags.Right)
                useLeft = false;
            else
                return false;

            List<BoundaryVertexSample> rail = ExtractBoundarySamples(railRows, useLeft);
            if (rail == null || rail.Count < 2)
                return false;

            rail.Sort((a, b) => a.s.CompareTo(b.s));
            RemoveDuplicateBoundarySamples(rail);
            if (rail.Count < 2)
                return false;

            float totalLength = 0f;
            for (int i = 0; i < rail.Count; i++)
            {
                int vertexIndex = rail[i].vertexIndex;
                if (!IsValidTriangleVertex(vertexIndex, worldVertices))
                    return false;

                Vector3 world = worldVertices[vertexIndex];
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, world);
                rail[i] = new BoundaryVertexSample
                {
                    vertexIndex = vertexIndex,
                    s = frame.s,
                    world = world,
                };

                if (i > 0)
                {
                    if (rail[i - 1].vertexIndex == vertexIndex)
                        return false;
                    totalLength += Vector3.Distance(rail[i - 1].world, world);
                }
            }

            rail.Sort((a, b) => a.s.CompareTo(b.s));
            RemoveDuplicateBoundarySamples(rail);
            if (rail.Count < 2 || totalLength <= 1e-6f)
                return false;

            boundary = rail;
            return true;
        }

        private static int MaterializeRailBoundaryIntoTaggedEdges(
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges,
            ClipBoundaryFlags targetFlags,
            List<BoundaryVertexSample> railBoundary)
        {
            if (taggedEdges == null || railBoundary == null || railBoundary.Count < 2)
                return 0;

            int added = 0;
            for (int i = 0; i < railBoundary.Count - 1; i++)
            {
                int a = railBoundary[i].vertexIndex;
                int b = railBoundary[i + 1].vertexIndex;
                if (a == b)
                    continue;
                if (AddOrMergeTaggedBoundaryEdge(taggedEdges, a, b, targetFlags))
                    added++;
            }
            return added;
        }

        private static List<BoundaryEdgeState> CollectTaggedBoundaryEdgeStates(
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges,
            ClipBoundaryFlags targetFlags)
        {
            var edges = new List<BoundaryEdgeState>();
            if (taggedEdges == null)
                return edges;

            foreach (var pair in taggedEdges)
            {
                TaggedBoundaryEdge edge = pair.Value;
                if ((edge.flags & targetFlags) == ClipBoundaryFlags.None)
                    continue;

                edges.Add(new BoundaryEdgeState
                {
                    a = edge.a,
                    b = edge.b,
                    keptCount = 1,
                    removedCount = 0,
                });
            }

            return edges;
        }

        private static int BridgeTaggedBoundaryFragmentsFromRail(
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges,
            ClipBoundaryFlags targetFlags,
            Vector3[] worldVertices,
            List<StructuredStripRow> railRows,
            List<BoundaryEdgeState> existingEdges)
        {
            if (taggedEdges == null || worldVertices == null || railRows == null || railRows.Count < 2 || existingEdges == null || existingEdges.Count == 0)
                return 0;

            bool useLeft;
            if (targetFlags == ClipBoundaryFlags.Left)
                useLeft = true;
            else if (targetFlags == ClipBoundaryFlags.Right)
                useLeft = false;
            else
                return 0;

            List<BoundaryVertexSample> rail = ExtractBoundarySamples(railRows, useLeft);
            if (rail == null || rail.Count < 2)
                return 0;

            rail.Sort((a, b) => a.s.CompareTo(b.s));

            List<List<int>> components = BuildBoundaryComponents(existingEdges);
            var componentByVertex = new Dictionary<int, int>();
            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                List<int> component = components[componentIndex];
                if (component == null)
                    continue;
                for (int i = 0; i < component.Count; i++)
                    componentByVertex[component[i]] = componentIndex;
            }

            var degreeByVertex = new Dictionary<int, int>();
            for (int i = 0; i < existingEdges.Count; i++)
            {
                BoundaryEdgeState edge = existingEdges[i];
                IncrementBoundaryDegree(degreeByVertex, edge.a);
                IncrementBoundaryDegree(degreeByVertex, edge.b);
            }

            int railCount = rail.Count;
            int[] directComponentByRailIndex = new int[railCount];
            int[] previousComponentByRailIndex = new int[railCount];
            int[] nextComponentByRailIndex = new int[railCount];
            for (int i = 0; i < railCount; i++)
            {
                int vertex = rail[i].vertexIndex;
                directComponentByRailIndex[i] = componentByVertex.TryGetValue(vertex, out int component) ? component : -1;
            }

            int previousComponent = -1;
            for (int i = 0; i < railCount; i++)
            {
                if (directComponentByRailIndex[i] >= 0)
                    previousComponent = directComponentByRailIndex[i];
                previousComponentByRailIndex[i] = previousComponent;
            }

            int nextComponent = -1;
            for (int i = railCount - 1; i >= 0; i--)
            {
                if (directComponentByRailIndex[i] >= 0)
                    nextComponent = directComponentByRailIndex[i];
                nextComponentByRailIndex[i] = nextComponent;
            }

            int added = 0;
            for (int i = 0; i < railCount - 1; i++)
            {
                int a = rail[i].vertexIndex;
                int b = rail[i + 1].vertexIndex;
                if (!IsValidTriangleVertex(a, worldVertices) || !IsValidTriangleVertex(b, worldVertices) || a == b)
                    continue;

                int componentA = directComponentByRailIndex[i];
                int componentB = directComponentByRailIndex[i + 1];
                int bridgeStartComponent = componentA >= 0 ? componentA : previousComponentByRailIndex[i];
                int bridgeEndComponent = componentB >= 0 ? componentB : nextComponentByRailIndex[i + 1];
                if (bridgeStartComponent < 0 || bridgeEndComponent < 0 || bridgeStartComponent == bridgeEndComponent)
                    continue;

                int degreeA = degreeByVertex.TryGetValue(a, out int da) ? da : 0;
                int degreeB = degreeByVertex.TryGetValue(b, out int db) ? db : 0;
                if ((componentA >= 0 && degreeA > 1) || (componentB >= 0 && degreeB > 1))
                    continue;

                if (AddOrMergeTaggedBoundaryEdge(taggedEdges, a, b, targetFlags))
                {
                    added++;
                    IncrementBoundaryDegree(degreeByVertex, a);
                    IncrementBoundaryDegree(degreeByVertex, b);
                }
            }

            return added;
        }

        private static void IncrementBoundaryDegree(Dictionary<int, int> degreeByVertex, int vertex)
        {
            if (degreeByVertex == null || vertex < 0)
                return;

            degreeByVertex.TryGetValue(vertex, out int degree);
            degreeByVertex[vertex] = degree + 1;
        }

        private static bool AddOrMergeTaggedBoundaryEdge(
            Dictionary<ulong, TaggedBoundaryEdge> taggedEdges,
            int a,
            int b,
            ClipBoundaryFlags flags)
        {
            if (taggedEdges == null || a < 0 || b < 0 || a == b || flags == ClipBoundaryFlags.None)
                return false;

            ulong key = MakeUndirectedEdgeKey(a, b);
            if (taggedEdges.TryGetValue(key, out TaggedBoundaryEdge existing))
            {
                ClipBoundaryFlags before = existing.flags;
                existing.flags |= flags;
                taggedEdges[key] = existing;
                return before != existing.flags;
            }

            taggedEdges.Add(key, new TaggedBoundaryEdge
            {
                a = a,
                b = b,
                flags = flags,
            });
            return true;
        }

        private static string BuildTaggedBoundarySourceSummary(
            BoundaryBuildDiagnostics leftDiagnostics,
            BoundaryBuildDiagnostics rightDiagnostics)
        {
            int bridgeCount = leftDiagnostics.railBridgeEdgeCount + rightDiagnostics.railBridgeEdgeCount;
            if (!leftDiagnostics.railFallbackUsed && !rightDiagnostics.railFallbackUsed && bridgeCount <= 0)
                return "taggedEdges";

            string fallback = (leftDiagnostics.railFallbackUsed || rightDiagnostics.railFallbackUsed)
                ? $" railFallback L={(leftDiagnostics.railFallbackUsed ? leftDiagnostics.railFallbackSampleCount.ToString() : "0")} R={(rightDiagnostics.railFallbackUsed ? rightDiagnostics.railFallbackSampleCount.ToString() : "0")}"
                : string.Empty;

            return
                $"taggedEdges+railBridge L={leftDiagnostics.railBridgeEdgeCount}({leftDiagnostics.preBridgeComponentCount}->{leftDiagnostics.componentCount}) " +
                $"R={rightDiagnostics.railBridgeEdgeCount}({rightDiagnostics.preBridgeComponentCount}->{rightDiagnostics.componentCount})" +
                fallback;
        }

        private static BoundaryOrderingResult BuildOrderedBoundaryFromComponents(
            List<List<int>> components,
            List<BoundaryEdgeState> edges,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            out string failReason)
        {
            failReason = null;
            var result = new BoundaryOrderingResult
            {
                samples = new List<BoundaryVertexSample>(),
            };
            if (components == null || edges == null)
            {
                failReason = "Boundary ordering input is invalid.";
                return result;
            }

            if (components.Count != 1)
            {
                failReason = $"Expected exactly one boundary component, but found {components.Count}.";
                return result;
            }

            List<int> component = components[0];
            Dictionary<int, List<int>> adjacency = BuildBoundaryAdjacency(component, edges);
            if (adjacency.Count == 0)
            {
                failReason = "Boundary component adjacency is empty.";
                return result;
            }

            CountBoundaryVertexDegrees(component, adjacency, out int degree1Count, out int degree2Count, out int branchCount);
            result.componentVertexCount = component != null ? component.Count : 0;
            result.degree1VertexCount = degree1Count;
            result.degree2VertexCount = degree2Count;
            result.branchVertexCount = branchCount;
            if (degree1Count != 2 || branchCount != 0)
            {
                failReason = $"Boundary graph is not a single open chain. deg=({degree1Count},{degree2Count},{branchCount})";
                return result;
            }

            List<int> ordered = OrderBoundaryVertices(component, adjacency, worldVertices, canonicalFrames);
            result.orderedVertexCount = ordered.Count;
            if (ordered.Count != component.Count)
            {
                failReason = $"Boundary ordering did not cover the whole component. ordered={ordered.Count}/{component.Count}";
                return result;
            }

            var samples = new List<BoundaryVertexSample>(ordered.Count);
            for (int j = 0; j < ordered.Count; j++)
            {
                int vertexIndex = ordered[j];
                if (!IsValidTriangleVertex(vertexIndex, worldVertices))
                {
                    failReason = $"Boundary ordering referenced an invalid vertex. vertex={vertexIndex}";
                    return result;
                }

                Vector3 world = worldVertices[vertexIndex];
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, world);
                samples.Add(new BoundaryVertexSample
                {
                    vertexIndex = vertexIndex,
                    s = frame.s,
                    world = world,
                });
            }

            RemoveDuplicateBoundarySamples(samples);
            if (samples.Count < 2)
            {
                failReason = "Boundary ordering collapsed to fewer than two samples.";
                return result;
            }

            result.samples = samples;
            return result;
        }

        private static Dictionary<int, List<int>> BuildBoundaryAdjacency(List<int> componentVertices, List<BoundaryEdgeState> edges)
        {
            var adjacency = new Dictionary<int, List<int>>();
            if (componentVertices == null || edges == null)
                return adjacency;

            var componentSet = new HashSet<int>(componentVertices);
            for (int i = 0; i < edges.Count; i++)
            {
                if (!componentSet.Contains(edges[i].a) || !componentSet.Contains(edges[i].b))
                    continue;
                AddBoundaryNeighbor(adjacency, edges[i].a, edges[i].b);
                AddBoundaryNeighbor(adjacency, edges[i].b, edges[i].a);
            }

            return adjacency;
        }

        private static void CountBoundaryVertexDegrees(
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            out int degree1Count,
            out int degree2Count,
            out int branchCount)
        {
            degree1Count = 0;
            degree2Count = 0;
            branchCount = 0;
            if (componentVertices == null || adjacency == null)
                return;

            for (int i = 0; i < componentVertices.Count; i++)
            {
                int degree = adjacency.TryGetValue(componentVertices[i], out List<int> neighbors) ? neighbors.Count : 0;
                if (degree <= 1)
                    degree1Count++;
                else if (degree == 2)
                    degree2Count++;
                else
                    branchCount++;
            }
        }

        private static List<int> OrderBoundaryVertices(
            List<int> componentVertices,
            Dictionary<int, List<int>> adjacency,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames)
        {
            var result = new List<int>();
            if (componentVertices == null || componentVertices.Count == 0 || adjacency == null || adjacency.Count == 0)
                return result;

            int start = -1;
            float bestS = float.PositiveInfinity;
            foreach (int vertex in componentVertices)
            {
                if (!adjacency.TryGetValue(vertex, out List<int> neighbors) || neighbors.Count == 0 || !IsValidTriangleVertex(vertex, worldVertices))
                    continue;

                float s = SampleFrameAtPoint(canonicalFrames, worldVertices[vertex]).s;
                if (neighbors.Count == 1 && (start < 0 || s < bestS))
                {
                    start = vertex;
                    bestS = s;
                }
            }

            if (start < 0)
                start = componentVertices[0];

            var visited = new HashSet<int>();
            int current = start;
            int previous = -1;
            while (current >= 0 && visited.Add(current))
            {
                result.Add(current);
                if (!adjacency.TryGetValue(current, out List<int> neighbors) || neighbors.Count == 0)
                    break;

                int next = -1;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int candidate = neighbors[i];
                    if (candidate == previous || visited.Contains(candidate))
                        continue;
                    next = candidate;
                    break;
                }

                if (next < 0)
                    break;

                previous = current;
                current = next;
            }

            if (result.Count >= 2)
            {
                float firstS = SampleFrameAtPoint(canonicalFrames, worldVertices[result[0]]).s;
                float lastS = SampleFrameAtPoint(canonicalFrames, worldVertices[result[result.Count - 1]]).s;
                if (firstS > lastS)
                    result.Reverse();
            }

            return result;
        }

        private static int CountTaggedBoundaryEdges(Dictionary<ulong, TaggedBoundaryEdge> taggedEdges, ClipBoundaryFlags mask)
        {
            if (taggedEdges == null)
                return 0;

            int count = 0;
            foreach (var pair in taggedEdges)
            {
                if ((pair.Value.flags & mask) != ClipBoundaryFlags.None)
                    count++;
            }
            return count;
        }

        private static HashSet<ulong> BuildTaggedBoundaryBlockerEdges(Dictionary<ulong, TaggedBoundaryEdge> taggedEdges)
        {
            var result = new HashSet<ulong>();
            if (taggedEdges == null)
                return result;

            foreach (var pair in taggedEdges)
            {
                if ((pair.Value.flags & (ClipBoundaryFlags.Left | ClipBoundaryFlags.Right | ClipBoundaryFlags.Start | ClipBoundaryFlags.End)) != ClipBoundaryFlags.None)
                    result.Add(pair.Key);
            }
            return result;
        }

        private static bool TryExtractExcisionRegion(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            HashSet<int> patchTriangleIndices,
            HashSet<int> removedCandidateTriangles,
            HashSet<ulong> blockerEdges,
            out HashSet<int> excisionTriangles,
            out int excisionComponentCount,
            out string failReason)
        {
            excisionTriangles = new HashSet<int>();
            excisionComponentCount = 0;
            failReason = null;
            if (work == null || worldVertices == null || canonicalFrames == null || canonicalFrames.Count < 2)
            {
                failReason = "Excision extraction input is invalid.";
                return false;
            }

            if (removedCandidateTriangles == null || removedCandidateTriangles.Count == 0)
            {
                failReason = "No removed-candidate triangles were produced after boundary remesh.";
                return false;
            }

            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.25f);
            float seedHalfWidth = Mathf.Max(openWidthWorld * 0.22f, averageEdgeLength * 0.35f);
            float normalTol = Mathf.Max(averageEdgeLength * 1.2f, openWidthWorld * 0.6f);
            List<int>[] adjacency = BuildTriangleAdjacency(work);

            var seedTriangles = new HashSet<int>();
            foreach (int tri in removedCandidateTriangles)
            {
                if (!patchTriangleIndices.Contains(tri))
                    continue;
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 centroid = (worldVertices[ia] + worldVertices[ib] + worldVertices[ic]) / 3f;
                SurfaceCoord coord = EvaluateSurfaceCoord(centroid, canonicalFrames);
                if (coord.s < -sPad || coord.s > totalLength + sPad)
                    continue;
                if (Mathf.Abs(coord.u) <= seedHalfWidth && coord.v >= -normalTol)
                    seedTriangles.Add(tri);
            }

            if (seedTriangles.Count == 0)
            {
                failReason = "No center-band seed triangles were found after remesh.";
                return false;
            }

            var remaining = new HashSet<int>(removedCandidateTriangles);
            var components = new List<HashSet<int>>();
            while (remaining.Count > 0)
            {
                int start = -1;
                foreach (int tri in remaining)
                {
                    start = tri;
                    break;
                }

                var component = new HashSet<int>();
                var queue = new Queue<int>();
                queue.Enqueue(start);
                remaining.Remove(start);
                component.Add(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<int> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int neighbor = neighbors[i];
                        if (!removedCandidateTriangles.Contains(neighbor) || !remaining.Contains(neighbor))
                            continue;

                        if (!TryGetSharedEdge(work.triangles, current, neighbor, out int edgeA, out int edgeB))
                            continue;
                        if (blockerEdges != null && blockerEdges.Contains(MakeUndirectedEdgeKey(edgeA, edgeB)))
                            continue;

                        remaining.Remove(neighbor);
                        component.Add(neighbor);
                        queue.Enqueue(neighbor);
                    }
                }

                components.Add(component);
            }

            excisionComponentCount = components.Count;
            if (components.Count != 1)
            {
                failReason = $"Expected exactly one excision component, but found {components.Count}.";
                return false;
            }

            HashSet<int> onlyComponent = components[0];
            foreach (int tri in seedTriangles)
            {
                if (onlyComponent.Contains(tri))
                {
                    excisionTriangles.UnionWith(onlyComponent);
                    break;
                }
            }

            if (excisionTriangles.Count == 0)
            {
                failReason = "Seed triangles did not map to the unique excision component.";
                return false;
            }

            return true;
        }

        private static bool TryValidatePatchQuality(
            Vector3[] worldVertices,
            List<int> triangles,
            HashSet<int> patchTriangleIndices,
            HashSet<int> excisionTriangles,
            ref float maxPatchTriangleAspect,
            ref float minPatchArea,
            ref int invertedTriangleCount,
            ref int degeneratePatchTriangleCount,
            ref int overAspectPatchTriangleCount,
            ref int auditedPatchTriangleCount)
        {
            if (worldVertices == null || triangles == null || patchTriangleIndices == null)
                return false;

            foreach (int tri in patchTriangleIndices)
            {
                if (excisionTriangles != null && excisionTriangles.Contains(tri))
                    continue;

                GetTriangle(triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 a = worldVertices[ia];
                Vector3 b = worldVertices[ib];
                Vector3 c = worldVertices[ic];
                auditedPatchTriangleCount++;
                float area = 0.5f * Vector3.Cross(b - a, c - a).magnitude;
                minPatchArea = Mathf.Min(minPatchArea, area);
                float aspect = ComputeTriangleAspectRatio3D(a, b, c);
                maxPatchTriangleAspect = Mathf.Max(maxPatchTriangleAspect, aspect);
                if (aspect > 12f)
                    overAspectPatchTriangleCount++;
                if (area <= 1e-10f)
                {
                    degeneratePatchTriangleCount++;
                    invertedTriangleCount++;
                }
            }

            if (float.IsInfinity(minPatchArea))
                minPatchArea = 0f;
            return true;
        }

        private static void AuditPatchQualityBucket(
            Vector3[] worldVertices,
            List<int> triangles,
            HashSet<int> triangleIndices,
            out PatchQualityBucketStats stats)
        {
            stats = default;
            if (worldVertices == null || triangles == null || triangleIndices == null)
                return;

            foreach (int tri in triangleIndices)
            {
                GetTriangle(triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 a = worldVertices[ia];
                Vector3 b = worldVertices[ib];
                Vector3 c = worldVertices[ic];
                stats.auditedTriangleCount++;
                float aspect = ComputeTriangleAspectRatio3D(a, b, c);
                stats.maxAspectRatio = Mathf.Max(stats.maxAspectRatio, aspect);
                if (aspect > 12f)
                    stats.overAspectTriangleCount++;
            }
        }

        private static void RemoveTrianglesByIndex(MeshWorkData work, HashSet<int> removedTriangles)
        {
            if (work == null || removedTriangles == null || removedTriangles.Count == 0)
                return;

            int triangleCount = work.triangles.Count / 3;
            var kept = new List<int>(Mathf.Max(0, work.triangles.Count - removedTriangles.Count * 3));
            for (int tri = 0; tri < triangleCount; tri++)
            {
                if (removedTriangles.Contains(tri))
                    continue;

                int baseIndex = tri * 3;
                kept.Add(work.triangles[baseIndex + 0]);
                kept.Add(work.triangles[baseIndex + 1]);
                kept.Add(work.triangles[baseIndex + 2]);
            }

            work.triangles.Clear();
            work.triangles.AddRange(kept);
        }

        private static int TriangulatePolygonIntoMesh(
            List<ClipVertex> polygon,
            MeshWorkData work,
            List<int> outTriangles,
            out float maxAspectRatio,
            out float minArea,
            out int invertedTriangleCount,
            out string failReason)
        {
            maxAspectRatio = 0f;
            minArea = float.PositiveInfinity;
            invertedTriangleCount = 0;
            failReason = null;
            if (polygon == null || polygon.Count < 3)
                return 0;

            for (int i = 0; i < polygon.Count; i++)
                polygon[i] = EnsureVertexMaterialized(polygon[i], work);

            if (!TryBuildPolygonTriangulationContext(polygon, out PolygonTriangulationContext primaryContext))
            {
                if (IsSingleSemanticBoundaryOnlyPolygon(polygon))
                    return 0;

                failReason = "TriangulatePolygonIntoMesh found a zero-area polygon in both (s,u) and local 3D plane.";
                return 0;
            }

            int triangleCountBeforePrimary = outTriangles.Count;
            int added = TryTriangulatePolygonWithContext(
                polygon,
                primaryContext,
                outTriangles,
                ref maxAspectRatio,
                ref minArea,
                ref invertedTriangleCount,
                out failReason);
            if (string.IsNullOrEmpty(failReason))
                return added;

            if (outTriangles.Count > triangleCountBeforePrimary)
                outTriangles.RemoveRange(triangleCountBeforePrimary, outTriangles.Count - triangleCountBeforePrimary);
            added = 0;
            maxAspectRatio = 0f;
            minArea = float.PositiveInfinity;
            invertedTriangleCount = 0;

            // The structured tip repair chain is 3D-first. A boundary sliver may still carry a tiny
            // non-zero (s,u) area and make the old 2D ear test report every corner as reflex. When
            // that happens, retry the exact same polygon in its local 3D plane instead of inventing
            // a fallback geometry path.
            if (!primaryContext.useLocalPlane && TryBuildLocalPlaneTriangulationContext(polygon, out PolygonTriangulationContext localContext))
            {
                int triangleCountBefore = outTriangles.Count;
                float localMaxAspect = 0f;
                float localMinArea = float.PositiveInfinity;
                int localInvertedCount = 0;
                string localFailReason;
                int localAdded = TryTriangulatePolygonWithContext(
                    polygon,
                    localContext,
                    outTriangles,
                    ref localMaxAspect,
                    ref localMinArea,
                    ref localInvertedCount,
                    out localFailReason);

                if (string.IsNullOrEmpty(localFailReason))
                {
                    maxAspectRatio = Mathf.Max(maxAspectRatio, localMaxAspect);
                    if (!float.IsInfinity(localMinArea))
                        minArea = Mathf.Min(minArea, localMinArea);
                    invertedTriangleCount += localInvertedCount;
                    failReason = null;
                    return added + localAdded;
                }

                if (outTriangles.Count > triangleCountBefore)
                    outTriangles.RemoveRange(triangleCountBefore, outTriangles.Count - triangleCountBefore);

                failReason = failReason + " | local3D retry failed: " + localFailReason;
            }

            if (IsSingleSemanticBoundaryOnlyPolygon(polygon))
            {
                failReason = null;
                if (float.IsInfinity(minArea))
                    minArea = 0f;
                return 0;
            }

            if (float.IsInfinity(minArea))
                minArea = 0f;
            return added;
        }

        private static int TryTriangulatePolygonWithContext(
            List<ClipVertex> polygon,
            PolygonTriangulationContext triangulationContext,
            List<int> outTriangles,
            ref float maxAspectRatio,
            ref float minArea,
            ref int invertedTriangleCount,
            out string failReason)
        {
            failReason = null;
            bool ccw = triangulationContext.signedArea > 0f;
            var indices = new List<int>(polygon.Count);
            for (int i = 0; i < polygon.Count; i++)
                indices.Add(i);

            int added = 0;
            int safety = polygon.Count * polygon.Count;
            while (indices.Count >= 3 && safety-- > 0)
            {
                if (indices.Count == 3)
                {
                    AppendPolygonTriangle(polygon, indices[0], indices[1], indices[2], triangulationContext, ccw, outTriangles, ref added, ref maxAspectRatio, ref minArea, ref invertedTriangleCount);
                    break;
                }

                int bestEar = -1;
                float bestScore = float.PositiveInfinity;
                for (int i = 0; i < indices.Count; i++)
                {
                    int prev = indices[(i - 1 + indices.Count) % indices.Count];
                    int curr = indices[i];
                    int next = indices[(i + 1) % indices.Count];
                    if (!IsValidEar(polygon, indices, prev, curr, next, ccw, triangulationContext))
                        continue;

                    float score = ScoreEar(polygon[prev], polygon[curr], polygon[next]);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestEar = i;
                    }
                }

                if (bestEar < 0)
                {
                    if (IsDegenerateResidualPolygon(polygon, indices, triangulationContext))
                        break;

                    failReason = "Constrained ear clipping failed to find a valid ear. " +
                        DescribeEarSearchFailure(polygon, indices, ccw, triangulationContext);
                    return added;
                }

                int earPrev = indices[(bestEar - 1 + indices.Count) % indices.Count];
                int earCurr = indices[bestEar];
                int earNext = indices[(bestEar + 1) % indices.Count];
                AppendPolygonTriangle(polygon, earPrev, earCurr, earNext, triangulationContext, ccw, outTriangles, ref added, ref maxAspectRatio, ref minArea, ref invertedTriangleCount);
                indices.RemoveAt(bestEar);
            }

            if (float.IsInfinity(minArea))
                minArea = 0f;
            return added;
        }

        private static void AppendPolygonTriangle(
            List<ClipVertex> polygon,
            int aIndex,
            int bIndex,
            int cIndex,
            bool ccw,
            List<int> outTriangles,
            ref int added,
            ref float maxAspectRatio,
            ref float minArea,
            ref int invertedTriangleCount)
        {
            AppendPolygonTriangle(
                polygon,
                aIndex,
                bIndex,
                cIndex,
                new PolygonTriangulationContext
                {
                    useLocalPlane = false,
                    planeNormal = Vector3.zero,
                    signedArea = ccw ? 1f : -1f,
                },
                ccw,
                outTriangles,
                ref added,
                ref maxAspectRatio,
                ref minArea,
                ref invertedTriangleCount);
        }

        private static void AppendPolygonTriangle(
            List<ClipVertex> polygon,
            int aIndex,
            int bIndex,
            int cIndex,
            PolygonTriangulationContext triangulationContext,
            bool ccw,
            List<int> outTriangles,
            ref int added,
            ref float maxAspectRatio,
            ref float minArea,
            ref int invertedTriangleCount)
        {
            ClipVertex a = polygon[aIndex];
            ClipVertex b = polygon[bIndex];
            ClipVertex c = polygon[cIndex];
            if (IsDegenerateTriangle(a.world, b.world, c.world))
                return;

            float signedArea = ComputeTriangleSignedArea(a, b, c, triangulationContext);
            if ((ccw && signedArea <= 1e-10f) || (!ccw && signedArea >= -1e-10f))
                invertedTriangleCount++;

            if (ccw)
            {
                outTriangles.Add(a.materializedIndex);
                outTriangles.Add(b.materializedIndex);
                outTriangles.Add(c.materializedIndex);
            }
            else
            {
                outTriangles.Add(a.materializedIndex);
                outTriangles.Add(c.materializedIndex);
                outTriangles.Add(b.materializedIndex);
            }

            added++;
            float area = 0.5f * Vector3.Cross(b.world - a.world, c.world - a.world).magnitude;
            minArea = Mathf.Min(minArea, area);
            maxAspectRatio = Mathf.Max(maxAspectRatio, ComputeTriangleAspectRatio3D(a.world, b.world, c.world));
        }

        private static bool IsValidEar(
            List<ClipVertex> polygon,
            List<int> indices,
            int prevIndex,
            int currIndex,
            int nextIndex,
            bool ccw,
            PolygonTriangulationContext triangulationContext)
        {
            ClipVertex prev = polygon[prevIndex];
            ClipVertex curr = polygon[currIndex];
            ClipVertex next = polygon[nextIndex];
            float signedArea = ComputeTriangleSignedArea(prev, curr, next, triangulationContext);
            if (Mathf.Abs(signedArea) <= 1e-10f)
                return false;
            if (ccw && signedArea <= 0f)
                return false;
            if (!ccw && signedArea >= 0f)
                return false;

            for (int i = 0; i < indices.Count; i++)
            {
                int candidateIndex = indices[i];
                if (candidateIndex == prevIndex || candidateIndex == currIndex || candidateIndex == nextIndex)
                    continue;

                if (IsPointStrictlyInsideTriangle(polygon[candidateIndex], prev, curr, next, triangulationContext))
                    return false;
            }

            return true;
        }

        private static bool IsPointStrictlyInsideTriangle(
            ClipVertex point,
            ClipVertex a,
            ClipVertex b,
            ClipVertex c,
            PolygonTriangulationContext triangulationContext)
        {
            const float epsilon = 1e-8f;
            float ab = ComputeTriangleSignedArea(a, b, point, triangulationContext);
            float bc = ComputeTriangleSignedArea(b, c, point, triangulationContext);
            float ca = ComputeTriangleSignedArea(c, a, point, triangulationContext);

            bool positive = ab > epsilon && bc > epsilon && ca > epsilon;
            bool negative = ab < -epsilon && bc < -epsilon && ca < -epsilon;
            return positive || negative;
        }

        private static bool IsDegenerateResidualPolygon(
            List<ClipVertex> polygon,
            List<int> indices,
            PolygonTriangulationContext triangulationContext)
        {
            if (polygon == null || indices == null || indices.Count < 3)
                return true;

            const float epsilon = 1e-10f;
            if (Mathf.Abs(ComputeIndexedPolygonSignedArea(polygon, indices, triangulationContext)) > epsilon)
                return false;

            for (int i = 0; i < indices.Count; i++)
            {
                int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];
                if (Mathf.Abs(ComputeTriangleSignedArea(polygon[prevIndex], polygon[currIndex], polygon[nextIndex], triangulationContext)) > epsilon)
                    return false;
            }

            return true;
        }

        private static float ComputeIndexedPolygonSignedArea(
            List<ClipVertex> polygon,
            List<int> indices,
            PolygonTriangulationContext triangulationContext)
        {
            if (polygon == null || indices == null || indices.Count < 3)
                return 0f;

            if (!triangulationContext.useLocalPlane)
            {
                float area = 0f;
                for (int i = 0; i < indices.Count; i++)
                {
                    ClipVertex a = polygon[indices[i]];
                    ClipVertex b = polygon[indices[(i + 1) % indices.Count]];
                    area += a.s * b.u - b.s * a.u;
                }

                return area * 0.5f;
            }

            ClipVertex origin = polygon[indices[0]];
            float signedArea = 0f;
            for (int i = 1; i < indices.Count - 1; i++)
                signedArea += ComputeTriangleSignedArea(origin, polygon[indices[i]], polygon[indices[i + 1]], triangulationContext);
            return signedArea;
        }

        private static string DescribeEarSearchFailure(
            List<ClipVertex> polygon,
            List<int> indices,
            bool ccw,
            PolygonTriangulationContext triangulationContext)
        {
            if (polygon == null || indices == null || indices.Count == 0)
                return "earState=empty";

            const float epsilon = 1e-10f;
            int convex = 0;
            int reflex = 0;
            int flat = 0;
            int blocked = 0;
            var preview = new StringBuilder();
            int previewCount = 0;
            for (int i = 0; i < indices.Count; i++)
            {
                int prevIndex = indices[(i - 1 + indices.Count) % indices.Count];
                int currIndex = indices[i];
                int nextIndex = indices[(i + 1) % indices.Count];
                float turn = ComputeTriangleSignedArea(polygon[prevIndex], polygon[currIndex], polygon[nextIndex], triangulationContext);
                bool isFlat = Mathf.Abs(turn) <= epsilon;
                bool isConvex = !isFlat && ((ccw && turn > 0f) || (!ccw && turn < 0f));
                string reason;
                if (isFlat)
                {
                    flat++;
                    reason = $"flat turn={turn:E3}";
                }
                else if (!isConvex)
                {
                    reflex++;
                    reason = $"reflex turn={turn:E3}";
                }
                else if (TryFindStrictEarBlocker(polygon, indices, prevIndex, currIndex, nextIndex, triangulationContext, out int blockerIndex))
                {
                    convex++;
                    blocked++;
                    reason = $"blocked by {FormatEarVertex(polygon[blockerIndex])}";
                }
                else
                {
                    convex++;
                    reason = "would-pass";
                }

                if (previewCount < 6)
                {
                    if (preview.Length > 0)
                        preview.Append(" ; ");
                    preview.Append(FormatEarVertex(polygon[currIndex]));
                    preview.Append(" ");
                    preview.Append(reason);
                    previewCount++;
                }
            }

            return
                $"earState=mode={(triangulationContext.useLocalPlane ? "local3D" : "su2D")} area={triangulationContext.signedArea:E3} remain={indices.Count} ccw={(ccw ? 1 : 0)} convex={convex} reflex={reflex} flat={flat} blocked={blocked} " +
                $"candidates={preview} remain={BuildIndexedCellPolygonVertexPreview(polygon, indices, 10)}";
        }

        private static bool TryFindStrictEarBlocker(
            List<ClipVertex> polygon,
            List<int> indices,
            int prevIndex,
            int currIndex,
            int nextIndex,
            PolygonTriangulationContext triangulationContext,
            out int blockerIndex)
        {
            blockerIndex = -1;
            ClipVertex a = polygon[prevIndex];
            ClipVertex b = polygon[currIndex];
            ClipVertex c = polygon[nextIndex];
            for (int i = 0; i < indices.Count; i++)
            {
                int candidateIndex = indices[i];
                if (candidateIndex == prevIndex || candidateIndex == currIndex || candidateIndex == nextIndex)
                    continue;

                if (!IsPointStrictlyInsideTriangle(polygon[candidateIndex], a, b, c, triangulationContext))
                    continue;

                blockerIndex = candidateIndex;
                return true;
            }

            return false;
        }

        private static string FormatEarVertex(ClipVertex vertex)
        {
            string materialized = vertex.materializedIndex >= 0 ? $"m{vertex.materializedIndex}" : "m?";
            return $"{materialized}({vertex.s:F4},{vertex.u:F4})/{FormatClipBoundaryFlags(vertex.boundaryFlags)}";
        }

        private static string BuildIndexedCellPolygonVertexPreview(List<ClipVertex> polygon, List<int> indices, int maxCount)
        {
            if (polygon == null || indices == null || indices.Count == 0)
                return "none";

            var sb = new StringBuilder();
            int count = Mathf.Min(maxCount, indices.Count);
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    sb.Append(" ; ");
                int vertexIndex = indices[i];
                sb.Append(i);
                sb.Append(":");
                sb.Append(FormatEarVertex(polygon[vertexIndex]));
            }
            if (indices.Count > count)
                sb.Append(" ; ...");
            return sb.ToString();
        }

        private static bool IsPointInsideTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float ab = Cross2D(b - a, p - a);
            float bc = Cross2D(c - b, p - b);
            float ca = Cross2D(a - c, p - c);
            bool hasNeg = ab < -1e-6f || bc < -1e-6f || ca < -1e-6f;
            bool hasPos = ab > 1e-6f || bc > 1e-6f || ca > 1e-6f;
            return !(hasNeg && hasPos);
        }

        private static float Cross2D(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static float ScoreEar(ClipVertex a, ClipVertex b, ClipVertex c)
        {
            float aspectPenalty = ComputeTriangleAspectRatio3D(a.world, b.world, c.world);
            Vector2 diag = new Vector2(c.s - a.s, c.u - a.u);
            float diagLength = diag.magnitude;
            float anglePenalty = 1f - Mathf.Abs(Vector2.Dot(diagLength > 1e-6f ? diag / diagLength : Vector2.right, Vector2.right));
            return aspectPenalty * 10f + anglePenalty * 2f + diagLength;
        }

        private static float ComputePolygonSignedAreaSU(List<ClipVertex> polygon)
        {
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex a = polygon[i];
                ClipVertex b = polygon[(i + 1) % polygon.Count];
                area += a.s * b.u - b.s * a.u;
            }
            return 0.5f * area;
        }

        private static bool TryBuildPolygonTriangulationContext(
            List<ClipVertex> polygon,
            out PolygonTriangulationContext context)
        {
            context = default;
            if (polygon == null || polygon.Count < 3)
                return false;

            float signedArea = ComputePolygonSignedAreaSU(polygon);
            if (Mathf.Abs(signedArea) > 1e-10f)
            {
                context.signedArea = signedArea;
                return true;
            }

            if (!TryComputePolygonPlaneNormal(polygon, out Vector3 planeNormal))
                return false;

            signedArea = ComputePolygonSignedAreaOnPlane(polygon, planeNormal);
            if (Mathf.Abs(signedArea) <= 1e-12f)
                return false;

            context.useLocalPlane = true;
            context.planeNormal = planeNormal;
            context.signedArea = signedArea;
            return true;
        }

        private static bool IsSingleSemanticBoundaryOnlyPolygon(List<ClipVertex> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            ClipBoundaryFlags semanticMask = ClipBoundaryFlags.Left | ClipBoundaryFlags.Right | ClipBoundaryFlags.Start | ClipBoundaryFlags.End;
            ClipBoundaryFlags common = semanticMask;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipBoundaryFlags flags = polygon[i].boundaryFlags & semanticMask;
                if (flags == ClipBoundaryFlags.None)
                    return false;
                common &= flags;
            }

            return (common & semanticMask) != ClipBoundaryFlags.None;
        }

        private static bool TryBuildLocalPlaneTriangulationContext(
            List<ClipVertex> polygon,
            out PolygonTriangulationContext context)
        {
            context = default;
            if (polygon == null || polygon.Count < 3)
                return false;

            if (!TryComputePolygonPlaneNormal(polygon, out Vector3 planeNormal))
                return false;

            float signedArea = ComputePolygonSignedAreaOnPlane(polygon, planeNormal);
            if (Mathf.Abs(signedArea) <= 1e-12f)
                return false;

            context.useLocalPlane = true;
            context.planeNormal = planeNormal;
            context.signedArea = signedArea;
            return true;
        }

        private static bool TryComputePolygonPlaneNormal(List<ClipVertex> polygon, out Vector3 planeNormal)
        {
            planeNormal = Vector3.zero;
            if (polygon == null || polygon.Count < 3)
                return false;

            Vector3 origin = polygon[0].world;
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                Vector3 cross = Vector3.Cross(polygon[i].world - origin, polygon[i + 1].world - origin);
                if (cross.sqrMagnitude <= 1e-14f)
                    continue;

                planeNormal = cross.normalized;
                return true;
            }

            return false;
        }

        private static float ComputePolygonSignedAreaOnPlane(List<ClipVertex> polygon, Vector3 planeNormal)
        {
            if (polygon == null || polygon.Count < 3)
                return 0f;

            Vector3 origin = polygon[0].world;
            float area = 0f;
            for (int i = 1; i < polygon.Count - 1; i++)
                area += 0.5f * Vector3.Dot(Vector3.Cross(polygon[i].world - origin, polygon[i + 1].world - origin), planeNormal);
            return area;
        }

        private static float ComputeTriangleSignedArea(
            ClipVertex a,
            ClipVertex b,
            ClipVertex c,
            PolygonTriangulationContext triangulationContext)
        {
            if (!triangulationContext.useLocalPlane)
                return ComputeTriangleSignedAreaSU(a, b, c);

            return 0.5f * Vector3.Dot(Vector3.Cross(b.world - a.world, c.world - a.world), triangulationContext.planeNormal);
        }

        private static float ComputeTriangleSignedAreaSU(ClipVertex a, ClipVertex b, ClipVertex c)
        {
            Vector2 ab = new Vector2(b.s - a.s, b.u - a.u);
            Vector2 ac = new Vector2(c.s - a.s, c.u - a.u);
            return 0.5f * Cross2D(ab, ac);
        }

        private static float ComputeTriangleAspectRatio3D(Vector3 a, Vector3 b, Vector3 c)
        {
            float ab = Vector3.Distance(a, b);
            float bc = Vector3.Distance(b, c);
            float ca = Vector3.Distance(c, a);
            float longest = Mathf.Max(ab, Mathf.Max(bc, ca));
            float area2 = Vector3.Cross(b - a, c - a).magnitude;
            if (longest <= 1e-8f || area2 <= 1e-12f)
                return float.PositiveInfinity;

            float minAltitude = float.PositiveInfinity;
            minAltitude = Mathf.Min(minAltitude, area2 / Mathf.Max(ab, 1e-8f));
            minAltitude = Mathf.Min(minAltitude, area2 / Mathf.Max(bc, 1e-8f));
            minAltitude = Mathf.Min(minAltitude, area2 / Mathf.Max(ca, 1e-8f));
            if (minAltitude <= 1e-8f)
                return float.PositiveInfinity;

            return longest / minAltitude;
        }

        private static int TriangulatePolygonIntoMesh(List<ClipVertex> polygon, MeshWorkData work, List<int> outTriangles)
        {
            if (polygon == null || polygon.Count < 3)
                return 0;

            for (int i = 0; i < polygon.Count; i++)
                polygon[i] = EnsureVertexMaterialized(polygon[i], work);

            int added = 0;
            int i0 = polygon[0].materializedIndex;
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                ClipVertex b = polygon[i];
                ClipVertex c = polygon[i + 1];
                if (IsDegenerateTriangle(polygon[0].world, b.world, c.world))
                    continue;

                outTriangles.Add(i0);
                outTriangles.Add(b.materializedIndex);
                outTriangles.Add(c.materializedIndex);
                added++;
            }

            return added;
        }

        private static int AppendDebugPolygon(List<ClipVertex> polygon, List<DebugTriangle> debugTriangles, int layer, DebugTriangleKind kind)
        {
            if (polygon == null || polygon.Count < 3)
                return 0;

            int added = 0;
            Vector3 a = polygon[0].world;
            for (int i = 1; i < polygon.Count - 1; i++)
            {
                Vector3 b = polygon[i].world;
                Vector3 c = polygon[i + 1].world;
                if (IsDegenerateTriangle(a, b, c))
                    continue;

                debugTriangles.Add(new DebugTriangle
                {
                    a = a,
                    b = b,
                    c = c,
                    layer = layer,
                    kind = kind,
                });
                added++;
            }

            return added;
        }

        private static bool IsDegenerateTriangle(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            float crossSq = Vector3.Cross(ab, ac).sqrMagnitude;
            float scale = Mathf.Max(ab.sqrMagnitude, ac.sqrMagnitude);
            return crossSq <= Mathf.Max(1e-14f, scale * 1e-10f);
        }

        private static ClipVertex EnsureVertexMaterialized(ClipVertex vertex, MeshWorkData work)
        {
            if (vertex.materializedIndex >= 0)
                return vertex;

            vertex.materializedIndex = AddClipVertexToMesh(work, vertex);
            return vertex;
        }

        private static int AddClipVertexToMesh(MeshWorkData work, ClipVertex vertex)
        {
            if (TryFindWeldedVertexIndex(work, vertex.local, out int existingIndex))
                return existingIndex;

            int index = work.vertices.Count;
            work.vertices.Add(vertex.local);
            if (work.hasUV)
                work.uv.Add(vertex.uv);
            if (work.hasTangents)
                work.tangents.Add(vertex.tangent);
            if (work.hasColors)
                work.colors.Add(vertex.color);
            RegisterWeldedVertex(work, index, vertex.local);
            return index;
        }

        private static bool TryFindWeldedVertexIndex(MeshWorkData work, Vector3 local, out int vertexIndex)
        {
            vertexIndex = -1;
            if (work == null)
                return false;

            EnsureVertexWeldBucketsInitialized(work);
            const float tolerance = 1e-5f;
            float toleranceSq = tolerance * tolerance;
            VertexWeldKey center = CreateVertexWeldKey(local, tolerance);
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        var key = new VertexWeldKey
                        {
                            x = center.x + dx,
                            y = center.y + dy,
                            z = center.z + dz,
                        };

                        if (!work.vertexWeldBuckets.TryGetValue(key, out List<int> bucket))
                            continue;

                        for (int i = 0; i < bucket.Count; i++)
                        {
                            int candidate = bucket[i];
                            if (candidate < 0 || candidate >= work.vertices.Count)
                                continue;
                            if ((work.vertices[candidate] - local).sqrMagnitude > toleranceSq)
                                continue;

                            vertexIndex = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static void EnsureVertexWeldBucketsInitialized(MeshWorkData work)
        {
            if (work == null || work.vertexWeldBucketsInitialized)
                return;

            work.vertexWeldBuckets.Clear();
            const float tolerance = 1e-5f;
            for (int i = 0; i < work.vertices.Count; i++)
                RegisterWeldedVertex(work, i, work.vertices[i], tolerance);
            work.vertexWeldBucketsInitialized = true;
        }

        private static void RegisterWeldedVertex(MeshWorkData work, int vertexIndex, Vector3 local)
        {
            RegisterWeldedVertex(work, vertexIndex, local, 1e-5f);
        }

        private static void RegisterWeldedVertex(MeshWorkData work, int vertexIndex, Vector3 local, float tolerance)
        {
            if (work == null)
                return;

            VertexWeldKey key = CreateVertexWeldKey(local, tolerance);
            if (!work.vertexWeldBuckets.TryGetValue(key, out List<int> bucket))
            {
                bucket = new List<int>(2);
                work.vertexWeldBuckets.Add(key, bucket);
            }

            if (!bucket.Contains(vertexIndex))
                bucket.Add(vertexIndex);
        }

        private static VertexWeldKey CreateVertexWeldKey(Vector3 local, float tolerance)
        {
            float scale = 1f / Mathf.Max(tolerance, 1e-8f);
            return new VertexWeldKey
            {
                x = Mathf.RoundToInt(local.x * scale),
                y = Mathf.RoundToInt(local.y * scale),
                z = Mathf.RoundToInt(local.z * scale),
            };
        }

        private static void AccumulateBoundaryVerticesFromPolygon(
            List<ClipVertex> polygon,
            float halfOpen,
            float totalLength,
            float tolerance,
            HashSet<int> leftBoundaryVertexIndices,
            HashSet<int> rightBoundaryVertexIndices)
        {
            if (polygon == null || polygon.Count == 0)
                return;

            float sMin = -tolerance;
            float sMax = totalLength + tolerance;
            for (int i = 0; i < polygon.Count; i++)
            {
                ClipVertex vertex = polygon[i];
                if (vertex.materializedIndex < 0)
                    continue;
                if (vertex.s < sMin || vertex.s > sMax)
                    continue;

                if (!TryClassifyBoundarySide(vertex.u, halfOpen, tolerance, out bool isLeft))
                    continue;

                if (isLeft)
                    leftBoundaryVertexIndices.Add(vertex.materializedIndex);
                else
                    rightBoundaryVertexIndices.Add(vertex.materializedIndex);
            }
        }

        private static bool TryExtractExplicitBoundaryChainsFromMesh(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            out List<BoundaryVertexSample> leftBoundary,
            out List<BoundaryVertexSample> rightBoundary,
            out int openBoundaryEdgeCount)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();
            openBoundaryEdgeCount = 0;

            List<BoundaryEdgeState> openEdges = BuildOpenBoundaryEdges(work);
            openBoundaryEdgeCount = openEdges.Count;
            if (openEdges.Count == 0)
                return false;

            float halfOpen = Mathf.Max(1e-5f, openWidthWorld * 0.5f);
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.5f);
            float uTolerance = ComputeBoundaryTolerance(halfOpen, averageEdgeLength, openWidthWorld, 0.22f, 0.14f);
            var leftEdges = new List<BoundaryEdgeState>();
            var rightEdges = new List<BoundaryEdgeState>();

            for (int i = 0; i < openEdges.Count; i++)
            {
                BoundaryEdgeState edge = openEdges[i];
                if (!IsValidTriangleVertex(edge.a, worldVertices) || !IsValidTriangleVertex(edge.b, worldVertices))
                    continue;

                Vector3 midpoint = 0.5f * (worldVertices[edge.a] + worldVertices[edge.b]);
                SurfaceCoord coord = EvaluateSurfaceCoord(midpoint, canonicalFrames);
                if (coord.s < -sPad || coord.s > totalLength + sPad)
                    continue;

                if (!TryClassifyBoundarySide(coord.u, halfOpen, uTolerance, out bool isLeft))
                    continue;

                if (isLeft)
                    leftEdges.Add(edge);
                else
                    rightEdges.Add(edge);
            }

            if (leftEdges.Count == 0 || rightEdges.Count == 0)
                return false;

            leftBoundary = BuildOrderedBoundary(leftEdges, worldVertices, canonicalFrames);
            rightBoundary = BuildOrderedBoundary(rightEdges, worldVertices, canonicalFrames);
            return leftBoundary.Count >= 2 && rightBoundary.Count >= 2;
        }

        private static bool TryBuildBoundarySamplesFromVertexSet(
            HashSet<int> vertexSet,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            out List<BoundaryVertexSample> boundary)
        {
            boundary = new List<BoundaryVertexSample>();
            if (vertexSet == null || vertexSet.Count == 0 || worldVertices == null || canonicalFrames == null || canonicalFrames.Count < 2)
                return false;

            foreach (int vertexIndex in vertexSet)
            {
                if (!IsValidTriangleVertex(vertexIndex, worldVertices))
                    continue;

                Vector3 world = worldVertices[vertexIndex];
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, world);
                boundary.Add(new BoundaryVertexSample
                {
                    vertexIndex = vertexIndex,
                    s = frame.s,
                    world = world,
                });
            }

            boundary.Sort((x, y) =>
            {
                int cmp = x.s.CompareTo(y.s);
                if (cmp != 0)
                    return cmp;
                return x.vertexIndex.CompareTo(y.vertexIndex);
            });

            RemoveDuplicateBoundarySamples(boundary);
            return boundary.Count >= 2;
        }

        private static float ComputeBoundaryTolerance(float halfOpen, float averageEdgeLength, float openWidthWorld, float edgeScale, float widthScale)
        {
            float baseTolerance = Mathf.Max(averageEdgeLength * edgeScale, openWidthWorld * widthScale, 1e-5f);
            float cap = Mathf.Max(1e-5f, halfOpen * 0.8f);
            return Mathf.Min(baseTolerance, cap);
        }

        private static bool TryClassifyBoundarySide(float u, float halfOpen, float tolerance, out bool isLeft)
        {
            float leftDelta = Mathf.Abs(u + halfOpen);
            float rightDelta = Mathf.Abs(u - halfOpen);
            float bestDelta = Mathf.Min(leftDelta, rightDelta);
            if (bestDelta > tolerance)
            {
                isLeft = false;
                return false;
            }

            if (Mathf.Abs(leftDelta - rightDelta) <= 1e-6f)
                isLeft = u <= 0f;
            else
                isLeft = leftDelta < rightDelta;

            return true;
        }

        private static List<BoundaryEdgeState> BuildOpenBoundaryEdges(MeshWorkData work)
        {
            var states = new Dictionary<ulong, BoundaryEdgeState>(work.triangles.Count);
            int triangleCount = work.triangles.Count / 3;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int a, out int b, out int c);
                AccumulateOpenEdge(states, a, b);
                AccumulateOpenEdge(states, b, c);
                AccumulateOpenEdge(states, c, a);
            }

            var result = new List<BoundaryEdgeState>();
            foreach (var pair in states)
            {
                BoundaryEdgeState state = pair.Value;
                if (state.keptCount == 1)
                    result.Add(state);
            }

            return result;
        }

        private static void AccumulateOpenEdge(Dictionary<ulong, BoundaryEdgeState> states, int a, int b)
        {
            ulong key = MakeUndirectedEdgeKey(a, b);
            if (!states.TryGetValue(key, out BoundaryEdgeState state))
            {
                state = new BoundaryEdgeState
                {
                    a = a,
                    b = b,
                    keptCount = 0,
                    removedCount = 0,
                };
            }

            state.keptCount++;
            states[key] = state;
        }

        private static List<int> BuildBoundaryPath(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            List<FrameSample> canonicalFrames,
            List<Vector3> targetWorld,
            float snapPenalty,
            float preferredSign)
        {
            List<int> anchors = BuildBoundaryAnchors(canonicalFrames, targetWorld, worldVertices, preferredSign);
            if (anchors.Count < 2)
                return new List<int>();

            return BuildMasterPath(worldVertices, adjacency, targetWorld, anchors, snapPenalty);
        }

        private static List<int> BuildTipBridgePath(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            int start,
            int end,
            float snapPenalty)
        {
            if (start < 0 || end < 0)
                return new List<int>();
            if (start == end)
                return new List<int> { start };

            return BuildPathBetweenVertices(
                worldVertices,
                adjacency,
                start,
                end,
                BuildConnectorGuide(start, end, worldVertices),
                snapPenalty);
        }

        private static bool IsPathConnected(List<int> path, int start, int end)
        {
            if (start < 0 || end < 0 || path == null || path.Count == 0)
                return false;
            if (start == end)
                return path.Count == 1 && path[0] == start;
            return path.Count >= 2 && path[0] == start && path[path.Count - 1] == end;
        }

        private static List<int> BuildBoundaryAnchors(
            List<FrameSample> canonicalFrames,
            List<Vector3> targets,
            Vector3[] worldVertices,
            float preferredSign)
        {
            var anchors = new List<int>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                int nearest = FindNearestBoundaryVertex(targets[i], canonicalFrames[i], worldVertices, preferredSign);
                if (nearest < 0)
                    continue;
                if (anchors.Count == 0 || anchors[anchors.Count - 1] != nearest)
                    anchors.Add(nearest);
            }

            if (anchors.Count >= 2)
                return anchors;

            for (int i = 0; i < targets.Count; i++)
            {
                int nearest = FindNearestVertexIndex(targets[i], worldVertices);
                if (nearest < 0)
                    continue;
                if (anchors.Count == 0 || anchors[anchors.Count - 1] != nearest)
                    anchors.Add(nearest);
            }

            return anchors;
        }

        private static int FindNearestBoundaryVertex(Vector3 target, FrameSample frame, Vector3[] worldVertices, float preferredSign)
        {
            int bestIndex = -1;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < worldVertices.Length; i++)
            {
                Vector3 world = worldVertices[i];
                Vector3 delta = world - frame.center;
                float sign = Mathf.Sign(Vector3.Dot(delta, frame.binormal));
                float signPenalty = 0f;
                if (preferredSign < 0f && sign > 0f)
                    signPenalty = 1000f;
                else if (preferredSign > 0f && sign < 0f)
                    signPenalty = 1000f;

                float score = (world - target).sqrMagnitude + signPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int FindNearestBoundaryVertex(
            Vector3 target,
            FrameSample frame,
            Vector3[] worldVertices,
            float preferredSign,
            HashSet<int> allowedVertices)
        {
            int bestIndex = -1;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < worldVertices.Length; i++)
            {
                if (allowedVertices != null && !allowedVertices.Contains(i))
                    continue;

                Vector3 world = worldVertices[i];
                Vector3 delta = world - frame.center;
                float sign = Mathf.Sign(Vector3.Dot(delta, frame.binormal));
                float signPenalty = 0f;
                if (preferredSign < 0f && sign > 0f)
                    signPenalty = 1000f;
                else if (preferredSign > 0f && sign < 0f)
                    signPenalty = 1000f;

                float score = (world - target).sqrMagnitude + signPenalty;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<int> BuildPathBetweenVertices(
            Vector3[] worldVertices,
            List<GraphEdge>[] adjacency,
            int start,
            int end,
            List<Vector3> guideWorld,
            float snapPenalty)
        {
            if (start < 0 || end < 0 || start >= worldVertices.Length || end >= worldVertices.Length)
                return new List<int>();
            if (start == end)
                return new List<int> { start };

            Vector3 expectedDir = worldVertices[end] - worldVertices[start];
            if (expectedDir.sqrMagnitude <= 1e-10f)
                expectedDir = Vector3.forward;
            expectedDir.Normalize();

            return SolveShortestPath(worldVertices, adjacency, guideWorld, start, end, expectedDir, snapPenalty);
        }

        private static List<Vector3> BuildConnectorGuide(int startVertex, int endVertex, Vector3[] worldVertices)
        {
            var guide = new List<Vector3>(2);
            if (startVertex >= 0 && startVertex < worldVertices.Length)
                guide.Add(worldVertices[startVertex]);
            if (endVertex >= 0 && endVertex < worldVertices.Length)
                guide.Add(worldVertices[endVertex]);
            return guide;
        }

        private static List<BoundaryVertexSample> BuildBoundarySamplesFromPath(
            List<int> path,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames)
        {
            var result = new List<BoundaryVertexSample>(path.Count);
            for (int i = 0; i < path.Count; i++)
            {
                int vertexIndex = path[i];
                if (!IsValidTriangleVertex(vertexIndex, worldVertices))
                    continue;

                Vector3 world = worldVertices[vertexIndex];
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, world);
                result.Add(new BoundaryVertexSample
                {
                    vertexIndex = vertexIndex,
                    s = frame.s,
                    world = world,
                });
            }

            result.Sort((x, y) =>
            {
                int cmp = x.s.CompareTo(y.s);
                if (cmp != 0)
                    return cmp;
                return x.vertexIndex.CompareTo(y.vertexIndex);
            });

            RemoveDuplicateBoundarySamples(result);
            return result;
        }

        private static HashSet<int> ExciseInsideBoundaryLoop(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            List<int> leftBoundaryPath,
            List<int> rightBoundaryPath,
            List<int> startBridgePath,
            List<int> endBridgePath,
            float openWidthWorld,
            float averageEdgeLength,
            List<DebugTriangle> debugTriangles,
            int layer)
        {
            var result = new HashSet<int>();
            int triangleCount = work.triangles.Count / 3;
            if (triangleCount <= 0)
                return result;

            HashSet<ulong> blockerEdges = BuildBoundaryBlockerEdges(leftBoundaryPath, rightBoundaryPath, startBridgePath, endBridgePath);
            float patchRadius = Mathf.Max(openWidthWorld * 3f, averageEdgeLength * 3.5f);
            float seedHalfWidth = Mathf.Max(openWidthWorld * 0.25f, averageEdgeLength * 0.45f);
            float sPad = Mathf.Max(openWidthWorld * 0.8f, averageEdgeLength * 1.25f);
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;

            List<int>[] adjacency = BuildTriangleAdjacency(work);
            var eval = new TriangleEval[triangleCount];
            var queue = new Queue<int>();
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 a = worldVertices[ia];
                Vector3 b = worldVertices[ib];
                Vector3 c = worldVertices[ic];
                Vector3 centroid = (a + b + c) / 3f;
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, centroid);
                Vector3 offset = centroid - frame.center;
                float dist = offset.magnitude;
                float signedLateral = Vector3.Dot(offset, frame.binormal);
                bool insideS = frame.s >= -sPad && frame.s <= totalLength + sPad;
                bool patch = insideS && dist <= patchRadius;
                bool seed = patch && Mathf.Abs(signedLateral) <= seedHalfWidth;

                eval[tri] = new TriangleEval
                {
                    patch = patch,
                    seed = seed,
                    signedLateral = signedLateral,
                    s = frame.s,
                };

                if (patch)
                {
                    debugTriangles.Add(new DebugTriangle
                    {
                        a = a,
                        b = b,
                        c = c,
                        layer = layer,
                        kind = DebugTriangleKind.Patch,
                    });
                }

                if (seed)
                {
                    result.Add(tri);
                    queue.Enqueue(tri);
                }
            }

            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                List<int> neighbors = adjacency[tri];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    if (neighbor < 0 || neighbor >= triangleCount || result.Contains(neighbor) || !eval[neighbor].patch)
                        continue;

                    if (!TryGetSharedEdge(work.triangles, tri, neighbor, out int edgeA, out int edgeB))
                        continue;
                    if (blockerEdges.Contains(MakeUndirectedEdgeKey(edgeA, edgeB)))
                        continue;

                    result.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            foreach (int tri in result)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                debugTriangles.Add(new DebugTriangle
                {
                    a = worldVertices[ia],
                    b = worldVertices[ib],
                    c = worldVertices[ic],
                    layer = layer,
                    kind = DebugTriangleKind.Excision,
                });
            }

            return result;
        }

        private static HashSet<ulong> BuildBoundaryBlockerEdges(
            List<int> leftBoundaryPath,
            List<int> rightBoundaryPath,
            List<int> startBridgePath,
            List<int> endBridgePath)
        {
            var result = new HashSet<ulong>();
            AppendPathEdges(result, leftBoundaryPath);
            AppendPathEdges(result, rightBoundaryPath);
            AppendPathEdges(result, startBridgePath);
            AppendPathEdges(result, endBridgePath);
            return result;
        }

        private static void AppendPathEdges(HashSet<ulong> edgeSet, List<int> path)
        {
            if (edgeSet == null || path == null || path.Count < 2)
                return;

            for (int i = 0; i < path.Count - 1; i++)
            {
                int a = path[i];
                int b = path[i + 1];
                if (a < 0 || b < 0 || a == b)
                    continue;
                edgeSet.Add(MakeUndirectedEdgeKey(a, b));
            }
        }

        private static HashSet<int> ExciseCenterBand(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            float openWidthWorld,
            float averageEdgeLength,
            List<DebugTriangle> debugTriangles,
            int layer)
        {
            var result = new HashSet<int>();
            int triangleCount = work.triangles.Count / 3;
            if (triangleCount <= 0)
                return result;

            float patchRadius = Mathf.Max(openWidthWorld * 2.5f, averageEdgeLength * 3f);
            float seedHalfWidth = Mathf.Max(openWidthWorld * 0.4f, averageEdgeLength * 0.55f);
            float floodHalfWidth = Mathf.Max(openWidthWorld * 0.72f, averageEdgeLength * 0.95f);
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.25f);
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;

            List<int>[] adjacency = BuildTriangleAdjacency(work);
            var eval = new TriangleEval[triangleCount];
            var queue = new Queue<int>();
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                Vector3 a = worldVertices[ia];
                Vector3 b = worldVertices[ib];
                Vector3 c = worldVertices[ic];
                Vector3 centroid = (a + b + c) / 3f;
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, centroid);
                Vector3 offset = centroid - frame.center;
                float dist = offset.magnitude;
                float signedLateral = Vector3.Dot(offset, frame.binormal);
                bool insideS = frame.s >= -sPad && frame.s <= totalLength + sPad;
                bool patch = insideS && dist <= patchRadius;
                bool seed = patch && Mathf.Abs(signedLateral) <= seedHalfWidth;

                eval[tri] = new TriangleEval
                {
                    patch = patch,
                    seed = seed,
                    signedLateral = signedLateral,
                    s = frame.s,
                };

                if (patch)
                {
                    debugTriangles.Add(new DebugTriangle
                    {
                        a = a,
                        b = b,
                        c = c,
                        layer = layer,
                        kind = DebugTriangleKind.Patch,
                    });
                }

                if (seed)
                {
                    result.Add(tri);
                    queue.Enqueue(tri);
                }
            }

            while (queue.Count > 0)
            {
                int tri = queue.Dequeue();
                List<int> neighbors = adjacency[tri];
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int neighbor = neighbors[i];
                    if (neighbor < 0 || neighbor >= triangleCount || result.Contains(neighbor) || !eval[neighbor].patch)
                        continue;

                    if (!TryGetSharedEdge(work.triangles, tri, neighbor, out int edgeA, out int edgeB))
                        continue;
                    if (!IsValidTriangleVertex(edgeA, worldVertices) || !IsValidTriangleVertex(edgeB, worldVertices))
                        continue;

                    Vector3 midpoint = 0.5f * (worldVertices[edgeA] + worldVertices[edgeB]);
                    FrameSample frame = SampleFrameAtPoint(canonicalFrames, midpoint);
                    float midLateral = Mathf.Abs(Vector3.Dot(midpoint - frame.center, frame.binormal));
                    bool insideS = frame.s >= -sPad && frame.s <= totalLength + sPad;
                    if (!insideS || midLateral > floodHalfWidth)
                        continue;

                    result.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            foreach (int tri in result)
            {
                GetTriangle(work.triangles, tri, out int ia, out int ib, out int ic);
                if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                    continue;

                debugTriangles.Add(new DebugTriangle
                {
                    a = worldVertices[ia],
                    b = worldVertices[ib],
                    c = worldVertices[ic],
                    layer = layer,
                    kind = DebugTriangleKind.Excision,
                });
            }

            return result;
        }

        private static List<int>[] BuildTriangleAdjacency(MeshWorkData work)
        {
            int triangleCount = work.triangles.Count / 3;
            var adjacency = new List<int>[triangleCount];
            for (int i = 0; i < triangleCount; i++)
                adjacency[i] = new List<int>(3);

            var edgeToTriangle = new Dictionary<ulong, int>(work.triangles.Count);
            for (int tri = 0; tri < triangleCount; tri++)
            {
                GetTriangle(work.triangles, tri, out int a, out int b, out int c);
                RegisterTriangleEdge(edgeToTriangle, adjacency, tri, a, b);
                RegisterTriangleEdge(edgeToTriangle, adjacency, tri, b, c);
                RegisterTriangleEdge(edgeToTriangle, adjacency, tri, c, a);
            }

            return adjacency;
        }

        private static void RegisterTriangleEdge(Dictionary<ulong, int> edgeToTriangle, List<int>[] adjacency, int triangleIndex, int a, int b)
        {
            ulong key = MakeUndirectedEdgeKey(a, b);
            if (edgeToTriangle.TryGetValue(key, out int otherTriangle))
            {
                if (!adjacency[triangleIndex].Contains(otherTriangle))
                    adjacency[triangleIndex].Add(otherTriangle);
                if (!adjacency[otherTriangle].Contains(triangleIndex))
                    adjacency[otherTriangle].Add(triangleIndex);
            }
            else
            {
                edgeToTriangle.Add(key, triangleIndex);
            }
        }

        private static int CountTriangleComponents(MeshWorkData work, HashSet<int> triangleIndices)
        {
            if (work == null || triangleIndices == null || triangleIndices.Count == 0)
                return 0;

            List<int>[] adjacency = BuildTriangleAdjacency(work);
            var visited = new HashSet<int>();
            int componentCount = 0;
            foreach (int start in triangleIndices)
            {
                if (start < 0 || start >= adjacency.Length || !visited.Add(start))
                    continue;

                componentCount++;
                var queue = new Queue<int>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    List<int> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        int neighbor = neighbors[i];
                        if (!triangleIndices.Contains(neighbor) || !visited.Add(neighbor))
                            continue;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            return componentCount;
        }

        private static bool TryExtractBandBoundaryChainsWithDiagnostics(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            HashSet<int> removedTriangles,
            float openWidthWorld,
            float averageEdgeLength,
            out List<BoundaryVertexSample> leftBoundary,
            out BoundaryBuildDiagnostics leftDiagnostics,
            out List<BoundaryVertexSample> rightBoundary,
            out BoundaryBuildDiagnostics rightDiagnostics,
            out string failReason)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();
            leftDiagnostics = default;
            rightDiagnostics = default;
            failReason = null;

            Dictionary<ulong, BoundaryEdgeState> edgeStates = BuildBoundaryEdgeStates(work, removedTriangles);
            if (edgeStates.Count == 0)
            {
                failReason = "Band baseline found no removed/kept boundary edges.";
                return false;
            }

            float edgeBandRadius = Mathf.Max(openWidthWorld * 1.6f, averageEdgeLength * 1.75f);
            var leftEdges = new List<BoundaryEdgeState>();
            var rightEdges = new List<BoundaryEdgeState>();
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.2f);

            foreach (var pair in edgeStates)
            {
                BoundaryEdgeState state = pair.Value;
                if (state.removedCount <= 0 || state.keptCount <= 0)
                    continue;
                if (!IsValidTriangleVertex(state.a, worldVertices) || !IsValidTriangleVertex(state.b, worldVertices))
                    continue;

                Vector3 midpoint = 0.5f * (worldVertices[state.a] + worldVertices[state.b]);
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, midpoint);
                if (frame.s < -sPad || frame.s > totalLength + sPad)
                    continue;

                float distance = (midpoint - frame.center).magnitude;
                if (distance > edgeBandRadius)
                    continue;

                float sign = Vector3.Dot(midpoint - frame.center, frame.binormal);
                if (sign < 0f)
                    leftEdges.Add(state);
                else
                    rightEdges.Add(state);
            }

            bool leftOk = TryBuildBoundarySamplesFromBoundaryEdges(
                leftEdges,
                worldVertices,
                canonicalFrames,
                out leftBoundary,
                out leftDiagnostics);
            bool rightOk = TryBuildBoundarySamplesFromBoundaryEdges(
                rightEdges,
                worldVertices,
                canonicalFrames,
                out rightBoundary,
                out rightDiagnostics);

            if (!leftOk || !rightOk)
            {
                string leftReason = string.IsNullOrEmpty(leftDiagnostics.failReason) ? "left band boundary invalid" : leftDiagnostics.failReason;
                string rightReason = string.IsNullOrEmpty(rightDiagnostics.failReason) ? "right band boundary invalid" : rightDiagnostics.failReason;
                failReason = $"Band baseline boundaries are invalid. left={leftReason}; right={rightReason}";
                return false;
            }

            return true;
        }

        private static bool TryBuildBoundarySamplesFromBoundaryEdges(
            List<BoundaryEdgeState> edges,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            out List<BoundaryVertexSample> boundary,
            out BoundaryBuildDiagnostics diagnostics,
            bool allowRailAuthoritativeFallback = false)
        {
            boundary = new List<BoundaryVertexSample>();
            diagnostics = default;
            diagnostics.edgeCount = edges != null ? edges.Count : 0;
            if (edges == null || edges.Count == 0)
            {
                diagnostics.failReason = "No boundary edges were produced.";
                return false;
            }

            List<List<int>> components = BuildBoundaryComponents(edges);
            diagnostics.componentCount = components.Count;
            diagnostics.componentSizes = FormatBoundaryComponentSizes(components);
            BoundaryOrderingResult ordering = BuildOrderedBoundaryFromComponents(components, edges, worldVertices, canonicalFrames, out string orderingFailReason);
            diagnostics.selectedComponentVertexCount = ordering.componentVertexCount;
            diagnostics.orderedVertexCount = ordering.orderedVertexCount;
            diagnostics.degree1VertexCount = ordering.degree1VertexCount;
            diagnostics.degree2VertexCount = ordering.degree2VertexCount;
            diagnostics.branchVertexCount = ordering.branchVertexCount;
            diagnostics.failReason = orderingFailReason;
            if (!string.IsNullOrEmpty(orderingFailReason))
                return false;

            boundary = ordering.samples;
            return boundary.Count >= 2;
        }

        private static bool TryExtractBoundaryChains(
            MeshWorkData work,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames,
            HashSet<int> removedTriangles,
            float openWidthWorld,
            float averageEdgeLength,
            out List<BoundaryVertexSample> leftBoundary,
            out List<BoundaryVertexSample> rightBoundary)
        {
            leftBoundary = new List<BoundaryVertexSample>();
            rightBoundary = new List<BoundaryVertexSample>();

            Dictionary<ulong, BoundaryEdgeState> edgeStates = BuildBoundaryEdgeStates(work, removedTriangles);
            if (edgeStates.Count == 0)
                return false;

            float edgeBandRadius = Mathf.Max(openWidthWorld * 1.6f, averageEdgeLength * 1.75f);
            var leftEdges = new List<BoundaryEdgeState>();
            var rightEdges = new List<BoundaryEdgeState>();
            float totalLength = canonicalFrames[canonicalFrames.Count - 1].s;
            float sPad = Mathf.Max(openWidthWorld, averageEdgeLength * 1.2f);

            foreach (var pair in edgeStates)
            {
                BoundaryEdgeState state = pair.Value;
                if (state.removedCount <= 0 || state.keptCount <= 0)
                    continue;
                if (!IsValidTriangleVertex(state.a, worldVertices) || !IsValidTriangleVertex(state.b, worldVertices))
                    continue;

                Vector3 midpoint = 0.5f * (worldVertices[state.a] + worldVertices[state.b]);
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, midpoint);
                if (frame.s < -sPad || frame.s > totalLength + sPad)
                    continue;

                float distance = (midpoint - frame.center).magnitude;
                if (distance > edgeBandRadius)
                    continue;

                float sign = Vector3.Dot(midpoint - frame.center, frame.binormal);
                if (sign < 0f)
                    leftEdges.Add(state);
                else
                    rightEdges.Add(state);
            }

            if (leftEdges.Count == 0 || rightEdges.Count == 0)
                return false;

            leftBoundary = BuildOrderedBoundary(leftEdges, worldVertices, canonicalFrames);
            rightBoundary = BuildOrderedBoundary(rightEdges, worldVertices, canonicalFrames);
            return leftBoundary.Count >= 2 && rightBoundary.Count >= 2;
        }

        private static Dictionary<ulong, BoundaryEdgeState> BuildBoundaryEdgeStates(MeshWorkData work, HashSet<int> removedTriangles)
        {
            var result = new Dictionary<ulong, BoundaryEdgeState>(work.triangles.Count);
            int triangleCount = work.triangles.Count / 3;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                bool removed = removedTriangles.Contains(tri);
                GetTriangle(work.triangles, tri, out int a, out int b, out int c);
                AccumulateBoundaryEdge(result, a, b, removed);
                AccumulateBoundaryEdge(result, b, c, removed);
                AccumulateBoundaryEdge(result, c, a, removed);
            }

            return result;
        }

        private static void AccumulateBoundaryEdge(Dictionary<ulong, BoundaryEdgeState> states, int a, int b, bool removed)
        {
            ulong key = MakeUndirectedEdgeKey(a, b);
            if (!states.TryGetValue(key, out BoundaryEdgeState state))
            {
                state = new BoundaryEdgeState
                {
                    a = a,
                    b = b,
                };
            }

            if (removed)
                state.removedCount++;
            else
                state.keptCount++;

            states[key] = state;
        }

        private static List<BoundaryVertexSample> BuildOrderedBoundary(
            List<BoundaryEdgeState> edges,
            Vector3[] worldVertices,
            List<FrameSample> canonicalFrames)
        {
            List<List<int>> components = BuildBoundaryComponents(edges);
            float bestScore = float.NegativeInfinity;
            List<int> bestVertices = null;
            for (int i = 0; i < components.Count; i++)
            {
                List<int> vertices = components[i];
                float score = 0f;
                for (int j = 1; j < vertices.Count; j++)
                    score += Vector3.Distance(worldVertices[vertices[j - 1]], worldVertices[vertices[j]]);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestVertices = vertices;
                }
            }

            var result = new List<BoundaryVertexSample>();
            if (bestVertices == null)
                return result;

            for (int i = 0; i < bestVertices.Count; i++)
            {
                int vertexIndex = bestVertices[i];
                if (!IsValidTriangleVertex(vertexIndex, worldVertices))
                    continue;

                Vector3 world = worldVertices[vertexIndex];
                FrameSample frame = SampleFrameAtPoint(canonicalFrames, world);
                result.Add(new BoundaryVertexSample
                {
                    vertexIndex = vertexIndex,
                    s = frame.s,
                    world = world,
                });
            }

            result.Sort((x, y) =>
            {
                int cmp = x.s.CompareTo(y.s);
                if (cmp != 0)
                    return cmp;
                return x.vertexIndex.CompareTo(y.vertexIndex);
            });

            RemoveDuplicateBoundarySamples(result);
            return result;
        }

        private static List<List<int>> BuildBoundaryComponents(List<BoundaryEdgeState> edges)
        {
            var adjacency = new Dictionary<int, List<int>>();
            for (int i = 0; i < edges.Count; i++)
            {
                AddBoundaryNeighbor(adjacency, edges[i].a, edges[i].b);
                AddBoundaryNeighbor(adjacency, edges[i].b, edges[i].a);
            }

            var visited = new HashSet<int>();
            var result = new List<List<int>>();
            foreach (var pair in adjacency)
            {
                if (!visited.Add(pair.Key))
                    continue;

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(pair.Key);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    component.Add(current);
                    List<int> neighbors = adjacency[current];
                    for (int i = 0; i < neighbors.Count; i++)
                    {
                        if (visited.Add(neighbors[i]))
                            queue.Enqueue(neighbors[i]);
                    }
                }

                result.Add(component);
            }

            return result;
        }

        private static string FormatBoundaryComponentSizes(List<List<int>> components)
        {
            if (components == null || components.Count == 0)
                return "none";

            var sizes = new List<int>(components.Count);
            for (int i = 0; i < components.Count; i++)
                sizes.Add(components[i] != null ? components[i].Count : 0);
            sizes.Sort((a, b) => b.CompareTo(a));
            return string.Join(",", sizes);
        }

        private static void AddBoundaryNeighbor(Dictionary<int, List<int>> adjacency, int a, int b)
        {
            if (!adjacency.TryGetValue(a, out List<int> list))
            {
                list = new List<int>(4);
                adjacency.Add(a, list);
            }

            if (!list.Contains(b))
                list.Add(b);
        }

        private static void RemoveDuplicateBoundarySamples(List<BoundaryVertexSample> samples)
        {
            if (samples.Count < 2)
                return;

            int write = 1;
            BoundaryVertexSample prev = samples[0];
            for (int i = 1; i < samples.Count; i++)
            {
                BoundaryVertexSample cur = samples[i];
                if (cur.vertexIndex == prev.vertexIndex)
                    continue;
                if (Mathf.Abs(cur.s - prev.s) <= 1e-5f && (cur.world - prev.world).sqrMagnitude <= 1e-10f)
                    continue;

                samples[write++] = cur;
                prev = cur;
            }

            if (write < samples.Count)
                samples.RemoveRange(write, samples.Count - write);
        }

        private static void ComputeBoundarySampleMetrics(
            List<BoundaryVertexSample> samples,
            out float polylineLength,
            out float sMin,
            out float sMax)
        {
            polylineLength = 0f;
            sMin = 0f;
            sMax = 0f;
            if (samples == null || samples.Count == 0)
                return;

            sMin = float.PositiveInfinity;
            sMax = float.NegativeInfinity;
            for (int i = 0; i < samples.Count; i++)
            {
                sMin = Mathf.Min(sMin, samples[i].s);
                sMax = Mathf.Max(sMax, samples[i].s);
                if (i > 0)
                    polylineLength += Vector3.Distance(samples[i - 1].world, samples[i].world);
            }

            if (float.IsInfinity(sMin))
                sMin = 0f;
            if (float.IsInfinity(sMax))
                sMax = 0f;
        }

        private static void BuildStationPairsFromBoundaries(
            List<FrameSample> canonicalFrames,
            List<BoundaryVertexSample> leftBoundary,
            List<BoundaryVertexSample> rightBoundary,
            float openWidthWorld,
            List<StationResult> outStations)
        {
            outStations.Clear();
            int leftCursor = 0;
            int rightCursor = 0;
            for (int i = 0; i < canonicalFrames.Count; i++)
            {
                FrameSample frame = canonicalFrames[i];
                BoundaryVertexSample left = FindNearestBoundarySample(leftBoundary, frame.s, ref leftCursor);
                BoundaryVertexSample right = FindNearestBoundarySample(rightBoundary, frame.s, ref rightCursor);
                if (left.vertexIndex < 0 || right.vertexIndex < 0 || left.vertexIndex == right.vertexIndex)
                    continue;

                Vector3 split = right.world - left.world;
                float actualOpen = split.magnitude;
                if (actualOpen <= 1e-6f)
                    continue;

                split /= actualOpen;
                if (Vector3.Dot(split, frame.binormal) < 0f)
                {
                    BoundaryVertexSample tmp = left;
                    left = right;
                    right = tmp;
                    split = -split;
                }

                outStations.Add(new StationResult
                {
                    s = frame.s,
                    strokeT = frame.strokeT,
                    targetOpen = frame.targetOpen,
                    centerWorld = 0.5f * (left.world + right.world),
                    tangentWorld = frame.tangent,
                    normalWorld = frame.normal,
                    binormalWorld = frame.binormal,
                    leftWorld = left.world,
                    rightWorld = right.world,
                    leftVertexIndex = left.vertexIndex,
                    rightVertexIndex = right.vertexIndex,
                });
            }

            CompactStations(outStations);
        }

        private static float ComputeAverageRealizedOpenWidth(List<StationResult> stations)
        {
            if (stations == null || stations.Count == 0)
                return 0f;

            float sum = 0f;
            int count = 0;
            for (int i = 0; i < stations.Count; i++)
            {
                StationResult station = stations[i];
                if (station.leftVertexIndex < 0 || station.rightVertexIndex < 0 || station.leftVertexIndex == station.rightVertexIndex)
                    continue;

                float width = Vector3.Distance(station.leftWorld, station.rightWorld);
                if (width <= 1e-6f)
                    continue;

                sum += width;
                count++;
            }

            return count > 0 ? (sum / count) : 0f;
        }

        private static float EstimateTaperLengthFromFrames(List<FrameSample> frames)
        {
            if (frames == null || frames.Count < 2)
                return 0f;

            float maxOpen = 0f;
            for (int i = 0; i < frames.Count; i++)
                maxOpen = Mathf.Max(maxOpen, frames[i].targetOpen);
            if (maxOpen <= 1e-6f)
                return 0f;

            float threshold = maxOpen * 0.98f;
            float firstPlateauS = frames[frames.Count - 1].s;
            float lastPlateauS = 0f;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].targetOpen >= threshold)
                {
                    firstPlateauS = frames[i].s;
                    break;
                }
            }

            for (int i = frames.Count - 1; i >= 0; i--)
            {
                if (frames[i].targetOpen >= threshold)
                {
                    lastPlateauS = frames[i].s;
                    break;
                }
            }

            float totalLength = frames[frames.Count - 1].s;
            return Mathf.Max(0f, Mathf.Min(firstPlateauS, totalLength - lastPlateauS));
        }

        private static BoundaryVertexSample FindNearestBoundarySample(List<BoundaryVertexSample> samples, float targetS, ref int cursor)
        {
            if (samples == null || samples.Count == 0)
                return new BoundaryVertexSample { vertexIndex = -1 };

            cursor = Mathf.Clamp(cursor, 0, samples.Count - 1);
            while (cursor + 1 < samples.Count && samples[cursor + 1].s <= targetS)
                cursor++;

            BoundaryVertexSample best = samples[cursor];
            if (cursor + 1 < samples.Count)
            {
                float currentDelta = Mathf.Abs(best.s - targetS);
                float nextDelta = Mathf.Abs(samples[cursor + 1].s - targetS);
                if (nextDelta < currentDelta)
                    best = samples[cursor + 1];
            }

            return best;
        }

        private static void CompactStations(List<StationResult> stations)
        {
            if (stations.Count < 2)
                return;

            int write = 1;
            StationResult prev = stations[0];
            for (int i = 1; i < stations.Count; i++)
            {
                StationResult cur = stations[i];
                bool duplicatePair = prev.leftVertexIndex == cur.leftVertexIndex && prev.rightVertexIndex == cur.rightVertexIndex;
                bool duplicateCenter = (prev.centerWorld - cur.centerWorld).sqrMagnitude <= 1e-10f;
                if (duplicatePair || duplicateCenter)
                    continue;

                stations[write++] = cur;
                prev = cur;
            }

            if (write < stations.Count)
                stations.RemoveRange(write, stations.Count - write);
        }

        private static void CompactKeptTriangles(MeshWorkData work, HashSet<int> removedTriangles)
        {
            var kept = new List<int>(work.triangles.Count);
            int triangleCount = work.triangles.Count / 3;
            for (int tri = 0; tri < triangleCount; tri++)
            {
                if (removedTriangles.Contains(tri))
                    continue;

                int baseIndex = tri * 3;
                kept.Add(work.triangles[baseIndex + 0]);
                kept.Add(work.triangles[baseIndex + 1]);
                kept.Add(work.triangles[baseIndex + 2]);
            }

            work.triangles.Clear();
            work.triangles.AddRange(kept);
        }

        private static void BuildInnerGuideFromOuterStations(
            List<StationResult> outerStations,
            MeshWorkData innerWork,
            Transform outerTransform,
            Transform innerTransform,
            AxisFrame outerAxis,
            AxisFrame innerAxis,
            List<Vector3> outGuideWorld)
        {
            outGuideWorld.Clear();
            if (outerStations == null || outerStations.Count == 0 || innerWork == null || innerTransform == null)
                return;

            Vector3[] innerWorldVertices = BuildWorldVertexArray(innerWork.vertices, innerTransform);
            if (!outerAxis.hasSweepStations ||
                !innerAxis.hasSweepStations)
                return;

            for (int i = 0; i < outerStations.Count; i++)
            {
                Vector3 localOuter = outerTransform != null
                    ? outerTransform.InverseTransformPoint(outerStations[i].centerWorld)
                    : outerStations[i].centerWorld;
                if (!TryProjectPointToSweepAxis(
                        outerAxis,
                        localOuter,
                        out float outerDistance,
                        out Vector3 outerCenter,
                        out Vector3 outerTangent,
                        out Vector3 outerRight,
                        out Vector3 outerUp,
                        out _))
                    continue;

                Vector3 outerRadial =
                    Vector3.ProjectOnPlane(
                        localOuter - outerCenter,
                        outerTangent);
                if (outerRadial.sqrMagnitude <= 1e-12f)
                    continue;
                float theta = Mathf.Atan2(
                    Vector3.Dot(outerRadial, outerUp),
                    Vector3.Dot(outerRadial, outerRight));
                float station01 =
                    outerAxis.totalStationDistanceLocal > 1e-6f
                        ? Mathf.Clamp01(
                            outerDistance /
                            outerAxis.totalStationDistanceLocal)
                        : 0f;
                float innerDistance =
                    station01 *
                    innerAxis.totalStationDistanceLocal;
                if (!TrySampleSweepAxis(
                        innerAxis,
                        innerDistance,
                        out Vector3 innerCenter,
                        out _,
                        out Vector3 innerRight,
                        out Vector3 innerUp,
                        out float innerRadius))
                    continue;

                Vector3 innerRadial =
                    innerRight * Mathf.Cos(theta) +
                    innerUp * Mathf.Sin(theta);
                Vector3 targetLocal =
                    innerCenter +
                    innerRadial * innerRadius;
                Vector3 targetWorld = innerTransform.TransformPoint(targetLocal);
                int nearest = FindNearestVertexIndex(targetWorld, innerWorldVertices);
                if (nearest >= 0)
                    outGuideWorld.Add(innerWorldVertices[nearest]);
            }

            RemoveDuplicatePoints(outGuideWorld);
        }

        private static void RemoveDuplicatePoints(List<Vector3> points)
        {
            if (points.Count < 2)
                return;

            int write = 1;
            Vector3 prev = points[0];
            for (int i = 1; i < points.Count; i++)
            {
                if ((points[i] - prev).sqrMagnitude <= 1e-12f)
                    continue;
                points[write++] = points[i];
                prev = points[i];
            }

            if (write < points.Count)
                points.RemoveRange(write, points.Count - write);
        }

        private static int FindNearestVertexIndex(Vector3 point, Vector3[] vertices)
        {
            int bestIndex = -1;
            float bestDistanceSq = float.PositiveInfinity;
            for (int i = 0; i < vertices.Length; i++)
            {
                float distanceSq = (vertices[i] - point).sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static List<float> BuildPolylineCumulative(List<Vector3> polyline)
        {
            var result = new List<float>(polyline.Count);
            float total = 0f;
            result.Add(0f);
            for (int i = 1; i < polyline.Count; i++)
            {
                total += Vector3.Distance(polyline[i - 1], polyline[i]);
                result.Add(total);
            }
            return result;
        }

        private static Vector3 SamplePolylineAtDistance(List<Vector3> polyline, List<float> cumulative, float distance)
        {
            if (polyline.Count == 0)
                return Vector3.zero;
            if (polyline.Count == 1)
                return polyline[0];

            float clampedDistance = Mathf.Clamp(distance, 0f, cumulative[cumulative.Count - 1]);
            for (int i = 0; i < cumulative.Count - 1; i++)
            {
                float a = cumulative[i];
                float b = cumulative[i + 1];
                if (clampedDistance <= b || i == cumulative.Count - 2)
                {
                    float denom = Mathf.Max(1e-6f, b - a);
                    float t = Mathf.Clamp01((clampedDistance - a) / denom);
                    return Vector3.Lerp(polyline[i], polyline[i + 1], t);
                }
            }

            return polyline[polyline.Count - 1];
        }

        private static TriangleProjection FindNearestTriangleProjection(
            MeshWorkData work,
            Vector3[] worldVertices,
            Vector3 point,
            ICollection<int> candidateTriangles)
        {
            var result = new TriangleProjection
            {
                triangleIndex = -1,
                point = point,
                distanceSq = float.PositiveInfinity,
            };
            if (work == null || worldVertices == null)
                return result;

            int triangleCount = work.triangles.Count / 3;
            if (candidateTriangles == null)
            {
                for (int tri = 0; tri < triangleCount; tri++)
                    EvaluateTriangleProjection(work, worldVertices, point, tri, ref result);
                return result;
            }

            foreach (int tri in candidateTriangles)
                EvaluateTriangleProjection(work, worldVertices, point, tri, ref result);
            return result;
        }

        private static void EvaluateTriangleProjection(
            MeshWorkData work,
            Vector3[] worldVertices,
            Vector3 point,
            int triangleIndex,
            ref TriangleProjection best)
        {
            if (triangleIndex < 0 || triangleIndex >= work.triangles.Count / 3)
                return;

            GetTriangle(work.triangles, triangleIndex, out int ia, out int ib, out int ic);
            if (!IsValidTriangleVertex(ia, worldVertices) || !IsValidTriangleVertex(ib, worldVertices) || !IsValidTriangleVertex(ic, worldVertices))
                return;

            Vector3 closest = ClosestPointOnTriangle(point, worldVertices[ia], worldVertices[ib], worldVertices[ic]);
            float distanceSq = (point - closest).sqrMagnitude;
            if (distanceSq < best.distanceSq)
            {
                best.triangleIndex = triangleIndex;
                best.point = closest;
                best.distanceSq = distanceSq;
            }
        }

        private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;

            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f)
                return a;

            Vector3 bp = point - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3)
                return b;

            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            {
                float v = d1 / Mathf.Max(1e-8f, d1 - d3);
                return a + ab * v;
            }

            Vector3 cp = point - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6)
                return c;

            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            {
                float w = d2 / Mathf.Max(1e-8f, d2 - d6);
                return a + ac * w;
            }

            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
            {
                float w = (d4 - d3) / Mathf.Max(1e-8f, (d4 - d3) + (d5 - d6));
                return b + (c - b) * w;
            }

            float denom = 1f / Mathf.Max(1e-8f, va + vb + vc);
            float vFace = vb * denom;
            float wFace = vc * denom;
            return a + ab * vFace + ac * wFace;
        }

        private static PolylineProjection ProjectPointToPolyline(Vector3 point, List<Vector3> polyline, List<float> cumulative)
        {
            var best = new PolylineProjection
            {
                point = polyline[0],
                tangent = polyline.Count > 1 ? (polyline[1] - polyline[0]).normalized : Vector3.forward,
                s = 0f,
                tOnSegment = 0f,
                segmentIndex = 0,
                distanceSq = (point - polyline[0]).sqrMagnitude,
            };

            for (int i = 0; i < polyline.Count - 1; i++)
            {
                Vector3 a = polyline[i];
                Vector3 b = polyline[i + 1];
                Vector3 ab = b - a;
                float denom = ab.sqrMagnitude;
                float t = denom > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom) : 0f;
                Vector3 sample = a + ab * t;
                float distanceSq = (point - sample).sqrMagnitude;
                if (distanceSq < best.distanceSq)
                {
                    best.point = sample;
                    best.tangent = denom > 1e-12f ? ab.normalized : best.tangent;
                    best.s = Mathf.Lerp(cumulative[i], cumulative[i + 1], t);
                    best.tOnSegment = t;
                    best.segmentIndex = i;
                    best.distanceSq = distanceSq;
                }
            }

            return best;
        }

        private static FrameSample SampleFrameAtPoint(List<FrameSample> frames, Vector3 point)
        {
            if (frames.Count == 1)
                return frames[0];

            float bestDistanceSq = float.PositiveInfinity;
            FrameSample best = frames[0];
            for (int i = 0; i < frames.Count - 1; i++)
            {
                Vector3 a = frames[i].center;
                Vector3 b = frames[i + 1].center;
                Vector3 ab = b - a;
                float denom = ab.sqrMagnitude;
                float t = denom > 1e-12f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / denom) : 0f;
                Vector3 samplePoint = a + ab * t;
                float distanceSq = (point - samplePoint).sqrMagnitude;
                if (distanceSq < bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    best.center = samplePoint;
                    best.tangent = Vector3.Slerp(frames[i].tangent, frames[i + 1].tangent, t).normalized;
                    best.normal = Vector3.Slerp(frames[i].normal, frames[i + 1].normal, t).normalized;
                    best.binormal = Vector3.Slerp(frames[i].binormal, frames[i + 1].binormal, t).normalized;
                    best.s = Mathf.Lerp(frames[i].s, frames[i + 1].s, t);
                    best.strokeT = Mathf.Lerp(frames[i].strokeT, frames[i + 1].strokeT, t);
                    best.targetOpen = Mathf.Lerp(frames[i].targetOpen, frames[i + 1].targetOpen, t);
                }
            }

            return best;
        }

        private static bool TryGetSharedEdge(List<int> triangles, int triA, int triB, out int edgeA, out int edgeB)
        {
            edgeA = -1;
            edgeB = -1;
            GetTriangle(triangles, triA, out int a0, out int a1, out int a2);
            GetTriangle(triangles, triB, out int b0, out int b1, out int b2);
            int sharedCount = 0;
            if (TriangleContainsVertex(b0, a0, a1, a2))
            {
                edgeA = b0;
                sharedCount++;
            }
            if (TriangleContainsVertex(b1, a0, a1, a2))
            {
                if (sharedCount == 0)
                    edgeA = b1;
                else
                    edgeB = b1;
                sharedCount++;
            }
            if (TriangleContainsVertex(b2, a0, a1, a2))
            {
                if (sharedCount == 0)
                    edgeA = b2;
                else
                    edgeB = b2;
                sharedCount++;
            }

            return sharedCount >= 2;
        }

        private static bool TriangleContainsVertex(int vertex, int a, int b, int c)
        {
            return vertex == a || vertex == b || vertex == c;
        }

        private static void GetTriangle(List<int> triangles, int triangleIndex, out int a, out int b, out int c)
        {
            int baseIndex = triangleIndex * 3;
            a = triangles[baseIndex + 0];
            b = triangles[baseIndex + 1];
            c = triangles[baseIndex + 2];
        }

        private static bool IsValidTriangleVertex(int index, Vector3[] vertices)
        {
            return index >= 0 && index < vertices.Length;
        }

        private static ulong MakeUndirectedEdgeKey(int a, int b)
        {
            uint ua = (uint)Mathf.Min(a, b);
            uint ub = (uint)Mathf.Max(a, b);
            return ((ulong)ua << 32) | ub;
        }
    }
}
