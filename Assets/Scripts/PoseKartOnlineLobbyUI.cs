using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PoseKart.Multiplayer
{
    [DisallowMultipleComponent]
    public class PoseKartOnlineLobbyUI : MonoBehaviour
    {
        [Header("System")]
        [SerializeField]
        private PoseKartOnlineSession online;


        [Header("Input")]
        [SerializeField]
        private TMP_InputField playerNameInput;

        [SerializeField]
        private TMP_InputField joinCodeInput;


        [Header("Buttons")]
        [SerializeField]
        private Button createRoomButton;

        [SerializeField]
        private Button joinRoomButton;

        [SerializeField]
        private Button readyButton;

        [SerializeField]
        private Button leaveButton;


        [Header("Texts")]
        [SerializeField]
        private TMP_Text statusText;

        [SerializeField]
        private TMP_Text roomCodeText;

        [SerializeField]
        private TMP_Text playerCountText;

        [SerializeField]
        private TMP_Text playerListText;

        [SerializeField]
        private TMP_Text aiFillText;

        [SerializeField]
        private TMP_Text readyButtonText;


        private void Awake()
        {
            RegisterButtons();
        }


        private void Start()
        {
            ResolveOnlineSystem();
            Subscribe();

            Refresh();
        }


        private void OnDestroy()
        {
            Unsubscribe();
        }


        private void ResolveOnlineSystem()
        {
            if (online == null)
            {
                online =
                    PoseKartOnlineSession.Instance;
            }

            if (online == null)
            {
                online =
                    FindFirstObjectByType<
                        PoseKartOnlineSession
                    >();
            }
        }


        private void RegisterButtons()
        {
            if (createRoomButton != null)
            {
                createRoomButton.onClick
                    .RemoveListener(
                        CreateRoom
                    );

                createRoomButton.onClick
                    .AddListener(
                        CreateRoom
                    );
            }


            if (joinRoomButton != null)
            {
                joinRoomButton.onClick
                    .RemoveListener(
                        JoinRoom
                    );

                joinRoomButton.onClick
                    .AddListener(
                        JoinRoom
                    );
            }


            if (readyButton != null)
            {
                readyButton.onClick
                    .RemoveListener(
                        ToggleReady
                    );

                readyButton.onClick
                    .AddListener(
                        ToggleReady
                    );
            }


            if (leaveButton != null)
            {
                leaveButton.onClick
                    .RemoveListener(
                        LeaveRoom
                    );

                leaveButton.onClick
                    .AddListener(
                        LeaveRoom
                    );
            }
        }


        private void Subscribe()
        {
            if (online == null)
                return;

            online.SessionChanged -=
                Refresh;

            online.SessionChanged +=
                Refresh;


            online.StatusChanged -=
                SetStatus;

            online.StatusChanged +=
                SetStatus;
        }


        private void Unsubscribe()
        {
            if (online == null)
                return;

            online.SessionChanged -=
                Refresh;

            online.StatusChanged -=
                SetStatus;
        }


        // =================================================
        // BUTTON CALLBACKS
        // =================================================

        private async void CreateRoom()
        {
            if (online == null)
                return;

            SetButtonsBusy(true);


            string playerName =
                GetPlayerName();


            bool success =
                await online.CreateRoomAsync(
                    playerName
                );


            SetButtonsBusy(false);

            if (!success)
            {
                SetStatus(
                    "FAILED TO CREATE ROOM"
                );
            }

            Refresh();
        }


        private async void JoinRoom()
        {
            if (online == null)
                return;


            string code =
                joinCodeInput != null
                    ? joinCodeInput.text
                    : "";


            if (string.IsNullOrWhiteSpace(code))
            {
                SetStatus(
                    "ENTER ROOM CODE"
                );

                return;
            }


            SetButtonsBusy(true);


            bool success =
                await online.JoinRoomAsync(
                    code,
                    GetPlayerName()
                );


            SetButtonsBusy(false);


            if (!success)
            {
                SetStatus(
                    "FAILED TO JOIN"
                );
            }


            Refresh();
        }


        private async void ToggleReady()
        {
            if (online == null ||
                !online.InSession)
            {
                return;
            }


            if (readyButton != null)
                readyButton.interactable = false;


            await online.ToggleReadyAsync();


            if (readyButton != null)
                readyButton.interactable = true;


            Refresh();
        }


        private async void LeaveRoom()
        {
            if (online == null)
                return;


            SetButtonsBusy(true);

            await online.LeaveRoomAsync();

            SetButtonsBusy(false);

            Refresh();
        }


        // =================================================
        // UI
        // =================================================

        private string GetPlayerName()
        {
            if (playerNameInput == null ||
                string.IsNullOrWhiteSpace(
                    playerNameInput.text
                ))
            {
                return "Player";
            }

            return playerNameInput.text.Trim();
        }


        private void Refresh()
        {
            if (online == null)
            {
                if (statusText != null)
                    statusText.text =
                        "ONLINE SYSTEM NOT FOUND";

                return;
            }


            bool inRoom =
                online.InSession;


            if (roomCodeText != null)
            {
                roomCodeText.text =
                    inRoom
                        ? $"ROOM CODE: {online.JoinCode}"
                        : "ROOM CODE: ------";
            }


            if (playerCountText != null)
            {
                playerCountText.text =
                    inRoom
                        ? $"PLAYERS  {online.PlayerCount} / 8"
                        : "PLAYERS  0 / 8";
            }


            if (playerListText != null)
            {
                playerListText.text =
                    inRoom
                        ? online.BuildPlayerListText()
                        : "NO PLAYERS";
            }


            if (aiFillText != null)
            {
                aiFillText.text =
                    inRoom
                        ? $"AI FILL: {online.AICount}"
                        : "AI FILL: 7";
            }


            if (readyButtonText != null)
            {
                readyButtonText.text =
                    online.LocalReady
                        ? "NOT READY"
                        : "READY";
            }


            if (createRoomButton != null)
            {
                createRoomButton.interactable =
                    !inRoom;
            }


            if (joinRoomButton != null)
            {
                joinRoomButton.interactable =
                    !inRoom;
            }


            if (readyButton != null)
            {
                readyButton.gameObject
                    .SetActive(inRoom);
            }


            if (leaveButton != null)
            {
                leaveButton.gameObject
                    .SetActive(inRoom);
            }


            if (inRoom &&
                online.IsHost &&
                online.AllPlayersReady)
            {
                SetStatus(
                    $"ALL READY • " +
                    $"{online.PlayerCount} HUMAN + " +
                    $"{online.AICount} AI"
                );
            }
        }


        private void SetStatus(
            string message)
        {
            if (statusText != null)
                statusText.text = message;
        }


        private void SetButtonsBusy(
            bool busy)
        {
            if (createRoomButton != null)
                createRoomButton.interactable =
                    !busy;

            if (joinRoomButton != null)
                joinRoomButton.interactable =
                    !busy;

            if (readyButton != null)
                readyButton.interactable =
                    !busy;

            if (leaveButton != null)
                leaveButton.interactable =
                    !busy;
        }
    }
}