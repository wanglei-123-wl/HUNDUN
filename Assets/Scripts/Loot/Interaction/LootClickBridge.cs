using System;
using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class LootClickBridge : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LootModuleRuntime moduleRuntime;
    [SerializeField] private Camera inputCamera;

    [Header("Pointer Raycast")]
    [SerializeField] private LayerMask lootLayers = ~0;
    [SerializeField, Min(0.1f)] private float maxRaycastDistance = 100f;
    [SerializeField, Range(0, 2)] private int mouseButton = 2;
    [SerializeField] private bool ignorePointerOverUi = true;

    public event Action<WorldLootView, LootInteractionResult> InteractionHandled;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Update()
    {
        if (!Input.GetMouseButtonDown(mouseButton)
            && !Input.GetMouseButtonDown(1))
        {
            return;
        }

        if (GameplayUiInputGuard.BlocksGameplayInput)
        {
            return;
        }

        if (ignorePointerOverUi
            && EventSystem.current != null
            && EventSystem.current.IsPointerOverGameObject())
        {
            return;
        }

        TryHandlePointerClick();
    }

    public LootInteractionResult TryHandlePointerClick()
    {
        ResolveReferences();
        Transform playerTransform = LocalPlayerProvider.Instance == null
            ? null
            : LocalPlayerProvider.Instance.PlayerTransform;
        if (moduleRuntime == null
            || playerTransform == null
            || !TryGetLootUnderPointer(out WorldLootView view))
        {
            return LootInteractionResult.InvalidTarget;
        }

        LootInteractionResult result = moduleRuntime.TryHandleClick(
            view,
            playerTransform.position);
        InteractionHandled?.Invoke(view, result);
        ShowInteractionFeedback(result);
        return result;
    }

    public bool TryGetLootUnderPointer(out WorldLootView view)
    {
        view = null;
        ResolveCamera();
        if (inputCamera == null)
        {
            return false;
        }

        Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            Mathf.Max(0.1f, maxRaycastDistance),
            lootLayers,
            QueryTriggerInteraction.Collide);
        Array.Sort(hits, CompareHitDistance);

        for (int index = 0; index < hits.Length; index++)
        {
            WorldLootView candidate =
                hits[index].collider.GetComponentInParent<WorldLootView>();
            if (candidate != null
                && candidate.isActiveAndEnabled
                && !string.IsNullOrWhiteSpace(candidate.LootId))
            {
                view = candidate;
                return true;
            }
        }

        return false;
    }

    private void ResolveReferences()
    {
        if (moduleRuntime == null)
        {
            moduleRuntime = FindObjectOfType<LootModuleRuntime>();
        }

        ResolveCamera();
    }

    private void ResolveCamera()
    {
        if (inputCamera != null)
        {
            return;
        }

        inputCamera = Camera.main;
        if (inputCamera != null)
        {
            return;
        }

        ThirdPersonCameraFollow cameraFollow =
            FindObjectOfType<ThirdPersonCameraFollow>();
        if (cameraFollow != null)
        {
            inputCamera = cameraFollow.GetComponent<Camera>();
        }
    }

    private void ShowInteractionFeedback(LootInteractionResult result)
    {
        LootFeedbackPresenter presenter = moduleRuntime == null
            ? null
            : moduleRuntime.FeedbackPresenter;
        if (presenter == null)
        {
            return;
        }

        switch (result)
        {
            case LootInteractionResult.ProtectedByOther:
                presenter.ShowError("该掉落仍受归属保护");
                break;
            case LootInteractionResult.MoveCloserRequested:
                presenter.ShowError("距离太远，请靠近后拾取");
                break;
            case LootInteractionResult.LootNotFound:
                presenter.ShowError("该掉落已不存在");
                break;
            case LootInteractionResult.WrongScene:
                presenter.ShowError("该掉落不在当前场景");
                break;
            case LootInteractionResult.PickupRejected:
                presenter.ShowError("暂时无法拾取该物品");
                break;
        }
    }

    private static int CompareHitDistance(RaycastHit left, RaycastHit right)
    {
        return left.distance.CompareTo(right.distance);
    }
}
