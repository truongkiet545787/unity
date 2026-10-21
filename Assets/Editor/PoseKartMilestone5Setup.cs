using System;
using System.Linq;
using PoseKart.AI;
using PoseKart.Camera;
using PoseKart.Input;
using PoseKart.Race;
using PoseKart.UI;
using PoseKart.Vehicle;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    public static class PoseKartMilestone5Setup
    {
        private const string SourceScenePath = "Assets/Scenes/Milestones_Archive/Milestone4_AIRace.unity";
        private const string ScenePath = "Assets/Scenes/Milestones_Archive/Milestone5_ArcadeRace.unity";
        private const int TotalLaps = 3;

        // A substantially larger loop than ML4. The long southern straight also provides
        // enough room for a staggered four-kart grid before the start/finish checkpoint.
        private static readonly Vector3[] CenterLine =
        {
            new Vector3(-55f, 0f, -65f),
            new Vector3(-20f, 0f, -65f),
            new Vector3(20f, 0f, -65f),
            new Vector3(60f, 0f, -65f),
            new Vector3(95f, 0f, -62f),
            new Vector3(120f, 0f, -52f),
            new Vector3(136f, 0f, -35f),
            new Vector3(143f, 0f, -12f),
            new Vector3(141f, 0f, 14f),
            new Vector3(132f, 0f, 36f),
            new Vector3(115f, 0f, 53f),
            new Vector3(90f, 0f, 64f),
            new Vector3(62f, 0f, 68f),
            new Vector3(38f, 0f, 62f),
            new Vector3(12f, 0f, 72f),
            new Vector3(-18f, 0f, 78f),
            new Vector3(-50f, 0f, 75f),
            new Vector3(-80f, 0f, 65f),
            new Vector3(-105f, 0f, 50f),
            new Vector3(-126f, 0f, 31f),
            new Vector3(-139f, 0f, 8f),
            new Vector3(-140f, 0f, -16f),
            new Vector3(-132f, 0f, -37f),
            new Vector3(-118f, 0f, -53f),
            new Vector3(-92f, 0f, -63f)
        };

        private static readonly float[] SegmentWidths =
        {
            28f, 28f, 28f, 28f, 26f,
            24f, 24f, 24f, 24f, 24f,
            24f, 22f, 22f, 22f, 24f,
            24f, 24f, 24f, 24f, 24f,
            24f, 24f, 24f, 26f, 28f
        };

        private static readonly int[] CheckpointNodeIndices =
        {
            0, 2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24
        };

        [MenuItem("PoseKart/Setup/Milestone 5 Arcade Race")]
        public static void SetupMilestone5ArcadeRace()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation before rebuilding the Milestone 5 arcade race.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                Debug.LogError($"Validated Milestone 4 source scene is missing at {SourceScenePath}.");
                return;
            }

            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !AssetDatabase.DeleteAsset(ScenePath))
            {
                Debug.LogError($"Could not replace the existing Milestone 5 scene at {ScenePath}.");
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                Debug.LogError($"Could not copy the Milestone 4 race to {ScenePath}.");
                return;
            }

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildArcadeRace(scene);
        }

        [MenuItem("PoseKart/Setup/Milestone 5 Arcade Race", true)]
        private static bool ValidateSetupMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static void BuildArcadeRace(Scene scene)
        {
            GameObject environment = GameObject.Find("Environment");
            GameObject roadRoot = GameObject.Find("Environment/Road");
            GameObject barrierRoot = GameObject.Find("Environment/Barriers");
            GameObject raceRoot = GameObject.Find("Race");
            GameObject playerObject = GameObject.Find("Player/Buggy_Player");
            if (environment == null || roadRoot == null || barrierRoot == null || raceRoot == null || playerObject == null)
            {
                throw new InvalidOperationException("Milestone 4 does not contain the required track, race, or player hierarchy.");
            }

            RaceManager raceManager = raceRoot.GetComponentInChildren<RaceManager>(true);
            CheckpointManager checkpointManager = raceRoot.GetComponentInChildren<CheckpointManager>(true);
            RacePositionCalculator positionCalculator = raceRoot.GetComponentInChildren<RacePositionCalculator>(true);
            CarController playerController = playerObject.GetComponent<CarController>();
            KeyboardInputProvider playerInput = playerObject.GetComponent<KeyboardInputProvider>();
            Rigidbody playerBody = playerObject.GetComponent<Rigidbody>();
            RaceParticipant player = playerObject.GetComponent<RaceParticipant>();
            LapTracker playerLapTracker = player != null ? player.LapTracker : null;
            if (raceManager == null || checkpointManager == null || positionCalculator == null ||
                playerController == null || playerInput == null || playerBody == null ||
                player == null || playerLapTracker == null)
            {
                throw new InvalidOperationException("Milestone 4 multi-racer wiring is incomplete.");
            }

            RebuildTrack(environment, roadRoot.transform, barrierRoot.transform);
            ClearOldRaceLayout(raceRoot.transform);

            Checkpoint[] checkpoints = BuildCheckpoints(raceRoot.transform);
            RacingLine racingLine = BuildRacingLine(raceRoot.transform);
            Transform racersRoot = CreateRoot("Racers", raceRoot.transform).transform;
            Vector3 forward = (CenterLine[1] - CenterLine[0]).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            PlaceRacer(playerObject, playerBody, CenterLine[0] - forward * 8f - right * 4.5f, forward);
            playerObject.SetActive(true);
            playerInput.enabled = true;
            playerBody.interpolation = RigidbodyInterpolation.Interpolate;
            playerBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            playerController.Configure(playerBody, playerInput);
            playerController.enabled = true;
            ApplyArcadeCarTuning(playerController);
            player.Configure("Player", true, playerBody, playerController, playerLapTracker);

            RaceParticipant[] racers = new RaceParticipant[4];
            racers[0] = player;
            racers[1] = BuildAIRacer(
                "AI Coral", racersRoot, racingLine,
                CenterLine[0] - forward * 12f + right * 4.5f,
                forward, 0.9f, 1.01f, 0.94f, 4.2f, 1.08f, -0.35f);
            racers[2] = BuildAIRacer(
                "AI Azure", racersRoot, racingLine,
                CenterLine[0] - forward * 24f - right * 4.5f,
                forward, 0.8f, 0.97f, 1.02f, 3.9f, 1.02f, 0.35f);
            racers[3] = BuildAIRacer(
                "AI Sunset", racersRoot, racingLine,
                CenterLine[0] - forward * 28f + right * 4.5f,
                forward, 0.7f, 0.93f, 1.09f, 3.6f, 0.98f, -0.2f);

            checkpointManager.ConfigureRacers(raceManager, checkpoints, racers);
            positionCalculator.Configure(checkpointManager, racers);
            raceManager.ConfigureRacers(checkpointManager, racers, positionCalculator, TotalLaps);
            TuneCamera();

            ValidateGeneratedScene(
                scene,
                roadRoot.transform,
                barrierRoot.transform,
                racingLine,
                raceManager,
                checkpointManager,
                positionCalculator,
                racers,
                checkpoints);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save Milestone 5 arcade race scene to {ScenePath}.");
                return;
            }

            Selection.activeGameObject = raceManager.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 5 arcade race created at {ScenePath}.", raceManager);
        }

        private static void RebuildTrack(GameObject environment, Transform roadRoot, Transform barrierRoot)
        {
            ClearChildren(roadRoot);
            ClearChildren(barrierRoot);

            for (int index = 0; index < CenterLine.Length; index++)
            {
                int nextIndex = (index + 1) % CenterLine.Length;
                Vector3 start = CenterLine[index];
                Vector3 end = CenterLine[nextIndex];
                Vector3 direction = (end - start).normalized;
                Vector3 midpoint = (start + end) * 0.5f;
                float length = Vector3.Distance(start, end);
                float width = SegmentWidths[index];

                // Extend each flat segment through both endpoints so the underlying BoxCollider
                // overlaps its neighbours instead of exposing a leading or trailing edge.
                GameObject segment = CreatePrimitive(
                    $"Road_{index:00}", PrimitiveType.Cube, roadRoot,
                    midpoint + Vector3.up * 0.05f,
                    new Vector3(width, 0.1f, length + 2f), true);
                segment.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                float previousWidth = SegmentWidths[(index - 1 + SegmentWidths.Length) % SegmentWidths.Length];
                float jointWidth = Mathf.Max(width, previousWidth);
                CreatePrimitive(
                    $"RoadJoint_{index:00}", PrimitiveType.Cube, roadRoot,
                    // Keep the joint top coplanar with Road_*; a recessed joint can behave
                    // like a small ramp when the kart crosses two colliders at once.
                    start + Vector3.up * 0.05f,
                    new Vector3(jointWidth, 0.1f, jointWidth), true);

                Vector3 right = Vector3.Cross(Vector3.up, direction);
                float barrierOffset = width * 0.5f + 0.45f;
                CreateBarrier($"Barrier_{index:00}_Left", barrierRoot, midpoint - right * barrierOffset, direction, length);
                CreateBarrier($"Barrier_{index:00}_Right", barrierRoot, midpoint + right * barrierOffset, direction, length);
            }

            GameObject zones = GameObject.Find("Environment/Zones");
            GameObject placeholders = GameObject.Find("Environment/Placeholders");
            if (zones != null)
            {
                zones.SetActive(false);
            }

            if (placeholders != null)
            {
                placeholders.SetActive(false);
            }

            GameObject ocean = GameObject.Find("Environment/Ocean_Placeholder");
            if (ocean != null)
            {
                ocean.transform.localScale = new Vector3(360f, 0.2f, 220f);
            }

            GameObject resetZone = GameObject.Find("Environment/ResetZones/OffTrack_Reset_Global");
            BoxCollider resetTrigger = resetZone != null ? resetZone.GetComponent<BoxCollider>() : null;
            if (resetTrigger != null)
            {
                resetZone.transform.position = new Vector3(0f, -7f, 0f);
                resetTrigger.size = new Vector3(400f, 6f, 260f);
                resetTrigger.isTrigger = true;
            }
        }

        private static void CreateBarrier(string name, Transform parent, Vector3 position, Vector3 direction, float length)
        {
            GameObject barrier = CreatePrimitive(
                name, PrimitiveType.Cube, parent,
                position + Vector3.up * 0.65f,
                new Vector3(0.9f, 1.3f, length + 1.75f), true);
            barrier.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        private static void ClearOldRaceLayout(Transform raceRoot)
        {
            RacingLine oldLine = raceRoot.GetComponentInChildren<RacingLine>(true);
            if (oldLine != null)
            {
                UnityEngine.Object.DestroyImmediate(oldLine.gameObject);
            }

            Transform oldRacers = raceRoot.Find("Racers");
            if (oldRacers != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRacers.gameObject);
            }

            Checkpoint[] oldCheckpoints = raceRoot.GetComponentsInChildren<Checkpoint>(true);
            foreach (Checkpoint checkpoint in oldCheckpoints)
            {
                UnityEngine.Object.DestroyImmediate(checkpoint.gameObject);
            }
        }

        private static Checkpoint[] BuildCheckpoints(Transform raceRoot)
        {
            Checkpoint[] checkpoints = new Checkpoint[CheckpointNodeIndices.Length];
            for (int checkpointIndex = 0; checkpointIndex < checkpoints.Length; checkpointIndex++)
            {
                int nodeIndex = CheckpointNodeIndices[checkpointIndex];
                int nextIndex = (nodeIndex + 1) % CenterLine.Length;
                Vector3 direction = (CenterLine[nextIndex] - CenterLine[nodeIndex]).normalized;
                string name = checkpointIndex == 0 ? "StartFinish" : $"CP{checkpointIndex:00}";
                GameObject checkpointObject = CreateRoot(name, raceRoot);
                checkpointObject.transform.position = CenterLine[nodeIndex] + Vector3.up * 1.6f;
                checkpointObject.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);

                BoxCollider trigger = checkpointObject.AddComponent<BoxCollider>();
                trigger.size = new Vector3(SegmentWidths[nodeIndex] + 2f, 3.2f, 2f);
                trigger.isTrigger = true;

                GameObject marker = CreatePrimitive(
                    "Marker", PrimitiveType.Cube, checkpointObject.transform,
                    new Vector3(0f, -1.53f, 0f),
                    new Vector3(SegmentWidths[nodeIndex] + 1f, 0.08f, 1.5f), false);
                marker.transform.localRotation = Quaternion.identity;
                checkpoints[checkpointIndex] = checkpointObject.AddComponent<Checkpoint>();
            }

            return checkpoints;
        }

        private static RacingLine BuildRacingLine(Transform raceRoot)
        {
            GameObject lineObject = CreateRoot("RacingLine", raceRoot);
            RacingLine racingLine = lineObject.AddComponent<RacingLine>();
            RacingWaypoint[] waypoints = new RacingWaypoint[CenterLine.Length];
            for (int index = 0; index < CenterLine.Length; index++)
            {
                GameObject waypointObject = CreateRoot($"Waypoint_{index:00}", lineObject.transform);
                waypointObject.transform.position = CenterLine[index] + Vector3.up * 0.2f;
                RacingWaypoint waypoint = waypointObject.AddComponent<RacingWaypoint>();
                waypoint.Configure(index, CalculateRecommendedSpeed(index));
                waypoints[index] = waypoint;
            }

            racingLine.Configure(waypoints);
            return racingLine;
        }

        private static float CalculateRecommendedSpeed(int index)
        {
            Vector3 previous = CenterLine[(index - 1 + CenterLine.Length) % CenterLine.Length];
            Vector3 current = CenterLine[index];
            Vector3 next = CenterLine[(index + 1) % CenterLine.Length];
            float severity = Mathf.Clamp01(Vector3.Angle(current - previous, next - current) / 85f);
            return Mathf.Lerp(1.03f, 0.7f, severity);
        }

        private static RaceParticipant BuildAIRacer(
            string racerName,
            Transform parent,
            RacingLine racingLine,
            Vector3 position,
            Vector3 forward,
            float skill,
            float speedMultiplier,
            float caution,
            float steeringResponse,
            float lookAheadMultiplier,
            float sideBias)
        {
            GameObject racerObject = new GameObject(racerName.Replace(' ', '_'));
            racerObject.transform.SetParent(parent, false);
            PlaceRacer(racerObject, null, position, forward);

            BoxCollider collider = racerObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.9f, 1f, 3.2f);

            Rigidbody body = racerObject.AddComponent<Rigidbody>();
            body.mass = 650f;
            body.linearDamping = 0.03f;
            body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            AIInputProvider input = racerObject.AddComponent<AIInputProvider>();
            CarController controller = racerObject.AddComponent<CarController>();
            controller.Configure(body, input);
            ApplyArcadeCarTuning(controller);

            LapTracker lapTracker = racerObject.AddComponent<LapTracker>();
            RaceParticipant participant = racerObject.AddComponent<RaceParticipant>();
            participant.Configure(racerName, false, body, controller, lapTracker);
            AIRacerBrain brain = racerObject.AddComponent<AIRacerBrain>();
            brain.Configure(
                racingLine, body, controller, input,
                skill, speedMultiplier, caution, steeringResponse, lookAheadMultiplier, sideBias);
            ApplyArcadeAITuning(brain);

            CreateBuggyVisuals(racerObject.transform);
            return participant;
        }

        private static void PlaceRacer(GameObject racerObject, Rigidbody body, Vector3 position, Vector3 forward)
        {
            racerObject.transform.position = position + Vector3.up * 0.7f;
            racerObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = racerObject.transform.position;
                body.rotation = racerObject.transform.rotation;
            }
        }

        private static void ApplyArcadeCarTuning(CarController controller)
        {
            SerializedObject serialized = new SerializedObject(controller);
            SetFloat(serialized, "maxForwardSpeed", 30f);
            SetFloat(serialized, "maxReverseSpeed", 9f);
            SetFloat(serialized, "acceleration", 8.5f);
            SetFloat(serialized, "reverseAcceleration", 11f);
            SetFloat(serialized, "brakeDeceleration", 26f);
            SetFloat(serialized, "coastingResistance", 0.45f);
            SetFloat(serialized, "linearDamping", 0.03f);
            SetFloat(serialized, "accelerationAtTopSpeed", 0.1f);
            SetFloat(serialized, "accelerationFalloffPower", 1.8f);
            SetFloat(serialized, "steeringDegreesPerSecond", 92f);
            SetFloat(serialized, "steeringAtMaxSpeedMultiplier", 0.5f);
            SetFloat(serialized, "cornerPenaltySteeringThreshold", 0.18f);
            SetFloat(serialized, "cornerPenaltyStartSpeed", 7f);
            SetFloat(serialized, "maximumCornerDeceleration", 18f);
            SetFloat(serialized, "cornerPenaltyPower", 1.65f);
            SetFloat(serialized, "lateralGrip", 7f);
            SetFloat(serialized, "maximumLateralSpeed", 5f);
            SetFloat(serialized, "speedDownforce", 0.42f);
            SetFloat(serialized, "groundProbeDistance", 0.9f);
            SetFloat(serialized, "maximumGroundedUpwardSpeed", 0.75f);
            SetFloat(serialized, "minimumImpactSpeed", 2f);
            SetFloat(serialized, "collisionSpeedLossMultiplier", 0.55f);
            SetFloat(serialized, "maximumCollisionSpeedLoss", 14f);
            SetFloat(serialized, "collisionLossCooldown", 0.25f);
            SetFloat(serialized, "sideImpactLossMultiplier", 0.12f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ApplyArcadeAITuning(AIRacerBrain brain)
        {
            SerializedObject serialized = new SerializedObject(brain);
            SetFloat(serialized, "baseLookAheadDistance", 9f);
            SetFloat(serialized, "speedLookAheadDistance", 0.5f);
            SetFloat(serialized, "waypointReachDistance", 5f);
            SetInt(serialized, "cornerPreviewSegments", 2);
            SetFloat(serialized, "minimumCornerSpeedFactor", 0.55f);
            SetFloat(serialized, "brakeSpeedMargin", 1.2f);
            SetFloat(serialized, "obstacleLookAhead", 9f);
            SetFloat(serialized, "obstacleCastRadius", 0.75f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void TuneCamera()
        {
            ChaseCameraController camera = UnityEngine.Object.FindFirstObjectByType<ChaseCameraController>();
            if (camera == null)
            {
                return;
            }

            SerializedObject serialized = new SerializedObject(camera);
            SetFloat(serialized, "distance", 8.5f);
            SetFloat(serialized, "height", 4.6f);
            SetFloat(serialized, "lookAhead", 3.5f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            camera.SnapToTarget();
        }

        private static void SetFloat(SerializedObject serialized, string propertyName, float value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized float property {propertyName}.");
            }

            property.floatValue = value;
        }

        private static void SetInt(SerializedObject serialized, string propertyName, int value)
        {
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing serialized integer property {propertyName}.");
            }

            property.intValue = value;
        }

        private static void CreateBuggyVisuals(Transform parent)
        {
            CreateVisual("Body_Visual", PrimitiveType.Cube, parent, new Vector3(0f, 0.2f, 0f), new Vector3(1.8f, 0.6f, 3f));
            CreateVisual("Cabin_Visual", PrimitiveType.Cube, parent, new Vector3(0f, 0.7f, -0.25f), new Vector3(1.3f, 0.45f, 1.45f));
            CreateWheel("Wheel_FrontLeft", parent, new Vector3(-1.05f, 0f, 1.05f));
            CreateWheel("Wheel_FrontRight", parent, new Vector3(1.05f, 0f, 1.05f));
            CreateWheel("Wheel_RearLeft", parent, new Vector3(-1.05f, 0f, -1.05f));
            CreateWheel("Wheel_RearRight", parent, new Vector3(1.05f, 0f, -1.05f));
        }

        private static void CreateWheel(string name, Transform parent, Vector3 position)
        {
            GameObject wheel = CreateVisual(name, PrimitiveType.Cylinder, parent, position, new Vector3(0.9f, 0.35f, 0.9f));
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        }

        private static GameObject CreateVisual(
            string name,
            PrimitiveType type,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale)
        {
            GameObject visual = GameObject.CreatePrimitive(type);
            visual.name = name;
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPosition;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = localScale;
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            return visual;
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
            primitive.transform.position = position;
            primitive.transform.rotation = Quaternion.identity;
            primitive.transform.localScale = scale;
            Collider collider = primitive.GetComponent<Collider>();
            if (!solid && collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            return primitive;
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(parent.GetChild(index).gameObject);
            }
        }

        private static void ValidateGeneratedScene(
            Scene scene,
            Transform roadRoot,
            Transform barrierRoot,
            RacingLine racingLine,
            RaceManager raceManager,
            CheckpointManager checkpointManager,
            RacePositionCalculator positionCalculator,
            RaceParticipant[] racers,
            Checkpoint[] checkpoints)
        {
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                throw new InvalidOperationException("Milestone 5 was not built in the expected scene.");
            }

            if (roadRoot.childCount != CenterLine.Length * 2 || barrierRoot.childCount != CenterLine.Length * 2)
            {
                throw new InvalidOperationException("Milestone 5 road or barrier loop is incomplete.");
            }

            if (!racingLine.IsConfigured || racingLine.Count != CenterLine.Length || checkpoints.Length != CheckpointNodeIndices.Length)
            {
                throw new InvalidOperationException("Milestone 5 racing line or checkpoints are incomplete.");
            }

            if (racers.Length != 4 || racers.Count(racer => racer != null && racer.IsPlayer) != 1 ||
                racers.Count(racer => racer != null && !racer.IsPlayer) != 3)
            {
                throw new InvalidOperationException("Milestone 5 requires exactly 1 player and 3 AI racers.");
            }

            for (int left = 0; left < racers.Length - 1; left++)
            {
                for (int right = left + 1; right < racers.Length; right++)
                {
                    if (Vector3.Distance(racers[left].Body.position, racers[right].Body.position) < 6f)
                    {
                        throw new InvalidOperationException("Milestone 5 starting grid contains overlapping or crowded racers.");
                    }
                }
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null || !racer.IsConfigured || !(racer.CarController.InputProviderSource is ICarInputProvider))
                {
                    throw new InvalidOperationException("A Milestone 5 racer is not wired through ICarInputProvider.");
                }

                if (!racer.IsPlayer)
                {
                    AIRacerBrain brain = racer.GetComponent<AIRacerBrain>();
                    if (!(racer.CarController.InputProviderSource is AIInputProvider) || brain == null || !brain.IsConfigured)
                    {
                        throw new InvalidOperationException("A Milestone 5 AI racer is not fully configured.");
                    }
                }
            }

            RaceParticipant player = racers.Single(racer => racer.IsPlayer);
            KeyboardInputProvider playerInput = player.GetComponent<KeyboardInputProvider>();
            if (playerInput == null || !playerInput.enabled ||
                player.GetComponent<AIInputProvider>() != null ||
                player.GetComponent<AIRacerBrain>() != null ||
                player.CarController.InputProviderSource != playerInput ||
                !player.CarController.enabled ||
                player.Body.isKinematic ||
                (player.Body.constraints & (RigidbodyConstraints.FreezePositionX | RigidbodyConstraints.FreezePositionZ)) != 0)
            {
                throw new InvalidOperationException("Milestone 5 player input or Rigidbody wiring is invalid.");
            }

            RaceHUD hud = UnityEngine.Object.FindFirstObjectByType<RaceHUD>();
            if (!raceManager.IsConfigured || !checkpointManager.IsConfigured || !positionCalculator.IsConfigured ||
                hud == null || !hud.IsConfigured)
            {
                throw new InvalidOperationException("Milestone 5 race, ranking, checkpoint, or HUD wiring is incomplete.");
            }
        }
    }
}
