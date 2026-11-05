using PoseKart.Race;
using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.UI
{
    /// <summary>Finds the human vehicle controlled by this process.</summary>
    public static class PoseKartLocalPlayerResolver
    {
        public static bool IsLocalHuman(CarController car)
        {
            if (car == null || !car.isActiveAndEnabled)
                return false;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                NetworkObject networkObject = car.GetComponentInParent<NetworkObject>();
                return networkObject != null && networkObject.IsSpawned &&
                       networkObject.IsPlayerObject && networkObject.IsOwner;
            }

            RaceParticipant participant = car.GetComponent<RaceParticipant>();
            return participant != null && participant.IsPlayer;
        }

        public static CarController FindLocalCar(RaceManager raceManager = null)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager != null && manager.IsListening)
            {
                NetworkObject playerObject = manager.LocalClient?.PlayerObject;
                if (playerObject != null && playerObject.IsSpawned)
                {
                    CarController ownedCar = playerObject.GetComponentInChildren<CarController>(true);
                    // LocalClient.PlayerObject is already NGO's authoritative
                    // local human object. Prefer its CarController directly;
                    // repeating the IsPlayerObject check here can reject a
                    // valid car during the short spawn/ownership setup window
                    // even though the Server has already accepted pickups.
                    if (ownedCar != null && ownedCar.isActiveAndEnabled)
                        return ownedCar;
                }

                CarController[] cars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
                for (int i = 0; i < cars.Length; i++)
                {
                    if (IsLocalHuman(cars[i]))
                        return cars[i];
                }
                return null;
            }

            if (raceManager != null && IsLocalHuman(raceManager.PlayerController))
                return raceManager.PlayerController;

            CarController[] offlineCars = Object.FindObjectsByType<CarController>(FindObjectsSortMode.None);
            for (int i = 0; i < offlineCars.Length; i++)
            {
                if (IsLocalHuman(offlineCars[i]))
                    return offlineCars[i];
            }
            return null;
        }
    }
}
