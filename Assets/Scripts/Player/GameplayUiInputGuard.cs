using System.Collections.Generic;
using Game.CharacterPanel.Presentation;
using Game.Chat.UI;
using Game.Friend.UI;
using Game.Inventory;
using Game.Inventory.Presentation.V2;
using Game.PlayerTrade.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class GameplayUiInputGuard
{
    private static readonly CharacterPanelPointerCapture inventoryCapture =
        new CharacterPanelPointerCapture();
    private static readonly CharacterPanelPointerCapture marketCapture =
        new CharacterPanelPointerCapture();
    private static readonly CharacterPanelPointerCapture modalCapture =
        new CharacterPanelPointerCapture();
    private static readonly CharacterPanelPointerCapture uiCapture =
        new CharacterPanelPointerCapture();
    private static readonly HashSet<GameObject> modalDialogs =
        new HashSet<GameObject>();
    private static readonly List<RaycastResult> pointerHits =
        new List<RaycastResult>();
    private static InventoryPanelView sampledPanel;
    private static PlayerTradeMarketToggleController sampledMarket;
    private static EventSystem pointerEventSystem;
    private static PointerEventData pointerEventData;
    private static int sampledFrame = -1;
    private static bool sampledMarketVisible;
    private static bool sampledModalVisible;
    private static bool inventoryBlockedThisFrame;
    private static bool marketBlockedThisFrame;
    private static bool modalBlockedThisFrame;
    private static bool uiBlockedThisFrame;
    private static EventSystem sampledEventSystem;

    public static void RegisterModal(GameObject dialog)
    {
        if (dialog != null) modalDialogs.Add(dialog);
    }

    public static void UnregisterModal(GameObject dialog)
    {
        if (dialog != null) modalDialogs.Remove(dialog);
    }

    public static bool BlocksGameplayInput
    {
        get
        {
            if (WorldEntryLoadingView.ActiveWorldEntry != null)
                return true;

            bool characterBlocked = CharacterPanelInputGuard.BlocksGameplayInput;
            bool panelBlocked = SamplePanels();
            return characterBlocked || panelBlocked || IsEditingText
                || InventoryDiscardConfirmationView.BlocksGameplayInput;
        }
    }

    public static bool BlocksWindowShortcuts =>
        WorldEntryLoadingView.ActiveWorldEntry != null || IsEditingText;

    public static bool IsEditingText =>
        WorldChatPanelController.IsChatInputFocused
        || PrivateChatPanelController.IsChatInputFocused
        || FriendSearchPanelController.IsSearchInputFocused
        || IsTextInputFocused();

    private static bool IsTextInputFocused()
    {
        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == null) return false;
        TMP_InputField tmp = selected.GetComponent<TMP_InputField>();
        if (tmp != null && tmp.isActiveAndEnabled && tmp.isFocused)
            return true;
        InputField legacy = selected.GetComponent<InputField>();
        return legacy != null && legacy.isActiveAndEnabled && legacy.isFocused;
    }

    private static bool SamplePanels()
    {
        InventoryPanelView panel = InventoryPanelView.ActivePanel;
        PlayerTradeMarketToggleController market =
            PlayerTradeMarketToggleController.ActiveMarket;
        EventSystem eventSystem = EventSystem.current;
        bool marketVisible = market != null && market.isActiveAndEnabled
            && market.IsVisible;
        bool modalVisible = false;
        foreach (GameObject dialog in modalDialogs)
        {
            if (dialog != null && dialog.activeInHierarchy)
            {
                modalVisible = true;
                break;
            }
        }
        if (sampledFrame == Time.frameCount && sampledPanel == panel
            && sampledMarket == market && sampledMarketVisible == marketVisible
            && sampledModalVisible == modalVisible
            && sampledEventSystem == eventSystem)
            return inventoryBlockedThisFrame || marketBlockedThisFrame
                || modalBlockedThisFrame || uiBlockedThisFrame;

        sampledFrame = Time.frameCount;
        sampledPanel = panel;
        sampledMarket = market;
        sampledMarketVisible = marketVisible;
        sampledModalVisible = modalVisible;
        sampledEventSystem = eventSystem;
        bool inventoryOpen = panel != null && panel.isActiveAndEnabled;
        RectTransform hitArea = inventoryOpen
            ? panel.GetComponent<RectTransform>()
            : null;
        bool overInventory = hitArea != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                hitArea, Input.mousePosition);
        bool overMarket = false;
        bool overUi = false;

        pointerHits.Clear();
        if (eventSystem != null)
        {
            if (pointerEventSystem != eventSystem)
            {
                pointerEventSystem = eventSystem;
                pointerEventData = new PointerEventData(pointerEventSystem);
            }

            pointerEventData.Reset();
            pointerEventData.position = Input.mousePosition;
            pointerEventSystem.RaycastAll(pointerEventData, pointerHits);
            foreach (RaycastResult hit in pointerHits)
            {
                if (hit.gameObject == null || !(hit.module is GraphicRaycaster))
                    continue;
                overUi = true;
                Transform hitTransform = hit.gameObject.transform;
                if (inventoryOpen && hitTransform.IsChildOf(panel.transform))
                    overInventory = true;
                if (marketVisible && hitTransform.IsChildOf(market.MarketRoot))
                    overMarket = true;
                if ((!inventoryOpen || overInventory)
                    && (!marketVisible || overMarket))
                    break;
            }
        }

        int held = 0;
        int pressed = 0;
        int released = 0;
        for (int button = 0; button <= 2; button++)
        {
            int mask = 1 << button;
            if (Input.GetMouseButton(button)) held |= mask;
            if (Input.GetMouseButtonDown(button)) pressed |= mask;
            if (Input.GetMouseButtonUp(button)) released |= mask;
        }

        inventoryBlockedThisFrame =
            inventoryCapture.Sample(inventoryOpen, overInventory,
                held, pressed, released);
        marketBlockedThisFrame =
            marketCapture.Sample(marketVisible, overMarket,
                held, pressed, released);
        modalBlockedThisFrame =
            modalCapture.Sample(modalVisible, modalVisible,
                held, pressed, released);
        uiBlockedThisFrame =
            uiCapture.Sample(overUi, overUi, held, pressed, released);
        return inventoryBlockedThisFrame || marketBlockedThisFrame
            || modalBlockedThisFrame || uiBlockedThisFrame;
    }
}
