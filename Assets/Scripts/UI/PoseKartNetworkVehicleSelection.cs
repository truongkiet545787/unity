using PoseKart.UI;
using Unity.Netcode;
using UnityEngine;

namespace PoseKart.Multiplayer
{
    /// <summary>
    /// Đồng bộ xe và màu riêng cho mỗi human NetworkPlayer.
    /// Gắn component này vào ROOT prefab PoseKart_NetworkPlayer,
    /// cùng GameObject với NetworkObject.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PoseKartNetworkVehicleSelection : NetworkBehaviour
    {
        // -1: chưa nhận lựa chọn từ owner. Một NetworkVariable duy nhất
        // giúp model và màu không bị cập nhật lệch nhau.
        private readonly NetworkVariable<int> packedSelection = new NetworkVariable<int>(
            -1,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

        private PoseKartVehiclePaint paint;

        private void ResolvePaint()
        {
            if (paint == null)
                paint = GetComponentInChildren<PoseKartVehiclePaint>(true);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            ResolvePaint();

            if (paint == null)
            {
                Debug.LogError(
                    "[VehicleSelection] PoseKart_NetworkPlayer thiếu " +
                    "PoseKartVehiclePaint (hoặc chưa gắn vehicleModels).",
                    this
                );
            }

            packedSelection.OnValueChanged += OnSelectionChanged;
            ApplyPackedSelection(packedSelection.Value);

            // Only the owner submits THIS play session's selection.
            // Reading PlayerPrefs here can import another editor player's choice
            // when testing multiple local players with shared preference data.
            if (!IsOwner)
                return;

            PoseKartLocalVehicleSelection.Get(out int vehicle, out int color);
            Debug.Log($"[VehicleSelection] OwnerClientId={OwnerClientId} submitting LOCAL choice: vehicle={vehicle}, color={color}, fromSession={PoseKartLocalVehicleSelection.HasCurrentChoice}", this);

            // Owner hiển thị xe mình ngay, không cần chờ round-trip RPC.
            ApplySelection(vehicle, color);

            if (IsServer)
                SetSelectionOnServer(vehicle, color);
            else
                SubmitSelectionRpc(vehicle, color);
        }

        public override void OnNetworkDespawn()
        {
            packedSelection.OnValueChanged -= OnSelectionChanged;
            base.OnNetworkDespawn();
        }

        // Rpc gửi lên Server từ owner của đúng NetworkPlayer.
        [Rpc(SendTo.Server)]
        private void SubmitSelectionRpc(int vehicle, int color, RpcParams rpcParams = default)
        {
            if (!IsServer || rpcParams.Receive.SenderClientId != OwnerClientId)
                return;

            SetSelectionOnServer(vehicle, color);
        }

        private void SetSelectionOnServer(int vehicle, int color)
        {
            if (!IsServer)
                return;

            ResolvePaint();
            int maxVehicles = PoseKartVehiclePaint.VehicleNames.Length;
            if (paint != null && paint.VehicleCount > 0)
                maxVehicles = Mathf.Min(maxVehicles, paint.VehicleCount);

            int colors = PoseKartVehiclePaint.ColorNames.Length;
            int safeVehicle = Mathf.Clamp(vehicle, 0, Mathf.Max(0, maxVehicles - 1));
            int safeColor = Mathf.Clamp(color, 0, colors - 1);

            packedSelection.Value = safeVehicle * colors + safeColor;
            // Áp dụng rõ ràng trên Host/Server kể cả khi giá trị không đổi.
            ApplySelection(safeVehicle, safeColor);

            Debug.Log(
                $"[VehicleSelection] OwnerClientId={OwnerClientId} " +
                $"Vehicle={safeVehicle} Color={safeColor}",
                this
            );
        }

        private void OnSelectionChanged(int previous, int current)
        {
            ApplyPackedSelection(current);
        }

        private void ApplyPackedSelection(int packed)
        {
            if (packed < 0)
                return;

            int colorCount = PoseKartVehiclePaint.ColorNames.Length;
            int vehicle = packed / colorCount;
            int color = packed % colorCount;
            ApplySelection(vehicle, color);
        }

        private void ApplySelection(int vehicle, int color)
        {
            ResolvePaint();
            if (paint != null)
                paint.Apply(vehicle, color);
        }
    }
}
