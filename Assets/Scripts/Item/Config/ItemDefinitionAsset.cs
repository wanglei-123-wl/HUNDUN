using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "NewItem",
        menuName = "Game/Items/Item Definition")]
    public sealed class ItemDefinitionAsset : ScriptableObject
    {
        [SerializeField] private ItemDefinition definition = new ItemDefinition();

        public ItemDefinition Definition => definition;
    }
}
