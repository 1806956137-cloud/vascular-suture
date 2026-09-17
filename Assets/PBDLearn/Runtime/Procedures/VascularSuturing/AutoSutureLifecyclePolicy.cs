using UnityEngine;

namespace PBDLearn
{
    public enum AutoSutureLifecycleStage
    {
        WaitRealContact = 0,
        AdvanceUntilHist = 1,
        PostHistClearance = 2,
        Ready = 3,
    }

    public static class AutoSutureLifecyclePolicy
    {
        public static Vector3 TransportPoseUp(Vector3 previousUp, Vector3 forward)
        {
            Vector3 normalizedForward = forward.sqrMagnitude > 1e-8f
                ? forward.normalized
                : Vector3.forward;
            Vector3 up = Vector3.ProjectOnPlane(previousUp, normalizedForward);
            if (up.sqrMagnitude < 1e-8f)
            {
                Vector3 fallback = Mathf.Abs(Vector3.Dot(normalizedForward, Vector3.up)) < 0.92f
                    ? Vector3.up
                    : Vector3.right;
                up = Vector3.ProjectOnPlane(fallback, normalizedForward);
            }
            up.Normalize();
            if (previousUp.sqrMagnitude > 1e-8f && Vector3.Dot(previousUp, up) < 0f)
                up = -up;
            return up;
        }

        public static float ComputeMotionStep(float speed, float deltaTime, float maxStep)
        {
            float distance = (speed > 0f ? speed : 0f) * (deltaTime > 0f ? deltaTime : 0f);
            float limit = maxStep > 0f ? maxStep : 0f;
            return distance < limit ? distance : limit;
        }

        public static AutoSutureLifecycleStage Evaluate(
            bool hasOuter,
            bool hasInner,
            bool hasCurrent,
            bool hasHist,
            float postHistTravel,
            float requiredPostHistTravel)
        {
            if (hasOuter && hasInner && hasCurrent && !hasHist)
                return AutoSutureLifecycleStage.AdvanceUntilHist;

            float requiredTravel = requiredPostHistTravel > 0f ? requiredPostHistTravel : 0f;
            if (hasOuter && hasInner && !hasCurrent && hasHist &&
                postHistTravel < requiredTravel)
            {
                return AutoSutureLifecycleStage.PostHistClearance;
            }

            if (hasOuter && hasInner && !hasCurrent && hasHist)
                return AutoSutureLifecycleStage.Ready;

            return AutoSutureLifecycleStage.WaitRealContact;
        }
    }
}
