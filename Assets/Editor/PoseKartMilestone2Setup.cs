using InvalidOperationException = System.InvalidOperationException;
using PoseKart.Camera;
using PoseKart.Input;
using PoseKart.Race;
using PoseKart.UI;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PoseKart.Editor
{
    public static class PoseKartMilestone2Setup
    {
        private const string ScenePath = "Assets/Scenes/Milestone2_RaceTest.unity";
        private const int TotalLaps = 3;

        [MenuItem("PoseKart/Setup/Milestone 2 Race Test Scene")]
        public static void SetupMilestone2RaceTestScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation before rebuilding the Milestone 2 scene.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject environment = BuildEnvironment();
            GameObject playerRoot = new GameObject("Player");
            GameObject buggy = BuildBuggy(playerRoot.transform, out Rigidbody playerBody, out CarController carController);
            GameObject raceRoot = BuildRace(playerBody, carController, out RaceManager raceManager, out Checkpoint[] checkpoints);
            GameObject uiRoot = BuildHud(raceManager, out RaceHUD raceHud);
            GameObject cameraObject = BuildCamera(buggy.transform);
            GameObject lighting = BuildLighting();

            ValidateGeneratedScene(
                environment,
                playerRoot,
                buggy,
                raceRoot,
                raceManager,
                checkpoints,
                uiRoot,
                raceHud,
                cameraObject,
                lighting);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save Milestone 2 scene to {ScenePath}.");
                return;
            }

            Selection.activeGameObject = raceManager.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 2 race test scene created at {ScenePath}.", raceManager);
        }

        [MenuItem("PoseKart/Setup/Milestone 2 Race Test Scene", true)]
        private static bool ValidateSetupMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static GameObject BuildEnvironment()
        {
            GameObject environment = new GameObject("Environment");
            CreateSolidCube("Ground", environment.transform, new Vector3(0f, -0.25f, 0f), new Vector3(60f, 0.5f, 110f));

            GameObject track = CreateRoot("Track", environment.transform);
            CreateSolidCube("Track_LeftStraight", track.transform, new Vector3(-15f, 0.05f, 0f), new Vector3(14f, 0.1f, 80f));
            CreateSolidCube("Track_RightStraight", track.transform, new Vector3(15f, 0.05f, 0f), new Vector3(14f, 0.1f, 80f));
            CreateSolidCube("Track_TopTurn", track.transform, new Vector3(0f, 0.05f, 40f), new Vector3(44f, 0.1f, 16f));
            CreateSolidCube("Track_BottomTurn", track.transform, new Vector3(0f, 0.05f, -40f), new Vector3(44f, 0.1f, 16f));

            GameObject barriers = CreateRoot("Barriers", environment.transform);
            CreateSolidCube("Barrier_OuterLeft", barriers.transform, new Vector3(-23f, 0.75f, 0f), new Vector3(1f, 1.5f, 97f));
            CreateSolidCube("Barrier_OuterRight", barriers.transform, new Vector3(23f, 0.75f, 0f), new Vector3(1f, 1.5f, 97f));
            CreateSolidCube("Barrier_OuterTop", barriers.transform, new Vector3(0f, 0.75f, 48f), new Vector3(47f, 1.5f, 1f));
            CreateSolidCube("Barrier_OuterBottom", barriers.transform, new Vector3(0f, 0.75f, -48f), new Vector3(47f, 1.5f, 1f));
            CreateSolidCube("InnerIsland", barriers.transform, new Vector3(0f, 0.5f, 0f), new Vector3(16f, 1f, 64f));

            GameObject obstacles = CreateRoot("Obstacles", environment.transform);
            CreateSolidCube("TestObstacle_01", obstacles.transform, new Vector3(-19f, 0.6f, 4f), new Vector3(1.5f, 1.2f, 2f));
            CreateSolidCube("TestObstacle_02", obstacles.transform, new Vector3(19f, 0.6f, 12f), new Vector3(1.5f, 1.2f, 2f));
            CreateSolidCube("TestObstacle_03", obstacles.transform, new Vector3(1f, 0.6f, 44f), new Vector3(2f, 1.2f, 1.5f));
            return environment;
        }

        private static GameObject BuildBuggy(
            Transform playerRoot,
            out Rigidbody body,
            out CarController carController)
        {
            GameObject buggy = new GameObject("Buggy_Player");
            buggy.transform.SetParent(playerRoot, false);
            buggy.transform.position = new Vector3(-15f, 0.7f, -32f);
            buggy.transform.rotation = Quaternion.identity;

            BoxCollider collider = buggy.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(1.9f, 1f, 3.2f);
            collider.enabled = true;
            collider.isTrigger = false;

            body = buggy.AddComponent<Rigidbody>();
            body.mass = 650f;
            body.linearDamping = 0.2f;
            body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.useGravity = true;
            body.isKinematic = false;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            KeyboardInputProvider input = buggy.AddComponent<KeyboardInputProvider>();
            carController = buggy.AddComponent<CarController>();
            carController.Configure(body, input);

            CarResetController resetController = buggy.AddComponent<CarResetController>();
            resetController.Configure(body);

            CreateVisualCube("Body_Visual", buggy.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.8f, 0.6f, 3f));
            CreateVisualCube("Cabin_Visual", buggy.transform, new Vector3(0f, 0.7f, -0.25f), new Vector3(1.3f, 0.45f, 1.45f));
            CreateWheel("Wheel_FrontLeft", buggy.transform, new Vector3(-1.05f, 0f, 1.05f));
            CreateWheel("Wheel_FrontRight", buggy.transform, new Vector3(1.05f, 0f, 1.05f));
            CreateWheel("Wheel_RearLeft", buggy.transform, new Vector3(-1.05f, 0f, -1.05f));
            CreateWheel("Wheel_RearRight", buggy.transform, new Vector3(1.05f, 0f, -1.05f));
            return buggy;
        }

        private static GameObject BuildRace(
            Rigidbody playerBody,
            CarController carController,
            out RaceManager raceManager,
            out Checkpoint[] checkpoints)
        {
            GameObject raceRoot = new GameObject("Race");
            GameObject managerObject = CreateRoot("RaceManager", raceRoot.transform);
            LapTracker lapTracker = managerObject.AddComponent<LapTracker>();
            CheckpointManager checkpointManager = managerObject.AddComponent<CheckpointManager>();
            raceManager = managerObject.AddComponent<RaceManager>();

            checkpoints = new[]
            {
                CreateCheckpoint("StartFinish", raceRoot.transform, new Vector3(-15f, 1.5f, -25f), 0f),
                CreateCheckpoint("CP01", raceRoot.transform, new Vector3(-15f, 1.5f, 8f), 0f),
                CreateCheckpoint("CP02", raceRoot.transform, new Vector3(-6f, 1.5f, 40f), 90f),
                CreateCheckpoint("CP03", raceRoot.transform, new Vector3(15f, 1.5f, 20f), 0f),
                CreateCheckpoint("CP04", raceRoot.transform, new Vector3(15f, 1.5f, -16f), 0f),
                CreateCheckpoint("CP05", raceRoot.transform, new Vector3(6f, 1.5f, -40f), 90f)
            };

            checkpointManager.Configure(playerBody, lapTracker, raceManager, checkpoints);
            raceManager.Configure(carController, checkpointManager, lapTracker, TotalLaps);
            return raceRoot;
        }

        private static Checkpoint CreateCheckpoint(string name, Transform parent, Vector3 position, float yaw)
        {
            GameObject checkpointObject = CreateRoot(name, parent);
            checkpointObject.transform.position = position;
            checkpointObject.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            BoxCollider trigger = checkpointObject.AddComponent<BoxCollider>();
            trigger.size = new Vector3(12f, 3f, 1f);
            trigger.center = Vector3.zero;
            trigger.isTrigger = true;
            trigger.enabled = true;

            GameObject marker = CreateVisualCube(
                "Marker",
                checkpointObject.transform,
                new Vector3(0f, -1.45f, 0f),
                new Vector3(12f, 0.05f, 1f));
            marker.transform.localRotation = Quaternion.identity;
            return checkpointObject.AddComponent<Checkpoint>();
        }

        private static GameObject BuildHud(RaceManager raceManager, out RaceHUD raceHud)
        {
            GameObject uiRoot = new GameObject("UI");
            GameObject hudObject = CreateRoot("RaceHUD", uiRoot.transform);
            Canvas canvas = hudObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = hudObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            hudObject.AddComponent<GraphicRaycaster>();
            raceHud = hudObject.AddComponent<RaceHUD>();

            Text position = CreateHudText(hudObject.transform, "Position", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(25f, -20f), new Vector2(220f, 90f), 28, TextAnchor.UpperLeft);
            Text lap = CreateHudText(hudObject.transform, "Lap", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(260f, -20f), new Vector2(220f, 90f), 28, TextAnchor.UpperLeft);
            Text time = CreateHudText(hudObject.transform, "Time", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(360f, 90f), 28, TextAnchor.UpperCenter);
            Text countdown = CreateHudText(hudObject.transform, "Countdown", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(500f, 180f), 72, TextAnchor.MiddleCenter);
            Text status = CreateHudText(hudObject.transform, "RaceStatus", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-25f, -20f), new Vector2(300f, 90f), 28, TextAnchor.UpperRight);
            raceHud.Configure(raceManager, position, lap, time, countdown, status);
            return uiRoot;
        }

        private static Text CreateHudText(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            TextAnchor alignment)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)textObject.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            Text text = textObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject BuildCamera(Transform target)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            UnityEngine.Camera camera = cameraObject.AddComponent<UnityEngine.Camera>();
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 500f;
            cameraObject.AddComponent<AudioListener>();
            ChaseCameraController chase = cameraObject.AddComponent<ChaseCameraController>();
            chase.SetTarget(target);
            chase.SnapToTarget();
            return cameraObject;
        }

        private static GameObject BuildLighting()
        {
            GameObject lighting = new GameObject("Lighting");
            GameObject lightObject = CreateRoot("Directional Light", lighting.transform);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;

            GameObject volumeObject = CreateRoot("Global Volume", lighting.transform);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/SampleSceneProfile.asset");
            return lighting;
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.SetActive(true);
            return root;
        }

        private static GameObject CreateSolidCube(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localRotation = Quaternion.identity;
            cube.transform.localScale = scale;
            cube.SetActive(true);
            BoxCollider collider = cube.GetComponent<BoxCollider>();
            collider.enabled = true;
            collider.isTrigger = false;
            cube.GetComponent<Renderer>().enabled = true;
            return cube;
        }

        private static GameObject CreateVisualCube(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            GameObject visual = CreateSolidCube(name, parent, position, scale);
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            return visual;
        }

        private static void CreateWheel(string name, Transform parent, Vector3 position)
        {
            GameObject wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent, false);
            wheel.transform.localPosition = position;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.9f, 0.35f, 0.9f);
            Object.DestroyImmediate(wheel.GetComponent<Collider>());
        }

        private static void ValidateGeneratedScene(
            GameObject environment,
            GameObject playerRoot,
            GameObject buggy,
            GameObject raceRoot,
            RaceManager raceManager,
            Checkpoint[] checkpoints,
            GameObject uiRoot,
            RaceHUD raceHud,
            GameObject cameraObject,
            GameObject lighting)
        {
            RequireActive(environment, "Environment");
            RequireActive(playerRoot, "Player");
            RequireActive(buggy, "Buggy_Player");
            RequireActive(raceRoot, "Race");
            RequireActive(uiRoot, "UI");
            RequireActive(cameraObject, "Main Camera");
            RequireActive(lighting, "Lighting");

            Transform ground = environment.transform.Find("Ground");
            BoxCollider groundCollider = ground != null ? ground.GetComponent<BoxCollider>() : null;
            if (groundCollider == null || !groundCollider.enabled || groundCollider.isTrigger)
            {
                throw new InvalidOperationException("Milestone 2 Ground requires an enabled, non-trigger BoxCollider.");
            }

            Rigidbody body = buggy.GetComponent<Rigidbody>();
            CarController controller = buggy.GetComponent<CarController>();
            KeyboardInputProvider input = buggy.GetComponent<KeyboardInputProvider>();
            if (body == null || controller == null || input == null || controller.TargetBody != body || controller.InputProviderSource != input)
            {
                throw new InvalidOperationException("Milestone 2 player input, controller, and Rigidbody wiring is incomplete.");
            }

            if (!raceManager.IsConfigured || checkpoints.Length < 2 || !raceHud.IsConfigured)
            {
                throw new InvalidOperationException("Milestone 2 race manager, checkpoints, or HUD is not fully configured.");
            }

            for (int index = 0; index < checkpoints.Length; index++)
            {
                BoxCollider trigger = checkpoints[index].GetComponent<BoxCollider>();
                if (checkpoints[index].CheckpointIndex != index || trigger == null || !trigger.enabled || !trigger.isTrigger)
                {
                    throw new InvalidOperationException($"Checkpoint {index} is missing its ordered trigger configuration.");
                }
            }
        }

        private static void RequireActive(GameObject gameObject, string expectedName)
        {
            if (gameObject == null || gameObject.name != expectedName || !gameObject.activeInHierarchy)
            {
                throw new InvalidOperationException($"Required active root '{expectedName}' was not generated.");
            }
        }
    }
}
