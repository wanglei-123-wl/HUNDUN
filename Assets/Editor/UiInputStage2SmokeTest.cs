using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Alchemy.Presentation;
using Game.Cultivation.Presentation;
using Game.Forging.Presentation;
using Game.Inventory;
using Game.Inventory.Presentation.V2;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Editor-only scene/factory acceptance. Does not enter play mode or save the scene.
public static class UiInputStage2SmokeTest
{
    [Serializable] private sealed class RegionResult
    {
        public string name;
        public string origin;
        public string root;
        public int raycastGraphics;
        public int nativeRaycastHits;
        public int graphicFilterHits;
        public int buttons;
        public int scrollRects;
        public int textInputs;
    }

    [Serializable] private sealed class Report
    {
        public bool passed;
        public bool gameplayTested;
        public string mode = "Editor scene/factory and UI geometry checks; no player loop";
        public string error;
        public string unityVersion;
        public List<RegionResult> windows = new List<RegionResult>();
        public List<RegionResult> hud = new List<RegionResult>();
        public bool transferHardLock;
    }

    public static void Run()
    {
        var report = new Report { unityVersion = Application.unityVersion };
        try
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/WorldShell.unity", OpenSceneMode.Single);
            EventSystem events = One(scene, "EventSystem") as EventSystem;
            Check(events != null, "WorldShell EventSystem is missing.");
            EventSystem.current = events;
            Add(scene, events, report.windows, "人物属性", "CharacterPanelToggle", "panelCanvas");
            Add(scene, events, report.windows, "背包", "InventoryPanelView");
            Add(scene, events, report.windows, "修炼技能", "CultivationWindowToggle", "cultivationWindow");
            Add(scene, events, report.windows, "炼丹", "AlchemyWindowToggle", "alchemyWindow");
            Add(scene, events, report.windows, "锻造", "锻造窗口开关", "forgingWindow");
            Add(scene, events, report.windows, "玩家市场", "PlayerTradeMarketToggleController", "marketPanel");

            Component chat = One(scene, "ChatPanelSwitcher");
            RegionResult publicChat = Inspect(events, "聊天", "scene",
                Field<GameObject>(chat, "publicContent").transform.parent.gameObject);
            // Private chat has an independently hidden subtree; inspect it too.
            Inspect(events, "私聊子页", "scene", Field<GameObject>(chat, "privatePanel"));
            report.windows.Add(publicChat);
            Check(Field<TMP_InputField>(One(scene, "WorldChatPanelController"), "inputField") != null,
                "Public chat input is missing.");
            Check(Field<TMP_InputField>(One(scene, "PrivateChatPanelController"), "inputField") != null,
                "Private chat input is missing.");

            Add(scene, events, report.windows, "提示面板", "提示面板交互", "提示面板");
            // This controller intentionally resolves the scene's named children
            // at runtime, including the price/quantity input components.
            Component listing = One(scene, "PlayerTradeSellPanelController");
            listing.GetType().GetMethod("ResolveReferences", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(listing, null);
            Add(scene, events, report.windows, "挂单面板", "PlayerTradeSellPanelController", "listingPanel");
            Check(Field<TMP_InputField>(listing, "quantityInput") != null
                && Field<TMP_InputField>(listing, "unitPriceInput") != null,
                "Listing quantity or price input is missing after runtime binding.");
            Add(scene, events, report.windows, "添加搜索好友", "添加好友弹窗控制器", "添加好友弹窗");
            Check(Field<TMP_InputField>(One(scene, "FriendSearchPanelController"), "nicknameInput") != null,
                "Friend search input is missing.");
            Add(scene, events, report.windows, "好友申请", "FriendRequestDialogController", "requestDialog");
            Add(scene, events, report.windows, "传送选择", "VillageTeleportPanel", "panelRoot");
            Component exitMenu = One(scene, "ExitMenuController");
            Add(scene, events, report.windows, "退出菜单", "ExitMenuController", "menuContent");
            foreach (string field in new[] { "continueButton", "logoutButton", "quitButton" })
                Check(Field<Button>(exitMenu, field) != null, "Exit menu button missing: " + field);

            var inventory = (InventoryPanelView)One(scene, "InventoryPanelView");
            Canvas canvas = inventory.GetComponentInParent<Canvas>(true);
            Check(canvas != null, "Inventory canvas is missing.");
            var refining = RefiningQuantityConfirmationView.Create(canvas, null);
            Check(refining != null, "Refining dialog factory failed.");
            refining.Show("stage2-test", "检查物品", 3, _ => true);
            report.windows.Add(Inspect(events, "炼化数量", "factory", refining.gameObject));
            refining.Hide();
            Check(!refining.gameObject.activeSelf, "Refining dialog failed to close.");
            var alchemy = AlchemyQuantityConfirmationView.Create(canvas, null);
            Check(alchemy != null, "Alchemy dialog factory failed.");
            alchemy.Show("stage2-test", "检查物品", 3, 1);
            report.windows.Add(Inspect(events, "炼丹数量", "factory", alchemy.gameObject));
            alchemy.Hide();
            Check(!alchemy.gameObject.activeSelf, "Alchemy dialog failed to close.");
            var forging = 锻造数量确认弹窗.Create(canvas, null);
            Check(forging != null, "Forging dialog factory failed.");
            forging.Show("stage2-test", "检查物品", 3, 1);
            report.windows.Add(Inspect(events, "锻造数量", "factory", forging.gameObject));
            forging.Hide();
            Check(!forging.gameObject.activeSelf, "Forging dialog failed to close.");
            var discard = InventoryDiscardConfirmationView.Create(inventory);
            Check(discard != null, "Discard dialog factory failed.");
            discard.Show("stage2-test", "检查物品", 3);
            report.windows.Add(Inspect(events, "丢弃确认", "factory", discard.gameObject));
            discard.Hide();
            Check(!discard.gameObject.activeSelf, "Discard dialog failed to close.");

            Add(scene, events, report.hud, "底部技能及物品快捷栏", "ProgressionHudPresenter");
            Component minimap = One(scene, "MinimapPresenter");
            minimap.GetType().GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(minimap, null);
            Add(scene, events, report.hud, "小地图", "MinimapPresenter");
            var transfer = (MapTransferStatusView)One(scene, "MapTransferStatusView");
            transfer.Show("界面输入检查");
            report.transferHardLock = GameplayUiInputGuard.BlocksWorldActions
                && GameplayUiInputGuard.BlocksCameraInput;
            Check(report.transferHardLock, "Map transfer lost its mandatory world lock.");
            transfer.Hide();
            Check(!GameplayUiInputGuard.BlocksWorldActions, "Map transfer did not release the world lock.");
            Check(report.windows.Count == 17, "The window inventory is incomplete.");
            report.passed = true;
        }
        catch (Exception exception)
        {
            report.error = exception.ToString();
            Debug.LogException(exception);
        }
        string output = Environment.GetEnvironmentVariable("HUNDUN_UI_STAGE2_REPORT");
        if (!string.IsNullOrEmpty(output))
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log(report.passed ? "PASS: UI_INPUT_STAGE2_NATIVE" : "FAIL: UI_INPUT_STAGE2_NATIVE");
        if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        else Check(report.passed, report.error);
    }

