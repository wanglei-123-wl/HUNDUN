using System.Collections.Generic;
using Game.CharacterPanel.Presentation;
using Game.Chat.UI;
using Game.Friend.UI;
using Game.Inventory;
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
    private static readonly HashSet<GameObject> worldLockDialogs =
        new HashSet<GameObject>();
    private static readonly List<RaycastResult> pointerHits =
        new List<RaycastResult>();
    private static EventSystem pointerEventSystem;
    private static PointerEventData pointerEventData;
    private static int capturedMouseButtons;
    private static int keyboardFrame = -1;
    private static bool keyboardBlockedThisFrame;
    private static bool previouslyEditingText;
    private static int textPointerPressFrame = -1;
    private static int textClickSampleFrame = -1;

    public static void RegisterModal(GameObject dialog, bool blocksWorldActions = false)
    {
        if (dialog == null) return;
        modalDialogs.Add(dialog);
        if (blocksWorldActions) worldLockDialogs.Add(dialog);
    }

    public static void UnregisterModal(GameObject dialog)
    {
        if (dialog == null) return;
        modalDialogs.Remove(dialog);
        worldLockDialogs.Remove(dialog);
    }

    // Ordinary windows own UI input; they do not pause an existing world action.
    public static bool BlocksWorldActions
    {
        get
        {
            if (WorldEntryLoadingView.ActiveWorldEntry != null) return true;
            foreach (GameObject dialog in worldLockDialogs)
                if (dialog != null && dialog.activeInHierarchy) return true;
            return false;
        }
    }
    public static bool BlocksGameplayInput => BlocksWorldPointerInput;
    public static bool BlocksCameraInput => BlocksWorldPointerInput;
    public static bool BlocksWorldPointerInput =>
        BlocksWorldActions || ConsumesPointerInput || BlocksWorldKeyboardInput;

    public static bool ConsumesPointerInput
    {
        get
        {
            bool characterBlocked = CharacterPanelInputGuard.BlocksGameplayInput;
            bool panelBlocked = SamplePanels();
            return characterBlocked || panelBlocked;
        }
    }

    public static int CapturedMouseButtons
    {
        get
        {
            SamplePanels();
            return capturedMouseButtons;
        }
    }

    public static bool IsWorldMouseButtonHeld(int button) =>
        !BlocksWorldActions && Input.GetMouseButton(button)
        && (CapturedMouseButtons & (1 << button)) == 0;

    public static bool BlocksWorldKeyboardInput
    {
        get
        {
            if (Input.GetMouseButtonDown(0) && textClickSampleFrame != Time.frameCount)
            {
                textClickSampleFrame = Time.frameCount;
                SamplePanels();
            }
            bool editing = IsEditingText || textPointerPressFrame == Time.frameCount;
            if (keyboardFrame != Time.frameCount)
            {
                keyboardFrame = Time.frameCount;
                // The key that submits/closes a text field still belongs to UI.
                keyboardBlockedThisFrame = editing || previouslyEditingText;
            }
            else keyboardBlockedThisFrame |= editing;
            previouslyEditingText = editing;
            return keyboardBlockedThisFrame;
        }
    }

    public static bool BlocksWindowShortcuts => BlocksWorldActions || BlocksWorldKeyboardInput;

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
        if (Input.GetMouseButtonDown(0)) textClickSampleFrame = Time.frameCount;
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
        bool inventoryOpen = panel != null && panel.isActiveAndEnabled;
        RectTransform hitArea = inventoryOpen
            ? panel.GetComponent<RectTransform>()
            : null;
        bool overInventory = hitArea != null
            && RectTransformUtility.RectangleContainsScreenPoint(
                hitArea, Input.mousePosition);
        bool overMarket = false;
        bool overModal = false;
        bool overUi = CharacterPanelInputGuard.PointerOnPanel || overInventory;

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
                if (Input.GetMouseButtonDown(0))
                {
                    TMP_InputField textInput = hit.gameObject.GetComponentInParent<TMP_InputField>();
                    InputField legacyInput = hit.gameObject.GetComponentInParent<InputField>();
                    // TMP may only set isFocused during LateUpdate. Consume the
                    // click/typing frame as soon as its UI hit is known.
                    if ((textInput != null && textInput.isActiveAndEnabled)
                        || (legacyInput != null && legacyInput.isActiveAndEnabled))
                        textPointerPressFrame = Time.frameCount;
                }
                Transform hitTransform = hit.gameObject.transform;
                if (inventoryOpen && hitTransform.IsChildOf(panel.transform))
                    overInventory = true;
                if (marketVisible && hitTransform.IsChildOf(market.MarketRoot))
                    overMarket = true;
                if (modalVisible && !overModal)
                    foreach (GameObject dialog in modalDialogs)
                        if (dialog != null && dialog.activeInHierarchy
                            && hitTransform.IsChildOf(dialog.transform))
                        {
                            overModal = true;
                            break;
                        }
                if ((!inventoryOpen || overInventory)
                    && (!marketVisible || overMarket)
                    && (!modalVisible || overModal))
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

        bool inventoryBlockedThisFrame =
            inventoryCapture.Sample(inventoryOpen, overInventory,
                held, pressed, released, Time.frameCount);
        bool marketBlockedThisFrame =
            marketCapture.Sample(marketVisible, overMarket,
                held, pressed, released, Time.frameCount);
        bool modalBlockedThisFrame =
            modalCapture.Sample(modalVisible, overModal,
                held, pressed, released, Time.frameCount);
        bool uiOpen = overUi || BlocksWorldKeyboardInput;
        bool uiBlockedThisFrame =
            uiCapture.Sample(uiOpen, uiOpen, held, pressed, released, Time.frameCount);
        capturedMouseButtons = inventoryCapture.CapturedButtonsThisFrame | marketCapture.CapturedButtonsThisFrame
            | modalCapture.CapturedButtonsThisFrame | uiCapture.CapturedButtonsThisFrame;
        return inventoryBlockedThisFrame || marketBlockedThisFrame
            || modalBlockedThisFrame || uiBlockedThisFrame;
    }
}
