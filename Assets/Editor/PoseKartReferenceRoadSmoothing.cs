using UnityEditor;

namespace PoseKart.Editor
{
    // Compatibility entry point. The old implementation edited the visual road
    // mesh, so it is intentionally replaced by the collision-only workflow.
    public static class PoseKartReferenceRoadSmoothing
    {
        [MenuItem("PoseKart/Reference/Smooth Continuous Sand Road (opens physics tool)", false, 1000)]
        private static void OpenPhysicsTool()
        {
            SmoothRoadColliderGenerator.Open();
        }
    }
}
