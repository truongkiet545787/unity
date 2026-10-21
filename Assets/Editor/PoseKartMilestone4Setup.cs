using System;
using System.Linq;
using PoseKart.AI;
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
    public static class PoseKartMilestone4Setup
    {
        private const string SourceScenePath = "Assets/Scenes/Milestones_Archive/Milestone3_TrackGraybox.unity";
        private const string ScenePath = "Assets/Scenes/Milestones_Archive/Milestone4_AIRace.unity";
        private const int TotalLaps = 3;

        // Matches the frozen Milestone 3 graybox center line.
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

        [MenuItem("PoseKart/Setup/Milestone 4 AI Race")]
        public static void SetupMilestone4AIRace()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation before rebuilding the Milestone 4 AI race.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                Debug.LogError($"Frozen Milestone 3 source scene is missing at {SourceScenePath}.");
                return;
            }

            // Keep the source scene untouched while ensuring an existing target is not open when replaced.
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !AssetDatabase.DeleteAsset(ScenePath))
            {
                Debug.LogError($"Could not replace the existing Milestone 4 scene at {ScenePath}.");
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                Debug.LogError($"Could not copy the Milestone 3 graybox to {ScenePath}.");
                return;
            }

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildAIRace(scene);
        }

        [MenuItem("PoseKart/Setup/Milestone 4 AI Race", true)]
        private static bool ValidateSetupMenu()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;
        }

        private static void BuildAIRace(Scene scene)
        {
            GameObject playerObject = GameObject.Find("Player/Buggy_Player");
            GameObject raceRoot = GameObject.Find("Race");
            if (playerObject == null || raceRoot == null)
            {
                throw new InvalidOperationException("The frozen Milestone 3 player or race root is missing.");
            }

            RaceManager raceManager = raceRoot.GetComponentInChildren<RaceManager>(true);
            CheckpointManager checkpointManager = raceRoot.GetComponentInChildren<CheckpointManager>(true);
            LapTracker playerLapTracker = raceManager != null ? raceManager.LapTracker : null;
            Checkpoint[] checkpoints = raceRoot.GetComponentsInChildren<Checkpoint>(true)
                .OrderBy(checkpoint => checkpoint.CheckpointIndex)
                .ToArray();
            CarController playerController = playerObject.GetComponent<CarController>();
            Rigidbody playerBody = playerObject.GetComponent<Rigidbody>();

            if (raceManager == null || checkpointManager == null || playerLapTracker == null ||
                playerController == null || playerBody == null || checkpoints.Length < 2)
            {
                throw new InvalidOperationException("The frozen Milestone 3 race wiring is incomplete.");
            }

            RacingLine racingLine = BuildRacingLine(raceRoot.transform);
            Transform racersRoot = CreateRoot("Racers", raceRoot.transform).transform;
            Vector3 forward = (CenterLine[1] - CenterLine[0]).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);

            playerObject.transform.position = CenterLine[0] - forward * 6f - right * 2.1f + Vector3.up * 0.7f;
            playerObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            playerBody.linearVelocity = Vector3.zero;
            playerBody.angularVelocity = Vector3.zero;
            RaceParticipant player = playerObject.GetComponent<RaceParticipant>();
            if (player == null)
            {
                player = playerObject.AddComponent<RaceParticipant>();
            }

            player.Configure("Player", true, playerBody, playerController, playerLapTracker);

            RaceParticipant[] racers = new RaceParticipant[4];
            racers[0] = player;
            racers[1] = BuildAIRacer(
                "AI Coral",
                racersRoot,
                racingLine,
                CenterLine[0] - forward * 6f + right * 2.1f + Vector3.up * 0.7f,
                forward,
                0.88f,
                1.01f,
                0.95f,
                4.1f,
                1.05f,
                -0.35f);
            racers[2] = BuildAIRacer(
                "AI Azure",
                racersRoot,
                racingLine,
                CenterLine[0] - forward * 11f - right * 2.1f + Vector3.up * 0.7f,
                forward,
                0.78f,
                0.96f,
                1.05f,
                3.7f,
                1f,
                0.35f);
            racers[3] = BuildAIRacer(
                "AI Sunset",
                racersRoot,
                racingLine,
                CenterLine[0] - forward * 11f + right * 2.1f + Vector3.up * 0.7f,
                forward,
                0.68f,
                0.91f,
                1.12f,
                3.4f,
                0.95f,
                -0.2f);

            RacePositionCalculator positionCalculator = raceManager.GetComponent<RacePositionCalculator>();
            if (positionCalculator == null)
            {
                positionCalculator = raceManager.gameObject.AddComponent<RacePositionCalculator>();
            }

            checkpointManager.ConfigureRacers(raceManager, checkpoints, racers);
            positionCalculator.Configure(checkpointManager, racers);
            raceManager.ConfigureRacers(checkpointManager, racers, positionCalculator, TotalLaps);

            ValidateGeneratedScene(scene, racingLine, raceManager, checkpointManager, positionCalculator, racers, checkpoints);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save Milestone 4 AI race scene to {ScenePath}.");
                return;
            }

            Selection.activeGameObject = raceManager.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 4 AI race created at {ScenePath} with 1 player and 3 AI racers.", raceManager);
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
            float severity = Mathf.Clamp01(Vector3.Angle(current - previous, next - current) / 90f);
            return Mathf.Lerp(1f, 0.72f, severity);
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
            racerObject.transform.position = position;
            racerObject.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            BoxCollider collider = racerObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(1.9f, 1f, 3.2f);

            Rigidbody body = racerObject.AddComponent<Rigidbody>();
            body.mass = 650f;
            body.linearDamping = 0.2f;
            body.angularDamping = 5f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            AIInputProvider input = racerObject.AddComponent<AIInputProvider>();
            CarController controller = racerObject.AddComponent<CarController>();
            controller.Configure(body, input);
            LapTracker lapTracker = racerObject.AddComponent<LapTracker>();
            RaceParticipant participant = racerObject.AddComponent<RaceParticipant>();
            participant.Configure(racerName, false, body, controller, lapTracker);
            AIRacerBrain brain = racerObject.AddComponent<AIRacerBrain>();
            brain.Configure(
                racingLine,
                body,
                controller,
                input,
                skill,
                speedMultiplier,
                caution,
                steeringResponse,
                lookAheadMultiplier,
                sideBias);

            CreateBuggyVisuals(racerObject.transform);
            return participant;
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
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null)
            {
                UnityEngine.Object.DestroyImmediate(visualCollider);
            }

            return visual;
        }

        private static GameObject CreateRoot(string name, Transform parent)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(parent, false);
            return root;
        }

        private static void ValidateGeneratedScene(
            Scene scene,
            RacingLine racingLine,
            RaceManager raceManager,
            CheckpointManager checkpointManager,
            RacePositionCalculator positionCalculator,
            RaceParticipant[] racers,
            Checkpoint[] checkpoints)
        {
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                throw new InvalidOperationException("Milestone 4 was not built in the expected scene.");
            }

            if (!racingLine.IsConfigured || racingLine.Count != CenterLine.Length)
            {
                throw new InvalidOperationException("Milestone 4 racing line is incomplete.");
            }

            if (racers.Length != 4 || racers.Count(racer => racer != null && racer.IsPlayer) != 1 ||
                racers.Count(racer => racer != null && !racer.IsPlayer) != 3)
            {
                throw new InvalidOperationException("Milestone 4 requires exactly 1 player and 3 AI racers.");
            }

            foreach (RaceParticipant racer in racers)
            {
                if (racer == null || !racer.IsConfigured || !(racer.CarController.InputProviderSource is ICarInputProvider))
                {
                    throw new InvalidOperationException("A Milestone 4 racer is not wired through ICarInputProvider.");
                }

                if (!racer.IsPlayer)
                {
                    AIRacerBrain brain = racer.GetComponent<AIRacerBrain>();
                    if (!(racer.CarController.InputProviderSource is AIInputProvider) || brain == null || !brain.IsConfigured)
                    {
                        throw new InvalidOperationException("A Milestone 4 AI racer is not fully configured.");
                    }
                }
            }

            RaceHUD hud = UnityEngine.Object.FindFirstObjectByType<RaceHUD>();
            if (!raceManager.IsConfigured || !checkpointManager.IsConfigured || !positionCalculator.IsConfigured ||
                checkpoints.Length != 13 || hud == null || !hud.IsConfigured)
            {
                throw new InvalidOperationException("Milestone 4 race, ranking, checkpoint, or HUD wiring is incomplete.");
            }
        }
    }
}
