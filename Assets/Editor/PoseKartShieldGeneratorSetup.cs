#if UNITY_EDITOR
using PoseKart.Race;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseKart.EditorTools
{
    public static class PoseKartShieldGeneratorSetup
    {
        [MenuItem("PoseKart/Items/Setup Force Shield Generator")]
        public static void SetupForceShieldGeneratorInScene()
        {
            CarController[] cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            if (cars == null || cars.Length == 0)
            {
                Debug.LogWarning("[Shield Generator Setup] No CarController found in active scene.");
                return;
            }

            int configuredCount = 0;

            foreach (CarController car in cars)
            {
                if (car == null) continue;

                Undo.RegisterCompleteObjectUndo(car.gameObject, "Setup Force Shield Generator");

                // 1. Ensure Generator Mount Point (On roof/rear of kart)
                Transform mount = car.transform.Find("ShieldGeneratorMount");
                if (mount == null)
                {
                    GameObject mountObj = new GameObject("ShieldGeneratorMount");
                    mountObj.transform.SetParent(car.transform, false);
                    mountObj.transform.localPosition = new Vector3(0f, 1.05f, -0.45f);
                    mountObj.transform.localRotation = Quaternion.identity;
                    mount = mountObj.transform;
                    Undo.RegisterCreatedObjectUndo(mountObj, "Create ShieldGeneratorMount");
                }

                // 2. Ensure Shield Center Point (Vehicle center for spherical force shield)
                Transform center = car.transform.Find("ShieldCenter");
                if (center == null)
                {
                    GameObject centerObj = new GameObject("ShieldCenter");
                    centerObj.transform.SetParent(car.transform, false);
                    centerObj.transform.localPosition = new Vector3(0f, 0.65f, 0f);
                    centerObj.transform.localRotation = Quaternion.identity;
                    center = centerObj.transform;
                    Undo.RegisterCreatedObjectUndo(centerObj, "Create ShieldCenter");
                }

                // 3. Ensure PoseKartShieldGeneratorVisual Component
                PoseKartShieldGeneratorVisual shieldVisual = car.GetComponent<PoseKartShieldGeneratorVisual>();
                if (shieldVisual == null)
                {
                    shieldVisual = Undo.AddComponent<PoseKartShieldGeneratorVisual>(car.gameObject);
                }

                // 4. Bind Mount & Center Points
                shieldVisual.GeneratorMountPoint = mount;
                shieldVisual.ShieldCenterPoint = center;

                // Call SetupHierarchy to ensure child structures are ready
                shieldVisual.SetupHierarchy();

                EditorUtility.SetDirty(shieldVisual);
                EditorUtility.SetDirty(car.gameObject);
                configuredCount++;
            }

            if (configuredCount > 0)
            {
                if (!Application.isPlaying)
                {
                    EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
                }
                Debug.Log($"<color=#55FF55><b>[Force Shield Generator Setup]</b> Successfully configured Force Shield Generator mounts & visual controller on {configuredCount} vehicles (Player + Demon AI)! Slots are ready for user prefabs.</color>");
            }
        }
    }
}
#endif
