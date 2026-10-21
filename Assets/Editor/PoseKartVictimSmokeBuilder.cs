using UnityEngine;
using UnityEditor;

namespace PoseKart.EditorTools
{
    public static class PoseKartVictimSmokeBuilder
    {
        [MenuItem("PoseKart/VFX/Build Victim Smoke Prefab", false, 200)]
        public static void BuildVictimSmokePrefab()
        {
            // 1. Tạo GameObject tạm
            GameObject go = new GameObject("Missile_Victim_Smoke");
            go.transform.localPosition = new Vector3(0f, 0.4f, -0.6f);

            // 2. Thêm ParticleSystem trước
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            // 3. Thêm MissileVictimSmoke controller
            go.AddComponent<PoseKart.Race.MissileVictimSmoke>();

            // 4. Cấu hình chi tiết ParticleSystem
            var main = ps.main;
            main.duration = 2f;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.0f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.20f, 0.20f, 0.22f, 0.85f),
                new Color(0.05f, 0.05f, 0.06f, 0.95f)
            );
            main.gravityModifier = -0.15f; // Khói bốc ngược lên trên tự nhiên

            var emission = ps.emission;
            emission.rateOverTime = 25f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.35f;

            // Size Over Lifetime: Khói phồng to dần khi bốc lên
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            AnimationCurve sizeCurve = new AnimationCurve();
            sizeCurve.AddKey(0f, 0.5f);
            sizeCurve.AddKey(0.4f, 0.9f);
            sizeCurve.AddKey(1f, 1.4f);
            sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Color Over Lifetime: Mờ dần ở cuối vòng đời
            var col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient grad = new Gradient();
            grad.SetKeys(
                new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) }
            );
            col.color = new ParticleSystem.MinMaxGradient(grad);

            // Renderer & Material URP
            ParticleSystemRenderer psRenderer = go.GetComponent<ParticleSystemRenderer>();
            Material smokeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PoseKart/Materials/M_Missile_Victim_Smoke.mat");
            if (smokeMat == null)
            {
                smokeMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/PoseKart/VFX/Drift/Materials/DriftSmoke.mat");
            }
            if (smokeMat != null)
            {
                psRenderer.material = smokeMat;
            }
            psRenderer.renderMode = ParticleSystemRenderMode.Billboard;
            psRenderer.sortMode = ParticleSystemSortMode.Distance;

            // 5. Lưu Prefab ở Assets/Prefabs/VFX/ và Assets/Resources/
            if (!System.IO.Directory.Exists("Assets/Prefabs/VFX"))
                System.IO.Directory.CreateDirectory("Assets/Prefabs/VFX");
            if (!System.IO.Directory.Exists("Assets/Resources"))
                System.IO.Directory.CreateDirectory("Assets/Resources");

            string vfxPath = "Assets/Prefabs/VFX/Missile_Victim_Smoke.prefab";
            string resPath = "Assets/Resources/Missile_Victim_Smoke.prefab";

            PrefabUtility.SaveAsPrefabAsset(go, vfxPath);
            PrefabUtility.SaveAsPrefabAsset(go, resPath);

            Object.DestroyImmediate(go);
            AssetDatabase.Refresh();

            Debug.Log($"<color=#4ADE80>[PoseKart]</color> Đã tạo thành công Prefab khói nạn nhân tại: {vfxPath} và {resPath}");
        }
    }
}
