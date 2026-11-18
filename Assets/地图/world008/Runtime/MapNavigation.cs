using UnityEngine;
using UnityEngine.AI;

namespace World008 {
    [DisallowMultipleComponent, ExecuteAlways]
    public sealed class MapNavigation : MonoBehaviour {
        [SerializeField] NavMeshData data;
        NavMeshDataInstance registration;
        Vector3 registeredPosition;
        Quaternion registeredRotation;

        public NavMeshData Data => data;

        public void Bind(NavMeshData bakedData) {
            Unregister();
            data = bakedData;
            Register();
        }

        void OnEnable() => Register();
        void OnDisable() => Unregister();

        void Update() {
            if (!SupportedScale()) {
                Unregister();
                return;
            }
            if (registration.valid && registeredPosition == transform.position && registeredRotation == transform.rotation)
                return;
            Unregister();
            Register();
        }

        bool SupportedScale() => (transform.lossyScale - Vector3.one).sqrMagnitude <= 0.000001f;

        void Register() {
            if (registration.valid || !data || !isActiveAndEnabled || !gameObject.scene.IsValid() || !SupportedScale()) return;
#if UNITY_EDITOR
            if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene)) return;
#endif
            registeredPosition = transform.position;
            registeredRotation = transform.rotation;
            registration = NavMesh.AddNavMeshData(data, registeredPosition, registeredRotation);
            if (registration.valid) registration.owner = this;
        }

        void Unregister() {
            if (registration.valid) registration.Remove();
            registration = default;
        }
    }
}
