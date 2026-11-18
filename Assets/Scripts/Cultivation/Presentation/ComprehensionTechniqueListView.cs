using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Items;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ComprehensionTechniqueListView : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private ComprehensionTechniqueItemView itemPrefab;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private Image selectedIcon;
        [SerializeField] private TMP_Text selectedName;
        [SerializeField] private TMP_Text selectedGrade;
        [SerializeField] private TMP_Text selectedDetails;
        [SerializeField] private TMP_Text materialsText;
        [SerializeField] private ScrollRect materialsScroll;
        [SerializeField] private Image taskFill;
        [SerializeField] private TMP_Text taskProgress;
        [SerializeField] private Button startButton;

        private readonly Dictionary<string, ComprehensionTechniqueItemView> activeItems =
            new Dictionary<string, ComprehensionTechniqueItemView>(StringComparer.Ordinal);
        private readonly List<LearnedTechniqueDisplayEntry> cachedEntries =
            new List<LearnedTechniqueDisplayEntry>();
        private NetworkManager network;
        private CultivationSessionRuntime runtime;
        private long characterDbId;
        private long connectionEpoch;
        private string selectedTechniqueId;
        private CultivationTaskDto displayedTask;
        private string completionTaskId;
        private long anchorServerTime;
        private double anchorMonotonicTime;
        private float nextCompletionCheck;
        private long displayedInventoryRevision = -1;

        public int VisibleCount => activeItems.Count;

        private void OnEnable()
        {
            selectedTechniqueId = null;
            ResolveReferences();
            if (taskProgress != null)
                taskProgress.enableWordWrapping = false;
            if (session != null)
                session.LocalWorldEntered += HandleWorldEntered;
            if (network != null)
                network.OnDisconnected += HandleDisconnected;

            BindRuntime();
            if (runtime != null)
                runtime.Refresh();
            if (characterDbId > 0 && !IsCurrentSession())
                Clear();
            if (runtime == null && characterDbId > 0)
                Render();
            if (startButton != null)
            {
                startButton.onClick.AddListener(StartSelectedTechnique);
                startButton.interactable = false;
            }
            RefreshFromRuntime();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        private void Update()
        {
            if (network != NetworkManager.Instance)
            {
                if (network != null)
                    network.OnDisconnected -= HandleDisconnected;
                network = NetworkManager.Instance;
                if (network != null)
                    network.OnDisconnected += HandleDisconnected;
                Clear();
            }
            BindRuntime();
            if (characterDbId > 0 && !IsCurrentSession())
                Clear();
            if (runtime != null && (!runtime.HasAcceptedSnapshot
                || !runtime.IsConfigurationReady))
            {
                if (characterDbId > 0)
                    Clear();
            }
            else if (runtime != null && characterDbId == 0)
                RefreshFromRuntime();
            long inventoryRevision = inventory?.CurrentSnapshot?.InventoryRevision ?? -1;
            if (inventoryRevision != displayedInventoryRevision && IsCurrentSession())
                RenderDetails();
            UpdateTaskProgress();
            CheckCompletion();
            RefreshStartButton();
        }

        private void OnDisable()
        {
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            if (network != null)
                network.OnDisconnected -= HandleDisconnected;
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartSelectedTechnique);
                startButton.interactable = false;
            }
            UnbindRuntime();
            displayedTask = null;
        }

        private void OnDestroy()
        {
            Clear();
        }

        // Only a confirmed full list for the active character belongs here.
        public bool ApplySnapshot(long ownerCharacterDbId,
            IReadOnlyList<LearnedTechniqueDisplayEntry> entries)
        {
            ResolveReferences();
            if (ownerCharacterDbId <= 0 || entries == null
                || session == null || !session.IsWorldReady
                || session.LocalCharacterDbId != ownerCharacterDbId
                || network == null || network != NetworkManager.Instance
                || !network.IsConnected
                || network.ConnectionEpoch <= 0
                || contentRoot == null || itemPrefab == null)
                return false;

            bool resetScroll = characterDbId != ownerCharacterDbId
                || connectionEpoch != network.ConnectionEpoch
                || cachedEntries.Count == 0;
            if (characterDbId != ownerCharacterDbId
                || connectionEpoch != network.ConnectionEpoch)
                Clear();

            characterDbId = ownerCharacterDbId;
            connectionEpoch = network.ConnectionEpoch;
            cachedEntries.Clear();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (LearnedTechniqueDisplayEntry entry in entries)
            {
                if (entry != null && !string.IsNullOrWhiteSpace(entry.TechniqueId)
                    && seen.Add(entry.TechniqueId))
                    cachedEntries.Add(entry);
            }

            if (isActiveAndEnabled)
                Render();
            if (resetScroll && scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
            return true;
        }

        public void Clear()
        {
            characterDbId = 0;
            connectionEpoch = 0;
            displayedInventoryRevision = -1;
            selectedTechniqueId = null;
            cachedEntries.Clear();
            foreach (ComprehensionTechniqueItemView view in activeItems.Values)
                Release(view);
            activeItems.Clear();
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
            ClearDetails();
        }

        private void Render()
        {
            if (contentRoot == null || itemPrefab == null)
                return;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < cachedEntries.Count; index++)
            {
                LearnedTechniqueDisplayEntry entry = cachedEntries[index];
                if (!activeItems.TryGetValue(entry.TechniqueId,
                    out ComprehensionTechniqueItemView view) || view == null)
                {
                    view = Instantiate(itemPrefab, contentRoot, false);
                    view.Selected += SelectTechnique;
                    activeItems[entry.TechniqueId] = view;
                }
                view.Bind(entry.TechniqueId, entry.DisplayName, entry.Icon,
                    entry.ProficiencyLabel);
                view.transform.SetSiblingIndex(index);
                seen.Add(entry.TechniqueId);
            }

            var removed = new List<string>();
            foreach (KeyValuePair<string, ComprehensionTechniqueItemView> pair
                in activeItems)
            {
                if (!seen.Contains(pair.Key))
                {
                    Release(pair.Value);
                    removed.Add(pair.Key);
                }
            }
            foreach (string id in removed)
                activeItems.Remove(id);

            if (selectedTechniqueId == null || !activeItems.ContainsKey(selectedTechniqueId))
                selectedTechniqueId = cachedEntries.Count > 0 ? cachedEntries[0].TechniqueId : null;
            UpdateSelectionEffects();
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
            if (runtime != null)
                RenderDetails();
        }

        private void Release(ComprehensionTechniqueItemView view)
        {
            if (view == null)
                return;
            view.Selected -= SelectTechnique;
            view.Clear();
            view.gameObject.SetActive(false);
            Destroy(view.gameObject);
        }

        private void BindRuntime()
        {
            CultivationSessionRuntime current = CultivationSessionRuntime.Instance;
            if (current == runtime)
                return;
            UnbindRuntime();
            Clear();
            runtime = current;
            if (runtime == null)
                return;
            runtime.SnapshotChanged += HandleSnapshotChanged;
            runtime.CatalogChanged += HandleCatalogChanged;
            runtime.ItemCatalogChanged += HandleItemCatalogChanged;
            runtime.StartResultReceived += HandleStartResult;
            runtime.StartRejected += HandleStartRejected;
            RefreshFromRuntime();
        }

        private void UnbindRuntime()
        {
            if (runtime == null)
                return;
            runtime.SnapshotChanged -= HandleSnapshotChanged;
            runtime.CatalogChanged -= HandleCatalogChanged;
            runtime.ItemCatalogChanged -= HandleItemCatalogChanged;
            runtime.StartResultReceived -= HandleStartResult;
            runtime.StartRejected -= HandleStartRejected;
            runtime = null;
        }

        private void StartSelectedTechnique()
        {
            if (!IsCurrentSession() || runtime == null
                || string.IsNullOrEmpty(selectedTechniqueId))
                return;
            if (HasOtherActiveTask())
            {
                GameNoticeService.Show("其他功法正在参悟中");
                return;
            }
            if (!runtime.TryStart(selectedTechniqueId))
                GameNoticeService.Show(runtime.HasUnresolvedOperation
                    ? "参悟结果待确认，正在同步"
                    : runtime.Snapshot?.activeTask != null
                        ? "已有功法参悟任务"
                        : "参悟条件已变化，请检查材料和背包");
            RefreshStartButton();
        }

        private void HandleStartResult(CultivationWriteResultDto result)
        {
            RefreshStartButton();
            if (result == null)
                return;
            if (result.success)
            {
                GameNoticeService.Show(result.idempotentReplay
                    ? "参悟结果已确认，正在同步"
                    : "参悟已开始");
                return;
            }
            if (result.errorCode == "cultivation_materials_insufficient"
                && result.missingMaterials != null
                && result.missingMaterials.Length > 0)
            {
                GameNoticeService.Show("材料不足：" +
                    FormatMaterials(result.missingMaterials, null, false));
                return;
            }
            HandleStartRejected(result.errorCode);
        }

        private void HandleStartRejected(string errorCode)
        {
            string message = errorCode == "cultivation_task_active"
                ? "已有功法参悟任务，正在同步"
                : errorCode == "cultivation_configuration_changed"
                    ? "功法配置已变化，请重新确认材料"
                    : errorCode == "inventory_revision_conflict"
                        ? "背包已变化，请重新确认材料"
                        : errorCode == "cultivation_persist_failed"
                            ? "参悟结果待确认，正在同步"
                            : errorCode == "cultivation_operation_conflict"
                                || errorCode == "invalid_cultivation_request"
                                ? "参悟请求身份异常，已停止自动重试"
                                : errorCode == "session_lease_lost"
                                    || errorCode == "session_lease_required"
                                    ? "角色写入许可已失效，请重新进场"
                                    : "无法开始参悟，请同步后重试";
            GameNoticeService.Show(message);
        }

        private void RefreshFromRuntime()
        {
            if (runtime == null || !runtime.IsConfigurationReady
                || runtime.Snapshot == null || runtime.Catalog == null
                || runtime.ItemCatalog == null || session == null
                || runtime.Snapshot.characterDbId != session.LocalCharacterDbId)
                return;

            CultivationSnapshotDto snapshot = runtime.Snapshot;
            var entries = new List<LearnedTechniqueDisplayEntry>();
            foreach (CultivationLearnedTechniqueDto learned in snapshot.techniques)
            {
                CultivationTechniqueDto definition = FindTechnique(learned.techniqueId);
                if (definition == null)
                    continue;
                entries.Add(new LearnedTechniqueDisplayEntry(learned.techniqueId,
                    definition.name, FindIcon(learned.techniqueId),
                    "等级 " + learned.level.ToString(CultureInfo.InvariantCulture)));
            }
            ApplySnapshot(snapshot.characterDbId, entries);
        }

        private CultivationTechniqueDto FindTechnique(string techniqueId)
        {
            if (runtime?.Catalog?.techniques == null)
                return null;
            foreach (CultivationTechniqueDto definition in runtime.Catalog.techniques)
                if (definition.techniqueId == techniqueId)
                    return definition;
            return null;
        }

        private Sprite FindIcon(string techniqueId)
        {
            if (runtime?.ItemCatalog?.items == null || inventory == null
                || inventory.ItemDefinitions == null)
                return null;
            Sprite icon = null;
            foreach (CultivationItemDefinitionDto item in runtime.ItemCatalog.items)
            {
                if (item.kind != "techniquebook"
                    || item.learnTechniqueId != techniqueId)
                    continue;
                if (!inventory.ItemDefinitions.TryGet(item.itemId,
                    out ItemDefinition definition) || definition.Icon == null)
                    continue;
                if (icon != null && icon != definition.Icon)
                    return null;
                icon = definition.Icon;
            }
            return icon;
        }

        private void SelectTechnique(string techniqueId)
        {
            if (!IsCurrentSession() || !activeItems.ContainsKey(techniqueId))
                return;
            selectedTechniqueId = techniqueId;
            UpdateSelectionEffects();
            RenderDetails();
        }

        private void UpdateSelectionEffects()
        {
            foreach (KeyValuePair<string, ComprehensionTechniqueItemView> item in activeItems)
                item.Value.SetSelected(item.Key == selectedTechniqueId);
        }

        private void RenderDetails()
        {
            if (runtime == null || !runtime.IsConfigurationReady
                || !IsCurrentSession() || string.IsNullOrEmpty(selectedTechniqueId))
            {
                ClearDetails();
                return;
            }
            CultivationTechniqueDto definition = FindTechnique(selectedTechniqueId);
            CultivationLearnedTechniqueDto learned = null;
            foreach (CultivationLearnedTechniqueDto entry in runtime.Snapshot.techniques)
                if (entry.techniqueId == selectedTechniqueId)
                {
                    learned = entry;
                    break;
                }
            if (definition == null || learned == null)
            {
                ClearDetails();
                return;
            }
            if (selectedName != null)
                selectedName.text = definition.name;
            if (selectedIcon != null)
            {
                selectedIcon.sprite = FindIcon(selectedTechniqueId);
                selectedIcon.enabled = selectedIcon.sprite != null;
                selectedIcon.preserveAspect = true;
            }
            if (selectedGrade != null)
            {
                selectedGrade.text = "--";
                foreach (CultivationGradeDto grade in runtime.Catalog.grades)
                    if (grade.gradeId == definition.gradeId)
                    {
                        selectedGrade.text = grade.name;
                        break;
                    }
            }
            if (selectedDetails != null)
            {
                string details = "等级 " + learned.level.ToString(
                    CultureInfo.InvariantCulture);
                foreach (CultivationLevelDto level in definition.levels)
                    if (level.level == learned.level)
                    {
                        details += "  悟性 +" + level.comprehension.ToString(
                            CultureInfo.InvariantCulture);
                        break;
                    }
                details += "\n总悟性 "
                    + runtime.Snapshot.totalComprehension.ToString(
                        CultureInfo.InvariantCulture);
                if (runtime.TryGetNextLevel(selectedTechniqueId,
                    out CultivationLevelDto next))
                {
                    details += "\n升至等级 " + next.level.ToString(
                        CultureInfo.InvariantCulture);
                    if (CultivationSessionRuntime.TryEstimateDurationSeconds(next,
                        runtime.Snapshot.totalComprehension, out long seconds))
                        details += "  预计 " + FormatDuration(seconds);
                }
                else
                    details += "\n已达最高级";
                selectedDetails.text = details;
            }

            CultivationTaskDto task = runtime.Snapshot.activeTask;
            displayedInventoryRevision =
                inventory?.CurrentSnapshot?.InventoryRevision ?? -1;
            if (materialsText != null)
            {
                string recipe = runtime.TryGetNextLevel(selectedTechniqueId,
                    out CultivationLevelDto target)
                    ? "所需材料  " + FormatMaterials(target.materials,
                        inventory?.CurrentSnapshot, true)
                    : string.Empty;
                if (materialsText.text != recipe)
                {
                    materialsText.text = recipe;
                    if (materialsScroll != null)
                        materialsScroll.verticalNormalizedPosition = 1f;
                }
            }
            displayedTask = task != null && task.techniqueId == selectedTechniqueId
                ? task : null;
            bool newTask = completionTaskId != task?.taskId;
            if (newTask || anchorServerTime != runtime.LastSnapshotServerTime)
            {
                anchorServerTime = runtime.LastSnapshotServerTime;
                anchorMonotonicTime = Time.realtimeSinceStartupAsDouble;
            }
            if (newTask)
            {
                completionTaskId = task?.taskId;
                nextCompletionCheck = Time.unscaledTime;
            }
            if (displayedTask == null)
            {
                if (taskFill != null)
                    taskFill.fillAmount = 0f;
                if (taskProgress != null)
                    taskProgress.text = string.Empty;
                RefreshStartButton();
                return;
            }
            UpdateTaskProgress();
            RefreshStartButton();
        }

        private void RefreshStartButton()
        {
            if (startButton != null)
                startButton.interactable = IsCurrentSession()
                    && runtime != null
                    && !string.IsNullOrEmpty(selectedTechniqueId)
                    && (HasOtherActiveTask() || runtime.CanStart(selectedTechniqueId,
                        inventory?.CurrentSnapshot));
        }

        private bool HasOtherActiveTask()
        {
            return runtime.IsConfigurationReady
                && runtime.Snapshot.activeTask is CultivationTaskDto task
                && task.techniqueId != selectedTechniqueId;
        }

        private string FormatMaterials(CultivationMaterialDto[] materials,
            InventoryOwnershipSnapshot ownership, bool includeOwned)
        {
            if (materials == null)
                return "--";
            if (materials.Length == 0)
                return "无需材料";
            var parts = new List<string>(materials.Length);
            foreach (CultivationMaterialDto material in materials)
            {
                if (material == null)
                    continue;
                string name = material.itemId;
                if (inventory?.ItemDefinitions != null
                    && inventory.ItemDefinitions.TryGet(material.itemId,
                        out ItemDefinition definition)
                    && !string.IsNullOrWhiteSpace(definition.DisplayName))
                    name = definition.DisplayName;
                string amount = material.quantity.ToString(
                    CultureInfo.InvariantCulture);
                if (includeOwned && ownership != null
                    && ownership.CharacterDbId == characterDbId)
                {
                    ownership.TryGetItemCount(material.itemId, out long count);
                    amount = count.ToString(CultureInfo.InvariantCulture)
                        + "/" + amount;
                }
                parts.Add(name + " " + amount);
            }
            return string.Join("  ", parts);
        }

        private static string FormatDuration(long seconds)
        {
            return (seconds / 3600).ToString(CultureInfo.InvariantCulture)
                + ":" + (seconds / 60 % 60).ToString("00",
                    CultureInfo.InvariantCulture)
                + ":" + (seconds % 60).ToString("00",
                    CultureInfo.InvariantCulture);
        }

        private void UpdateTaskProgress()
        {
            if (displayedTask == null || !IsCurrentSession()
                || runtime == null || runtime.Snapshot == null
                || runtime.Snapshot.activeTask != displayedTask
                || anchorServerTime <= 0
                || displayedTask.endsAt <= displayedTask.startedAt)
                return;

            double now = anchorServerTime
                + (Time.realtimeSinceStartupAsDouble - anchorMonotonicTime) * 1000d;
            double duration = (double)displayedTask.endsAt - displayedTask.startedAt;
            if (taskFill != null)
                taskFill.fillAmount = Mathf.Clamp01((float)(
                    (now - displayedTask.startedAt) / duration));
            if (taskProgress != null)
            {
                long seconds = (long)Math.Ceiling(Math.Max(0d,
                    displayedTask.endsAt - now) / 1000d);
                taskProgress.text = seconds > 0 ? FormatDuration(seconds) : string.Empty;
            }
        }

        private void CheckCompletion()
        {
            CultivationTaskDto task = runtime?.Snapshot?.activeTask;
            if (task != null && IsCurrentSession()
                && anchorServerTime > 0 && task.endsAt > task.startedAt
                && anchorServerTime
                    + (Time.realtimeSinceStartupAsDouble - anchorMonotonicTime) * 1000d
                    >= task.endsAt
                && Time.unscaledTime >= nextCompletionCheck)
            {
                nextCompletionCheck = Time.unscaledTime + 5f;
                runtime.RequestSnapshot();
            }
        }

        private void ClearDetails()
        {
            displayedTask = null;
            completionTaskId = null;
            if (selectedIcon != null)
            {
                selectedIcon.sprite = null;
                selectedIcon.enabled = false;
            }
            if (selectedName != null)
                selectedName.text = "--";
            if (selectedGrade != null)
                selectedGrade.text = "--";
            if (selectedDetails != null)
                selectedDetails.text = runtime != null
                    && runtime.IsConfigurationReady && IsCurrentSession()
                    ? "总悟性 " + runtime.Snapshot.totalComprehension.ToString(
                        CultureInfo.InvariantCulture) : "--";
            if (materialsText != null)
                materialsText.text = string.Empty;
            if (materialsScroll != null)
                materialsScroll.verticalNormalizedPosition = 1f;
            if (taskFill != null)
                taskFill.fillAmount = 0f;
            if (taskProgress != null)
                taskProgress.text = string.Empty;
            RefreshStartButton();
        }

        private void HandleSnapshotChanged(CultivationSnapshotDto snapshot)
        {
            RefreshFromRuntime();
        }

        private void HandleCatalogChanged(CultivationCatalogResultDto catalog)
        {
            RefreshFromRuntime();
        }

        private void HandleItemCatalogChanged(CultivationItemCatalogResultDto catalog)
        {
            RefreshFromRuntime();
        }

        private bool IsCurrentSession()
        {
            return session != null && session.IsWorldReady
                && session.LocalCharacterDbId == characterDbId
                && network != null && network == NetworkManager.Instance
                && network.IsConnected && network.ConnectionEpoch == connectionEpoch;
        }

        private void ResolveReferences()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (network == null || network != NetworkManager.Instance)
                network = NetworkManager.Instance;
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
        }

        private void HandleWorldEntered(long epoch, long ownerCharacterDbId)
        {
            Clear();
        }

        private void HandleDisconnected(string reason)
        {
            Clear();
        }
    }
}
