using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Inventory.Protocol.V2;

namespace Game.Inventory
{
    public sealed class InventoryServerAdapter : IInventoryDataSource, IDisposable
    {
        private const float MinimumSnapshotRequestInterval = 1f;
        private const float SnapshotRequestTimeout = 5f;

        private readonly NetworkManager networkManager;
        private readonly NetMessageDispatcher dispatcher;
        private readonly Func<float> getLocalTime;

        private bool initialized;
        private bool snapshotRequestPending;
        private float snapshotRequestStartedAt = float.NegativeInfinity;
        private float lastSnapshotRequestTime = float.NegativeInfinity;

        public bool IsSnapshotRequestPending
        {
            get
            {
                if (!snapshotRequestPending)
                {
                    return false;
                }

                if (getLocalTime() - snapshotRequestStartedAt
                    <= SnapshotRequestTimeout)
                {
                    return true;
                }

                snapshotRequestPending = false;
                return false;
            }
        }

        public event Action<InventorySnapshotData> SnapshotReceived;
        public event Action<InventoryDeltaData> DeltaReceived;
        public event Action<InventoryDataSourceError> ErrorReceived;

        public InventoryServerAdapter(
            NetworkManager networkManager,
            NetMessageDispatcher dispatcher,
            Func<float> getLocalTime = null)
        {
            this.networkManager = networkManager
                ?? throw new ArgumentNullException(nameof(networkManager));
            this.dispatcher = dispatcher
                ?? throw new ArgumentNullException(nameof(dispatcher));
            this.getLocalTime = getLocalTime
                ?? (() => Time.unscaledTime);
        }

        public void Initialize(bool registerSnapshot = true)
        {
            if (initialized)
            {
                return;
            }

            if (registerSnapshot)
                dispatcher.Register(InventoryProtocolConstants.SnapshotResult, HandleSnapshotResult);
            dispatcher.Register(
                InventoryProtocolConstants.Error,
                HandleError);

            initialized = true;
        }

        public void RequestSnapshot()
        {
            EnsureInitialized();

            if (IsSnapshotRequestPending)
            {
                return;
            }

            float now = getLocalTime();
            if (now - lastSnapshotRequestTime
                < MinimumSnapshotRequestInterval)
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotRequest,
                    "client_snapshot_rate_limited",
                    "Inventory snapshot requests must be at least one second apart.",
                    0L);
                return;
            }

