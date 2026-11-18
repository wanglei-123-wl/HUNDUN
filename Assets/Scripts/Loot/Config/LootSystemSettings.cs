using UnityEngine;

[CreateAssetMenu(fileName = "LootSystemSettings", menuName = "Game/Loot/System Settings")]
public sealed class LootSystemSettings : ScriptableObject
{
    [Min(0f)] public float PickupHorizontalDistance = 2.8f;
    [Min(0f)] public float PickupVerticalDistance = 2.8f;
    [Min(0f)] public float PickupRequestTimeout = 5f;
    [Min(0.05f)] public float OwnershipRefreshInterval = 0.25f;
    public GameObject DefaultLootPrefab;
    public string UnknownItemName = "未知物品";
}
