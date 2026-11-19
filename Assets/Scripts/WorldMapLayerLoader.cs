using System;
using System.Collections;
using System.Globalization;
using Game.Alchemy.Runtime;
using Game.Chat.Model;
using Game.Chat.Runtime;
using Game.Client.BattleSync;
using Game.Cultivation.Runtime;
using Game.Forging.Protocol;
using Game.Forging.Runtime;
using Game.Friend.Runtime;
using Game.Inventory;
using Game.Inventory.Protocol.V2;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public sealed class WorldMapLayerLoader : MonoBehaviour
{
    private const string SelectedCharacterDbIdKey = "selected_character_db_id";
    private const float SnapshotRetryIntervalSeconds = 2f;
    private const float PreparationProgressLimit = 0.95f;
    private const double CompletionDurationSeconds = 1d;
    [SerializeField] private GameSession gameSession;
    [SerializeField, Min(1f)] private float initialPlayerPreparationTimeout = 20f;

    private IEnumerator Start()
    {
        if (gameSession == null)
        {
            FailEntry("游戏会话组件缺失，请检查客户端资源。", "world_session_missing");
            yield break;
        }

        NetworkManager network = NetworkManager.Instance;
        if (!TryGetDestination(network, out long characterDbId,
                out string sceneId, out string mapId, out string mapAddress))
        {
            FailEntry("未收到有效的目标地图，或进场连接已失效。", "map_handoff_invalid");
            yield break;
        }
        long connectionEpoch = network.ConnectionEpoch;

        WorldEntryLoadingView loadingView = WorldEntryLoadingView.ActiveWorldEntry;
        if (loadingView != null)
            loadingView.Report(WorldEntryLoadingView.WorldSceneLoadedProgress, "正在加载目标地图");

        AsyncOperationHandle<SceneInstance> map;
        try
        {
            map = RemoteGameContent.LoadMapSceneAsync(mapAddress);
        }
        catch (System.Exception error)
        {
            Debug.LogError("World map load failed: " + error);
            FailEntry("目标地图资源加载失败，请检查资源连接后重试。", "map_load_failed");
            yield break;
        }

        while (!map.IsDone)
        {
            if (loadingView != null)
            {
                float progress = Mathf.Lerp(WorldEntryLoadingView.WorldSceneLoadedProgress,
                    WorldEntryLoadingView.TargetMapLoadedProgress, map.PercentComplete);
                loadingView.Report(progress, "正在加载目标地图");
            }
            yield return null;
        }
        if (map.Status != AsyncOperationStatus.Succeeded || !map.Result.Scene.IsValid())
        {
            Debug.LogError("World map load failed: " + map.OperationException);
            if (map.IsValid())
                Addressables.Release(map);
            FailEntry("目标地图资源加载失败，请检查资源连接后重试。", "map_load_failed");
            yield break;
        }

        if (network == null || network.ConnectionEpoch != connectionEpoch
            || !TryGetDestination(network, out long currentCharacterDbId,
                out string currentSceneId, out string currentMapId, out string currentAddress)
            || currentCharacterDbId != characterDbId
            || currentSceneId != sceneId || currentMapId != mapId
            || currentAddress != mapAddress)
        {
            FailEntry("加载目标地图时，进场连接或地图已变化。", "map_handoff_invalid");
            yield break;
        }

        if (!SceneManager.SetActiveScene(map.Result.Scene))
        {
            FailEntry("目标地图无法激活，请检查客户端资源。", "map_load_failed");
            yield break;
        }

        if (loadingView != null)
            loadingView.Report(WorldEntryLoadingView.TargetMapLoadedProgress, "正在初始化游戏会话");
        gameSession.StartSession();
        if (gameSession.IsReturningToLogin) yield break;

        if (loadingView != null)
            loadingView.Report(WorldEntryLoadingView.TargetMapLoadedProgress, "正在准备地图");
        // Keep network handshakes active, but do not open local input until the map query is ready.
        yield return gameSession.PrepareMapTraversal(sceneId, mapId, connectionEpoch,
            () => gameSession.LocalCharacterDbId <= 0 || gameSession.LocalCharacterDbId == characterDbId);
        if (gameSession.IsReturningToLogin) yield break;
        if (!gameSession.IsMapTraversalPrepared(sceneId, mapId)
            || network == null || NetworkManager.Instance != network || !network.IsConnected
            || network.ConnectionEpoch != connectionEpoch
            || !gameSession.CommitMapTraversal(sceneId, mapId))
        {
            FailEntry("地图资源准备失败，请更新游戏资源后重试。", "map_traversal_data_failed");
            yield break;
        }

        if (loadingView != null)
            yield return WaitForInitialPlayerData(loadingView, network, connectionEpoch,
                characterDbId, sceneId, mapId);
        else
            gameSession.StartLocalPlayerInput();
    }

    private IEnumerator WaitForInitialPlayerData(WorldEntryLoadingView loadingView,
        NetworkManager network, long connectionEpoch, long characterDbId,
        string sceneId, string mapId)
    {
        InventoryModuleRuntime inventory = FindObjectOfType<InventoryModuleRuntime>(true);
        CombatStateStore combat = FindObjectOfType<CombatStateStore>();
        ThirdPersonCameraFollow cameraFollow = FindObjectOfType<ThirdPersonCameraFollow>();
        string playerEntityId = characterDbId.ToString(CultureInfo.InvariantCulture);
        string sceneSessionId = network == null ? null : network.SceneSessionId;
        double startedAt = Time.realtimeSinceStartupAsDouble;
        double deadline = startedAt + initialPlayerPreparationTimeout;
        double nextSnapshotRequestAt = startedAt;
        double worldSnapshotRequestAt = startedAt + SnapshotRetryIntervalSeconds;
        bool requestedWorldSnapshots = false;
        float preparationTarget = WorldEntryLoadingView.TargetMapLoadedProgress;
        double? completionStartedAt = null;
        float completionStartProgress = 0f;
        bool showedCompletion = false;

        while (loadingView != null && WorldEntryLoadingView.ActiveWorldEntry == loadingView
            && !gameSession.IsReturningToLogin)
        {
            if (network == null || NetworkManager.Instance != network || !network.IsConnected
                || network.ConnectionEpoch != connectionEpoch
                || !string.Equals(network.SceneSessionId, sceneSessionId, StringComparison.Ordinal)
                || (gameSession.LocalCharacterDbId > 0 && gameSession.LocalCharacterDbId != characterDbId)
                || (gameSession.IsWorldReady && !gameSession.IsCurrentMap(sceneId, mapId)))
            {
                FailEntry("进场连接已中断或角色会话已变化。", "initial_player_session_changed");
                yield break;
            }

            bool inventoryReady = inventory?.CurrentSnapshot?.CharacterDbId == characterDbId;
            double now = Time.realtimeSinceStartupAsDouble;
            if (gameSession.IsWorldReady && now >= nextSnapshotRequestAt)
            {
                nextSnapshotRequestAt = now + SnapshotRetryIntervalSeconds;
                if (gameSession.LocalAttributeSnapshot == null)
                    gameSession.RequestAttributeSnapshot();
                if (gameSession.LocalProgressionSnapshot == null)
                    gameSession.RequestProgressionSnapshot();
                if (gameSession.LocalSkillSnapshot == null)
                    gameSession.RequestSkillSnapshot();
                if (!inventoryReady && inventory != null)
                    inventory.RequestSnapshot();
            }

            BattleSyncRuntime battleSync = gameSession.BattleSync;
            InitialSnapshotKind missingWorldSnapshots = battleSync?.GetMissingEntrySnapshots(
                connectionEpoch, sceneId) ?? BattleSyncCoordinator.EntrySnapshotKinds;
            if (gameSession.IsWorldReady && !requestedWorldSnapshots && now >= worldSnapshotRequestAt
                && missingWorldSnapshots != InitialSnapshotKind.None && battleSync != null)
            {
                requestedWorldSnapshots = battleSync.RequestMissingEntrySnapshots(connectionEpoch, sceneId);
                if (requestedWorldSnapshots)
                {
                    Debug.Log("[WorldEntry] Requested initial world snapshots. Missing=" + missingWorldSnapshots);
                    missingWorldSnapshots = battleSync.GetMissingEntrySnapshots(connectionEpoch, sceneId);
                }
            }

            string pending = GetPendingInitialData(cameraFollow, combat, playerEntityId,
                inventory, inventoryReady, out float progress);
            if (pending == null)
                pending = GetPendingWorldData(missingWorldSnapshots, connectionEpoch, characterDbId, out progress);
            if (pending == null && now < deadline && gameSession.IsSettingDefaultAttackMode)
            {
                pending = "切换攻击模式";
                progress = 1f;
            }
            if (pending != null)
            {
                if (now >= deadline)
                {
                    FailEntry("进场等待超时，未完成「" + pending + "」。", "initial_player_data_timeout");
                    yield break;
                }

                completionStartedAt = null;
                showedCompletion = false;
                float portion = Mathf.InverseLerp(WorldEntryLoadingView.TargetMapLoadedProgress, 1f, progress);
                preparationTarget = Mathf.Max(preparationTarget, Mathf.Lerp(
                    WorldEntryLoadingView.TargetMapLoadedProgress, PreparationProgressLimit, portion));
                loadingView.Report(preparationTarget, "正在" + pending);
            }
            else
            {
                if (showedCompletion)
                {
                    battleSync?.FinishEntrySnapshotTracking();
                    WorldEntryLoadingView.CloseWorldEntry();
                    gameSession.StartLocalPlayerInput();
                    yield break;
                }

                if (!completionStartedAt.HasValue)
                {
                    completionStartedAt = now;
                    completionStartProgress = loadingView.DisplayedProgress;
                }
                double elapsed = now - completionStartedAt.Value;
                showedCompletion = elapsed >= CompletionDurationSeconds;
                float finishing = (float)(elapsed / CompletionDurationSeconds);
                float completionProgress = showedCompletion ? 1f
                    : Mathf.Min(0.9999f, Mathf.SmoothStep(completionStartProgress, 1f, finishing));
                loadingView.Report(completionProgress, "正在进入游戏", immediate: true);
            }
            yield return null;
        }
    }

    private string GetPendingInitialData(ThirdPersonCameraFollow cameraFollow,
        CombatStateStore combat, string playerEntityId, InventoryModuleRuntime inventory,
        bool inventoryReady, out float progress)
    {
        progress = WorldEntryLoadingView.TargetMapLoadedProgress;
        if (!gameSession.IsWorldReady || !gameSession.HasPreparedLocalCharacter(cameraFollow))
            return "准备角色和相机";

        progress = 0.82f;
        if (combat == null || (gameSession.LocalEnterWorldHp < 0
            && (!combat.TryGetState("player", playerEntityId, out CombatStateData health)
                || health.MaxHp <= 0)))
            return "同步角色血量";

        progress = 0.84f;
        if (gameSession.LocalAttributeSnapshot == null || gameSession.LocalProgressionSnapshot == null)
            return "同步属性、等级和法力";

        progress = 0.86f;
        if (gameSession.LocalSkillSnapshot == null)
            return "同步技能";

        progress = 0.88f;
        if (!inventoryReady)
            return "同步背包、装备和货币";

        progress = 0.89f;
        if (CultivationSessionRuntime.Instance?.IsItemCatalogReady != true)
            return "同步物品资料";

        progress = 0.90f;
        bool quickbarSupported = gameSession.QuickbarProtocolVersion == QuickbarProtocol.Version
            && gameSession.QuickbarSlotCount == QuickbarProtocol.SlotCount
            && !string.IsNullOrWhiteSpace(gameSession.QuickbarOperationSessionId);
        if (quickbarSupported && inventory?.Quickbar?.IsReady != true)
            return "同步快捷栏";

        progress = 1f;
        return null;
    }

    private string GetPendingWorldData(InitialSnapshotKind missing, long epoch,
        long characterDbId, out float progress)
    {
        progress = 0.91f;
        if ((missing & InitialSnapshotKind.World) != 0)
            return "同步附近玩家";
        progress = 0.92f;
        if ((missing & InitialSnapshotKind.Monster) != 0)
            return "同步附近怪物";
        progress = 0.93f;
        if ((missing & InitialSnapshotKind.Loot) != 0)
            return "同步地面掉落";
        progress = 0.94f;
        if ((missing & InitialSnapshotKind.Combat) != 0)
            return "同步战斗状态";

        progress = 0.95f;
        RefiningSessionRuntime refining = RefiningSessionRuntime.Instance;
        if (Guid.TryParseExact(gameSession.JingjieOperationSessionId, "D", out _)
            && (refining == null || refining.Catalog == null || refining.Snapshot == null))
            return "同步修炼和炼化";
        progress = 0.96f;
        if (Guid.TryParseExact(gameSession.CultivationOperationSessionId, "D", out _)
            && (CultivationSessionRuntime.Instance == null
                || !CultivationSessionRuntime.Instance.IsConfigurationReady))
            return "同步功法和参悟";
        progress = 0.97f;
        AlchemySessionRuntime alchemy = AlchemySessionRuntime.Instance;
        if (Guid.TryParseExact(gameSession.AlchemyOperationSessionId, "D", out _)
            && (alchemy == null || !alchemy.IsCatalogCurrent || !alchemy.IsSnapshotCurrent))
            return "同步炼丹";
        progress = 0.98f;
        锻造会话运行时 forging = 锻造会话运行时.Instance;
        if (锻造消息.IsSupportedVersion(gameSession.ForgingProtocolVersion)
            && Guid.TryParseExact(gameSession.ForgingOperationSessionId, "D", out _)
            && (forging == null || !forging.IsCatalogCurrent || !forging.IsSnapshotCurrent))
            return "同步锻造";

        progress = 0.99f;
        var friends = FriendSessionRuntime.Instance?.State;
        if (friends == null || friends.ConnectionEpoch != epoch
            || friends.CharacterDbId != characterDbId || !friends.HasSnapshot)
            return "同步好友列表";
        WorldChatTimeline chat = WorldChatSessionRuntime.Instance?.Timeline;
        if (chat == null || chat.ConnectionEpoch != epoch || chat.Status != WorldChatHistoryStatus.Complete)
            return "同步世界聊天";

        progress = 1f;
        return null;
    }

    private static bool TryGetDestination(NetworkManager network, out long characterDbId,
        out string sceneId, out string mapId, out string mapAddress)
    {
        string savedCharacterDbId = PlayerPrefs.GetString(SelectedCharacterDbIdKey, "");
        characterDbId = 0;
        sceneId = null;
        mapId = null;
        mapAddress = null;
        return network != null && network.IsConnected
            && long.TryParse(savedCharacterDbId, out characterDbId)
            && !(NetMessageDispatcher.Instance?.HasDeferredWorldOverflow ?? false)
            && GateEntryHandoff.TryGetDestination(network.ConnectionEpoch,
                characterDbId, out sceneId, out mapId)
            && WorldMapSceneCatalog.TryGetAddress(sceneId, mapId, out mapAddress);
    }

    private void FailEntry(string message, string reason)
    {
        Debug.LogError("World map entry failed: " + message);
        GateEntryHandoff.SetFailure(message);
        if (gameSession != null)
        {
            gameSession.ReturnToLogin(reason);
            return;
        }

        GateEntryHandoff.Clear();
        NetMessageDispatcher.Instance?.ClearDeferredWorldMessages();
        NetworkManager.Instance?.Disconnect();
        WorldEntryLoadingView view = WorldEntryLoadingView.ActiveWorldEntry;
        if (view != null)
            view.ReturnToLoginScene(RemoteGameContent.LoginSceneName);
        else
            RemoteGameContent.LoadSceneOrLog(RemoteGameContent.LoginSceneName);
    }
}