            if (!networkManager.IsConnected)
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotRequest,
                    "client_network_disconnected",
                    "Cannot request inventory snapshot while disconnected.",
                    0L);
                return;
            }

            InventorySnapshotRequestDto request =
                new InventorySnapshotRequestDto();
            if (!networkManager.Send(request))
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotRequest,
                    "client_snapshot_send_failed",
                    "Failed to send inventory snapshot request.",
                    0L);
                return;
            }

            lastSnapshotRequestTime = now;
            snapshotRequestStartedAt = now;
            snapshotRequestPending = true;
            Debug.Log("[InventorySnapshot][TX] protocol=legacy requestSequence=<omitted>");
        }

        public void ResetConnectionState()
        {
            snapshotRequestPending = false;
            snapshotRequestStartedAt = float.NegativeInfinity;
            lastSnapshotRequestTime = float.NegativeInfinity;
        }

        public void Dispose()
        {
            if (initialized)
            {
                dispatcher.Unregister(
                    InventoryProtocolConstants.SnapshotResult,
                    HandleSnapshotResult);
                dispatcher.Unregister(
                    InventoryProtocolConstants.Error,
                    HandleError);
                initialized = false;
            }

            SnapshotReceived = null;
            DeltaReceived = null;
            ErrorReceived = null;
            ResetConnectionState();
        }

        private void HandleSnapshotResult(string json)
        {
            snapshotRequestPending = false;

            InventorySnapshotProtocol protocol =
                InventoryV2Codec.ReadSnapshot(json, out _, out string protocolError);
            if (protocol != InventorySnapshotProtocol.Legacy)
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotRequest,
                    protocol == InventorySnapshotProtocol.V2
                        ? "client_v2_inventory_not_connected"
                        : "client_inventory_protocol_invalid",
                    protocol == InventorySnapshotProtocol.V2
                        ? "V2 inventory requires the new presentation module."
                        : protocolError,
                    0L);
                return;
            }

            if (!TryParse(
                json,
                InventoryProtocolConstants.SnapshotResult,
                out InventorySnapshotResultDto message))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(message.errorCode))
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotRequest,
                    message.errorCode,
                    message.message,
                    message.serverTime);
                return;
            }

            try
            {
                SnapshotReceived?.Invoke(ConvertSnapshot(message));
            }
            catch (Exception exception)
            {
                EmitError(
                    InventoryProtocolConstants.SnapshotResult,
                    "client_snapshot_conversion_failed",
                    exception.Message,
                    message.serverTime);
            }
        }

        public void ApplyLootPickupResult(LootPickupResultMessageDto message)
        {
            if (message == null || !message.success
                || !string.Equals(
                    message.status,
                    InventoryProtocolConstants.PickupStatuses.Succeeded,
                    StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                DeltaReceived?.Invoke(ConvertDelta(message));
            }
            catch (Exception exception)
            {
                EmitError(
                    InventoryProtocolConstants.LootPickupResult,
                    "client_delta_conversion_failed",
                    exception.Message,
                    message.serverTime);
            }
        }

        private void HandleError(string json)
        {
            if (!TryParse(
                json,
                InventoryProtocolConstants.Error,
                out InventoryServerErrorDto message)
                || !string.Equals(
                    message.requestMsgId,
                    InventoryProtocolConstants.SnapshotRequest,
                    StringComparison.Ordinal))
            {
                return;
            }

            snapshotRequestPending = false;
            EmitError(
                message.requestMsgId,
                message.errorCode,
                message.message,
                message.serverTime);
        }

        private static InventorySnapshotData ConvertSnapshot(
            InventorySnapshotResultDto message)
        {
            List<InventoryItemState> items =
                new List<InventoryItemState>();
            Dictionary<int, bool> usedSlotIndexes =
                new Dictionary<int, bool>();
            Dictionary<long, bool> usedSlotIds =
                new Dictionary<long, bool>();

            if (message.slots != null)
            {
                for (int index = 0; index < message.slots.Length; index++)
                {
                    InventoryServerSlotDto slot = message.slots[index];
                    if (slot == null)
                    {
                        continue;
                    }

                    if (usedSlotIndexes.ContainsKey(slot.slotIndex)
                        || usedSlotIds.ContainsKey(slot.slotId))
                    {
                        throw new InvalidOperationException(
                            "Inventory snapshot contains duplicate slots.");
                    }

                    InventoryItemState item = new InventoryItemState(
                        slot.slotId,
                        slot.slotIndex,
                        slot.itemId,
                        slot.count,
                        slot.isBound);
                    usedSlotIndexes.Add(slot.slotIndex, true);
                    usedSlotIds.Add(slot.slotId, true);
                    items.Add(item);
                }
            }

            Dictionary<string, long> wallets =
                new Dictionary<string, long>(StringComparer.Ordinal);
            if (message.wallets != null)
            {
                for (int index = 0; index < message.wallets.Length; index++)
                {
                    InventoryServerWalletDto wallet = message.wallets[index];
                    if (wallet == null
                        || string.IsNullOrWhiteSpace(wallet.currencyId)
                        || wallet.balance < 0L)
                    {
                        throw new InvalidOperationException(
                            "Inventory snapshot contains an invalid wallet.");
                    }

                    if (wallets.ContainsKey(wallet.currencyId))
                    {
                        throw new InvalidOperationException(
                            "Inventory snapshot contains duplicate currencies.");
                    }

                    wallets.Add(wallet.currencyId, wallet.balance);
                }
            }

            return new InventorySnapshotData(
                message.capacity,
                message.inventoryRevision,
                items,
                wallets,
                message.serverTime);
        }

        private static InventoryDeltaData ConvertDelta(
            LootPickupResultMessageDto message)
        {
            List<InventorySlotChangeData> slotChanges =
                new List<InventorySlotChangeData>();
            if (message.slotChanges != null)
            {
                for (int index = 0; index < message.slotChanges.Length; index++)
                {
                    InventorySlotChangeDto change = message.slotChanges[index];
                    if (change == null)
                    {
                        continue;
                    }

                    slotChanges.Add(
                        new InventorySlotChangeData(
                            change.slotId,
                            change.slotIndex,
                            change.itemId,
                            change.previousCount,
                            change.newCount,
                            change.isBound));
                }
            }

            List<InventoryWalletChangeData> walletChanges =
                new List<InventoryWalletChangeData>();
            if (message.walletChanges != null)
            {
                for (int index = 0; index < message.walletChanges.Length; index++)
                {
                    WalletChangeDto change = message.walletChanges[index];
                    if (change == null)
                    {
                        continue;
                    }

                    walletChanges.Add(
                        new InventoryWalletChangeData(
                            change.currencyId,
                            change.previousBalance,
                            change.newBalance));
                }
            }

            return new InventoryDeltaData(
                message.grantId,
                message.inventoryRevision,
                slotChanges,
                walletChanges,
                message.serverTime);
        }

        private bool TryParse<T>(
            string json,
            string expectedType,
            out T message)
            where T : NetMessageBase
        {
            message = null;

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                message = JsonUtility.FromJson<T>(json);
            }
            catch (Exception exception)
            {
                EmitError(
                    expectedType,
                    "client_json_parse_failed",
                    exception.Message,
                    0L);
                return false;
            }

            if (message == null
                || !string.Equals(
                    message.msgId,
                    expectedType,
                    StringComparison.Ordinal)
                || !string.Equals(
                    message.type,
                    expectedType,
                    StringComparison.Ordinal))
            {
                EmitError(
                    expectedType,
                    "client_protocol_type_mismatch",
                    "Inventory message msgId/type does not match.",
                    0L);
                return false;
            }

            return true;
        }

        private void EmitError(
            string requestType,
            string errorCode,
            string message,
            long serverTime)
        {
            ErrorReceived?.Invoke(
                new InventoryDataSourceError(
                    requestType,
                    errorCode,
                    message,
                    serverTime));
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }
    }
}
