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
    public static class PoseKartMilestone3Setup
    {
        private const string ScenePath = "Assets/Scenes/Milestones_Archive/Milestone3_TrackGraybox.unity";
        private const int TotalLaps = 3;

        // Clockwise center line derived from the authoritative exact-track schematic.
        private static readonly Vector3[] CenterLine =
        {
            new Vector3(-65f, 0f, -42f),
            new Vector3(-75f, 0f, -32f),
            new Vector3(-82f, 0f, -15f),
            new Vector3(-82f, 0f, 5f),
            new Vector3(-76f, 0f, 18f),
            new Vector3(-65f, 0f, 28f),
            new Vector3(-55f, 0f, 42f),
            new Vector3(-40f, 0f, 50f),
            new Vector3(-20f, 0f, 48f),
            new Vector3(-5f, 0f, 42f),
            new Vector3(10f, 0f, 52f),
            new Vector3(30f, 0f, 55f),
            new Vector3(45f, 0f, 48f),
            new Vector3(55f, 0f, 35f),
            new Vector3(65f, 0f, 20f),
            new Vector3(74f, 0f, 2f),
            new Vector3(72f, 0f, -15f),
            new Vector3(64f, 0f, -30f),
            new Vector3(50f, 0f, -39f),
            new Vector3(32f, 0f, -43f),
            new Vector3(10f, 0f, -42f),
            new Vector3(-15f, 0f, -38f),
            new Vector3(-35f, 0f, -42f),
            new Vector3(-53f, 0f, -48f)
        };

        private static readonly float[] SegmentWidths =
        {
            16f, 14f, 10f, 8f, 8f, 9f,
            14f, 14f, 14f,
            11f, 10f, 10f,
            10f, 10f,
            10f, 10f, 10f, 10f, 11f, 12f,
            14f, 16f, 16f, 16f
        };

        private static readonly int[] CheckpointNodeIndices =
        {
            0, 2, 4, 5, 7, 9, 11, 12, 14, 15, 17, 20, 22
        };

        [MenuItem("PoseKart/Setup/Milestone 3 Track Graybox")]
        public static void SetupMilestone3TrackGraybox()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation before rebuilding the Milestone 3 graybox.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject environment = BuildEnvironment(out Transform roadRoot, out Transform zoneRoot);
            GameObject playerRoot = new GameObject("Player");
            GameObject buggy = BuildBuggy(
                playerRoot.transform,
                out Rigidbody playerBody,
                out CarController carController,
                out CarResetController resetController);
            VehicleResetZone resetZone = BuildResetZones(environment.transform, playerBody, resetController);
            GameObject raceRoot = BuildRace(playerBody, carController, out RaceManager raceManager, out Checkpoint[] checkpoints);
            GameObject uiRoot = BuildHud(raceManager, out RaceHUD raceHud);
            GameObject cameraObject = BuildCamera(buggy.transform);
            GameObject lighting = BuildLighting();

            ValidateGeneratedScene(
                environment,
                roadRoot,
                zoneRoot,
                playerRoot,
                buggy,
                resetZone,
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
                Debug.LogError($"Could not save Milestone 3 graybox scene to {ScenePath}.");
                return;
            }

            Selection.activeGameObject = raceManager.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 3 track graybox created at {ScenePath}.", raceManager);
        }

        [MenuItem("PoseKart/Setup/Milestone 3 Track Graybox", true)]
        private static bool ValidateSetupMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static GameObject BuildEnvironment(out Transform roadRoot, out Transform zoneRoot)
        {
            GameObject environment = new GameObject("Environment");
            GameObject ocean = CreatePrimitive(
                "Ocean_Placeholder",
                PrimitiveType.Cube,
                environment.transform,
                new Vector3(0f, -3f, 0f),
                new Vector3(190f, 0.2f, 140f),
                false);
            ocean.GetComponent<Renderer>().enabled = true;

            roadRoot = CreateRoot("Road", environment.transform).transform;
            Transform barriers = CreateRoot("Barriers", environment.transform).transform;
            BuildContinuousRoad(roadRoot, barriers);

            zoneRoot = CreateRoot("Zones", environment.transform).transform;
            BuildHarbor(CreateRoot("Harbor", zoneRoot).transform);
            BuildVillage(CreateRoot("Coastal_Village", zoneRoot).transform);
            BuildBeach(CreateRoot("Beach_Dirt", zoneRoot).transform);
            BuildCanyon(CreateRoot("Rocky_Canyon", zoneRoot).transform);
            BuildCave(CreateRoot("Cave", zoneRoot).transform);
            BuildCliff(CreateRoot("Cliff_Road", zoneRoot).transform);

            BuildPlaceholders(CreateRoot("Placeholders", environment.transform).transform);
            return environment;
        }

        private static void BuildContinuousRoad(Transform roadRoot, Transform barrierRoot)
        {
            for (int index = 0; index < CenterLine.Length; index++)
            {
                int nextIndex = (index + 1) % CenterLine.Length;
                Vector3 start = CenterLine[index];
                Vector3 end = CenterLine[nextIndex];
                Vector3 direction = (end - start).normalized;
                Vector3 midpoint = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);
                float width = SegmentWidths[index];

                GameObject segment = CreatePrimitive(
                    $"Road_{index:00}",
                    PrimitiveType.Cube,
                    roadRoot,
                    midpoint + Vector3.up * 0.05f,
                    new Vector3(width, 0.1f, length + 0.75f),
                    true);
                segment.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                float previousWidth = SegmentWidths[(index - 1 + SegmentWidths.Length) % SegmentWidths.Length];
                float jointSize = Mathf.Max(width, previousWidth);
                CreatePrimitive(
                    $"RoadJoint_{index:00}",
                    PrimitiveType.Cube,
                    roadRoot,
                    start + Vector3.up * 0.045f,
                    new Vector3(jointSize, 0.09f, jointSize),
                    true);

                Vector3 right = Vector3.Cross(Vector3.up, direction);
                float barrierOffset = width * 0.5f + 0.35f;
                CreateBarrier($"Barrier_{index:00}_Left", barrierRoot, midpoint - right * barrierOffset, direction, length);
                CreateBarrier($"Barrier_{index:00}_Right", barrierRoot, midpoint + right * barrierOffset, direction, length);
            }
        }

        private static void CreateBarrier(string name, Transform parent, Vector3 position, Vector3 direction, float length)
        {
            GameObject barrier = CreatePrimitive(
                name,
                PrimitiveType.Cube,
                parent,
                position + Vector3.up * 0.55f,
                new Vector3(0.7f, 1.1f, length + 1f),
                true);
            barrier.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private static void BuildHarbor(Transform parent)
        {
            CreatePrimitive("Pier_Main", PrimitiveType.Cube, parent, new Vector3(-62f, -0.05f, -55f), new Vector3(28f, 0.5f, 8f), true);
            CreatePrimitive("Dock_Block_01", PrimitiveType.Cube, parent, new Vector3(-78f, 1f, -52f), new Vector3(8f, 2f, 7f), true);
            CreatePrimitive("Dock_Block_02", PrimitiveType.Cube, parent, new Vector3(-48f, 1f, -55f), new Vector3(7f, 2f, 6f), true);
            CreatePrimitive("Crate_Block_01", PrimitiveType.Cube, parent, new Vector3(-73f, 0.75f, -43f), new Vector3(1.5f, 1.5f, 1.5f), true);
            CreatePrimitive("Crate_Block_02", PrimitiveType.Cube, parent, new Vector3(-70f, 0.6f, -45f), new Vector3(1.2f, 1.2f, 1.2f), true);
            CreatePrimitive("Lighthouse_Base", PrimitiveType.Cylinder, parent, new Vector3(-88f, 3f, -54f), new Vector3(5f, 3f, 5f), true);
            CreatePrimitive("Lighthouse_Top", PrimitiveType.Cylinder, parent, new Vector3(-88f, 7f, -54f), new Vector3(3f, 1f, 3f), false);
        }

        private static void BuildVillage(Transform parent)
        {
            CreateHouse("House_01", parent, new Vector3(-94f, 2f, -4f), new Vector3(8f, 4f, 8f));
            CreateHouse("House_02", parent, new Vector3(-94f, 2.5f, 10f), new Vector3(9f, 5f, 7f));
            CreateHouse("House_03", parent, new Vector3(-83f, 2f, 30f), new Vector3(8f, 4f, 8f));
            CreateHouse("House_04", parent, new Vector3(-67f, 2f, 42f), new Vector3(9f, 4f, 7f));
            CreateHouse("House_05", parent, new Vector3(-66f, 2.5f, 17f), new Vector3(7f, 5f, 7f));
            CreatePrimitive("Market_Block", PrimitiveType.Cube, parent, new Vector3(-73f, 1f, 7f), new Vector3(4f, 2f, 4f), true);
        }

        private static void CreateHouse(string name, Transform parent, Vector3 position, Vector3 scale)
        {
            CreatePrimitive(name, PrimitiveType.Cube, parent, position, scale, true);
            CreatePrimitive($"{name}_Roof", PrimitiveType.Cube, parent, position + Vector3.up * (scale.y * 0.6f), new Vector3(scale.x + 1f, 0.8f, scale.z + 1f), false);
        }

        private static void BuildBeach(Transform parent)
        {
            CreatePrimitive("Sand_OpenArea", PrimitiveType.Cube, parent, new Vector3(-30f, -0.35f, 53f), new Vector3(58f, 0.2f, 25f), false);
            CreatePrimitive("Beach_Rock_01", PrimitiveType.Cube, parent, new Vector3(-50f, 1.25f, 59f), new Vector3(4f, 2.5f, 5f), true);
            CreatePrimitive("Beach_Rock_02", PrimitiveType.Cube, parent, new Vector3(-7f, 1f, 57f), new Vector3(3f, 2f, 4f), true);
            CreatePrimitive("Beach_Marker_01", PrimitiveType.Cylinder, parent, new Vector3(-35f, 1.5f, 62f), new Vector3(1f, 1.5f, 1f), false);
            CreatePrimitive("Beach_Marker_02", PrimitiveType.Cylinder, parent, new Vector3(-18f, 1.5f, 60f), new Vector3(1f, 1.5f, 1f), false);
        }

        private static void BuildCanyon(Transform parent)
        {
            Vector3[] rocks =
            {
                new Vector3(4f, 3f, 63f), new Vector3(14f, 4f, 65f),
                new Vector3(27f, 3.5f, 66f), new Vector3(39f, 4f, 62f),
                new Vector3(3f, 3f, 38f), new Vector3(18f, 4f, 42f),
                new Vector3(35f, 3f, 43f)
            };
            for (int index = 0; index < rocks.Length; index++)
            {
                CreatePrimitive($"RockWall_{index:00}", PrimitiveType.Cube, parent, rocks[index], new Vector3(7f, rocks[index].y * 2f, 6f), true);
            }
        }

        private static void BuildCave(Transform parent)
        {
            Vector3 caveDirection = (CenterLine[14] - CenterLine[12]).normalized;
            Quaternion caveRotation = Quaternion.LookRotation(caveDirection, Vector3.up);
            Vector3 caveCenter = (CenterLine[12] + CenterLine[14]) * 0.5f;

            GameObject leftWall = CreatePrimitive("Cave_LeftWall", PrimitiveType.Cube, parent, caveCenter - Vector3.Cross(Vector3.up, caveDirection) * 7f + Vector3.up * 3f, new Vector3(3f, 6f, 34f), true);
            GameObject rightWall = CreatePrimitive("Cave_RightWall", PrimitiveType.Cube, parent, caveCenter + Vector3.Cross(Vector3.up, caveDirection) * 7f + Vector3.up * 3f, new Vector3(3f, 6f, 34f), true);
            GameObject roof = CreatePrimitive("Cave_Roof", PrimitiveType.Cube, parent, caveCenter + Vector3.up * 8f, new Vector3(17f, 2f, 34f), true);
            leftWall.transform.rotation = caveRotation;
            rightWall.transform.rotation = caveRotation;
            roof.transform.rotation = caveRotation;

            CreatePointLight("Cave_Light_Entry", parent, Vector3.Lerp(CenterLine[12], CenterLine[14], 0.25f) + Vector3.up * 3f);
            CreatePointLight("Cave_Light_Exit", parent, Vector3.Lerp(CenterLine[12], CenterLine[14], 0.75f) + Vector3.up * 3f);
        }

        private static void CreatePointLight(string name, Transform parent, Vector3 position)
        {
            GameObject lightObject = CreateRoot(name, parent);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 18f;
            light.intensity = 2f;
            light.color = new Color(1f, 0.55f, 0.25f);
        }

        private static void BuildCliff(Transform parent)
        {
            Vector3[] cliffBlocks =
            {
                new Vector3(84f, 2.5f, 16f), new Vector3(88f, 3f, 1f),
                new Vector3(84f, 3.5f, -17f), new Vector3(76f, 3f, -35f),
                new Vector3(55f, 2.5f, -49f), new Vector3(31f, 2f, -53f)
            };
            for (int index = 0; index < cliffBlocks.Length; index++)
            {
                CreatePrimitive($"Cliff_Block_{index:00}", PrimitiveType.Cube, parent, cliffBlocks[index], new Vector3(8f, cliffBlocks[index].y * 2f, 9f), true);
            }
        }

        private static void BuildPlaceholders(Transform parent)
        {
            CreateTrackMarker("BoostPad_A", parent, 8, new Vector3(5f, 0.12f, 2.5f), true);
            CreateTrackMarker("BoostPad_B", parent, 15, new Vector3(5f, 0.12f, 2.5f), true);
            CreateTrackMarker("BoostPad_C", parent, 19, new Vector3(5f, 0.12f, 2.5f), true);

            GameObject ramp = CreateTrackMarker("SmallJumpRamp", parent, 18, new Vector3(6f, 0.8f, 4f), true);
            ramp.transform.rotation *= Quaternion.Euler(8f, 0f, 0f);
            ramp.SetActive(false);

            GameObject shortcutEntrance = CreatePrimitive("Shortcut_Entrance", PrimitiveType.Cube, parent, new Vector3(-72f, 1f, 24f), new Vector3(2f, 2f, 2f), false);
            GameObject shortcutExit = CreatePrimitive("Shortcut_Exit", PrimitiveType.Cube, parent, new Vector3(-43f, 1f, 44f), new Vector3(2f, 2f, 2f), false);
            shortcutEntrance.SetActive(false);
            shortcutExit.SetActive(false);
        }

        private static GameObject CreateTrackMarker(string name, Transform parent, int segmentIndex, Vector3 scale, bool active)
        {
            int nextIndex = (segmentIndex + 1) % CenterLine.Length;
            Vector3 direction = (CenterLine[nextIndex] - CenterLine[segmentIndex]).normalized;
            GameObject marker = CreatePrimitive(
                name,
                PrimitiveType.Cube,
                parent,
                Vector3.Lerp(CenterLine[segmentIndex], CenterLine[nextIndex], 0.5f) + Vector3.up * 0.13f,
                scale,
                false);
            marker.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            marker.SetActive(active);
            return marker;
        }

        private static GameObject BuildBuggy(
            Transform playerRoot,
            out Rigidbody body,
            out CarController carController,
            out CarResetController resetController)
        {
            Vector3 startDirection = (CenterLine[1] - CenterLine[0]).normalized;
            GameObject buggy = new GameObject("Buggy_Player");
            buggy.transform.SetParent(playerRoot, false);
            buggy.transform.position = CenterLine[0] - startDirection * 5f + Vector3.up * 0.7f;
            buggy.transform.rotation = Quaternion.LookRotation(startDirection, Vector3.up);

            BoxCollider collider = buggy.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(1.9f, 1f, 3.2f);
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
            resetController = buggy.AddComponent<CarResetController>();
            resetController.Configure(body);

            CreatePrimitive("Body_Visual", PrimitiveType.Cube, buggy.transform, new Vector3(0f, 0.2f, 0f), new Vector3(1.8f, 0.6f, 3f), false);
            CreatePrimitive("Cabin_Visual", PrimitiveType.Cube, buggy.transform, new Vector3(0f, 0.7f, -0.25f), new Vector3(1.3f, 0.45f, 1.45f), false);
            CreateWheel("Wheel_FrontLeft", buggy.transform, new Vector3(-1.05f, 0f, 1.05f));
            CreateWheel("Wheel_FrontRight", buggy.transform, new Vector3(1.05f, 0f, 1.05f));
            CreateWheel("Wheel_RearLeft", buggy.transform, new Vector3(-1.05f, 0f, -1.05f));
            CreateWheel("Wheel_RearRight", buggy.transform, new Vector3(1.05f, 0f, -1.05f));
            return buggy;
        }

        private static VehicleResetZone BuildResetZones(Transform environment, Rigidbody body, CarResetController resetController)
        {
            GameObject root = CreateRoot("ResetZones", environment);
            GameObject resetObject = CreateRoot("OffTrack_Reset_Global", root.transform);
            resetObject.transform.position = new Vector3(0f, -6f, 0f);
            BoxCollider trigger = resetObject.AddComponent<BoxCollider>();
            trigger.size = new Vector3(210f, 4f, 160f);
            trigger.isTrigger = true;
            VehicleResetZone resetZone = resetObject.AddComponent<VehicleResetZone>();
            resetZone.Configure(body, resetController);
            return resetZone;
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

            checkpoints = new Checkpoint[CheckpointNodeIndices.Length];
            for (int checkpointIndex = 0; checkpointIndex < checkpoints.Length; checkpointIndex++)
            {
                int nodeIndex = CheckpointNodeIndices[checkpointIndex];
                string name = checkpointIndex == 0 ? "StartFinish" : $"CP{checkpointIndex:00}";
                checkpoints[checkpointIndex] = CreateCheckpoint(name, raceRoot.transform, nodeIndex, SegmentWidths[nodeIndex]);
            }

            checkpointManager.Configure(playerBody, lapTracker, raceManager, checkpoints);
            raceManager.Configure(carController, checkpointManager, lapTracker, TotalLaps);
            return raceRoot;
        }

        private static Checkpoint CreateCheckpoint(string name, Transform parent, int nodeIndex, float roadWidth)
        {
            int nextIndex = (nodeIndex + 1) % CenterLine.Length;
            Vector3 direction = (CenterLine[nextIndex] - CenterLine[nodeIndex]).normalized;
            GameObject checkpointObject = CreateRoot(name, parent);
            checkpointObject.transform.position = CenterLine[nodeIndex] + Vector3.up * 1.5f;
            checkpointObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

            BoxCollider trigger = checkpointObject.AddComponent<BoxCollider>();
            trigger.size = new Vector3(roadWidth + 1f, 3f, 1.2f);
            trigger.isTrigger = true;

            GameObject marker = CreatePrimitive(
                "Marker",
                PrimitiveType.Cube,
                checkpointObject.transform,
                new Vector3(0f, -1.43f, 0f),
                new Vector3(roadWidth + 0.5f, 0.08f, 1.2f),
                false);
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

        private static GameObject CreatePrimitive(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            bool solid)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = position;
            primitive.transform.localRotation = Quaternion.identity;
            primitive.transform.localScale = scale;
            primitive.SetActive(true);
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = true;
            }

            Collider collider = primitive.GetComponent<Collider>();
            if (solid)
            {
                if (collider != null)
                {
                    collider.enabled = true;
                    collider.isTrigger = false;
                }
            }
            else if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return primitive;
        }

        private static void CreateWheel(string name, Transform parent, Vector3 position)
        {
            GameObject wheel = CreatePrimitive(name, PrimitiveType.Cylinder, parent, position, new Vector3(0.9f, 0.35f, 0.9f), false);
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static void ValidateGeneratedScene(
            GameObject environment,
            Transform roadRoot,
            Transform zoneRoot,
            GameObject playerRoot,
            GameObject buggy,
            VehicleResetZone resetZone,
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

            if (roadRoot == null || roadRoot.childCount < CenterLine.Length * 2 || zoneRoot == null || zoneRoot.childCount != 6)
            {
                throw new InvalidOperationException("Milestone 3 road or zone hierarchy is incomplete.");
            }

            Rigidbody body = buggy.GetComponent<Rigidbody>();
            CarController controller = buggy.GetComponent<CarController>();
            KeyboardInputProvider input = buggy.GetComponent<KeyboardInputProvider>();
            if (body == null || controller == null || input == null || controller.TargetBody != body || controller.InputProviderSource != input)
            {
                throw new InvalidOperationException("Milestone 3 player wiring is incomplete.");
            }

            if (resetZone == null || !resetZone.IsConfigured)
            {
                throw new InvalidOperationException("Milestone 3 off-track reset zone is not configured.");
            }

            if (!raceManager.IsConfigured || checkpoints.Length != 13 || !raceHud.IsConfigured)
            {
                throw new InvalidOperationException("Milestone 3 race, checkpoint, or HUD configuration is incomplete.");
            }

            for (int index = 0; index < checkpoints.Length; index++)
            {
                BoxCollider trigger = checkpoints[index].GetComponent<BoxCollider>();
                if (checkpoints[index].CheckpointIndex != index || trigger == null || !trigger.enabled || !trigger.isTrigger)
                {
                    throw new InvalidOperationException($"Milestone 3 checkpoint {index} is not an ordered trigger.");
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
