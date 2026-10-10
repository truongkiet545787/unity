using System;
using UnityEngine;

namespace PoseKart.Audio
{
    [Serializable]
    public sealed class PoseKartEngineRpmSample
    {
        [Min(1)] public int rpm;
        public AudioClip clip;
    }

    [CreateAssetMenu(fileName = "PoseKart_EngineSoundBank", menuName = "PoseKart/Audio/Engine Sound Bank")]
    public sealed class PoseKartEngineSoundBank : ScriptableObject
    {
        public const int DefaultSampleCount = 16;
        public const int DefaultMinimumRpm = 1000;
        public const int DefaultMaximumRpm = 8500;
        public const int DefaultStepRpm = 500;

        [SerializeField, Min(1)] private int rpmStep = DefaultStepRpm;
        [SerializeField, Min(1)] private int minimumSampleRpm = DefaultMinimumRpm;
        [SerializeField, Min(1)] private int maximumSampleRpm = DefaultMaximumRpm;
        [SerializeField] private PoseKartEngineRpmSample[] samples = CreateDefaultSamples();

        public int RpmStep => rpmStep;
        public int MinimumSampleRpm => minimumSampleRpm;
        public int MaximumSampleRpm => maximumSampleRpm;
        public int SampleCount => samples == null ? 0 : samples.Length;
        public int AssignedClipCount
        {
            get
            {
                int count = 0;
                if (samples == null) return count;
                for (int i = 0; i < samples.Length; i++) if (samples[i] != null && samples[i].clip != null) count++;
                return count;
            }
        }
        public bool HasAnyClip => AssignedClipCount > 0;

        public PoseKartEngineRpmSample GetSample(int index)
        {
            return samples != null && index >= 0 && index < samples.Length ? samples[index] : null;
        }

        public bool TryGetPair(float targetRpm, out AudioClip lowerClip, out int lowerRpm, out AudioClip upperClip, out int upperRpm)
        {
            lowerClip = null; upperClip = null; lowerRpm = 0; upperRpm = 0;
            if (samples == null) return false;
            float target = Mathf.Clamp(targetRpm, minimumSampleRpm, maximumSampleRpm);
            int floorRpm = int.MinValue;
            int ceilingRpm = int.MaxValue;
            for (int i = 0; i < samples.Length; i++)
            {
                PoseKartEngineRpmSample sample = samples[i];
                if (sample == null || sample.clip == null || sample.rpm <= 0) continue;
                if (sample.rpm <= target && sample.rpm > floorRpm)
                {
                    floorRpm = sample.rpm; lowerRpm = sample.rpm; lowerClip = sample.clip;
                }
                if (sample.rpm >= target && sample.rpm < ceilingRpm)
                {
                    ceilingRpm = sample.rpm; upperRpm = sample.rpm; upperClip = sample.clip;
                }
            }

            if (lowerClip == null)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    PoseKartEngineRpmSample sample = samples[i];
                    if (sample != null && sample.clip != null && (upperClip == null || sample.rpm < upperRpm))
                    { upperRpm = sample.rpm; upperClip = sample.clip; }
                }
                lowerRpm = upperRpm; lowerClip = upperClip;
            }
            if (upperClip == null)
            {
                for (int i = 0; i < samples.Length; i++)
                {
                    PoseKartEngineRpmSample sample = samples[i];
                    if (sample != null && sample.clip != null && (lowerClip == null || sample.rpm > lowerRpm))
                    { lowerRpm = sample.rpm; lowerClip = sample.clip; }
                }
                upperRpm = lowerRpm; upperClip = lowerClip;
            }
            return lowerClip != null && upperClip != null;
        }

        private void OnValidate()
        {
            rpmStep = Mathf.Max(1, rpmStep);
            minimumSampleRpm = Mathf.Max(1, minimumSampleRpm);
            maximumSampleRpm = Mathf.Max(minimumSampleRpm, maximumSampleRpm);
            EnsureDefaultSlots();
        }

        private void OnEnable()
        {
            EnsureDefaultSlots();
        }

        private void EnsureDefaultSlots()
        {
            PoseKartEngineRpmSample[] old = samples;
            if (old == null || old.Length != DefaultSampleCount)
            {
                samples = new PoseKartEngineRpmSample[DefaultSampleCount];
                if (old != null)
                {
                    int copyCount = Mathf.Min(old.Length, samples.Length);
                    for (int i = 0; i < copyCount; i++) samples[i] = old[i];
                }
            }
            for (int i = 0; i < DefaultSampleCount; i++)
            {
                if (samples[i] == null) samples[i] = new PoseKartEngineRpmSample();
                if (samples[i].rpm <= 0) samples[i].rpm = DefaultMinimumRpm + i * DefaultStepRpm;
            }
        }

        private static PoseKartEngineRpmSample[] CreateDefaultSamples()
        {
            PoseKartEngineRpmSample[] result = new PoseKartEngineRpmSample[DefaultSampleCount];
            for (int i = 0; i < result.Length; i++) result[i] = new PoseKartEngineRpmSample { rpm = DefaultMinimumRpm + i * DefaultStepRpm };
            return result;
        }
    }
}
