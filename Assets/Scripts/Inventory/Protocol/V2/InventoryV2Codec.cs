using System;
using Game.Inventory.Model.V2;
using UnityEngine;

namespace Game.Inventory.Protocol.V2
{
    public enum InventorySnapshotProtocol
    {
        Invalid,
        Legacy,
        V2,
        Unsupported
    }

    public static class InventoryV2Codec
    {
        public static bool TryReadOperation(
            string json, out InventoryV2OperationResultDto result, out string error)
        {
            result = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Equipment operation result is empty.";
                return false;
            }
            try
            {
                result = JsonUtility.FromJson<InventoryV2OperationResultDto>(json);
                return InventoryV2OperationValidator.TryValidate(result, out error);
            }
            catch (Exception exception)
            {
                result = null;
                error = "Equipment operation JSON could not be parsed: " + exception.Message;
                return false;
            }
        }

        public static InventorySnapshotProtocol ReadSnapshot(
            string json, out InventoryOwnershipSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Inventory snapshot is empty.";
                return InventorySnapshotProtocol.Invalid;
            }

            try
            {
                InventorySnapshotEnvelope header =
                    JsonUtility.FromJson<InventorySnapshotEnvelope>(json);
                if (header == null || header.msgId != InventoryV2Protocol.SnapshotResult
                    || (!string.IsNullOrEmpty(header.type)
                        && header.type != InventoryV2Protocol.SnapshotResult))
                {
                    error = "Inventory snapshot message type is invalid.";
                    return InventorySnapshotProtocol.Invalid;
                }

                if (header.equipmentProtocolVersion == 0
                    && header.stackItems == null && header.equipmentItems == null)
                    return InventorySnapshotProtocol.Legacy;
                if (!InventoryV2Protocol.SupportsSnapshotVersion(
                    header.equipmentProtocolVersion))
                {
                    error = "Unsupported inventory equipment protocol version.";
                    return InventorySnapshotProtocol.Unsupported;
                }
                InventoryV2SnapshotResultDto message =
                    JsonUtility.FromJson<InventoryV2SnapshotResultDto>(json);
                return InventoryV2SnapshotValidator.TryCreate(message, out snapshot, out error)
                    ? InventorySnapshotProtocol.V2
                    : InventorySnapshotProtocol.Invalid;
            }
            catch (Exception exception)
            {
                error = "Inventory snapshot JSON could not be parsed: " + exception.Message;
                return InventorySnapshotProtocol.Invalid;
            }
        }
    }
}
