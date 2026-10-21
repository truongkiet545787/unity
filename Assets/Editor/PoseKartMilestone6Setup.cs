using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    /// <summary>Builds presentation only; the ML5 race hierarchy remains untouched.</summary>
    public static class PoseKartMilestone6Setup
    {
        private const string SourceScenePath = "Assets/Scenes/Milestones_Archive/Milestone5_ArcadeRace.unity";
        private const string ScenePath = "Assets/Scenes/Milestones_Archive/Milestone6_BuggyBeach.unity";
        private const string ImportedRootName = "ImportedLocalArt";
        private const string GrandRootName = "GrandBuggyBeach";
        private const string ImportedMeshRoot = "Assets/Art/Environment/ImportedLocal/BoatAttack/Meshes/";
        private const string PalmMeshPath = ImportedMeshRoot + "Palm_01.fbx";
        private const string BushMeshPath = ImportedMeshRoot + "Bush_Large_01.fbx";
        private const string RockMeshPath = ImportedMeshRoot + "Env_Rocks.fbx";
        private const string CliffMeshPath = ImportedMeshRoot + "Env_Cliffs.fbx";
        private const string PropsMeshPath = ImportedMeshRoot + "Props_low.fbx";
        private const string HousesMeshPath = ImportedMeshRoot + "HousesJetty_LOW.fbx";
        private const string HeroMountainMeshPath = ImportedMeshRoot + "Env_Hero_Mountain.fbx";
        private const string HeroTowerMeshPath = ImportedMeshRoot + "Env_Hero_Tower.fbx";
        private const string HeroCaveMeshPath = ImportedMeshRoot + "Env_Cave.fbx";
        private const string FullCliffMeshPath = ImportedMeshRoot + "Env_Cliff_Full_01.fbx";
        private const string Palm02MeshPath = ImportedMeshRoot + "Palm_02.fbx";
        private const string Palm03MeshPath = ImportedMeshRoot + "Palm_03.fbx";
        private const string TreesAtlasPath =
            "Assets/Art/Environment/ImportedLocal/BoatAttack/Textures/Trees_Albedo.png";

        private sealed class Materials
        {
            public Material Asphalt, Sand, Rock, Concrete, Wood, Metal, Container;
            public Material Vegetation, Guardrail, Ocean, BoostPad, RoadMarking, Cave;
            public Material ImportedFoliage;
            public Material Grass, Stucco, Coral, Terracotta, Turquoise, Gold, Foam, Cloud;
        }

        [InitializeOnLoadMethod]
        private static void QueueOpenSceneArtUpgrade()
        {
            EditorApplication.update -= UpgradeOpenMilestone6IfNeeded;
            EditorApplication.update += UpgradeOpenMilestone6IfNeeded;
        }

        private static void UpgradeOpenMilestone6IfNeeded()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                return;
            }

            EditorApplication.update -= UpgradeOpenMilestone6IfNeeded;

            Scene scene = SceneManager.GetActiveScene();
            GameObject root = GameObject.Find("Environment/ML6_BuggyBeach");
            if (!scene.IsValid() || scene.path != ScenePath || root == null ||
                root.transform.Find(GrandRootName) != null ||
                AssetDatabase.LoadAssetAtPath<GameObject>(PalmMeshPath) == null)
            {
                return;
            }

            Materials materials = CreateMaterials();
            if (root.transform.Find(ImportedRootName) == null)
            {
                CreateImportedLocalPass(root.transform, materials);
            }
            CreateGrandEnvironmentPass(root.transform, materials);
            ConfigureLighting(materials);
            EditorSceneManager.MarkSceneDirty(scene);
            if (EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.Log("ML6.2 imported local low-poly art pass applied to the open Buggy Beach scene.", root);
            }
        }

        [MenuItem("PoseKart/Setup/Milestone 6 Buggy Beach")]
        public static void SetupMilestone6BuggyBeach()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                Debug.LogWarning("Exit Play Mode and wait for compilation before building Milestone 6.");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !AssetDatabase.DeleteAsset(ScenePath))
            {
                Debug.LogError($"Could not replace {ScenePath}.");
                return;
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                Debug.LogError($"Could not create {ScenePath} from ML5.");
                return;
            }

            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            BuildEnvironment(scene);
        }

        [MenuItem("PoseKart/Setup/Milestone 6 Buggy Beach", true)]
        private static bool ValidateSetupMenu() =>
            !EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling;

        private static void BuildEnvironment(Scene scene)
        {
            GameObject environment = GameObject.Find("Environment");
            GameObject roadRoot = GameObject.Find("Environment/Road");
            GameObject barrierRoot = GameObject.Find("Environment/Barriers");
            if (environment == null || roadRoot == null || barrierRoot == null)
            {
                throw new InvalidOperationException("Milestone 5 environment hierarchy is missing.");
            }

            Transform oldRoot = environment.transform.Find("ML6_BuggyBeach");
            if (oldRoot != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRoot.gameObject);
            }

            Materials materials = CreateMaterials();
            NormalizeCopiedRoadJoints(roadRoot.transform);
            ApplyTrackMaterials(roadRoot.transform, barrierRoot.transform, materials);
            Transform root = new GameObject("ML6_BuggyBeach").transform;
            root.SetParent(environment.transform, false);

            CreateOcean(root, materials);
            CreateBeachStart(root, materials);
            CreateCoastalRoad(root, materials);
            CreateHarbor(root, materials);
            CreateCanyonAndCave(root, materials);
            CreateCliffRoad(root, materials);
            CreateImportedLocalPass(root, materials);
            CreateGrandEnvironmentPass(root, materials);
            ConfigureLighting(materials);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save {ScenePath}.");
                return;
            }

            Selection.activeGameObject = root.gameObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log($"Pose Kart Milestone 6 Buggy Beach created at {ScenePath}.", root);
        }

        private static void NormalizeCopiedRoadJoints(Transform roadRoot)
        {
            foreach (Transform child in roadRoot)
            {
                if (!child.name.StartsWith("RoadJoint_", StringComparison.Ordinal))
                {
                    continue;
                }

                // Older ML5 scene assets used a 0.09 m-high joint beneath 0.10 m road
                // segments. The exposed collider edge can act like a tiny ramp at speed.
                Vector3 position = child.localPosition;
                position.y = 0.05f;
                child.localPosition = position;

                Vector3 scale = child.localScale;
                scale.y = 0.1f;
                child.localScale = scale;
            }
        }

        private static Materials CreateMaterials()
        {
            return new Materials
            {
                Asphalt = MakeMaterial("ML6 Asphalt", new Color(0.075f, 0.09f, 0.1f), 0.15f, 0.65f),
                Sand = MakeMaterial("ML6 Sand", new Color(0.78f, 0.61f, 0.34f), 0f, 0.9f),
                Rock = MakeMaterial("ML6 Rock", new Color(0.28f, 0.2f, 0.15f), 0f, 0.95f),
                Concrete = MakeMaterial("ML6 Concrete", new Color(0.35f, 0.38f, 0.38f), 0f, 0.8f),
                Wood = MakeMaterial("ML6 Wood", new Color(0.28f, 0.13f, 0.045f), 0f, 0.85f),
                Metal = MakeMaterial("ML6 Metal", new Color(0.16f, 0.22f, 0.25f), 0.75f, 0.35f),
                Container = MakeMaterial("ML6 Container", new Color(0.05f, 0.27f, 0.42f), 0.45f, 0.45f),
                Vegetation = MakeMaterial("ML6 Vegetation", new Color(0.09f, 0.32f, 0.12f), 0f, 0.85f),
                Guardrail = MakeMaterial("ML6 Guardrail", new Color(0.42f, 0.47f, 0.49f), 0.8f, 0.3f),
                Ocean = MakeMaterial("ML6 Ocean", new Color(0.025f, 0.28f, 0.45f), 0.35f, 0.25f),
                BoostPad = MakeMaterial("ML6 BoostPad", new Color(0.05f, 0.9f, 0.85f), 0.3f, 0.25f),
                RoadMarking = MakeMaterial("ML6 RoadMarking", new Color(0.95f, 0.78f, 0.18f), 0f, 0.55f),
                Cave = MakeMaterial("ML6 Cave", new Color(0.11f, 0.08f, 0.065f), 0f, 1f),
                ImportedFoliage = MakeImportedFoliageMaterial(),
                Grass = MakeMaterial("ML6 Island Grass", new Color(0.12f, 0.43f, 0.18f), 0f, 0.82f),
                Stucco = MakeMaterial("ML6 Sunlit Stucco", new Color(0.96f, 0.88f, 0.68f), 0f, 0.72f),
                Coral = MakeMaterial("ML6 Coral Accent", new Color(0.92f, 0.22f, 0.13f), 0f, 0.48f),
                Terracotta = MakeMaterial("ML6 Terracotta Roof", new Color(0.66f, 0.16f, 0.07f), 0f, 0.75f),
                Turquoise = MakeMaterial("ML6 Tropical Turquoise", new Color(0.02f, 0.62f, 0.65f), 0.08f, 0.35f),
                Gold = MakeMaterial("ML6 Festival Gold", new Color(1f, 0.55f, 0.06f), 0.04f, 0.38f),
                Foam = MakeMaterial("ML6 Sea Foam", new Color(0.72f, 0.96f, 1f), 0f, 0.25f),
                Cloud = MakeMaterial("ML6 Clouds", new Color(0.92f, 0.98f, 1f), 0f, 0.18f)
            };
        }

        private static Material MakeImportedFoliageMaterial()
        {
            Material material = MakeMaterial("ML6 Imported Foliage", Color.white, 0f, 0.35f);
            Texture2D atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(TreesAtlasPath);
            if (atlas == null)
            {
                return material;
            }

            material.SetTexture("_BaseMap", atlas);
            material.SetTexture("_MainTex", atlas);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.45f);
            material.SetFloat("_Cull", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = 2450;
            material.doubleSidedGI = true;
            return material;
        }

        private static Material MakeMaterial(string name, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            material.color = color;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private static void ApplyTrackMaterials(Transform roadRoot, Transform barrierRoot, Materials m)
        {
            foreach (Renderer renderer in roadRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = m.Asphalt;
            }
            foreach (Renderer renderer in barrierRoot.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = m.Guardrail;
            }
        }

        private static void CreateOcean(Transform root, Materials m)
        {
            Visual("Ocean", PrimitiveType.Cube, root, new Vector3(0f, -0.55f, 4f), new Vector3(390f, 0.4f, 265f), m.Ocean);
            Visual("Distant_Island_East", PrimitiveType.Cylinder, root, new Vector3(192f, 4f, 45f), new Vector3(38f, 8f, 28f), m.Rock);
            Visual("Distant_Island_West", PrimitiveType.Cylinder, root, new Vector3(-202f, 3f, -22f), new Vector3(42f, 6f, 30f), m.Rock);
        }

        private static void CreateBeachStart(Transform root, Materials m)
        {
            Transform beach = Section("Beach_StartFinish", root);
            Visual("Beach_Sand", PrimitiveType.Cube, beach, new Vector3(0f, -0.18f, -101f), new Vector3(164f, 0.3f, 42f), m.Sand);
            Visual("StartFinish_BannerTop", PrimitiveType.Cube, beach, new Vector3(-53f, 6f, -65f), new Vector3(2f, 1.1f, 30f), m.RoadMarking);
            Visual("StartFinish_PostL", PrimitiveType.Cube, beach, new Vector3(-53f, 3f, -79f), new Vector3(1f, 6f, 1f), m.Metal);
            Visual("StartFinish_PostR", PrimitiveType.Cube, beach, new Vector3(-53f, 3f, -51f), new Vector3(1f, 6f, 1f), m.Metal);
            for (int index = 0; index < 8; index++)
            {
                float x = -48f + index * 13f;
                Visual($"GuideMark_{index:00}", PrimitiveType.Cube, beach, new Vector3(x, 0.12f, -65f), new Vector3(6f, 0.025f, 0.4f), m.RoadMarking);
            }
            for (int index = 0; index < 6; index++)
            {
                float x = -47f + index * 19f;
                Palm(beach, new Vector3(x, 0f, -90f + (index % 2) * 5f), m);
                Flag(beach, new Vector3(x, 0f, -43f - (index % 2) * 5f), m);
            }
            Visual("Beach_Hut", PrimitiveType.Cube, beach, new Vector3(52f, 2.5f, -93f), new Vector3(12f, 5f, 8f), m.Wood);
            Visual("Beach_Hut_Roof", PrimitiveType.Cylinder, beach, new Vector3(52f, 5.4f, -93f), new Vector3(10f, 1f, 7f), m.Rock);
        }

        private static void CreateCoastalRoad(Transform root, Materials m)
        {
            Transform coast = Section("Coastal_Road", root);
            Visual("Coast_Sand_East", PrimitiveType.Cube, coast, new Vector3(155f, -0.15f, -15f), new Vector3(50f, 0.25f, 115f), m.Sand);
            for (int index = 0; index < 7; index++)
            {
                float z = -54f + index * 16f;
                Rock(coast, new Vector3(174f + (index % 2) * 8f, 1.2f, z), new Vector3(7f, 3f, 6f), m.Rock);
                Palm(coast, new Vector3(155f, 0f, z + 6f), m);
            }
            Visual("Coast_Overlook", PrimitiveType.Cube, coast, new Vector3(174f, 4f, 62f), new Vector3(30f, 8f, 22f), m.Rock);
        }

        private static void CreateHarbor(Transform root, Materials m)
        {
            Transform harbor = Section("Harbor", root);
            Visual("Harbor_Dock", PrimitiveType.Cube, harbor, new Vector3(63f, 0.5f, -100f), new Vector3(104f, 1f, 28f), m.Concrete);
            for (int index = 0; index < 7; index++)
            {
                float x = 7f + index * 17f;
                Material containerMaterial = index % 2 == 0 ? m.Container : m.Metal;
                Visual($"Container_{index:00}", PrimitiveType.Cube, harbor, new Vector3(x, 2f, -114f), new Vector3(14f, 4f, 6f), containerMaterial);
                if (index % 3 == 0)
                    Visual($"ContainerStack_{index:00}", PrimitiveType.Cube, harbor, new Vector3(x, 6f, -114f), new Vector3(14f, 4f, 6f), m.Container);
            }
            Visual("Warehouse", PrimitiveType.Cube, harbor, new Vector3(97f, 7f, -108f), new Vector3(46f, 14f, 22f), m.Concrete);
            for (int index = 0; index < 3; index++)
                Crane(harbor, new Vector3(23f + index * 38f, 0f, -92f), m);
            for (int index = 0; index < 10; index++)
                Visual($"Harbor_Barrel_{index:00}", PrimitiveType.Cylinder, harbor, new Vector3(3f + index * 12f, 0.7f, -94f), new Vector3(1.1f, 1.4f, 1.1f), m.Metal);
        }

        private static void CreateCanyonAndCave(Transform root, Materials m)
        {
            Transform canyon = Section("Canyon_Cave", root);
            for (int index = 0; index < 8; index++)
            {
                float t = index / 7f;
                float x = Mathf.Lerp(-42f, -137f, t);
                float z = Mathf.Lerp(89f, 20f, t);
                Rock(canyon, new Vector3(x - 18f, 5f, z + 8f), new Vector3(15f, 12f, 16f), m.Rock);
                Rock(canyon, new Vector3(x + 18f, 5f, z - 8f), new Vector3(15f, 12f, 16f), m.Rock);
            }
            Transform cave = Section("Cave_Tunnel", canyon);
            Vector3 caveCenter = new Vector3(-116f, 0f, 39f);
            Visual("Cave_Ceiling", PrimitiveType.Cube, cave, caveCenter + Vector3.up * 10f, new Vector3(39f, 5f, 30f), m.Cave);
            Visual("Cave_Wall_L", PrimitiveType.Cube, cave, caveCenter + new Vector3(-20f, 5f, 0f), new Vector3(7f, 12f, 30f), m.Cave);
            Visual("Cave_Wall_R", PrimitiveType.Cube, cave, caveCenter + new Vector3(20f, 5f, 0f), new Vector3(7f, 12f, 30f), m.Cave);
            for (int index = 0; index < 4; index++)
                TunnelLight(cave, new Vector3(-116f, 6f, 28f + index * 8f), m);
        }

        private static void CreateCliffRoad(Transform root, Materials m)
        {
            Transform cliff = Section("Cliff_Ocean_Road", root);
            for (int index = 0; index < 7; index++)
            {
                float z = -56f + index * 17f;
                Rock(cliff, new Vector3(-169f, 6f, z), new Vector3(32f, 14f, 18f), m.Rock);
                Rock(cliff, new Vector3(-191f, 2f, z + 5f), new Vector3(22f, 5f, 15f), m.Rock);
            }
            Visual("Cliff_ObservationTower", PrimitiveType.Cylinder, cliff, new Vector3(-178f, 12f, -50f), new Vector3(7f, 24f, 7f), m.Concrete);
            Visual("Cliff_TowerRoof", PrimitiveType.Cylinder, cliff, new Vector3(-178f, 25f, -50f), new Vector3(9f, 2f, 9f), m.Metal);
        }

        private static void CreateImportedLocalPass(Transform root, Materials m)
        {
            Transform existing = root.Find(ImportedRootName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            DisableReplacedPrimitiveVisuals(root);
            Transform imported = Section(ImportedRootName, root);

            Mesh palm = FindImportedMesh(PalmMeshPath, "Palm_01");
            Mesh bush = FindImportedMesh(BushMeshPath, "Bush_Large_01");
            Mesh rock = FindImportedMesh(RockMeshPath, "Rock_Medium_01", "Rock_Large_01");
            Mesh cliff = FindImportedMesh(CliffMeshPath, "Cliff_Wall_01_LOD0", "Cliff_Wall_01");
            Mesh arrow = FindImportedMesh(PropsMeshPath, "Geo_Props_ArrowSign_LOD_0", "Geo_Props_ArrowSign");
            Mesh house = FindImportedMesh(HousesMeshPath, "House01");
            Mesh warehouse = FindImportedMesh(HousesMeshPath, "House03");
            Mesh jetty = FindImportedMesh(HousesMeshPath, "JettyLong01");

            for (int index = 0; index < 6; index++)
            {
                float x = -47f + index * 19f;
                AddImportedMesh($"BA_BeachPalm_{index:00}", palm, imported,
                    new Vector3(x, 0f, -90f + (index % 2) * 5f),
                    Quaternion.Euler(0f, index * 47f, 0f), new Vector3(5f, 8f, 5f), m.ImportedFoliage);
                AddImportedMesh($"BA_BeachBush_{index:00}", bush, imported,
                    new Vector3(x + 5f, 0f, -44f - (index % 2) * 5f),
                    Quaternion.Euler(0f, index * 61f, 0f), new Vector3(4f, 2.2f, 4f), m.ImportedFoliage);
            }

            for (int index = 0; index < 7; index++)
            {
                float z = -54f + index * 16f;
                AddImportedMesh($"BA_CoastPalm_{index:00}", palm, imported,
                    new Vector3(155f, 0f, z + 6f), Quaternion.Euler(0f, 18f + index * 39f, 0f),
                    new Vector3(5f, 8f, 5f), m.ImportedFoliage);
                AddImportedMesh($"BA_CoastRock_{index:00}", rock, imported,
                    new Vector3(174f + (index % 2) * 8f, 1.2f, z),
                    Quaternion.Euler(8f, index * 37f, 4f), new Vector3(8f, 4f, 7f), m.Rock);
            }

            for (int index = 0; index < 8; index++)
            {
                float t = index / 7f;
                float x = Mathf.Lerp(-42f, -137f, t);
                float z = Mathf.Lerp(89f, 20f, t);
                AddImportedMesh($"BA_CanyonRockL_{index:00}", rock, imported,
                    new Vector3(x - 18f, 5f, z + 8f), Quaternion.Euler(8f, index * 31f, 4f),
                    new Vector3(16f, 12f, 16f), m.Rock);
                AddImportedMesh($"BA_CanyonRockR_{index:00}", rock, imported,
                    new Vector3(x + 18f, 5f, z - 8f), Quaternion.Euler(-5f, 22f + index * 29f, -3f),
                    new Vector3(16f, 12f, 16f), m.Rock);
            }

            for (int index = 0; index < 7; index++)
            {
                float z = -56f + index * 17f;
                AddImportedMesh($"BA_CliffRock_{index:00}", rock, imported,
                    new Vector3(-176f, 5f, z), Quaternion.Euler(3f, index * 41f, 0f),
                    new Vector3(30f, 14f, 18f), m.Rock);
            }

            AddImportedMesh("BA_CaveWall_West", cliff, imported, new Vector3(-136f, 5f, 39f),
                Quaternion.Euler(0f, 90f, 0f), new Vector3(8f, 12f, 30f), m.Cave);
            AddImportedMesh("BA_CaveWall_East", cliff, imported, new Vector3(-96f, 5f, 39f),
                Quaternion.Euler(0f, -90f, 0f), new Vector3(8f, 12f, 30f), m.Cave);
            AddImportedMesh("BA_BeachHut", house, imported, new Vector3(52f, 0f, -93f),
                Quaternion.Euler(0f, 90f, 0f), new Vector3(12f, 7f, 10f), m.Wood);
            AddImportedMesh("BA_HarborWarehouse", warehouse, imported, new Vector3(97f, 0f, -108f),
                Quaternion.Euler(0f, 90f, 0f), new Vector3(44f, 14f, 22f), m.Concrete);
            AddImportedMesh("BA_HarborJetty", jetty, imported, new Vector3(63f, 0.9f, -100f),
                Quaternion.identity, new Vector3(70f, 1.2f, 12f), m.Wood);

            Vector3[] signPositions =
            {
                new Vector3(-47f, 0f, -51f), new Vector3(88f, 0f, -50f),
                new Vector3(151f, 0f, -4f), new Vector3(105f, 0f, 66f),
                new Vector3(5f, 0f, 82f), new Vector3(-94f, 0f, 59f)
            };
            for (int index = 0; index < signPositions.Length; index++)
            {
                AddImportedMesh($"BA_DirectionSign_{index:00}", arrow, imported, signPositions[index],
                    Quaternion.Euler(0f, index * 58f, 0f), new Vector3(2.5f, 3f, 1.2f), m.RoadMarking);
            }
        }

        private static void DisableReplacedPrimitiveVisuals(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                string objectName = child.name;
                if (objectName == "Palm_Trunk" || objectName == "Palm_Leaf" || objectName == "Rock" ||
                    objectName == "Beach_Hut" || objectName == "Beach_Hut_Roof" || objectName == "Warehouse")
                {
                    child.gameObject.SetActive(false);
                }
            }
        }

        private static Mesh FindImportedMesh(string assetPath, params string[] preferredNames)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            foreach (string preferredName in preferredNames)
            {
                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset is Mesh mesh && string.Equals(mesh.name, preferredName, StringComparison.OrdinalIgnoreCase))
                    {
                        return mesh;
                    }
                }
            }

            foreach (string preferredName in preferredNames)
            {
                foreach (UnityEngine.Object asset in assets)
                {
                    if (asset is Mesh mesh && mesh.name.IndexOf(preferredName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return mesh;
                    }
                }
            }

            Debug.LogWarning($"ML6.2 could not find mesh {string.Join("/", preferredNames)} in {assetPath}.");
            return null;
        }

        private static GameObject AddImportedMesh(
            string name,
            Mesh mesh,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            Vector3 targetSize,
            Material material)
        {
            if (mesh == null)
            {
                return null;
            }

            GameObject instance = new GameObject(name);
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.rotation = rotation;
            instance.isStatic = true;

            Vector3 meshSize = mesh.bounds.size;
            instance.transform.localScale = new Vector3(
                targetSize.x / Mathf.Max(0.001f, meshSize.x),
                targetSize.y / Mathf.Max(0.001f, meshSize.y),
                targetSize.z / Mathf.Max(0.001f, meshSize.z));

            instance.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = instance.AddComponent<MeshRenderer>();
            Material[] materials = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int index = 0; index < materials.Length; index++)
            {
                materials[index] = material;
            }
            renderer.sharedMaterials = materials;
            return instance;
        }

        private static Mesh FindFirstImportedMesh(string assetPath)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (asset is Mesh mesh && !mesh.name.EndsWith("_billboard", StringComparison.OrdinalIgnoreCase))
                {
                    return mesh;
                }
            }

            Debug.LogWarning($"ML6 Grand Buggy Beach could not find a mesh in {assetPath}.");
            return null;
        }

        private static void CreateGrandEnvironmentPass(Transform root, Materials m)
        {
            Transform existing = root.Find(GrandRootName);
            if (existing != null)
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            Transform grand = Section(GrandRootName, root);
            Mesh palm01 = FindImportedMesh(PalmMeshPath, "Palm_01");
            Mesh palm02 = FindImportedMesh(Palm02MeshPath, "Palm_02");
            Mesh palm03 = FindImportedMesh(Palm03MeshPath, "Palm_03");
            Mesh bush = FindImportedMesh(BushMeshPath, "Bush_Large_01");
            Mesh rock = FindImportedMesh(RockMeshPath, "Rock_Large_01", "Rock_Medium_01");
            Mesh cliff = FindImportedMesh(CliffMeshPath, "Cliff_Wall_01_LOD0", "Cliff_Wall_01");
            Mesh heroMountain = FindFirstImportedMesh(HeroMountainMeshPath);
            Mesh heroTower = FindFirstImportedMesh(HeroTowerMeshPath);
            Mesh heroCave = FindFirstImportedMesh(HeroCaveMeshPath);
            Mesh fullCliff = FindFirstImportedMesh(FullCliffMeshPath);
            Mesh house01 = FindImportedMesh(HousesMeshPath, "House01");
            Mesh house02 = FindImportedMesh(HousesMeshPath, "House02");
            Mesh house04 = FindImportedMesh(HousesMeshPath, "House04");
            Mesh house05 = FindImportedMesh(HousesMeshPath, "House05");
            Mesh jetty = FindImportedMesh(HousesMeshPath, "JettyLong01");
            Mesh jettySquare = FindImportedMesh(HousesMeshPath, "JettySqr01");
            Mesh umbrella = FindImportedMesh(PropsMeshPath, "Geo_Props_Umbrella_LOD_0", "Geo_Props_Umbrella");
            Mesh kayak = FindImportedMesh(PropsMeshPath, "Geo_Props_Kayak");
            Mesh buoy = FindImportedMesh(PropsMeshPath, "Geo_Props_Bouy_LOD_0", "Geo_Props_Bouy");
            Mesh blimp = FindImportedMesh(PropsMeshPath, "Geo_Props_Blimp");

            CreateIslandMass(grand, heroMountain, fullCliff, rock, bush, m);
            CreateGrandHarbor(grand, jetty, jettySquare, house01, house02, kayak, buoy, m);
            CreateCoastalVillage(grand, house01, house02, house04, house05, palm02, bush, m);
            CreateHeroBeach(grand, palm01, palm02, palm03, umbrella, kayak, bush, m);
            CreateRockyCanyon(grand, rock, cliff, heroCave, m);
            CreateCliffLandmark(grand, rock, cliff, heroTower, fullCliff, palm03, m);
            CreateFestivalDetails(grand, m);
            CreateSkyline(grand, blimp, m);
        }

        private static void CreateIslandMass(
            Transform parent, Mesh mountain, Mesh fullCliff, Mesh rock, Mesh bush, Materials m)
        {
            Transform island = Section("01_Island_Mass", parent);
            Visual("Island_Rock_Foundation", PrimitiveType.Cylinder, island,
                new Vector3(0f, -6.2f, 0f), new Vector3(214f, 5.8f, 154f), m.Rock);
            Visual("Island_Sand_Shelf", PrimitiveType.Cylinder, island,
                new Vector3(0f, -1.45f, -5f), new Vector3(198f, 1.25f, 139f), m.Sand);
            Visual("Island_Grass_Core", PrimitiveType.Cylinder, island,
                new Vector3(-10f, -1.2f, 18f), new Vector3(145f, 1.12f, 101f), m.Grass);

            AddImportedMesh("Central_Hero_Mountain", mountain, island, new Vector3(-8f, -0.2f, 38f),
                Quaternion.Euler(0f, 18f, 0f), new Vector3(122f, 46f, 78f), m.Rock);
            AddImportedMesh("North_Ridge_West", fullCliff, island, new Vector3(-82f, 1f, 103f),
                Quaternion.Euler(0f, 8f, 0f), new Vector3(82f, 28f, 38f), m.Rock);
            AddImportedMesh("North_Ridge_East", fullCliff, island, new Vector3(70f, 1f, 102f),
                Quaternion.Euler(0f, 173f, 0f), new Vector3(86f, 30f, 40f), m.Rock);

            Vector3[] shoreRocks =
            {
                new Vector3(-206f, 0f, 78f), new Vector3(-214f, 0f, 12f),
                new Vector3(-201f, 0f, -74f), new Vector3(205f, 0f, 70f),
                new Vector3(215f, 0f, 2f), new Vector3(199f, 0f, -77f),
                new Vector3(-134f, 0f, 136f), new Vector3(12f, 0f, 148f),
                new Vector3(145f, 0f, 134f)
            };
            for (int index = 0; index < shoreRocks.Length; index++)
            {
                Vector3 p = shoreRocks[index];
                AddImportedMesh($"Shore_Hero_Rock_{index:00}", rock, island, p,
                    Quaternion.Euler(index * 7f, index * 41f, index % 2 == 0 ? 4f : -5f),
                    new Vector3(22f + index % 3 * 5f, 14f + index % 2 * 5f, 20f), m.Rock);
                AddImportedMesh($"Shore_Bush_{index:00}", bush, island, p + new Vector3(7f, 6f, 5f),
                    Quaternion.Euler(0f, index * 53f, 0f), new Vector3(8f, 4f, 8f), m.ImportedFoliage);
            }

            for (int index = 0; index < 9; index++)
            {
                float x = -170f + index * 42f;
                Visual($"Foam_South_{index:00}", PrimitiveType.Cube, island,
                    new Vector3(x, -0.12f, -139f + Mathf.Sin(index * 1.7f) * 5f),
                    new Vector3(25f, 0.025f, 1.3f), m.Foam).transform.rotation = Quaternion.Euler(0f, index % 2 == 0 ? 8f : -8f, 0f);
            }
        }

        private static void CreateGrandHarbor(
            Transform parent, Mesh jetty, Mesh jettySquare, Mesh house01, Mesh house02,
            Mesh kayak, Mesh buoy, Materials m)
        {
            Transform harbor = Section("02_Grand_Harbor", parent);
            for (int index = 0; index < 5; index++)
            {
                AddImportedMesh($"Harbor_Pier_{index:00}", jetty, harbor,
                    new Vector3(18f + index * 26f, 0.45f, -123f), Quaternion.identity,
                    new Vector3(25f, 1.2f, 10f), m.Wood);
                AddImportedMesh($"Harbor_Deck_{index:00}", jettySquare, harbor,
                    new Vector3(20f + index * 27f, 0.5f, -108f), Quaternion.Euler(0f, 90f, 0f),
                    new Vector3(16f, 1.2f, 16f), m.Wood);
            }

            for (int index = 0; index < 6; index++)
            {
                Mesh house = index % 2 == 0 ? house01 : house02;
                Material facade = index % 3 == 0 ? m.Coral : index % 3 == 1 ? m.Turquoise : m.Stucco;
                AddImportedMesh($"Harbor_Fisher_House_{index:00}", house, harbor,
                    new Vector3(-6f + index * 25f, 0.2f, -105f), Quaternion.Euler(0f, 180f, 0f),
                    new Vector3(17f, 11f + index % 2 * 3f, 14f), facade);
            }

            for (int index = 0; index < 10; index++)
            {
                float x = 4f + index * 16f;
                Visual($"Harbor_Pylon_{index:00}", PrimitiveType.Cylinder, harbor,
                    new Vector3(x, 1.8f, -134f), new Vector3(0.55f, 3.6f, 0.55f), m.Wood);
                AddImportedMesh($"Harbor_Buoy_{index:00}", buoy, harbor,
                    new Vector3(x + 5f, 0.1f, -147f - (index % 2) * 5f), Quaternion.Euler(0f, index * 29f, 0f),
                    new Vector3(1.8f, 2.8f, 1.8f), index % 2 == 0 ? m.Coral : m.Gold);
            }

            AddImportedMesh("Harbor_Fishing_Boat_A", kayak, harbor, new Vector3(57f, -0.05f, -153f),
                Quaternion.Euler(0f, 18f, 0f), new Vector3(12f, 2.2f, 4f), m.Coral);
            AddImportedMesh("Harbor_Fishing_Boat_B", kayak, harbor, new Vector3(105f, -0.05f, -151f),
                Quaternion.Euler(0f, -24f, 0f), new Vector3(14f, 2.5f, 4.5f), m.Turquoise);
        }

        private static void CreateCoastalVillage(
            Transform parent, Mesh house01, Mesh house02, Mesh house04, Mesh house05,
            Mesh palm, Mesh bush, Materials m)
        {
            Transform village = Section("03_Coastal_Village", parent);
            Vector3[] positions =
            {
                new Vector3(-151f, 0f, -24f), new Vector3(-147f, 0f, -2f),
                new Vector3(-143f, 0f, 21f), new Vector3(-135f, 0f, 46f),
                new Vector3(-117f, 0f, 67f), new Vector3(-93f, 0f, 78f),
                new Vector3(-72f, 0f, 86f), new Vector3(-121f, 7f, 20f),
                new Vector3(-102f, 8f, 39f), new Vector3(-82f, 10f, 55f),
                new Vector3(-58f, 11f, 69f), new Vector3(-39f, 12f, 82f)
            };
            Mesh[] houses = { house01, house02, house04, house05 };
            Material[] colors = { m.Stucco, m.Coral, m.Turquoise, m.Gold };
            for (int index = 0; index < positions.Length; index++)
            {
                AddImportedMesh($"Village_House_{index:00}", houses[index % houses.Length], village,
                    positions[index], Quaternion.Euler(0f, 62f + index * 13f, 0f),
                    new Vector3(14f + index % 3 * 2f, 10f + index % 2 * 3f, 13f), colors[index % colors.Length]);
                AddImportedMesh($"Village_Palm_{index:00}", palm, village,
                    positions[index] + new Vector3(index % 2 == 0 ? 9f : -9f, 0f, 7f),
                    Quaternion.Euler(0f, index * 37f, 0f), new Vector3(6f, 11f, 6f), m.ImportedFoliage);
                if (index % 2 == 0)
                {
                    AddImportedMesh($"Village_Bush_{index:00}", bush, village,
                        positions[index] + new Vector3(4f, 0f, -6f), Quaternion.Euler(0f, index * 61f, 0f),
                        new Vector3(5f, 2.5f, 5f), m.ImportedFoliage);
                }
            }

            for (int index = 0; index < 9; index++)
            {
                Visual($"Village_Banner_{index:00}", PrimitiveType.Cube, village,
                    new Vector3(-146f + index * 12f, 8f + Mathf.Sin(index) * 1.2f, 32f + index * 5f),
                    new Vector3(4f, 0.35f, 0.08f), index % 2 == 0 ? m.Coral : m.Gold)
                    .transform.rotation = Quaternion.Euler(0f, 25f, index % 2 == 0 ? 8f : -8f);
            }
        }

        private static void CreateHeroBeach(
            Transform parent, Mesh palm01, Mesh palm02, Mesh palm03, Mesh umbrella,
            Mesh kayak, Mesh bush, Materials m)
        {
            Transform beach = Section("04_Hero_Beach", parent);
            Visual("Hero_Beach_Sand", PrimitiveType.Cube, beach,
                new Vector3(38f, -0.05f, -91f), new Vector3(146f, 0.18f, 43f), m.Sand);

            Mesh[] palms = { palm01, palm02, palm03 };
            for (int index = 0; index < 22; index++)
            {
                float x = -60f + index * 10f;
                float z = -111f + Mathf.Sin(index * 1.3f) * 7f;
                AddImportedMesh($"Beach_Palm_Hero_{index:00}", palms[index % palms.Length], beach,
                    new Vector3(x, 0f, z), Quaternion.Euler(0f, index * 47f, index % 2 == 0 ? 2f : -2f),
                    new Vector3(6f + index % 3, 11f + index % 4, 6f + index % 3), m.ImportedFoliage);
                if (index % 3 == 0)
                {
                    AddImportedMesh($"Beach_Umbrella_{index:00}", umbrella, beach,
                        new Vector3(x + 4f, 0f, -128f), Quaternion.Euler(0f, index * 33f, 0f),
                        new Vector3(5f, 4.5f, 5f), index % 2 == 0 ? m.Coral : m.Turquoise);
                    AddImportedMesh($"Beach_Shrub_{index:00}", bush, beach,
                        new Vector3(x - 5f, 0f, z + 5f), Quaternion.Euler(0f, index * 51f, 0f),
                        new Vector3(5f, 2.5f, 5f), m.ImportedFoliage);
                }
            }

            AddImportedMesh("Beach_Kayak_Red", kayak, beach, new Vector3(-18f, 0.1f, -132f),
                Quaternion.Euler(0f, 68f, -6f), new Vector3(10f, 2f, 3f), m.Coral);
            AddImportedMesh("Beach_Kayak_Blue", kayak, beach, new Vector3(71f, 0.1f, -130f),
                Quaternion.Euler(0f, -52f, 5f), new Vector3(10f, 2f, 3f), m.Turquoise);
        }

        private static void CreateRockyCanyon(Transform parent, Mesh rock, Mesh cliff, Mesh cave, Materials m)
        {
            Transform canyon = Section("05_Rocky_Canyon_And_Cave", parent);
            for (int index = 0; index < 11; index++)
            {
                float t = index / 10f;
                Vector3 center = Vector3.Lerp(new Vector3(-35f, 0f, 91f), new Vector3(-126f, 0f, 28f), t);
                Vector3 side = new Vector3(15f, 0f, -19f).normalized * (20f + Mathf.Sin(index) * 4f);
                AddImportedMesh($"Canyon_Wall_Outer_{index:00}", cliff, canyon, center + side + Vector3.up * 7f,
                    Quaternion.Euler(0f, 48f + index * 8f, 0f), new Vector3(24f, 22f, 18f), m.Rock);
                AddImportedMesh($"Canyon_Wall_Inner_{index:00}", rock, canyon, center - side + Vector3.up * 5f,
                    Quaternion.Euler(index % 2 == 0 ? 5f : -5f, index * 31f, 0f),
                    new Vector3(18f, 16f, 18f), m.Rock);
            }

            AddImportedMesh("Hero_Cave_Mass", cave, canyon, new Vector3(-116f, 0f, 39f),
                Quaternion.Euler(0f, 90f, 0f), new Vector3(54f, 30f, 48f), m.Cave);
            Visual("Cave_Glow_Core", PrimitiveType.Cube, canyon, new Vector3(-116f, 5f, 39f),
                new Vector3(18f, 5f, 12f), MakeEmissiveMaterial("ML6 Cave Amber", new Color(1f, 0.22f, 0.025f), 2.6f));
            for (int index = 0; index < 8; index++)
            {
                Vector3 p = new Vector3(-130f + index * 4f, 4.5f, 30f + (index % 2) * 18f);
                GameObject torch = Visual($"Canyon_Torch_{index:00}", PrimitiveType.Sphere, canyon, p,
                    Vector3.one * 0.45f, m.Gold);
                Light light = torch.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.35f, 0.08f);
                light.intensity = 3.5f;
                light.range = 16f;
                light.shadows = LightShadows.None;
            }
        }

        private static void CreateCliffLandmark(
            Transform parent, Mesh rock, Mesh cliff, Mesh tower, Mesh fullCliff, Mesh palm, Materials m)
        {
            Transform landmark = Section("06_Cliff_Road_And_Lighthouse", parent);
            for (int index = 0; index < 10; index++)
            {
                float z = -77f + index * 18f;
                AddImportedMesh($"West_Cliff_Face_{index:00}", index % 3 == 0 ? fullCliff : cliff, landmark,
                    new Vector3(-188f - (index % 2) * 7f, 2f + index % 3 * 3f, z),
                    Quaternion.Euler(0f, 88f + index * 9f, 0f), new Vector3(35f, 25f + index % 2 * 8f, 24f), m.Rock);
                AddImportedMesh($"West_Cliff_Rock_{index:00}", rock, landmark,
                    new Vector3(-214f, -0.5f, z + 5f), Quaternion.Euler(4f, index * 39f, -3f),
                    new Vector3(24f, 14f, 22f), m.Rock);
            }

            AddImportedMesh("Lighthouse_Hero_Rock", tower, landmark, new Vector3(-215f, 0f, -51f),
                Quaternion.Euler(0f, 32f, 0f), new Vector3(34f, 31f, 34f), m.Rock);
            Visual("Lighthouse_White_Tower", PrimitiveType.Cylinder, landmark,
                new Vector3(-215f, 25f, -51f), new Vector3(6.8f, 15f, 6.8f), m.Stucco);
            Visual("Lighthouse_Red_Band_Low", PrimitiveType.Cylinder, landmark,
                new Vector3(-215f, 17f, -51f), new Vector3(7f, 2.3f, 7f), m.Coral);
            Visual("Lighthouse_Red_Band_High", PrimitiveType.Cylinder, landmark,
                new Vector3(-215f, 31f, -51f), new Vector3(7f, 2.3f, 7f), m.Coral);
            Visual("Lighthouse_Lantern_Room", PrimitiveType.Cylinder, landmark,
                new Vector3(-215f, 42f, -51f), new Vector3(8.2f, 3f, 8.2f), m.Metal);
            Visual("Lighthouse_Lantern", PrimitiveType.Sphere, landmark,
                new Vector3(-215f, 43f, -51f), Vector3.one * 4.2f,
                MakeEmissiveMaterial("ML6 Lighthouse Beam", new Color(1f, 0.82f, 0.32f), 3.5f));
            Visual("Lighthouse_Roof", PrimitiveType.Cylinder, landmark,
                new Vector3(-215f, 47f, -51f), new Vector3(9f, 1.2f, 9f), m.Coral);

            Light beacon = new GameObject("Lighthouse_Beacon_Light").AddComponent<Light>();
            beacon.transform.SetParent(landmark, false);
            beacon.transform.position = new Vector3(-215f, 44f, -51f);
            beacon.type = LightType.Point;
            beacon.color = new Color(1f, 0.82f, 0.38f);
            beacon.intensity = 5f;
            beacon.range = 85f;
            beacon.shadows = LightShadows.None;

            for (int index = 0; index < 8; index++)
            {
                AddImportedMesh($"Cliff_Top_Palm_{index:00}", palm, landmark,
                    new Vector3(-180f + index % 2 * 19f, 12f + index % 3 * 3f, -66f + index * 20f),
                    Quaternion.Euler(0f, index * 43f, 0f), new Vector3(6f, 12f, 6f), m.ImportedFoliage);
            }
        }

        private static void CreateFestivalDetails(Transform parent, Materials m)
        {
            Transform details = Section("07_Race_Festival_Details", parent);
            Vector3[] gates =
            {
                new Vector3(-53f, 0f, -65f), new Vector3(92f, 0f, -66f),
                new Vector3(153f, 0f, 18f), new Vector3(70f, 0f, 82f),
                new Vector3(-76f, 0f, 77f), new Vector3(-154f, 0f, 5f)
            };
            for (int index = 0; index < gates.Length; index++)
            {
                Vector3 p = gates[index];
                Visual($"Festival_Gate_PostL_{index:00}", PrimitiveType.Cylinder, details,
                    p + new Vector3(-6f, 3.5f, 0f), new Vector3(0.35f, 3.5f, 0.35f), m.Coral);
                Visual($"Festival_Gate_PostR_{index:00}", PrimitiveType.Cylinder, details,
                    p + new Vector3(6f, 3.5f, 0f), new Vector3(0.35f, 3.5f, 0.35f), m.Turquoise);
                Visual($"Festival_Gate_Banner_{index:00}", PrimitiveType.Cube, details,
                    p + Vector3.up * 6.7f, new Vector3(12f, 0.7f, 0.25f), index % 2 == 0 ? m.Gold : m.Coral);
            }

            for (int index = 0; index < 24; index++)
            {
                float angle = index / 24f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(angle) * 173f, 5f + index % 3, Mathf.Sin(angle) * 108f);
                GameObject flag = Visual($"Island_Flag_{index:00}", PrimitiveType.Cube, details,
                    p, new Vector3(2.6f, 1.2f, 0.12f), index % 3 == 0 ? m.Coral : index % 3 == 1 ? m.Gold : m.Turquoise);
                flag.transform.rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + 90f, index % 2 == 0 ? 8f : -8f);
            }
        }

        private static void CreateSkyline(Transform parent, Mesh blimp, Materials m)
        {
            Transform skyline = Section("08_Tropical_Skyline", parent);
            Vector3[] islands =
            {
                new Vector3(-325f, -3f, 96f), new Vector3(318f, -4f, 118f),
                new Vector3(-278f, -5f, -190f), new Vector3(300f, -5f, -176f)
            };
            for (int index = 0; index < islands.Length; index++)
            {
                Visual($"Distant_Island_{index:00}", PrimitiveType.Cylinder, skyline, islands[index],
                    new Vector3(42f + index * 5f, 6f + index % 2 * 3f, 28f + index * 4f), m.Rock);
                Visual($"Distant_Island_Green_{index:00}", PrimitiveType.Cylinder, skyline,
                    islands[index] + Vector3.up * 8f,
                    new Vector3(32f + index * 3f, 3f, 22f + index * 3f), m.Grass);
            }

            for (int cluster = 0; cluster < 7; cluster++)
            {
                Vector3 basePosition = new Vector3(-260f + cluster * 85f, 62f + cluster % 3 * 9f, 105f + cluster % 2 * 80f);
                for (int puff = 0; puff < 4; puff++)
                {
                    Visual($"Cloud_{cluster:00}_{puff:00}", PrimitiveType.Sphere, skyline,
                        basePosition + new Vector3(puff * 7f, Mathf.Sin(puff * 1.9f) * 3f, puff % 2 * 4f),
                        new Vector3(13f, 5f + puff % 2 * 2f, 7f), m.Cloud);
                }
            }

            AddImportedMesh("Festival_Blimp", blimp, skyline, new Vector3(70f, 72f, 90f),
                Quaternion.Euler(0f, -28f, 0f), new Vector3(30f, 11f, 11f), m.Coral);
        }

        private static Material MakeEmissiveMaterial(string name, Color color, float intensity)
        {
            Material material = MakeMaterial(name, color, 0f, 0.25f);
            material.SetColor("_EmissionColor", color * intensity);
            material.EnableKeyword("_EMISSION");
            return material;
        }

        private static void ConfigureLighting(Materials m)
        {
            Light sun = UnityEngine.Object.FindFirstObjectByType<Light>();
            if (sun == null)
            {
                sun = new GameObject("ML6 Sun").AddComponent<Light>();
            }
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            sun.intensity = 1.42f;
            sun.color = new Color(1f, 0.9f, 0.72f);
            sun.shadows = LightShadows.Soft;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.5f, 0.61f, 0.66f);
            RenderSettings.fog = true;
            RenderSettings.fogColor = new Color(0.52f, 0.76f, 0.84f);
            RenderSettings.fogDensity = 0.0011f;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material sky = new Material(skyShader) { name = "ML6 Tropical Procedural Sky" };
                sky.SetColor("_SkyTint", new Color(0.18f, 0.63f, 0.92f));
                sky.SetColor("_GroundColor", new Color(0.46f, 0.55f, 0.52f));
                sky.SetFloat("_AtmosphereThickness", 0.85f);
                sky.SetFloat("_Exposure", 1.22f);
                sky.SetFloat("_SunSize", 0.045f);
                RenderSettings.skybox = sky;
            }

            // The project also has a PoseKart.Camera namespace, so qualify the engine type.
            foreach (UnityEngine.Camera camera in
                     UnityEngine.Object.FindObjectsByType<UnityEngine.Camera>(FindObjectsSortMode.None))
            {
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.backgroundColor = new Color(0.22f, 0.67f, 0.86f);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 1200f);
            }
        }

        private static Transform Section(string name, Transform parent)
        {
            GameObject section = new GameObject(name);
            section.transform.SetParent(parent, false);
            section.isStatic = true;
            return section.transform;
        }

        private static GameObject Visual(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent, false);
            obj.transform.position = position;
            obj.transform.localScale = scale;
            obj.isStatic = true;
            Collider collider = obj.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }

        private static void Rock(Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject rock = Visual("Rock", PrimitiveType.Cube, parent, position, scale, material);
            rock.transform.rotation = Quaternion.Euler(8f, position.x * 0.7f, position.z * 0.25f);
        }

        private static void Palm(Transform parent, Vector3 position, Materials m)
        {
            Visual("Palm_Trunk", PrimitiveType.Cylinder, parent, position + Vector3.up * 3f, new Vector3(0.55f, 6f, 0.55f), m.Wood);
            for (int index = 0; index < 5; index++)
            {
                GameObject leaf = Visual("Palm_Leaf", PrimitiveType.Cube, parent, position + Vector3.up * 6.2f, new Vector3(0.55f, 0.16f, 5f), m.Vegetation);
                leaf.transform.rotation = Quaternion.Euler(20f, index * 72f, 0f);
            }
        }

        private static void Flag(Transform parent, Vector3 position, Materials m)
        {
            Visual("Flag_Pole", PrimitiveType.Cylinder, parent, position + Vector3.up * 2.5f, new Vector3(0.12f, 5f, 0.12f), m.Metal);
            Visual("Flag", PrimitiveType.Cube, parent, position + new Vector3(1.1f, 4f, 0f), new Vector3(2.2f, 1.4f, 0.08f), m.RoadMarking);
        }

        private static void Crane(Transform parent, Vector3 position, Materials m)
        {
            Visual("Crane_Tower", PrimitiveType.Cube, parent, position + Vector3.up * 9f, new Vector3(2f, 18f, 2f), m.Metal);
            Visual("Crane_Boom", PrimitiveType.Cube, parent, position + new Vector3(11f, 17f, 0f), new Vector3(24f, 1.2f, 1.2f), m.Metal);
            Visual("Crane_Cable", PrimitiveType.Cylinder, parent, position + new Vector3(20f, 10f, 0f), new Vector3(0.08f, 13f, 0.08f), m.Metal);
        }

        private static void TunnelLight(Transform parent, Vector3 position, Materials m)
        {
            GameObject lamp = Visual("Tunnel_Lamp", PrimitiveType.Sphere, parent, position, Vector3.one * 0.6f, m.RoadMarking);
            Light light = lamp.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.58f, 0.22f);
            light.intensity = 2.2f;
            light.range = 12f;
            light.shadows = LightShadows.None;
        }
    }
}
