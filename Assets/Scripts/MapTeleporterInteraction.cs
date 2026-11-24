using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class MapTeleporterInteraction : MonoBehaviour
{
    private const float MaxRaycastDistance = 100f;

    private GameSession session;
    private VillageTeleportPanel panel;
    private LootClickBridge lootClickBridge;
    private VillageTeleporter pendingTeleporter;

    internal void Initialize(GameSession owner)
    {
        session = owner;
        pendingTeleporter = null;
    }

    private void Update()
    {
        bool pressed = Input.GetMouseButtonDown(1);
        if (pendingTeleporter == null && !pressed)
            return;

        if (session == null || !session.IsWorldReady || GameplayUiInputGuard.BlocksWorldPointerInput
            || Input.GetMouseButton(0) || (panel != null && panel.IsOpen))
        {
            pendingTeleporter = null;
            return;
        }

        Camera camera = Camera.main;
        if (pressed)
            pendingTeleporter = TryGetPointer(out Vector2 position)
                ? FindTeleporterUnderPointer(camera, position) : null;
        if (pendingTeleporter == null)
            return;

        if (camera != null && camera.TryGetComponent(out ThirdPersonCameraFollow cameraFollow))
        {
            cameraFollow.SampleInput();
            if (cameraFollow.IsRightMouseDragging)
            {
                pendingTeleporter = null;
                return;
            }
        }

        if (!Input.GetMouseButtonUp(1))
            return;

        VillageTeleporter target = pendingTeleporter;
        pendingTeleporter = null;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;
        if (FindTeleporterUnderPointer(camera, Input.mousePosition) != target)
            return;

        if (panel == null)
            panel = FindObjectOfType<VillageTeleportPanel>(true);
        if (panel != null)
            panel.Show();
    }

    internal static VillageTeleporter FindTeleporterUnderPointer(Camera camera, Vector2 position)
    {
        if (camera == null)
            return null;

        return FindTeleporter(camera.ScreenPointToRay(position), SceneManager.GetActiveScene());
    }

    internal static VillageTeleporter FindTeleporter(Ray ray, Scene map)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, MaxRaycastDistance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            var teleporter = hit.collider.GetComponentInParent<VillageTeleporter>();
            if (teleporter != null && teleporter.isActiveAndEnabled && teleporter.gameObject.scene == map)
                return teleporter;
            if (!hit.collider.isTrigger)
                break;
        }

        return null;
    }

    private bool TryGetPointer(out Vector2 position)
    {
        position = default;
        if (!Input.GetMouseButtonDown(1)
            || (EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject()))
            return false;
        if (lootClickBridge == null)
            lootClickBridge = FindObjectOfType<LootClickBridge>();
        if (lootClickBridge != null && lootClickBridge.isActiveAndEnabled
            && lootClickBridge.TryGetLootUnderPointer(out _))
            return false;
        position = Input.mousePosition;
        return true;
    }

    private void OnDisable() => pendingTeleporter = null;
}
