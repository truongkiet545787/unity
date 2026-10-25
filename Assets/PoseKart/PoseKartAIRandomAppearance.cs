using PoseKart.Race;
using PoseKart.UI;
using UnityEngine;

namespace PoseKart.AI
{
    [DisallowMultipleComponent]
    public sealed class PoseKartAIRandomAppearance : MonoBehaviour
    {
        [SerializeField] private PoseKartVehiclePaint vehiclePaint;

        private void Awake()
        {
            RaceParticipant participant = GetComponent<RaceParticipant>();

            // Không random Player
            if (participant != null && participant.IsPlayer)
                return;

            if (vehiclePaint == null)
            {
                vehiclePaint =
                    GetComponentInChildren<PoseKartVehiclePaint>(true);
            }

            if (vehiclePaint == null)
                return;

            int vehicleCount = vehiclePaint.VehicleCount;
            int colorCount = vehiclePaint.ColorCount;

            if (vehicleCount <= 0 || colorCount <= 0)
                return;

            int randomVehicle =
                Random.Range(0, vehicleCount);

            int randomColor =
                Random.Range(0, colorCount);

            vehiclePaint.Apply(
                randomVehicle,
                randomColor
            );
        }
    }
}