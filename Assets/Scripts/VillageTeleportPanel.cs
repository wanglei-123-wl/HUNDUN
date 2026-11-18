using System;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class VillageTeleportPanel : MonoBehaviour
{
    [Serializable]
    private sealed class Destination
    {
        public string name;
        public string sceneId;
        public string mapId;
        public Button button;

        [NonSerialized] public ColorBlock normalColors;
        [NonSerialized] public UnityAction click;
        [NonSerialized] public MapTransferRoute route;
        [NonSerialized] public bool ambiguous;
    }

    [Serializable]
    private sealed class Departure
    {
        public string name;
        public string sceneId;
        public string mapId;
        public DestinationRoute[] allowedRoutes;
    }

    [Serializable]
    private sealed class DestinationRoute
    {
        public string sceneId;
        public string routeId;
    }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;
    [SerializeField, InspectorName("目的地按钮")]
    private Destination[] destinations = Array.Empty<Destination>();
    [SerializeField, InspectorName("出发地图与可去目的地")]
    private Departure[] departures = Array.Empty<Departure>();
    private GameSession gameSession;
    private NetworkManager network;
    private NetMessageDispatcher dispatcher;
    private Action<MapTransferRoute> transferHandler;
    private Departure currentDeparture;
    private string querySessionId;
    private long queryEpoch;
    private float queryDeadline;
    private bool queryPending;
    private bool initialized;

    private void Awake()
    {
        if (panelRoot == null || closeButton == null || destinations.Length == 0
            || Array.Exists(destinations, destination => destination == null || destination.button == null))
        {
            Debug.LogError("Village teleport panel is missing its UI references.", this);
            enabled = false;
            return;
        }

        panelRoot.SetActive(false);
        closeButton.onClick.AddListener(Close);
        foreach (var destination in destinations)
        {
            destination.normalColors = destination.button.colors;
            destination.click = () => SelectDestination(destination);
            destination.button.onClick.AddListener(destination.click);
        }
        initialized = true;
        RefreshDestinationButtons();
    }

    private void Update()
    {
        if (!panelRoot.activeSelf)
            return;

        if (querySessionId != null && !IsQueryCurrent())
        {
            Close();
            return;
        }

        if (queryPending && Time.unscaledTime >= queryDeadline)
        {
            queryPending = false;
            GameNoticeService.Show("传送路线查询超时，请关闭后重试");
        }
    }

    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    public void Show()
    {
        if (isActiveAndEnabled && !IsOpen && panelRoot != null
            && transferHandler != null)
            Open();
    }

    public void SetTransferHandler(Action<MapTransferRoute> handler)
    {
        transferHandler = handler;
    }

    public void Hide()
    {
        if (initialized)
            Close();
    }

    private void Open()
    {
        querySessionId = null;
        ClearRoutes();
        queryPending = false;
        gameSession = FindObjectOfType<GameSession>();
        currentDeparture = gameSession == null ? null : Array.Find(departures, departure =>
            departure != null && gameSession.IsCurrentMap(departure.sceneId, departure.mapId));
        panelRoot.SetActive(true);
        RefreshDestinationButtons();
        GameplayUiInputGuard.RegisterModal(panelRoot);

        network = NetworkManager.Instance;
        NetMessageDispatcher currentDispatcher = NetMessageDispatcher.Instance;
        if (gameSession == null || !gameSession.IsWorldReady
            || network == null || !network.IsConnected
            || string.IsNullOrWhiteSpace(gameSession.SceneSessionId)
            || currentDispatcher == null)
        {
            GameNoticeService.Show("尚未进入场景，传送路线暂不可用");
            return;
        }

        if (dispatcher != currentDispatcher)
        {
            Unsubscribe();
            dispatcher = currentDispatcher;
            dispatcher.Register("map_transfer_options_result", HandleOptionsResult);
            dispatcher.Register("error", HandleOptionsError);
        }

        querySessionId = gameSession.SceneSessionId;
        queryEpoch = network.ConnectionEpoch;
        queryDeadline = Time.unscaledTime + 5f;
        queryPending = true;
        if (!network.Send(new MapTransferOptionsRequest()))
        {
            queryPending = false;
            GameNoticeService.Show("传送路线查询失败，请稍后重试");
        }
    }

    private bool IsQueryCurrent()
    {
        return panelRoot.activeSelf && gameSession != null && gameSession.IsWorldReady
            && network != null && network.IsConnected
            && network.ConnectionEpoch == queryEpoch
            && string.Equals(querySessionId, gameSession.SceneSessionId,
                StringComparison.Ordinal);
    }

    private void HandleOptionsResult(string json)
    {
        if (!queryPending || !IsQueryCurrent())
            return;

        MapTransferOptionsResult result;
        try { result = JsonUtility.FromJson<MapTransferOptionsResult>(json); }
        catch (Exception)
        {
            queryPending = false;
            GameNoticeService.Show("传送路线数据异常");
            return;
        }

        if (result == null || !string.Equals(result.sceneSessionId,
                querySessionId, StringComparison.Ordinal))
            return;

        queryPending = false;
        bool anyRoute = false;
        bool anyAmbiguous = false;
        foreach (var destination in destinations)
        {
            DestinationRoute allowed = FindAllowedRoute(destination);
            if (allowed == null)
                continue;
            destination.route = FindRoute(result.routes, destination.sceneId, destination.mapId,
                allowed.routeId, out destination.ambiguous);
            anyRoute |= destination.route != null;
            anyAmbiguous |= destination.ambiguous;
        }
        if (anyAmbiguous)
            GameNoticeService.Show("目的地路线配置不明确");
        else if (!anyRoute)
            GameNoticeService.Show("当前位置没有可用的传送路线");
    }

    private void HandleOptionsError(string json)
    {
        if (!queryPending || !IsQueryCurrent())
            return;

        ServerErrorMessage error;
        try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
        catch (Exception) { return; }
        if (error == null || error.requestMsgId != "map_transfer_options")
            return;

        queryPending = false;
        GameNoticeService.Show(error.errorCode == "too_far"
            ? "请靠近传送员" : "传送路线暂不可用");
    }

    private DestinationRoute FindAllowedRoute(Destination destination)
    {
        if (currentDeparture?.allowedRoutes == null
            || string.Equals(currentDeparture.sceneId, destination.sceneId, StringComparison.Ordinal))
            return null;

        return Array.Find(currentDeparture.allowedRoutes, route => route != null
            && string.Equals(route.sceneId, destination.sceneId, StringComparison.Ordinal));
    }

    private void SelectDestination(Destination destination)
    {
        if (FindAllowedRoute(destination) == null)
        {
            GameNoticeService.Show("目前所在位置无法前往该地区");
            return;
        }
        if (transferHandler == null || !IsQueryCurrent())
        {
            GameNoticeService.Show("传送暂不可用，请重新打开菜单");
            return;
        }
        if (queryPending)
        {
            GameNoticeService.Show("正在查询传送路线，请稍候");
            return;
        }
        if (destination.ambiguous)
        {
            GameNoticeService.Show("前往" + destination.name + "的路线配置不明确");
            return;
        }
        if (destination.route == null)
        {
            GameNoticeService.Show("当前位置没有前往" + destination.name + "的可用路线，请靠近传送点后重试");
            return;
        }

        transferHandler(destination.route);
    }

    private static MapTransferRoute FindRoute(MapTransferRoute[] routes, string sceneId,
        string mapId, string configuredRouteId, out bool ambiguous)
    {
        ambiguous = false;
        MapTransferRoute selected = null;
        if (routes == null)
            return null;
        foreach (MapTransferRoute route in routes)
        {
            if (route == null || string.IsNullOrWhiteSpace(route.routeId) || route.routeId.Length > 64
                || !string.Equals(route.sceneId, sceneId, StringComparison.Ordinal)
                || !string.Equals(route.mapId, mapId, StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(configuredRouteId)
                    && !string.Equals(route.routeId, configuredRouteId, StringComparison.Ordinal)))
                continue;
            if (selected != null && !string.Equals(selected.routeId, route.routeId,
                    StringComparison.Ordinal))
                ambiguous = true;
            selected = route;
        }
        return ambiguous || selected == null ? null : new MapTransferRoute
        {
            routeId = selected.routeId, sceneId = selected.sceneId, mapId = selected.mapId
        };
    }

    private void ClearRoutes()
    {
        foreach (var destination in destinations)
        {
            destination.route = null;
            destination.ambiguous = false;
        }
    }

    private void RefreshDestinationButtons()
    {
        foreach (var destination in destinations)
        {
            ColorBlock colors = destination.normalColors;
            // Keep the click target active so a grey button can explain why the route is unavailable.
            if (FindAllowedRoute(destination) == null)
            {
                colors.normalColor = colors.disabledColor;
                colors.highlightedColor = colors.disabledColor;
                colors.pressedColor = colors.disabledColor;
                colors.selectedColor = colors.disabledColor;
            }
            destination.button.colors = colors;
            destination.button.interactable = IsOpen;
        }
    }

    private void Unsubscribe()
    {
        if (dispatcher == null)
            return;
        dispatcher.Unregister("map_transfer_options_result", HandleOptionsResult);
        dispatcher.Unregister("error", HandleOptionsError);
        dispatcher = null;
    }

    private void Close()
    {
        queryPending = false;
        ClearRoutes();
        currentDeparture = null;
        querySessionId = null;
        queryEpoch = 0;
        queryDeadline = 0f;
        Unsubscribe();
        GameplayUiInputGuard.UnregisterModal(panelRoot);
        panelRoot.SetActive(false);
        RefreshDestinationButtons();
    }

    private void OnDisable()
    {
        if (initialized)
            Close();
    }

    private void OnDestroy()
    {
        if (!initialized)
            return;
        Unsubscribe();
        closeButton.onClick.RemoveListener(Close);
        foreach (var destination in destinations)
            destination.button.onClick.RemoveListener(destination.click);
    }
}
