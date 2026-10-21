#if UNITY_EDITOR
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseKart.EditorTools
{
    public static class PoseKartBananaLauncherSetup
    {
        private const string DefaultLauncherPrefabPath = "Assets/PoseKart/Asset Them Tu Be Ngoai/ROCKET_LAUNCHER_FULL/Rocket Launcher/Prefabs/Launcher.prefab";
        private const string DefaultBananaTrapPrefabPath = "Assets/PoseKart/Asset Them Tu Be Ngoai/banana-peel-mario-kart/source/Banana Peel.prefab";

        [MenuItem("PoseKart/Items/Setup Banana Triple Launcher")]
        public static void SetupBananaTripleLauncherInScene()
        {
            CarController[] cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            if (cars == null || cars.Length == 0)
            {
                Debug.LogWarning("[Banana Launcher Setup] No CarController found in active scene.");
                return;
            }

            GameObject launcherPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultLauncherPrefabPath);
            GameObject bananaTrapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultBananaTrapPrefabPath);

            int configuredCount = 0;

            foreach (CarController car in cars)
            {
                if (car == null) continue;

                Undo.RegisterCompleteObjectUndo(car.gameObject, "Setup Banana Triple Launcher");

                // 1. Ensure Weapon Mount Transform
                Transform mount = car.transform.Find("BananaWeaponMount");
                if (mount == null)
                {
                    GameObject mountObj = new GameObject("BananaWeaponMount");
                    mountObj.transform.SetParent(car.transform, false);
                    mountObj.transform.localPosition = new Vector3(0f, 0.95f, -0.45f);
                    mountObj.transform.localRotation = Quaternion.identity;
                    mount = mountObj.transform;
                    Undo.RegisterCreatedObjectUndo(mountObj, "Create BananaWeaponMount");
                }

                // 2. Ensure PoseKartBananaTripleLauncher Component
                PoseKartBananaTripleLauncher launcher = car.GetComponent<PoseKartBananaTripleLauncher>();
                if (launcher == null)
                {
                    launcher = Undo.AddComponent<PoseKartBananaTripleLauncher>(car.gameObject);
                }

                // 3. Configure References
                launcher.WeaponMount = mount;

                if (launcher.LauncherPrefab == null && launcherPrefab != null)
                {
                    launcher.LauncherPrefab = launcherPrefab;
                }

                if (launcher.BananaTrapPrefab == null && bananaTrapPrefab != null)
                {
                    launcher.BananaTrapPrefab = bananaTrapPrefab;
                }

                // Call setup to instantiate/resolve hierarchy and MuzzlePoint
                launcher.SetupHierarchy();

                EditorUtility.SetDirty(launcher);
                EditorUtility.SetDirty(car.gameObject);
                configuredCount++;
            }

            if (configuredCount > 0)
            {
                if (!Application.isPlaying)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }
                Debug.Log($"<color=#55FF55><b>[Banana Launcher Setup]</b> Successfully configured Banana Triple Launcher on {configuredCount} vehicles (Player + Demon AI)!</color>");
            }
        }
    }
}
#endif