    private static void Add(Scene scene, EventSystem events, List<RegionResult> rows,
        string name, string type, string field = null)
    {
        Component component = One(scene, type);
        UnityEngine.Object reference = field == null ? component : Field<UnityEngine.Object>(component, field);
        GameObject root = reference is GameObject gameObject ? gameObject : (reference as Component)?.gameObject;
        Check(root != null, type + "." + field + " has no UI root.");
        rows.Add(Inspect(events, name, "scene", root));
    }

    private static RegionResult Inspect(EventSystem events, string name, string origin, GameObject root)
    {
        var activeStates = new Dictionary<GameObject, bool>();
        for (Transform node = root.transform; node != null; node = node.parent)
        {
            activeStates.Add(node.gameObject, node.gameObject.activeSelf);
            node.gameObject.SetActive(true);
        }
        try
        {
            Canvas.ForceUpdateCanvases();
            var hits = new List<RaycastResult>();
            var pointer = new PointerEventData(events);
            var row = new RegionResult { name = name, origin = origin, root = HierarchyPath(root.transform) };
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>())
            {
                if (!graphic.isActiveAndEnabled || !graphic.raycastTarget) continue;
                Canvas canvas = graphic.canvas;
                GraphicRaycaster raycaster = canvas == null ? null : canvas.GetComponent<GraphicRaycaster>();
                if (raycaster == null || !raycaster.isActiveAndEnabled) continue;
                row.raycastGraphics++;
                pointer.position = RectTransformUtility.WorldToScreenPoint(
                    canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera,
                    graphic.rectTransform.TransformPoint(graphic.rectTransform.rect.center));
                hits.Clear();
                events.RaycastAll(pointer, hits);
                if (hits.Any(hit => hit.module is GraphicRaycaster && hit.gameObject != null
                    && hit.gameObject.transform.IsChildOf(root.transform))) row.nativeRaycastHits++;
                // Edit-mode batch runs may have no rendered mesh depth, so the
                // EventSystem hit count is diagnostic, not a gameplay claim.
                Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                if (RectTransformUtility.RectangleContainsScreenPoint(graphic.rectTransform, pointer.position, camera)
                    && graphic.Raycast(pointer.position, camera)) row.graphicFilterHits++;
            }
            row.buttons = root.GetComponentsInChildren<Button>().Length;
            row.scrollRects = root.GetComponentsInChildren<ScrollRect>().Length;
            row.textInputs = root.GetComponentsInChildren<TMP_InputField>().Length;
            foreach (TMP_InputField input in root.GetComponentsInChildren<TMP_InputField>())
                Check(input.GetComponentsInChildren<Graphic>().Any(graphic =>
                    graphic.isActiveAndEnabled && graphic.raycastTarget),
                    name + " input has no clickable graphic: " + HierarchyPath(input.transform));
            Check(row.raycastGraphics > 0 && row.graphicFilterHits > 0,
                name + " has no eligible UI graphic/filter hit (" + row.root + ").");
            Check(!GameplayUiInputGuard.BlocksWorldActions,
                name + " incorrectly pauses existing world actions.");
            return row;
        }
        finally
        {
            foreach (var state in activeStates) state.Key.SetActive(state.Value);
        }
    }

    private static Component One(Scene scene, string type)
    {
        Component[] found = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Component>(true))
            .Where(component => component != null && component.GetType().Name == type).ToArray();
        Check(found.Length == 1, "Expected one " + type + ", found " + found.Length);
        return found[0];
    }

    private static T Field<T>(Component owner, string name) where T : UnityEngine.Object
    {
        var field = owner.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        T value = field?.GetValue(owner) as T;
        Check(value != null, owner.GetType().Name + "." + name + " is unassigned.");
        return value;
    }

    private static string HierarchyPath(Transform node) => node.parent == null
        ? node.name : HierarchyPath(node.parent) + "/" + node.name;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
