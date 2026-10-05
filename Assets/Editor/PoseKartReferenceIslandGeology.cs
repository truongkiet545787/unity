using UnityEditor;
using UnityEngine;

namespace PoseKart.Editor
{
    public static partial class PoseKartReferenceIslandArt
    {
        private const string RockPack = "Assets/Skyden_Games/Low Poly Environment/Prefabs/";

        private static bool ImportedRock(Transform parent, Vector3 bottom, Vector3 size, float yaw)
        {
            bool cliff = size.y > 7;
            if(cliff)size.y*=1.25f;
            int variant = Mathf.Abs(Mathf.RoundToInt(yaw)) % (cliff ? 7 : 5) + 1;
            string path = RockPack + (cliff ? "Cliff " + variant : "Rock " + variant + " ") + ".prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) return false;
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = cliff ? "Layered cliff " + variant : "Coastal rock " + variant;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) { Object.DestroyImmediate(instance); return false; }
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
            instance.transform.localScale = new Vector3(size.x / Mathf.Max(.01f, bounds.size.x),
                size.y / Mathf.Max(.01f, bounds.size.y), size.z / Mathf.Max(.01f, bounds.size.z));
            Vector3 anchor = Vector3.Scale(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z), instance.transform.localScale);
            Quaternion rotation = Quaternion.Euler(0, yaw, 0);
            instance.transform.SetPositionAndRotation(bottom - rotation * anchor, rotation);
            Vector2 location = ImagePoint(bottom);
            bool canyon = location.x > 205 && location.x < 445 && location.y < 183;
            Material material = canyon
                ? Mat("Canyon sandstone matte", new Color(.66f, .30f, .14f), .02f)
                : Mat("Coastal granite matte", new Color(.43f, .49f, .51f), .02f);
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
            }
            return true;
        }

        private static void MountainMass(Transform parent, Vector3 bottom, Vector3 size, float yaw)
        {
            // Overlapping broad tiers read as one massif instead of isolated elongated stones.
            for (int tier = 0; tier < 3; tier++)
            {
                float taper = 1f - tier * .19f;
                Vector3 dimensions = new Vector3(size.x * taper, size.y * .46f, size.z * taper);
                Vector3 position = bottom + Vector3.up * (size.y * tier * .28f);
                if (RoadDistance(position) < Mathf.Sqrt(dimensions.x * dimensions.x + dimensions.z * dimensions.z) * .5f + 1f) continue;
                Boulder(parent, position, dimensions, yaw + tier * 13);
            }
        }

        private static void DressCaveMountain(Transform parent)
        {
            for (int i = 148; i <= 169; i += 3)
            {
                Vector3 forward = (route[i + 1] - route[i - 1]).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                float yaw = Quaternion.LookRotation(forward).eulerAngles.y;
                // Roof rocks stay above the tunnel's full clearance envelope.
                Boulder(parent, route[i] + Vector3.up * 14.5f,
                    new Vector3(23, 9 + i % 3, 13), yaw);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 position = route[i] + right * side * 15;
                    if (RoadDistance(position) < 7) continue;
                    Boulder(parent, position - Vector3.up * 3,
                        new Vector3(10, 21, 11), yaw);
                }
            }
        }
    }
}
