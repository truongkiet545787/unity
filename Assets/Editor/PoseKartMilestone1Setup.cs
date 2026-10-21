using PoseKart.Camera;
using PoseKart.Input;
using PoseKart.Vehicle;
using InvalidOperationException = System.InvalidOperationException;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    public static class PoseKartMilestone1Setup
    {
        private const string ScenePath = "Assets/Scenes/Milestones_Archive/Milestone1_TestDrive.unity";

        private static readonly string[] ManagedRootNames =
        {
            "Environment",
            "Player",
            "Main Camera",
            "Lighting"
        };

        [MenuItem("PoseKart/Setup/Milestone 1 Test Scene")]
        public static void SetupMilestone1TestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation to finish before rebuilding the Milestone 1 scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RemovePreviouslyGeneratedRoots(scene);

            GameObject environment = new GameObject("Environment");
            environment.SetActive(true);
            GameObject ground = BuildEnvironment(environment.transform);

            GameObject player = new GameObject("Player");
            player.SetActive(true);
            GameObject buggy = BuildBuggy(player.transform);

            GameObject cameraObject = BuildCamera(buggy.transform);
            GameObject lighting = BuildLighting();

            ValidateGeneratedScene(environment, ground, player, buggy, cameraObject, lighting);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save Milestone 1 scene to {ScenePath}.");
                return;
            }

            Selection.activeGameObject = buggy;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 1 test scene created at {ScenePath}.", cameraObject);
        }

        [MenuItem("PoseKart/Setup/Milestone 1 Test Scene", true)]
        private static bool ValidateSetupMilestone1TestSceneMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static GameObject BuildEnvironment(Transform parent)
        {
            GameObject ground = CreatePrimitive(
                "Ground",
                PrimitiveType.Cube,
                parent,
                new Vector3(0f, -0.25f, 15f),
                new Vector3(24f, 0.5f, 80f));

            CreatePrimitive(
                "Barrier_Left",
                PrimitiveType.Cube,
                parent,
                new Vector3(-12.25f, 0.75f, 15f),
                new Vector3(0.5f, 1.5f, 80f));

            CreatePrimitive(
                "Barrier_Right",
                PrimitiveType.Cube,
                parent,
                new Vector3(12.25f, 0.75f, 15f),
                new Vector3(0.5f, 1.5f, 80f));

            CreatePrimitive(
                "TestObstacle_01",
                PrimitiveType.Cube,
                parent,
                new Vector3(-4f, 0.5f, 0f),
                new Vector3(2.5f, 1f, 2.5f));

            CreatePrimitive(
                "TestObstacle_02",
                PrimitiveType.Cube,
                parent,
                new Vector3(4f, 0.5f, 14f),
                new Vector3(2.5f, 1f, 2.5f));

            CreatePrimitive(
                "TestObstacle_03",
                PrimitiveType.Cube,
                parent,
                new Vector3(-3f, 0.5f, 29f),
                new Vector3(3f, 1f, 2f));

            return ground;
        }

        private static GameObject BuildBuggy(Transform playerRoot)
        {
            GameObject buggy = new GameObject("Buggy_Player");
            buggy.transform.SetParent(playerRoot, false);
            buggy.transform.position = new Vector3(0f, 0.6f, -20f);
            buggy.SetActive(true);

            BoxCollider bodyCollider = buggy.AddComponent<BoxCollider>();
            bodyCollider.center = Vector3.zero;
            bodyCollider.size = new Vector3(1.9f, 1f, 3.2f);
            bodyCollider.enabled = true;
            bodyCollider.isTrigger = false;

            Rigidbody body = buggy.AddComponent<Rigidbody>();
            body.mass = 650f;
            body.linearDamping = 0.2f;
            body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = true;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            KeyboardInputProvider keyboardInput = buggy.AddComponent<KeyboardInputProvider>();
            keyboardInput.enabled = true;
            CarController carController = buggy.AddComponent<CarController>();
            carController.enabled = true;
            carController.Configure(body, keyboardInput);

            CarResetController resetController = buggy.AddComponent<CarResetController>();
            resetController.enabled = true;
            resetController.Configure(body);

            GameObject bodyVisual = CreatePrimitive(
                "Body_Visual",
                PrimitiveType.Cube,
                buggy.transform,
                new Vector3(0f, 0.2f, 0f),
                new Vector3(1.8f, 0.6f, 3f),
                true);

            CreatePrimitive(
                "Cabin_Visual",
                PrimitiveType.Cube,
                buggy.transform,
                new Vector3(0f, 0.7f, -0.25f),
                new Vector3(1.3f, 0.45f, 1.45f),
                true);

            CreateWheel("Wheel_FrontLeft", buggy.transform, new Vector3(-1.05f, 0f, 1.05f));
            CreateWheel("Wheel_FrontRight", buggy.transform, new Vector3(1.05f, 0f, 1.05f));
            CreateWheel("Wheel_RearLeft", buggy.transform, new Vector3(-1.05f, 0f, -1.05f));
            CreateWheel("Wheel_RearRight", buggy.transform, new Vector3(1.05f, 0f, -1.05f));

            bodyVisual.transform.SetAsFirstSibling();
            return buggy;
        }

        private static GameObject BuildCamera(Transform target)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.SetActive(true);

            UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.enabled = true;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            AudioListener audioListener = cameraObject.AddComponent<AudioListener>();
            audioListener.enabled = true;

            ChaseCameraController chaseCamera = cameraObject.AddComponent<ChaseCameraController>();
            chaseCamera.enabled = true;
            chaseCamera.SetTarget(target);
            chaseCamera.SnapToTarget();
            return cameraObject;
        }

        private static GameObject BuildLighting()
        {
            GameObject lighting = new GameObject("Lighting");
            lighting.SetActive(true);

            GameObject lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(lighting.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            lightObject.SetActive(true);

            Light directionalLight = lightObject.AddComponent<Light>();
            directionalLight.enabled = true;
            directionalLight.type = LightType.Directional;
            directionalLight.intensity = 1.2f;
            directionalLight.shadows = LightShadows.Soft;

            GameObject volumeObject = new GameObject("Global Volume");
            volumeObject.transform.SetParent(lighting.transform, false);
            volumeObject.SetActive(true);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.enabled = true;
            volume.isGlobal = true;
            volume.priority = 0f;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                "Assets/Settings/SampleSceneProfile.asset");

            return lighting;
        }

        private static void CreateWheel(string name, Transform parent, Vector3 localPosition)
        {
            GameObject wheel = CreatePrimitive(
                name,
                PrimitiveType.Cylinder,
                parent,
                localPosition,
                new Vector3(0.9f, 0.35f, 0.9f),
                true);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType primitiveType,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            bool removeCollider = false)
        {
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.transform.localRotation = Quaternion.identity;
            gameObject.transform.localScale = scale;
            gameObject.SetActive(true);

            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
            }

            if (removeCollider)
            {
                Object.DestroyImmediate(gameObject.GetComponent<Collider>());
            }
            else
            {
                BoxCollider boxCollider = gameObject.GetComponent<BoxCollider>();
                if (boxCollider == null)
                {
                    boxCollider = gameObject.AddComponent<BoxCollider>();
                }

                boxCollider.enabled = true;
                boxCollider.isTrigger = false;
                boxCollider.center = Vector3.zero;
                boxCollider.size = Vector3.one;
            }

            return gameObject;
        }

        private static void ValidateGeneratedScene(
            GameObject environment,
            GameObject ground,
            GameObject player,
            GameObject buggy,
            GameObject cameraObject,
            GameObject lighting)
        {
            RequireActive(environment);
            RequireActive(player);
            RequireActive(buggy);
            RequireActive(cameraObject);
            RequireActive(lighting);

            ValidateSolid(environment.transform, "Ground");
            ValidateSolid(environment.transform, "Barrier_Left");
            ValidateSolid(environment.transform, "Barrier_Right");
            ValidateSolid(environment.transform, "TestObstacle_01");
            ValidateSolid(environment.transform, "TestObstacle_02");
            ValidateSolid(environment.transform, "TestObstacle_03");

            if (ground.transform.localPosition != new Vector3(0f, -0.25f, 15f) ||
                ground.transform.localRotation != Quaternion.identity ||
                ground.transform.localScale != new Vector3(24f, 0.5f, 80f))
            {
                throw new InvalidOperationException("Ground was not rebuilt with the required transform and scale.");
            }

            RequireEnabledBoxCollider(buggy);
            KeyboardInputProvider keyboardInput = RequireEnabled<KeyboardInputProvider>(buggy);
            CarController carController = RequireEnabled<CarController>(buggy);
            RequireEnabled<CarResetController>(buggy);

            Rigidbody body = buggy.GetComponent<Rigidbody>();
            RigidbodyConstraints expectedConstraints =
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (body == null || body.isKinematic || !body.useGravity || body.constraints != expectedConstraints)
            {
                throw new InvalidOperationException("Buggy_Player Rigidbody was not rebuilt with the required physics settings.");
            }

            if (carController.TargetBody != body || carController.InputProviderSource != keyboardInput)
            {
                throw new InvalidOperationException(
                    "CarController was not explicitly wired to the generated Rigidbody and KeyboardInputProvider.");
            }

            RequireEnabled<UnityEngine.Camera>(cameraObject);
            RequireEnabled<AudioListener>(cameraObject);
            RequireEnabled<ChaseCameraController>(cameraObject);

            Transform directionalLight = lighting.transform.Find("Directional Light");
            Transform globalVolume = lighting.transform.Find("Global Volume");
            if (directionalLight == null || globalVolume == null)
            {
                throw new InvalidOperationException("Lighting children were not rebuilt.");
            }

            RequireActive(directionalLight.gameObject);
            RequireActive(globalVolume.gameObject);
            RequireEnabled<Light>(directionalLight.gameObject);
            RequireEnabled<Volume>(globalVolume.gameObject);
        }

        private static void ValidateSolid(Transform parent, string childName)
        {
            Transform child = parent.Find(childName);
            if (child == null)
            {
                throw new InvalidOperationException($"Required scene object '{childName}' was not rebuilt.");
            }

            RequireActive(child.gameObject);
            BoxCollider collider = RequireEnabledBoxCollider(child.gameObject);
            if (collider.isTrigger)
            {
                throw new InvalidOperationException($"Required collider on '{childName}' cannot be a trigger.");
            }
        }

        private static void RequireActive(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                string objectName = gameObject == null ? "<missing>" : gameObject.name;
                throw new InvalidOperationException($"Required scene object '{objectName}' is not active.");
            }
        }

        private static T RequireEnabled<T>(GameObject gameObject) where T : Behaviour
        {
            T component = gameObject.GetComponent<T>();
            if (component == null || !component.enabled)
            {
                throw new InvalidOperationException(
                    $"Required enabled component '{typeof(T).Name}' is missing from '{gameObject.name}'.");
            }

            return component;
        }

        private static BoxCollider RequireEnabledBoxCollider(GameObject gameObject)
        {
            BoxCollider collider = gameObject.GetComponent<BoxCollider>();
            if (collider == null || !collider.enabled)
            {
                throw new InvalidOperationException(
                    $"Required enabled component '{nameof(BoxCollider)}' is missing from '{gameObject.name}'.");
            }

            return collider;
        }

        private static void RemovePreviouslyGeneratedRoots(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (string managedName in ManagedRootNames)
                {
                    if (root.name == managedName)
                    {
                        Object.DestroyImmediate(root);
                        break;
                    }
                }
            }
        }
    }
}
