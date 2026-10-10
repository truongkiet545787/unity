using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PoseKart.Editor
{
    /// <summary>Editor-only generator for a persistent collision-only road mesh.</summary>
    public sealed class SmoothRoadColliderGenerator : EditorWindow
    {
        private const string RoadName = "Continuous sand and dirt road";
        private const string PhysicsChildName = "SmoothRoadPhysics";
        private const string OriginalMeshPath = "Assets/PoseKart/ReferenceIslandArt/002_Continuous sand and dirt road.asset";
        private const string LegacyMeshPath = "Assets/PoseKart/ReferenceIslandArt/003_Continuous sand and dirt road - smoothed.asset";
        private const string PhysicsMeshPath = "Assets/PoseKart/GeneratedMeshes/Physics/SmoothMainRoadCollision.asset";
        private const string ReportPath = "Assets/PoseKart/GeneratedMeshes/Physics/SmoothMainRoadCollision_Diagnostics.txt";

        [SerializeField] private GameObject roadObject;
        [SerializeField, Range(0f, 1f)] private float smoothingStrength = .4f;
        [SerializeField, Min(1)] private int smoothingIterations = 3;
        [SerializeField, Min(0f)] private float maximumHeightDisplacement = .15f;
        [SerializeField] private float verticalOffset;
        [SerializeField] private bool preserveBoundaries = true;
        [SerializeField] private bool preserveMajorSlopes = true;
        [SerializeField] private bool previewInScene;

        private Vector3[] previewVertices;
        private string diagnosticText = "Chưa chạy chẩn đoán.";

        [MenuItem("Tools/PoseKart/Smooth Road Physics", false, 10)]
        public static void Open()
        {
            SmoothRoadColliderGenerator window = GetWindow<SmoothRoadColliderGenerator>("Smooth Road Physics");
            window.minSize = new Vector2(460f, 620f);
            window.FindRoadInWindow();
            window.Show();
        }

        [MenuItem("Tools/PoseKart/Apply Smooth Road Physics (Current Scene)", false, 11)]
        public static void ApplyCurrentScene()
        {
            GameObject road = FindRoadInScene();
            if (road == null) { Debug.LogError("PoseKart: không tìm thấy main road."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply(road, new Settings());
        }

        [MenuItem("Tools/PoseKart/Restore Original Road Collider", false, 12)]
        public static void RestoreCurrentScene()
        {
            GameObject road = FindRoadInScene();
            if (road != null) Restore(road);
        }

        private void OnEnable() { SceneView.duringSceneGui += DrawPreview; FindRoadInWindow(); }
        private void OnDisable() { SceneView.duringSceneGui -= DrawPreview; previewVertices = null; }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Giữ nguyên mesh hiển thị. Tool chỉ tạo collider vật lý riêng và chỉ thay setup sau khi bấm Apply.", MessageType.Info);
            roadObject = (GameObject)EditorGUILayout.ObjectField("Road object", roadObject, typeof(GameObject), true);
            EditorGUILayout.LabelField("Smoothing parameters", EditorStyles.boldLabel);
            smoothingStrength = EditorGUILayout.Slider("Strength", smoothingStrength, 0f, 1f);
            smoothingIterations = EditorGUILayout.IntSlider("Iterations", smoothingIterations, 1, 8);
            maximumHeightDisplacement = EditorGUILayout.FloatField("Max height displacement", maximumHeightDisplacement);
            verticalOffset = EditorGUILayout.FloatField("Vertical offset", verticalOffset);
            preserveBoundaries = EditorGUILayout.Toggle("Preserve boundaries", preserveBoundaries);
            preserveMajorSlopes = EditorGUILayout.Toggle("Preserve major slopes", preserveMajorSlopes);
            previewInScene = EditorGUILayout.Toggle("Preview in Scene", previewInScene);
            EditorGUILayout.Space(6f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find road")) FindRoadInWindow();
                if (GUILayout.Button("Run diagnostics")) RunDiagnostics();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview / dry run")) GeneratePreview();
                if (GUILayout.Button("Generate asset")) GenerateAssetOnly();
            }
            GUI.backgroundColor = new Color(.78f, .92f, .78f);
            if (GUILayout.Button("APPLY: assign SmoothRoadPhysics")) ApplyFromWindow();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Restore original visual + collider")) RestoreFromWindow();
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(diagnosticText, GUILayout.MinHeight(250f));
        }

        private void FindRoadInWindow()
        {
            if (roadObject == null || roadObject.name != RoadName) roadObject = FindRoadInScene();
            Repaint();
        }

        private Settings CurrentSettings() => new Settings
        {
            strength = smoothingStrength,
            iterations = smoothingIterations,
            maxDisplacement = Mathf.Max(0f, maximumHeightDisplacement),
            verticalOffset = verticalOffset,
            preserveBoundaries = preserveBoundaries,
            preserveMajorSlopes = preserveMajorSlopes
        };

        private void RunDiagnostics()
        {
            if (!TryGetSource(roadObject, out GameObject road, out Mesh source, out MeshFilter filter, out MeshCollider oldCollider)) return;
            roadObject = road;
            diagnosticText = BuildDiagnostics(road, source, filter, oldCollider);
            WriteReport(diagnosticText);
            Debug.Log(diagnosticText, road);
            Repaint();
        }

        private void GeneratePreview()
        {
            if (!TryGetSource(roadObject, out _, out Mesh source, out _, out _)) return;
            Mesh generated = BuildCollisionMesh(source, CurrentSettings());
            previewVertices = generated.vertices;
            diagnosticText = BuildMeshComparison(source, generated, "Preview (not assigned)");
            DestroyImmediate(generated);
            Repaint();
            SceneView.RepaintAll();
        }

        private void GenerateAssetOnly()
        {
            if (!TryGetSource(roadObject, out GameObject road, out Mesh source, out MeshFilter filter, out MeshCollider oldCollider)) return;
            Mesh generated = SaveGeneratedMesh(source, CurrentSettings());
            diagnosticText = BuildDiagnostics(road, source, filter, oldCollider) + "\n\n" + BuildMeshComparison(source, generated, "Persistent asset generated; scene unchanged");
            WriteReport(diagnosticText);
            Repaint();
        }

        private void ApplyFromWindow()
        {
            if (roadObject == null) FindRoadInWindow();
            if (roadObject == null || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Apply(roadObject, CurrentSettings());
            RunDiagnostics();
        }

        private void RestoreFromWindow()
        {
            if (roadObject == null) FindRoadInWindow();
            if (roadObject == null) return;
            Restore(roadObject);
            RunDiagnostics();
        }

        private static void Apply(GameObject road, Settings settings)
        {
            if (!TryGetSource(road, out _, out Mesh source, out MeshFilter filter, out MeshCollider oldCollider)) return;
            Mesh generated = SaveGeneratedMesh(source, settings);
            if (generated == null || generated.vertexCount < 4 || generated.triangles == null || generated.triangles.Length < 3)
            {
                Debug.LogError("PoseKart: smooth road collider asset chưa hợp lệ; giữ nguyên collider gốc để xe không rơi xuyên map.", road);
                return;
            }
            Transform childTransform = road.transform.Find(PhysicsChildName);
            GameObject child;
            if (childTransform == null)
            {
                child = new GameObject(PhysicsChildName);
                Undo.RegisterCreatedObjectUndo(child, "Create SmoothRoadPhysics");
                child.transform.SetParent(road.transform, false);
            }
            else child = childTransform.gameObject;

            Undo.RecordObject(filter, "Restore original road visual mesh");
            filter.sharedMesh = source;
            if (oldCollider != null)
            {
                Undo.RecordObject(oldCollider, "Retain original road collider");
                oldCollider.sharedMesh = source;
                oldCollider.enabled = true;
            }
            MeshCollider physicsCollider = child.GetComponent<MeshCollider>() ?? Undo.AddComponent<MeshCollider>(child);
            Undo.RecordObject(physicsCollider, "Assign smooth road physics mesh");
            physicsCollider.sharedMesh = null;
            physicsCollider.sharedMesh = generated;
            physicsCollider.convex = false;
            physicsCollider.isTrigger = false;
            physicsCollider.enabled = true;
            if (physicsCollider.sharedMesh != generated)
            {
                Debug.LogError("PoseKart: Unity không gán được smooth road collider; giữ collider gốc bật.", child);
                physicsCollider.enabled = false;
                if (oldCollider != null) oldCollider.enabled = true;
                return;
            }
            if (oldCollider != null) oldCollider.enabled = false;
            child.layer = road.layer;
            EditorUtility.SetDirty(road); EditorUtility.SetDirty(filter); EditorUtility.SetDirty(physicsCollider);
            if (oldCollider != null) EditorUtility.SetDirty(oldCollider);
            EditorSceneManager.MarkSceneDirty(road.scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(road.scene);
            Debug.Log($"PoseKart: applied {PhysicsMeshPath} to {road.name}/{PhysicsChildName}. Original visual mesh restored.", child);
        }

        private static void Restore(GameObject road)
        {
            if (!TryGetSource(road, out _, out Mesh source, out MeshFilter filter, out MeshCollider oldCollider)) return;
            Undo.RecordObject(filter, "Restore original road visual mesh"); filter.sharedMesh = source;
            if (oldCollider != null) { Undo.RecordObject(oldCollider, "Restore original road collider"); oldCollider.sharedMesh = source; oldCollider.enabled = true; }
            Transform child = road.transform.Find(PhysicsChildName);
            MeshCollider smooth = child == null ? null : child.GetComponent<MeshCollider>();
            if (smooth != null) { Undo.RecordObject(smooth, "Disable smooth road physics"); smooth.enabled = false; EditorUtility.SetDirty(smooth); }
            EditorUtility.SetDirty(filter); if (oldCollider != null) EditorUtility.SetDirty(oldCollider);
            EditorSceneManager.MarkSceneDirty(road.scene); EditorSceneManager.SaveScene(road.scene);
            Debug.Log("PoseKart: restored original visual and road collider; SmoothRoadPhysics is disabled.", road);
        }

        private static Mesh SaveGeneratedMesh(Mesh source, Settings settings)
        {
            EnsureAssetFolder("Assets/PoseKart/GeneratedMeshes/Physics");
            Mesh generated = BuildCollisionMesh(source, settings); generated.name = "SmoothMainRoadCollision";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(PhysicsMeshPath);
            if (existing == null) { AssetDatabase.CreateAsset(generated, PhysicsMeshPath); existing = generated; }
            else { EditorUtility.CopySerialized(generated, existing); EditorUtility.SetDirty(existing); DestroyImmediate(generated); }
            AssetDatabase.SaveAssets(); return existing;
        }

        private static Mesh BuildCollisionMesh(Mesh source, Settings settings)
        {
            Vector3[] original = source.vertices;
            if (original.Length < 4 || (original.Length & 1) != 0)
            {
                Debug.LogError("PoseKart: road mesh không có cấu trúc strip 2 vertex/điểm; dừng tạo collider.");
                return UnityEngine.Object.Instantiate(source);
            }
            Vector3[] result = (Vector3[])original.Clone();
            int pointCount = original.Length / 2;
            float[] heights = new float[pointCount];
            for (int i = 0; i < pointCount; i++) heights[i] = (original[i * 2].y + original[i * 2 + 1].y) * .5f;
            float[] work = (float[])heights.Clone();
            bool closed = IsClosedStrip(original);
            for (int pass = 0; pass < Mathf.Max(1, settings.iterations); pass++)
            {
                float[] next = (float[])work.Clone();
                for (int i = 0; i < pointCount; i++)
                {
                    if (settings.preserveBoundaries && !closed && (i == 0 || i == pointCount - 1)) continue;
                    int previous = i - 1; int following = i + 1;
                    if (previous < 0) previous = closed ? pointCount - 1 : 0;
                    if (following >= pointCount) following = closed ? 0 : pointCount - 1;
                    float amount = settings.strength;
                    if (settings.preserveMajorSlopes)
                    {
                        float slope = Mathf.Abs(work[following] - work[previous]);
                        float step = Mathf.Max(.001f, Vector2.Distance(new Vector2(original[previous * 2].x, original[previous * 2].z), new Vector2(original[following * 2].x, original[following * 2].z)));
                        if (slope / step > .28f) amount *= .25f;
                    }
                    next[i] = Mathf.Lerp(work[i], (work[previous] + work[following]) * .5f, amount);
                }
                work = next;
            }
            Mesh resultMesh = new Mesh { name = source.name + " - smooth collision" };
            for (int i = 0; i < pointCount; i++)
            {
                float delta = work[i] - heights[i] + settings.verticalOffset;
                if (settings.maxDisplacement > 0f) delta = Mathf.Clamp(delta, -settings.maxDisplacement, settings.maxDisplacement);
                result[i * 2].y = original[i * 2].y + delta;
                result[i * 2 + 1].y = original[i * 2 + 1].y + delta;
            }
            resultMesh.vertices = result; resultMesh.triangles = source.triangles;
            if (source.uv != null && source.uv.Length == result.Length) resultMesh.uv = source.uv;
            resultMesh.RecalculateNormals(); resultMesh.RecalculateBounds(); return resultMesh;
        }

        private static bool IsClosedStrip(Vector3[] vertices)
        {
            Vector3 first = (vertices[0] + vertices[1]) * .5f;
            int last = vertices.Length - 2;
            Vector3 final = (vertices[last] + vertices[last + 1]) * .5f;
            Vector3 next = (vertices[2] + vertices[3]) * .5f;
            return Vector3.Distance(first, final) <= Mathf.Max(.5f, Vector3.Distance(first, next) * 1.5f);
        }

        private static bool TryGetSource(GameObject candidate, out GameObject road, out Mesh source, out MeshFilter filter, out MeshCollider oldCollider)
        {
            road = candidate != null && candidate.name == RoadName ? candidate : FindRoadInScene();
            source = null; filter = road == null ? null : road.GetComponent<MeshFilter>(); oldCollider = road == null ? null : road.GetComponent<MeshCollider>();
            if (road == null || filter == null) { Debug.LogError("PoseKart: main road cần MeshFilter."); return false; }
            source = filter.sharedMesh;
            string path = source == null ? string.Empty : AssetDatabase.GetAssetPath(source);
            if (source == null || path == LegacyMeshPath || path.Contains("003_Continuous sand and dirt road - smoothed")) source = AssetDatabase.LoadAssetAtPath<Mesh>(OriginalMeshPath);
            if (source == null) { Debug.LogError("PoseKart: không tìm thấy mesh gốc của main road.", road); return false; }
            return true;
        }

        private static GameObject FindRoadInScene()
        {
            GameObject road = GameObject.Find(RoadName); if (road != null) return road;
            foreach (MeshFilter filter in UnityEngine.Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (filter.name == RoadName) return filter.gameObject;
            return null;
        }

        private static string BuildDiagnostics(GameObject road, Mesh source, MeshFilter filter, MeshCollider oldCollider)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("PoseKart Smooth Road Physics diagnostics"); report.AppendLine("Scene: " + SceneManager.GetActiveScene().path); report.AppendLine("Road: " + GetHierarchyPath(road.transform)); report.AppendLine();
            report.AppendLine("Visual MeshFilter.sharedMesh: " + DescribeMesh(filter.sharedMesh));
            report.AppendLine("Original source selected: " + DescribeMesh(source));
            report.AppendLine("Original MeshCollider: " + (oldCollider == null ? "missing" : DescribeMesh(oldCollider.sharedMesh) + $" | enabled={oldCollider.enabled}, trigger={oldCollider.isTrigger}, convex={oldCollider.convex}"));
            Transform child = road.transform.Find(PhysicsChildName); MeshCollider smooth = child == null ? null : child.GetComponent<MeshCollider>();
            report.AppendLine("SmoothRoadPhysics: " + (smooth == null ? "not present" : DescribeMesh(smooth.sharedMesh) + $" | enabled={smooth.enabled}, trigger={smooth.isTrigger}, convex={smooth.convex}")); report.AppendLine();
            AppendHeightStats(report, source); report.AppendLine(); report.AppendLine("Nearby colliders (diagnostic only; none changed automatically):");
            Bounds roadBounds = WorldBounds(road, source.bounds);
            foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!collider.enabled || !collider.bounds.Intersects(Expanded(roadBounds, 1.5f))) continue;
                MeshCollider meshCollider = collider as MeshCollider; string mesh = meshCollider == null ? collider.GetType().Name : DescribeMesh(meshCollider.sharedMesh);
                report.AppendLine($"  - {GetHierarchyPath(collider.transform)} | enabled={collider.enabled}, trigger={collider.isTrigger}, {mesh}, bounds={collider.bounds.size}");
            }
            report.AppendLine(); int vehicleCount = 0;
            foreach (PoseKart.Vehicle.CarController vehicle in UnityEngine.Object.FindObjectsByType<PoseKart.Vehicle.CarController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                vehicleCount++; Rigidbody body = vehicle.GetComponent<Rigidbody>();
                report.AppendLine($"Vehicle physics: {GetHierarchyPath(vehicle.transform)} | Rigidbody={(body == null ? "missing" : $"mass={body.mass}, collision={body.collisionDetectionMode}, interpolation={body.interpolation}")}, ground probe=Physics.RaycastNonAlloc, WheelCollider={vehicle.GetComponentInChildren<WheelCollider>() != null}");
            }
            if (vehicleCount == 0) report.AppendLine("Vehicle physics: no CarController in current scene.");
            report.AppendLine(); report.AppendLine("Diagnosis: the active scene used the same generated smoothed mesh for visual and collision. The road strip has deliberate low-poly height steps; its MeshCollider exposed those triangle transitions to the Rigidbody and ground raycasts. A fitted terrain collider overlaps the road region and is retained for scenery, not deleted automatically.");
            report.AppendLine("No vehicle, waypoint, ramp, bridge, boost or trigger component was modified by diagnostics."); return report.ToString();
        }

        private static string BuildMeshComparison(Mesh source, Mesh generated, string title)
        {
            StringBuilder report = new StringBuilder(title); report.AppendLine(); report.AppendLine("Source: " + DescribeMesh(source)); report.AppendLine("Generated: " + DescribeMesh(generated));
            report.AppendLine($"Vertex count preserved: {source.vertexCount == generated.vertexCount}; triangle count preserved: {source.triangles.Length == generated.triangles.Length}");
            report.AppendLine($"X/Z max delta: {MaxHorizontalDelta(source, generated):0.0000}; largest vertical delta: {MaxVerticalDelta(source, generated):0.0000}"); return report.ToString();
        }

        private static void AppendHeightStats(StringBuilder report, Mesh mesh)
        {
            Vector3[] v = mesh.vertices; if (v.Length < 4 || (v.Length & 1) != 0) { report.AppendLine("Height stats: unsupported non-strip topology."); return; }
            float maxStep = 0f, maxAngle = 0f, totalStep = 0f; int count = v.Length / 2;
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count; float horizontal = Vector2.Distance(new Vector2(v[i * 2].x, v[i * 2].z), new Vector2(v[next * 2].x, v[next * 2].z));
                float dy = Mathf.Abs(((v[next * 2].y + v[next * 2 + 1].y) - (v[i * 2].y + v[i * 2 + 1].y)) * .5f); float angle = Mathf.Atan2(dy, Mathf.Max(.001f, horizontal)) * Mathf.Rad2Deg;
                maxStep = Mathf.Max(maxStep, dy); maxAngle = Mathf.Max(maxAngle, angle); totalStep += dy;
            }
            report.AppendLine($"Height stats: {count} strip points, bounds={mesh.bounds.size}, average adjacent |dy|={totalStep / count:0.000}, max adjacent |dy|={maxStep:0.000}, max slope angle={maxAngle:0.0} degrees.");
        }

        private static string DescribeMesh(Mesh mesh)
        {
            if (mesh == null) return "null"; string path = AssetDatabase.GetAssetPath(mesh); string guid = string.IsNullOrEmpty(path) ? "(in-memory)" : AssetDatabase.AssetPathToGUID(path);
            return $"{mesh.name} | path={path}, guid={guid}, vertices={mesh.vertexCount}, triangles={mesh.triangles.Length / 3}, bounds={mesh.bounds.size}";
        }

        private static float MaxHorizontalDelta(Mesh a, Mesh b)
        {
            Vector3[] av = a.vertices, bv = b.vertices; float max = 0f; for (int i = 0; i < Mathf.Min(av.Length, bv.Length); i++) max = Mathf.Max(max, Vector2.Distance(new Vector2(av[i].x, av[i].z), new Vector2(bv[i].x, bv[i].z))); return max;
        }
        private static float MaxVerticalDelta(Mesh a, Mesh b)
        {
            Vector3[] av = a.vertices, bv = b.vertices; float max = 0f; for (int i = 0; i < Mathf.Min(av.Length, bv.Length); i++) max = Mathf.Max(max, Mathf.Abs(av[i].y - bv[i].y)); return max;
        }
        private static Bounds WorldBounds(GameObject go, Bounds local)
        {
            Matrix4x4 matrix = go.transform.localToWorldMatrix; Vector3[] corners = new Vector3[8]; Vector3 e = local.extents, c = local.center; int index = 0;
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2) corners[index++] = matrix.MultiplyPoint3x4(c + Vector3.Scale(e, new Vector3(x, y, z)));
            Bounds result = new Bounds(corners[0], Vector3.zero); for (int i = 1; i < corners.Length; i++) result.Encapsulate(corners[i]); return result;
        }
        private static Bounds Expanded(Bounds bounds, float amount) { bounds.Expand(amount * 2f); return bounds; }
        private static string GetHierarchyPath(Transform transform)
        {
            StringBuilder path = new StringBuilder(transform.name); while (transform.parent != null) { transform = transform.parent; path.Insert(0, transform.name + "/"); } return path.ToString();
        }
        private static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/'); string current = parts[0]; for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; }
        }
        private static void WriteReport(string contents)
        {
            EnsureAssetFolder("Assets/PoseKart/GeneratedMeshes/Physics"); File.WriteAllText(Path.Combine(Application.dataPath, "../" + ReportPath.Substring("Assets/".Length)), contents); AssetDatabase.ImportAsset(ReportPath);
        }
        private void DrawPreview(SceneView sceneView)
        {
            if (!previewInScene || previewVertices == null || previewVertices.Length < 4 || roadObject == null) return;
            Handles.color = new Color(1f, .75f, .1f, .9f); int count = previewVertices.Length / 2;
            for (int i = 0; i < count; i++) { int next = (i + 1) % count; Handles.DrawAAPolyLine(3f, roadObject.transform.TransformPoint(previewVertices[i * 2]), roadObject.transform.TransformPoint(previewVertices[next * 2])); }
        }
        private struct Settings
        {
            public float strength; public int iterations; public float maxDisplacement; public float verticalOffset; public bool preserveBoundaries; public bool preserveMajorSlopes;
        }
    }
}
