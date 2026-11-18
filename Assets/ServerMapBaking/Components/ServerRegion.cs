using UnityEngine;

namespace ServerMapBaking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ServerRegion : MonoBehaviour
    {
        [SerializeField] private int regionId = 1;
        [SerializeField] private ServerRegionType regionType = ServerRegionType.Custom;
        [SerializeField] private string payload = string.Empty;

        public int RegionId => regionId;
        public ServerRegionType RegionType => regionType;
        public string Payload => payload;

        private void Reset()
        {
            BoxCollider boxCollider = GetComponent<BoxCollider>();
            boxCollider.isTrigger = true;
        }
    }
}
