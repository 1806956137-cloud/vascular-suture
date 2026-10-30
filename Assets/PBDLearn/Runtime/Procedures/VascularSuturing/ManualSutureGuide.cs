using UnityEngine;

namespace PBDLearn.Procedures.VascularSuturing
{
    /// <summary>Consumes real puncture evidence. It never moves the needle or creates portals.</summary>
    public sealed class ManualSutureGuide
    {
        public int CompletedStitches { get; private set; }
        public int AcceptedPasses { get; private set; }
        public int LastAcceptedPass { get; private set; }
        public int PassA { get; private set; }
        public int PassB { get; private set; }
        public int PendingPass { get; private set; }
        public bool PullRequested { get; private set; }
        public bool AwaitingPull => AcceptedPasses == (CompletedStitches + 1) * 2;
        public bool IsComplete => CompletedStitches == 6;
        public int PairIndex => Mathf.Min(5, CompletedStitches);
        public int PassInPair => AwaitingPull ? 1 : AcceptedPasses % 2;

        public bool ObservePass(
            ClothSimulationDriverDoubleLayer.SuturePassSequenceRecord record,
            NeedlePassLifecycleInfo lifecycle)
        {
            if (IsComplete || AwaitingPull || record.passIndex <= LastAcceptedPass)
                return false;
            if (PendingPass == 0)
            {
                if (!record.isComplete || !record.hasOuter || !record.hasInner)
                    return false;
                PendingPass = record.passIndex;
            }
            if (record.passIndex != PendingPass || lifecycle.hasOuter == 0 ||
                lifecycle.hasInner == 0 || lifecycle.hasCurrent != 0 || lifecycle.hasHist == 0)
                return false;

            if (AcceptedPasses % 2 == 0) PassA = record.passIndex;
            else PassB = record.passIndex;
            LastAcceptedPass = record.passIndex;
            PendingPass = 0;
            AcceptedPasses++;
            return true;
        }

        public bool RequestPull()
        {
            if (IsComplete || !AwaitingPull || PullRequested || PassA <= 0 || PassB <= 0)
                return false;
            PullRequested = true;
            return true;
        }

        public bool ObserveClosure(bool workflowAdvanceReady)
        {
            if (!AwaitingPull || !PullRequested || !workflowAdvanceReady)
                return false;
            CompletedStitches++;
            PullRequested = false;
            PassA = 0;
            PassB = 0;
            return true;
        }
    }
}
