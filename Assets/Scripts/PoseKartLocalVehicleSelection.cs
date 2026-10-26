using UnityEngine;

namespace PoseKart.UI
{
    /// <summary>
    /// Choice belonging to THIS running game instance. Survives scene changes,
    /// unlike the scene's VehicleSelect component. PlayerPrefs remains a fallback
    /// for a newly launched game, not the live multiplayer source of truth.
    /// </summary>
    public static class PoseKartLocalVehicleSelection
    {
        public static bool HasCurrentChoice { get; private set; }
        public static int VehicleIndex { get; private set; }
        public static int ColorIndex { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewPlaySession()
        {
            HasCurrentChoice = false;
            VehicleIndex = 0;
            ColorIndex = 0;
        }

        public static void Choose(int vehicle, int color)
        {
            VehicleIndex = Mathf.Clamp(vehicle, 0, PoseKartVehiclePaint.VehicleNames.Length - 1);
            ColorIndex = Mathf.Clamp(color, 0, PoseKartVehiclePaint.ColorNames.Length - 1);
            HasCurrentChoice = true;
        }

        public static void Get(out int vehicle, out int color)
        {
            if (HasCurrentChoice)
            {
                vehicle = VehicleIndex;
                color = ColorIndex;
                return;
            }

            // Only a fallback when this run has not selected a vehicle yet.
            vehicle = Mathf.Clamp(
                PlayerPrefs.GetInt(PoseKartVehiclePaint.VehiclePreferenceKey, 0),
                0, PoseKartVehiclePaint.VehicleNames.Length - 1);
            color = Mathf.Clamp(
                PlayerPrefs.GetInt(PoseKartVehiclePaint.ColorPreferenceKey, 0),
                0, PoseKartVehiclePaint.ColorNames.Length - 1);
        }
    }
}
