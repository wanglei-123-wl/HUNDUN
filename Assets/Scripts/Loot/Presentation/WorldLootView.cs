using System;
using System.Collections;
using Game.Items;
using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
[AddComponentMenu("游戏/地面掉落物")]
public sealed class WorldLootView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField] private SpriteRenderer iconRenderer;
    [SerializeField] private TMP_Text displayNameText;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Renderer[] qualityRenderers;
    [SerializeField] private GameObject ownershipLockedRoot;
    [SerializeField] private GameObject pickupPendingRoot;
    [SerializeField] private Animator animator;
    [SerializeField] private string appearTrigger;
    [SerializeField] private string pickedTrigger;
    [SerializeField] private string expiredTrigger;
    [SerializeField] private string removeTrigger;
    [SerializeField, Min(0f)] private float removeDelay;

    private MaterialPropertyBlock materialPropertyBlock;
    private Coroutine removeCoroutine;

    public string LootId { get; private set; }
    public string SceneId { get; private set; }
    public string ItemId { get; private set; }
    public long CurrentRevision { get; private set; }
    public bool PickupPending { get; private set; }

    private void Awake()
    {
        ResolveReferences();
    }

    public void Bind(LootState state, ItemDefinition config)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        LootId = state.LootId;
        SceneId = state.SceneId;
        ItemId = state.ItemId;
        CurrentRevision = state.AoiRevision;
        ApplyState(state, config);
        SetPickupPending(false);
        PlayAnimatorTrigger(appearTrigger);
    }

    public void Refresh(LootState state, ItemDefinition config)
    {
        if (state == null || !string.Equals(LootId, state.LootId, StringComparison.Ordinal))
        {
            return;
        }

        if (CurrentRevision > state.AoiRevision)
        {
            return;
        }

        SceneId = state.SceneId;
        ItemId = state.ItemId;
        CurrentRevision = state.AoiRevision;
        ApplyState(state, config);
    }

    public void SetOwnershipState(LootOwnershipState state)
    {
        if (ownershipLockedRoot != null)
        {
            ownershipLockedRoot.SetActive(state == LootOwnershipState.ProtectedByOther);
        }
    }

    public void SetPickupPending(bool pending)
    {
        PickupPending = pending;

        if (pickupPendingRoot != null)
        {
            pickupPendingRoot.SetActive(pending);
        }
    }

    public void PlayRemove(LootRemoveReason reason, Action completed)
    {
        if (removeCoroutine != null)
        {
            StopCoroutine(removeCoroutine);
            removeCoroutine = null;
        }

        SetPickupPending(false);
        PlayAnimatorTrigger(GetRemoveTrigger(reason));

        if (!isActiveAndEnabled || removeDelay <= 0f)
        {
            completed?.Invoke();
            return;
        }

        removeCoroutine = StartCoroutine(CompleteRemoveAfterDelay(completed));
    }

    public void ResetView()
    {
        if (removeCoroutine != null)
        {
            StopCoroutine(removeCoroutine);
            removeCoroutine = null;
        }

        LootId = null;
        SceneId = null;
        ItemId = null;
        CurrentRevision = 0;
        SetPickupPending(false);
        SetOwnershipState(LootOwnershipState.Free);

        if (iconRenderer != null)
        {
            iconRenderer.sprite = null;
        }

        if (displayNameText != null)
        {
            displayNameText.text = string.Empty;
        }

        if (countText != null)
        {
            countText.text = string.Empty;
        }

        ApplyQualityColor(Color.white);
    }

    private void ApplyState(LootState state, ItemDefinition config)
    {
        transform.position = state.Position;

        if (iconRenderer != null)
        {
            iconRenderer.sprite = config == null ? null : config.Icon;
        }

        if (displayNameText != null)
        {
            displayNameText.text = config != null
                && !string.IsNullOrWhiteSpace(config.DisplayName)
                    ? config.DisplayName
                    : state.ItemId;
        }

        if (countText != null)
        {
            countText.text = state.Count > 1
                ? state.Count.ToString()
                : string.Empty;
        }

        ApplyQualityColor(config == null ? Color.white : config.QualityColor);
    }

    private void ApplyQualityColor(Color color)
    {
        ResolveReferences();
        materialPropertyBlock ??= new MaterialPropertyBlock();

        for (int index = 0; index < qualityRenderers.Length; index++)
        {
            Renderer targetRenderer = qualityRenderers[index];
            if (targetRenderer == null)
            {
                continue;
            }

            targetRenderer.GetPropertyBlock(materialPropertyBlock);
            materialPropertyBlock.SetColor(BaseColorId, color);
            materialPropertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(materialPropertyBlock);
        }
    }

    private IEnumerator CompleteRemoveAfterDelay(Action completed)
    {
        yield return new WaitForSeconds(removeDelay);
        removeCoroutine = null;
        completed?.Invoke();
    }

    private string GetRemoveTrigger(LootRemoveReason reason)
    {
        switch (reason)
        {
            case LootRemoveReason.Picked:
                return pickedTrigger;
            case LootRemoveReason.Expired:
                return expiredTrigger;
            default:
                return removeTrigger;
        }
    }

    private void PlayAnimatorTrigger(string triggerName)
    {
        ResolveReferences();

        if (animator == null || string.IsNullOrWhiteSpace(triggerName))
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int index = 0; index < parameters.Length; index++)
        {
            if (parameters[index].type == AnimatorControllerParameterType.Trigger
                && string.Equals(parameters[index].name, triggerName, StringComparison.Ordinal))
            {
                animator.SetTrigger(triggerName);
                return;
            }
        }
    }

    private void ResolveReferences()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        if (qualityRenderers == null || qualityRenderers.Length == 0)
        {
            qualityRenderers = GetComponentsInChildren<Renderer>(true);
        }
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(WorldLootView))]
[CanEditMultipleObjects]
internal sealed class WorldLootViewInspector : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            DrawField("m_Script", "脚本");
        DrawField("iconRenderer", "图标渲染器");
        DrawField("displayNameText", "物品名称文本");
        DrawField("countText", "物品数量文本");
        DrawField("qualityRenderers", "品质着色渲染器");
        DrawField("ownershipLockedRoot", "归属锁定提示");
        DrawField("pickupPendingRoot", "拾取等待提示");
        DrawField("animator", "动画组件");
        DrawField("appearTrigger", "出现动画触发器");
        DrawField("pickedTrigger", "拾取动画触发器");
        DrawField("expiredTrigger", "过期动画触发器");
        DrawField("removeTrigger", "移除动画触发器");
        DrawField("removeDelay", "移除延迟（秒）");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawField(string propertyName, string label)
    {
        EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName),
            new GUIContent(label), true);
    }
}
#endif
