using Unity.Netcode;
using UnityEngine;
using PoseKart.Vehicle;

namespace PoseKart.Race
{
    /// <summary>
    /// Local-only presentation router for an authoritative Shockwave contact.
    /// It never changes simulation time, Rigidbody state, input or network state.
    /// </summary>
    public static class PoseKartShockwaveImpactPresentation
    {
        public static void PlayLocal(float strength, Vector3 impactPoint)
        {
            strength = Mathf.Clamp01(strength);
            if (strength <= 0f)
                return;

            UnityEngine.Camera localCamera = UnityEngine.Camera.main;
            if (localCamera != null)
            {
                PoseKartCinematicCamera cinematic =
                    localCamera.GetComponent<PoseKartCinematicCamera>();
                if (cinematic != null)
                {
                    cinematic.PlayShockwaveImpact(strength);
                    return;
                }
            }

            // Fallback for scenes that use the standalone camera-shake component
            // but do not have PoseKartCinematicCamera on the active camera.
            KartCollisionCameraShake[] shakers =
                Object.FindObjectsByType<KartCollisionCameraShake>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            for (int index = 0; index < shakers.Length; index++)
            {
                KartCollisionCameraShake shaker = shakers[index];
                if (shaker == null)
                    continue;

                NetworkObject networkObject =
                    shaker.GetComponentInParent<NetworkObject>();
                if (networkObject != null &&
                    (!networkObject.IsPlayerObject || !networkObject.IsOwner))
                {
                    continue;
                }

                shaker.PlayShake(strength);
                return;
            }
        }
    }
}
