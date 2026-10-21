using System.Collections.Generic;
using PoseKart.AI;
using UnityEngine;

namespace PoseKart.Race
{
    /// <summary>
    /// Runtime layout for the track Nitro route. It duplicates the existing
    /// double-Nitro pickup along the configured racing line, creating centre
    /// chains and occasional left/right choices without changing the track.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("PoseKart/Race/Nitro Chain Spawner")]
    public sealed class PoseKartNitroChainSpawner : MonoBehaviour
    {
        [Header("Source")]
        [SerializeField] private GameObject sourcePickup;
        [SerializeField] private string sourcePickupName = "Nitro Pickup 02";
        [SerializeField] private string doubleNitroToken = "double_nitro";
        [SerializeField] private bool hideOtherPickupVariants = true;

        [Header("Pickup Prefabs")]
        [Tooltip("Optional blue Nitro visual or complete pickup prefab. Blue pickups add the smaller amount below.")]
        [SerializeField] private GameObject blueNitroPrefab;
        [SerializeField, Min(0.01f)] private float blueNitroAmount = 0.35f;
        [Tooltip("Optional orange Nitro visual or complete pickup prefab. Orange pickups add the larger amount below.")]
        [SerializeField] private GameObject orangeNitroPrefab;
        [SerializeField, Min(0.01f)] private float orangeNitroAmount = 0.75f;
        [SerializeField, Min(0f)] private float pickupRespawnSeconds = 6f;
        [SerializeField, Min(1)] private int orangeEverySamples = 8;

        [Header("Route Layout")]
        [SerializeField, Min(8)] private int routeSamples = 48;
        [SerializeField, Min(0.5f)] private float laneSpacing = 3.4f;
        [SerializeField, Min(-2f)] private float heightOffset = 0.2f;
        [SerializeField, Min(0)] private int firstBranchSample = 4;
        [SerializeField, Min(1)] private int branchEverySamples = 6;
        [SerializeField, Min(1)] private int tripleLaneEverySamples = 12;
        [SerializeField] private bool spawnOnPlay = true;

        [Header("Runtime Debug")]
        [SerializeField] private int spawnedPickupCount;
        [SerializeField] private string activeSourceName = "None";

        private readonly List<GameObject> spawnedPickups = new List<GameObject>();
        private bool hasSpawned;

        private void Start()
        {
            if (spawnOnPlay)
                SpawnRoute();
        }

        public void SpawnRoute()
        {
            if (hasSpawned || !Application.isPlaying)
                return;

            RacingLine racingLine = FindFirstObjectByType<RacingLine>();
            if (racingLine == null || !racingLine.IsConfigured)
            {
                Debug.LogWarning("[PoseKartNitroChainSpawner] No configured RacingLine was found.", this);
                return;
            }

            GameObject template = ResolveSourcePickup();
            if (template == null && blueNitroPrefab == null && orangeNitroPrefab == null)
            {
                Debug.LogWarning(
                    "[PoseKartNitroChainSpawner] No Nitro source or blue/orange prefab was found. Expected a pickup named " +
                    sourcePickupName + ".", this);
                return;
            }

            Transform parent = transform;
            int sampleCount = Mathf.Clamp(routeSamples, 8, Mathf.Max(8, racingLine.Count));
            for (int sample = 0; sample < sampleCount; sample++)
            {
                int waypointIndex = Mathf.RoundToInt(sample * racingLine.Count / (float)sampleCount);
                Vector3 position = racingLine.GetPosition(waypointIndex) + Vector3.up * heightOffset;
                Quaternion rotation = racingLine.GetForwardRotation(waypointIndex);
                Vector3 lateral = rotation * Vector3.right;

                if (ShouldCreateTripleLane(sample))
                {
                    SpawnPickup(template, parent, position - lateral * laneSpacing, rotation, sample, "L", false);
                    SpawnPickup(template, parent, position, rotation, sample, "C", true);
                    SpawnPickup(template, parent, position + lateral * laneSpacing, rotation, sample, "R", false);
                }
                else if (ShouldCreateBranch(sample))
                {
                    SpawnPickup(template, parent, position - lateral * laneSpacing, rotation, sample, "L", false);
                    SpawnPickup(template, parent, position + lateral * laneSpacing, rotation, sample, "R", false);
                }
                else
                {
                    SpawnPickup(template, parent, position, rotation, sample, "C", ShouldCreateOrange(sample));
                }
            }

            hasSpawned = true;
            spawnedPickupCount = spawnedPickups.Count;
            activeSourceName = template != null
                ? template.name
                : "Blue/Orange Nitro prefabs";
        }

        private GameObject ResolveSourcePickup()
        {
            if (sourcePickup != null)
                return sourcePickup;

            PoseKartNitroPickup[] pickups = GetComponentsInChildren<PoseKartNitroPickup>(true);
            for (int i = 0; i < pickups.Length; i++)
            {
                if (pickups[i] != null && pickups[i].gameObject.name == sourcePickupName)
                {
                    sourcePickup = pickups[i].gameObject;
                    return sourcePickup;
                }
            }

            return null;
        }

