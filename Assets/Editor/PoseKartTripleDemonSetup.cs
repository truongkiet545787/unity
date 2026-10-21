#if UNITY_EDITOR
using System.Collections.Generic;
using PoseKart.AI;
using PoseKart.Race;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseKart.Editor
{
    public static class PoseKartTripleDemonSetup
    {
        private const string MenuPath = "PoseKart/AI/Apply Triple Demon Preset";

        [MenuItem(MenuPath, priority = 10)]
        public static void ApplyTripleDemonPresetToScene()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                EditorUtility.DisplayDialog(
                    "PoseKart AI",
                    "Vui lòng mở scene đua xe trước khi áp dụng Triple Demon Preset.",
                    "OK");
                return;
            }

            RaceParticipant[] allParticipants = Object.FindObjectsByType<RaceParticipant>(FindObjectsSortMode.None);
            List<AIRacerBrain> aiBrains = new List<AIRacerBrain>(3);

            foreach (var participant in allParticipants)
            {
                if (participant == null || participant.IsPlayer) continue;

                var brain = participant.GetComponent<AIRacerBrain>();
                if (brain == null)
                {
                    brain = Undo.AddComponent<AIRacerBrain>(participant.gameObject);
                }

                if (!aiBrains.Contains(brain))
                {
                    aiBrains.Add(brain);
                }
            }

            // Fallback tìm kiếm theo tên nếu chưa gán RaceParticipant
            if (aiBrains.Count < 3)
            {
                AIRacerBrain[] allBrains = Object.FindObjectsByType<AIRacerBrain>(FindObjectsSortMode.None);
                foreach (var brain in allBrains)
                {
                    if (brain == null) continue;
                    if (brain.name.IndexOf("Player", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;

                    if (!aiBrains.Contains(brain))
                    {
                        aiBrains.Add(brain);
                    }
                }
            }

            if (aiBrains.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PoseKart Triple Demon AI",
                    "Không tìm thấy xe AI nào trong Scene hiện tại!",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Apply Triple Demon Preset");
            int group = Undo.GetCurrentGroup();

            // Áp dụng bộ thông số Demon hoàn chỉnh cho cả 3 xe AI, với vi sai seed nhỏ (-0.2, 0, +0.2)
            // để chống chạy hàng một dập khuôn mà vẫn giữ nguyên 100% năng lực quái vật
            float[] microSeeds = new float[] { -0.2f, 0.0f, 0.2f };

            for (int i = 0; i < aiBrains.Count; i++)
            {
                AIRacerBrain brain = aiBrains[i];
                Undo.RecordObject(brain, "Configure Demon AI");

                float seed = i < microSeeds.Length ? microSeeds[i] : 0f;
                brain.ResolveReferences();
                brain.ApplyDemonPreset(seed);

                EditorUtility.SetDirty(brain);
                EditorUtility.SetDirty(brain.gameObject);
                Debug.Log($"<color=#00FFCC><b>[PoseKart Demon AI]</b></color> Đã áp dụng Triple Demon Preset thành công cho <b>{brain.gameObject.name}</b> (Micro-Seed: {seed:0.00}).");
            }

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            EditorUtility.DisplayDialog(
                "Triple Demon AI Overhaul Thành Công!",
                $"Đã nâng cấp {aiBrains.Count} xe AI đối thủ thành TRIPLE DEMON RACERS hoàn chỉnh:\n" +
                string.Join("\n", aiBrains.ConvertAll(b => $"• {b.gameObject.name}")) +
                "\n\nTất cả 3 xe AI giờ đây đều sở hữu đồng thời:\n" +
                "✓ Outside-Inside-Outside Apex Cornering\n" +
                "✓ 4-Layer Track Boundary Protection\n" +
                "✓ Relentless Single/Double/Triple & Perfect Nitro\n" +
                "✓ Predictive Slipstream Hunting & Slingshot Overtaking\n" +
                "✓ Tactical Combat (Shield Reflex, Apex Banana, Missile)",
                "Tuyệt vời!");
        }
    }
}
#endif
