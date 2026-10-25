using PoseKart.AI;
using PoseKart.Vehicle;
using Unity.Netcode;
using UnityEngine;
namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PoseKartNetworkAI : NetworkBehaviour
    {
        [SerializeField]
        private CarResetController resetController;

        [SerializeField] private Rigidbody body;
        [Header("AI Components")]
        [SerializeField] private AIRacerBrain aiBrain;
        [SerializeField] private AIInputProvider aiInput;
        [SerializeField] private CarController carController;

        private void Awake()
        {
            if (resetController == null)
            {
                resetController =
                    GetComponent<CarResetController>();
            }
            if (body == null)
                body = GetComponent<Rigidbody>();
            if (aiBrain == null)
                aiBrain = GetComponent<AIRacerBrain>();

            if (aiInput == null)
                aiInput = GetComponent<AIInputProvider>();

            if (carController == null)
                carController = GetComponent<CarController>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            bool runAI = IsServer;
            if (resetController != null)
                resetController.enabled = runAI;
            if (body == null)
                body = GetComponent<Rigidbody>();

            if (aiBrain == null)
                aiBrain = GetComponent<AIRacerBrain>();

            if (aiInput == null)
                aiInput = GetComponent<AIInputProvider>();

            if (carController == null)
                carController = GetComponent<CarController>();

            if (resetController != null)
                resetController.enabled = runAI;
            // HOST chạy physics + AI.
            // CLIENT chỉ nhận transform từ Host.
            if (body != null)
                body.isKinematic = !runAI;


            if (aiBrain != null)
                aiBrain.enabled = runAI;

            if (aiInput != null)
                aiInput.enabled = runAI;

            if (carController != null)
                carController.enabled = runAI;


            if (runAI && aiBrain != null)
            {
                RacingLine line =
                    FindFirstObjectByType<RacingLine>();

                aiBrain.BindNetworkRuntime(line);
            }


            Debug.Log(
                $"[NetworkAI] {name} | " +
                $"IsServer={IsServer} | " +
                $"AI Active={runAI}",
                this
            );
        }

    }
}