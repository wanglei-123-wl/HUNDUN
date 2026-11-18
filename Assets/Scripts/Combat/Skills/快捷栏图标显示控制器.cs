using System.Collections.Generic;
using Game.Inventory;
using Game.Inventory.Integration.V2;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 管理快捷栏图标的显示状态。
/// 空图标只隐藏 Image 的视觉内容，不禁用 Image，保证仍可接收拖放。
/// </summary>
[DisallowMultipleComponent]
public sealed class 快捷栏图标显示控制器 :
    MonoBehaviour, IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler,
    IPointerClickHandler
{
    private sealed class 图标状态
    {
        public Image 命中区域;
        public Image 图标;
        public Color 原始颜色;
        public bool 独立图标;
        public int 槽位;
        public TMP_Text 数量;
    }

    [SerializeField, Range(1, QuickbarProtocol.SlotCount)]
    private int displayedSlotCount = QuickbarProtocol.SlotCount;

    private readonly List<图标状态> 图标列表 =
        new List<图标状态>();
    private InventoryModuleRuntime 背包运行时;
    private QuickbarSession 快捷栏会话;
    private 图标状态 拖拽源;
    private RectTransform 拖拽预览;
    private Canvas 拖拽画布;

    private void Awake()
    {
        收集图标();
        隐藏空图标();
    }

    private void OnEnable()
    {
        绑定会话();
    }

    private void Update()
    {
        绑定会话();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        图标状态 图标 = 查找鼠标下的图标(
            eventData.position, eventData.pressEventCamera);
        if (快捷栏会话 != null && 快捷栏会话.IsReady
            && 图标?.图标 != null && 图标.图标.sprite != null)
            背包运行时?.TryUseQuickbarSlot(图标.槽位);
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData == null || eventData.pointerDrag == null)
        {
            return;
        }

        图标状态 目标图标 = 查找鼠标下的图标(
            eventData.position, eventData.enterEventCamera);
        if (目标图标 == null || 快捷栏会话 == null || !快捷栏会话.IsReady)
        {
            return;
        }

        if (eventData.pointerDrag == gameObject && 拖拽源 != null)
        {
            if (拖拽源 != 目标图标)
                快捷栏会话.TryMove(拖拽源.槽位, 目标图标.槽位);
            return;
        }

        InventoryBagDragSlot source =
            eventData.pointerDrag.GetComponent<InventoryBagDragSlot>();
        InventoryOwnershipSnapshot snapshot = 背包运行时?.CurrentSnapshot;
        if (source == null || snapshot == null
            || string.IsNullOrEmpty(source.DragIdentity))
            return;

        foreach (InventoryStackState stack in snapshot.Stacks)
        {
            if (stack.Identity == source.DragIdentity)
            {
                快捷栏会话.TryAssign(目标图标.槽位, stack.StackId);
                return;
            }
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData == null
            || eventData.button != PointerEventData.InputButton.Left)
        {
            return;
        }

        图标状态 来源 = 查找鼠标下的图标(
            eventData.pressPosition, eventData.pressEventCamera);
        if (快捷栏会话 == null || !快捷栏会话.IsReady
            || 来源 == null || 来源.图标 == null
            || 来源.图标.sprite == null || 来源.图标.color.a <= 0f)
        {
            return;
        }

        if (!InventoryDragPreview.TryCreate(
            this, "快捷栏拖拽图标", out 拖拽预览, out 拖拽画布))
        {
            return;
        }

        拖拽源 = 来源;
        拖拽预览.sizeDelta = 来源.图标.rectTransform.rect.size;
        Image 预览图片 = 拖拽预览.GetComponent<Image>();
        预览图片.sprite = 来源.图标.sprite;
        预览图片.color = new Color(1f, 1f, 1f, 0.8f);
        OnDrag(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (拖拽源 != null)
        {
            InventoryDragPreview.Move(拖拽预览, 拖拽画布, eventData);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (拖拽源 != null && eventData != null)
        {
            InventoryPanelView panel = InventoryPanelView.ActivePanel;
            GameObject hit = eventData.pointerCurrentRaycast.gameObject;
            if (panel != null && hit != null
                && hit.transform.IsChildOf(panel.transform))
                快捷栏会话?.TryClear(拖拽源.槽位);
        }

        清理拖拽();
    }

    private void OnDisable()
    {
        清理拖拽();
        解除会话();
    }

    private void 绑定会话()
    {
        if (背包运行时 == null)
            背包运行时 = FindObjectOfType<InventoryModuleRuntime>();

        QuickbarSession next = 背包运行时 != null ? 背包运行时.Quickbar : null;
        if (快捷栏会话 == next) return;

        解除会话();
        快捷栏会话 = next;
        if (快捷栏会话 != null)
            快捷栏会话.StateChanged += 刷新图标;
        刷新图标();
    }

    private void 解除会话()
    {
        if (快捷栏会话 != null)
            快捷栏会话.StateChanged -= 刷新图标;
        快捷栏会话 = null;
    }

    private void 刷新图标()
    {
        for (int index = 0; index < 图标列表.Count; index++)
        {
            图标状态 状态 = 图标列表[index];
            if (状态?.图标 == null) continue;

            QuickbarSlotDto slot = 快捷栏会话?.GetSlot(状态.槽位);
            Sprite icon = null;
            if (slot != null && slot.stackId > 0 && 背包运行时?.ItemDefinitions != null
                && 背包运行时.ItemDefinitions.TryGet(slot.itemId,
                    out ItemDefinition definition))
                icon = definition?.Icon;

            状态.图标.sprite = icon;
            设置图标可见性(状态, icon != null);
            if (状态.数量 != null)
            {
                状态.数量.text = slot != null && slot.count > 1
                    ? slot.count.ToString() : string.Empty;
                状态.数量.gameObject.SetActive(icon != null && slot.count > 1);
            }
        }
    }

    private void 收集图标()
    {
        图标列表.Clear();

        for (int index = 0; index < transform.childCount; index++)
        {
            Transform child = transform.GetChild(index);
            Image 命中区域 = child.GetComponent<Image>();
            if (命中区域 == null)
            {
                continue;
            }

            Image 图标 = child.Find("HUD格子按钮")?.GetComponent<Image>();
            if (图标 == null)
                图标 = 命中区域;
            bool 独立图标 = 图标 != 命中区域;
            命中区域.raycastTarget = true;
            if (独立图标)
                图标.raycastTarget = false;
            图标列表.Add(new 图标状态
            {
                命中区域 = 命中区域,
                图标 = 图标,
                原始颜色 = 图标.color,
                独立图标 = 独立图标,
                槽位 = 图标列表.Count + 1,
                数量 = 创建数量标签(child)
            });
        }

        if (图标列表.Count != displayedSlotCount)
            Debug.LogError("[Quickbar] Unexpected visible slot count: "
                + 图标列表.Count + " (expected " + displayedSlotCount + ").", this);
    }

    private static TMP_Text 创建数量标签(Transform parent)
    {
        var labelObject = new GameObject(
            "数量", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)labelObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-1f, -1f);
        rect.sizeDelta = new Vector2(28f, 14f);

        TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
        label.fontSize = 10f;
        label.enableAutoSizing = true;
        label.fontSizeMin = 6f;
        label.fontSizeMax = 10f;
        label.alignment = TextAlignmentOptions.TopRight;
        label.enableWordWrapping = false;
        label.raycastTarget = false;
        label.gameObject.SetActive(false);
        return label;
    }

    private void 隐藏空图标()
    {
        for (int index = 0; index < 图标列表.Count; index++)
        {
            图标状态 状态 = 图标列表[index];
            if (状态?.图标 != null)
            {
                设置图标可见性(状态, false);
            }
        }
    }

    private 图标状态 查找鼠标下的图标(
        Vector2 position, Camera eventCamera)
    {
        for (int index = 0; index < 图标列表.Count; index++)
        {
            图标状态 状态 = 图标列表[index];
            if (状态?.命中区域 == null)
            {
                continue;
            }

            RectTransform rectTransform = 状态.命中区域.rectTransform;
            if (RectTransformUtility.RectangleContainsScreenPoint(
                rectTransform, position, eventCamera))
            {
                return 状态;
            }
        }

        return null;
    }

    private void 清理拖拽()
    {
        if (拖拽预览 != null)
        {
            Destroy(拖拽预览.gameObject);
        }

        拖拽源 = null;
        拖拽预览 = null;
        拖拽画布 = null;
    }

    private static void 设置图标可见性(
        图标状态 状态, bool visible)
    {
        if (状态 == null || 状态.图标 == null)
        {
            return;
        }

        Color color = 状态.原始颜色;
        color.a = visible
            ? (状态.独立图标 ? 1f : 状态.原始颜色.a)
            : 0f;
        状态.图标.color = color;
        状态.图标.enabled = true;
        if (状态.独立图标)
            状态.图标.gameObject.SetActive(visible);
    }
}