        private bool ShouldCreateBranch(int sample)
        {
            if (sample < firstBranchSample || branchEverySamples <= 0)
                return false;
            return sample % branchEverySamples == 0;
        }

        private bool ShouldCreateTripleLane(int sample)
        {
            if (sample < firstBranchSample || tripleLaneEverySamples <= 0)
                return false;
            return sample % tripleLaneEverySamples == 0;
        }

        private bool ShouldCreateOrange(int sample)
        {
            return orangeNitroPrefab != null &&
                   orangeEverySamples > 0 &&
                   sample >= firstBranchSample &&
                   sample % orangeEverySamples == 0;
        }

        private void SpawnPickup(
            GameObject template,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            int sample,
            string lane,
            bool orange)
        {
            GameObject visualPrefab = orange ? orangeNitroPrefab : blueNitroPrefab;
            GameObject fallbackPrefab = visualPrefab == null && template == null
                ? (orange ? blueNitroPrefab : orangeNitroPrefab)
                : null;
            GameObject configuredPrefab = visualPrefab != null ? visualPrefab : fallbackPrefab;
            GameObject pickup = configuredPrefab != null
                ? InstantiateConfiguredPrefab(
                    configuredPrefab,
                    parent,
                    position,
                    rotation,
                    orange ? orangeNitroAmount : blueNitroAmount,
                    orange)
                : Instantiate(template, position, rotation, parent);

            if (visualPrefab == null && pickup != null)
            {
                PoseKartNitroPickup existingPickup = pickup.GetComponent<PoseKartNitroPickup>();
                if (existingPickup != null)
                    existingPickup.Configure(
                        orange ? orangeNitroAmount : blueNitroAmount,
                        pickupRespawnSeconds,
                        orange);
            }

            pickup.name = string.Format("Nitro Chain {0:00} {1}", spawnedPickups.Count + 1, lane);
            if (configuredPrefab == null)
                KeepDoubleNitroVisual(pickup);
            spawnedPickups.Add(pickup);
        }

        private GameObject InstantiateConfiguredPrefab(
            GameObject prefab,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            float nitroAmount,
            bool orange)
        {
            // The pickup may have been authored with a 90-degree correction
            // in the prefab. Keep that local orientation while still using the
            // racing-line rotation for the spawned pickup's placement.
            Quaternion authoredRootRotation = prefab.transform.localRotation;
            PoseKartNitroPickup prefabPickup = prefab.GetComponent<PoseKartNitroPickup>() ??
                                                prefab.GetComponentInChildren<PoseKartNitroPickup>(true);

            GameObject pickup;
            if (prefabPickup != null)
            {
                pickup = Instantiate(prefab, position, rotation, parent);
                Quaternion pathLocalRotation = parent != null
                    ? Quaternion.Inverse(parent.rotation) * rotation
                    : rotation;
                pickup.transform.localRotation = pathLocalRotation * authoredRootRotation;
            }
            else
            {
                pickup = new GameObject(prefab.name + " Pickup");
                pickup.transform.SetParent(parent, false);
                pickup.transform.SetPositionAndRotation(position, rotation);
                GameObject visual = Instantiate(prefab, pickup.transform);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = authoredRootRotation;
            }

            PoseKartNitroPickup pickupComponent = pickup.GetComponent<PoseKartNitroPickup>() ??
                                                   pickup.GetComponentInChildren<PoseKartNitroPickup>(true);
            if (pickupComponent == null)
                pickupComponent = pickup.AddComponent<PoseKartNitroPickup>();

            pickupComponent.Configure(nitroAmount, pickupRespawnSeconds, orange);
            return pickup;
        }

        private void KeepDoubleNitroVisual(GameObject pickup)
        {
            if (!hideOtherPickupVariants)
                return;

            string token = string.IsNullOrEmpty(doubleNitroToken)
                ? "double_nitro"
                : doubleNitroToken.ToLowerInvariant();

            Transform[] children = pickup.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                Transform child = children[i];
                if (child == pickup.transform)
                    continue;

                string childName = child.name.ToLowerInvariant();
                bool isPickupVariant = childName.Contains("gen_prop_") ||
                                       childName.Contains("nitro-bottle_") ||
                                       childName.Contains("soccer_ball");
                if (isPickupVariant)
                    child.gameObject.SetActive(childName.Contains(token));
            }
        }

        private void OnValidate()
        {
            routeSamples = Mathf.Max(8, routeSamples);
            laneSpacing = Mathf.Max(0.5f, laneSpacing);
            blueNitroAmount = Mathf.Max(0.01f, blueNitroAmount);
            orangeNitroAmount = Mathf.Max(0.01f, orangeNitroAmount);
            pickupRespawnSeconds = Mathf.Max(0f, pickupRespawnSeconds);
            orangeEverySamples = Mathf.Max(1, orangeEverySamples);
            branchEverySamples = Mathf.Max(1, branchEverySamples);
            tripleLaneEverySamples = Mathf.Max(1, tripleLaneEverySamples);
            firstBranchSample = Mathf.Max(0, firstBranchSample);
        }
    }
}
