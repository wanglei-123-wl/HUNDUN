using UnityEngine;

namespace Game.Monsters.Config
{
    [CreateAssetMenu(fileName = "新怪物配置", menuName = "游戏/怪物/怪物配置")]
    public sealed class MonsterDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject prefab;

        public string Id => id;
        public string DisplayName => displayName;
        public GameObject Prefab => prefab;
    }
}
