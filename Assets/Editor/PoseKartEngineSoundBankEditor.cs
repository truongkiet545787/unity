using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using PoseKart.Audio;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PoseKartEngineSoundBank))]
public sealed class PoseKartEngineSoundBankEditor : Editor
{
    private static readonly Regex RpmPattern = new Regex(@"(?<!\d)(\d{3,5})\s*RPM(?!\d)", RegexOptions.IgnoreCase);
    private SerializedProperty rpmStep;
    private SerializedProperty minimumSampleRpm;
    private SerializedProperty maximumSampleRpm;
    private SerializedProperty samples;

    private void OnEnable()
    {
        rpmStep = serializedObject.FindProperty("rpmStep");
        minimumSampleRpm = serializedObject.FindProperty("minimumSampleRpm");
        maximumSampleRpm = serializedObject.FindProperty("maximumSampleRpm");
        samples = serializedObject.FindProperty("samples");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(rpmStep);
        EditorGUILayout.PropertyField(minimumSampleRpm);
        EditorGUILayout.PropertyField(maximumSampleRpm);
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Engine RPM Sound Bank", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Exactly 16 slots are kept. Each slot can use a separate loopable AudioClip.", MessageType.Info);

        if (samples.arraySize != PoseKartEngineSoundBank.DefaultSampleCount)
            samples.arraySize = PoseKartEngineSoundBank.DefaultSampleCount;
        for (int i = 0; i < samples.arraySize; i++)
        {
            SerializedProperty element = samples.GetArrayElementAtIndex(i);
            SerializedProperty rpm = element.FindPropertyRelative("rpm");
            SerializedProperty clip = element.FindPropertyRelative("clip");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField($"Element {i}", GUILayout.Width(72f));
                EditorGUILayout.PropertyField(rpm, new GUIContent("RPM"), GUILayout.Width(116f));
                EditorGUILayout.PropertyField(clip, new GUIContent("AudioClip"));
            }
        }

        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.Space(8f);
        if (GUILayout.Button("Auto-Assign RPM Clips From Folder")) AutoAssignFromFolder();
        if (GUILayout.Button("Diagnose Loop Boundaries")) DiagnoseLoopBoundaries();
        if (GUILayout.Button("Clear All RPM Clips") && EditorUtility.DisplayDialog(
                "Clear RPM bank", "Remove every AudioClip assignment from this bank?", "Clear", "Cancel"))
        {
            serializedObject.Update();
            for (int i = 0; i < samples.arraySize; i++)
                samples.GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue = null;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }

    private void AutoAssignFromFolder()
    {
        string selected = EditorUtility.OpenFolderPanel("Select folder containing RPM AudioClips", Application.dataPath, "");
        if (string.IsNullOrEmpty(selected)) return;
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string normalizedRoot = projectRoot.Replace('\\', '/').TrimEnd('/');
        string normalizedSelected = Path.GetFullPath(selected).Replace('\\', '/').TrimEnd('/');
        if (!normalizedSelected.StartsWith(normalizedRoot + "/", StringComparison.OrdinalIgnoreCase))
        {
            EditorUtility.DisplayDialog("Folder outside project", "Choose a folder inside this Unity project.", "OK");
            return;
        }

        string relativeFolder = normalizedSelected.Substring(normalizedRoot.Length + 1);
        Dictionary<int, List<AudioClip>> matches = new Dictionary<int, List<AudioClip>>();
        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { relativeFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            Match match = RpmPattern.Match(Path.GetFileNameWithoutExtension(assetPath));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, out int rpmValue)) continue;
            if (rpmValue < 1000 || rpmValue > 8500 || (rpmValue - 1000) % 500 != 0) continue;
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
            if (clip == null) continue;
            if (!matches.TryGetValue(rpmValue, out List<AudioClip> list))
            {
                list = new List<AudioClip>();
                matches.Add(rpmValue, list);
            }
            list.Add(clip);
        }

        if (matches.Count == 0)
        {
            EditorUtility.DisplayDialog("No RPM clips found", "No files matching 1000RPM through 8500RPM were found.", "OK");
            return;
        }
        List<int> ambiguous = new List<int>();
        foreach (KeyValuePair<int, List<AudioClip>> pair in matches)
            if (pair.Value.Count > 1) ambiguous.Add(pair.Key);
        if (ambiguous.Count > 0)
        {
            ambiguous.Sort();
            Debug.LogWarning($"PoseKart RPM bank: ambiguous clips for {string.Join(", ", ambiguous)} RPM. Those slots were skipped.", target);
        }

        serializedObject.Update();
        bool hasExisting = false;
        for (int i = 0; i < samples.arraySize; i++)
            hasExisting |= samples.GetArrayElementAtIndex(i).FindPropertyRelative("clip").objectReferenceValue != null;
        bool overwrite = !hasExisting || EditorUtility.DisplayDialog(
            "RPM bank assignments exist", "Overwrite existing matching RPM assignments?", "Overwrite", "Fill empty only");

        int assigned = 0;
        for (int i = 0; i < samples.arraySize; i++)
        {
            SerializedProperty element = samples.GetArrayElementAtIndex(i);
            int rpmValue = element.FindPropertyRelative("rpm").intValue;
            if (!matches.TryGetValue(rpmValue, out List<AudioClip> list) || list.Count != 1) continue;
            SerializedProperty clip = element.FindPropertyRelative("clip");
            if (clip.objectReferenceValue == null || overwrite)
            {
                clip.objectReferenceValue = list[0];
                assigned++;
            }
        }
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
        AssetDatabase.SaveAssets();
        Debug.Log($"PoseKart RPM bank: assigned {assigned} clip(s) from {relativeFolder}.", target);
    }

    private void DiagnoseLoopBoundaries()
    {
        PoseKartEngineSoundBank bank = (PoseKartEngineSoundBank)target;
        HashSet<AudioClip> inspected = new HashSet<AudioClip>();
        int warnings = 0;
        for (int i = 0; i < bank.SampleCount; i++)
        {
            PoseKartEngineRpmSample sample = bank.GetSample(i);
            if (sample == null || sample.clip == null || !inspected.Add(sample.clip)) continue;
            if (sample.clip.samples < 2 || sample.clip.channels < 1)
            {
                Debug.LogWarning($"PoseKart loop diagnostic: {sample.clip.name} is too short to inspect.", sample.clip);
                warnings++;
                continue;
            }

            const int probeFrames = 256;
            int frames = Mathf.Min(probeFrames, sample.clip.samples);
            int channels = Mathf.Max(1, sample.clip.channels);
            float[] first = new float[frames * channels];
            float[] last = new float[frames * channels];
            bool firstRead = sample.clip.GetData(first, 0);
            bool lastRead = sample.clip.GetData(last, Mathf.Max(0, sample.clip.samples - frames));
            if (!firstRead || !lastRead)
            {
                Debug.LogWarning($"PoseKart loop diagnostic: could not read PCM data from {sample.clip.name}. "
                    + "Set the clip to PCM/Decompress On Load for inspection.", sample.clip);
                warnings++;
                continue;
            }

            float maxWrapJump = 0f;
            float sumSquared = 0f;
            int lastFrame = (frames - 1) * channels;
            for (int channel = 0; channel < channels; channel++)
            {
                float jump = Mathf.Abs(last[lastFrame + channel] - first[channel]);
                maxWrapJump = Mathf.Max(maxWrapJump, jump);
                sumSquared += jump * jump;
            }
            float rmsWrapJump = Mathf.Sqrt(sumSquared / channels);
            string severity = maxWrapJump >= 0.25f ? "WARNING" : maxWrapJump >= 0.1f ? "review" : "OK";
            if (maxWrapJump >= 0.25f) warnings++;
            Debug.Log($"PoseKart loop diagnostic [{severity}] {sample.clip.name}: "
                + $"max wrap jump={maxWrapJump:0.000}, RMS={rmsWrapJump:0.000}, "
                + $"samples={sample.clip.samples}, channels={sample.clip.channels}, rate={sample.clip.frequency}Hz.", sample.clip);
        }

        Debug.Log($"PoseKart loop diagnostic complete: inspected {inspected.Count} unique clip(s), "
            + $"{warnings} warning(s). No source files were modified.", target);
    }
}
