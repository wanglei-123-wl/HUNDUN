using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Alchemy.Model;
using Game.Alchemy.Protocol;
using Game.Alchemy.Runtime;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Alchemy.Presentation
{
    [DisallowMultipleComponent]
    public sealed class AlchemyPanelView : MonoBehaviour, IDropHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private const int MaterialColumns = 5;
        private const float MaterialSlotSize = 50f;
        private const float MaterialSpacing = 3f;

        [SerializeField] private RectTransform recipeContent;
        [SerializeField] private AlchemyRecipeRowView recipePrefab;
        [SerializeField] private RectTransform materialContent;
        [SerializeField] private AlchemyMaterialSlotView materialPrefab;
        [SerializeField] private Image recipeIcon;
        [SerializeField] private TMP_Text recipeName;
        [SerializeField] private TMP_Text primaryMedicineText;
        [SerializeField] private TMP_Text secondaryMedicineText;
        [SerializeField] private TMP_Text auxiliaryMedicineText;
        [SerializeField] private Button startButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image taskFill;
        [SerializeField] private TMP_Text taskStatus;
        [SerializeField] private Image resultIcon;

        private AlchemyMaterialSelection selection =
            new AlchemyMaterialSelection();
        private readonly List<AlchemyRecipeRowView> rows =
            new List<AlchemyRecipeRowView>();
        private readonly List<AlchemyMaterialSlotView> slots =
            new List<AlchemyMaterialSlotView>();
        private GameSession session;
        private InventoryModuleRuntime inventory;
        private AlchemySessionRuntime runtime;
        private CultivationSessionRuntime itemCatalog;
        private AlchemyQuantityConfirmationView quantityDialog;
        private readonly List<RaycastResult> claimDropHits =
            new List<RaycastResult>();
        private RectTransform claimPreview;
        private Canvas claimDragCanvas;
        private string draggingClaimTaskId;
        private string selectedRecipeId;
        private long lastInventoryRevision = -1;
        private long lastCharacterDbId;
        private long lastEpoch;
        private AlchemyRecipeDto[] shownCatalog;
        private AlchemySnapshotDto shownSnapshot;
        private CultivationItemCatalogResultDto shownItems;
        private bool shownReady;
        private long pendingStackId;
        private long pendingInventoryRevision;
        private long pendingConfigurationRevision;
        private string pendingItemId;
        private string pendingRecipeId;
        private AlchemyTaskDto displayedTask;
        private AlchemyTaskClock taskClock;
        private long syncedServerTime;
        private long displayedSeconds = -1;
        private bool completionQuerySent;
        private bool invalidClockQuerySent;

        public AlchemyMaterialSelection Selection => selection;

        private void OnEnable()
        {
            ClearMedicineTexts();
            GameSession currentSession = FindObjectOfType<GameSession>();
            if (session != currentSession)
            {
                if (session != null)
                    session.LocalWorldEntered -= HandleWorldEntered;
                session = currentSession;
                if (session != null)
                    session.LocalWorldEntered += HandleWorldEntered;
            }
            inventory = FindObjectOfType<InventoryModuleRuntime>();
            EnsureSelectionCharacter(session?.LocalCharacterDbId ?? 0);
            if (startButton != null)
            {
                startButton.interactable = false;
                startButton.onClick.AddListener(StartAlchemy);
            }
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (taskStatus != null)
            {
                taskStatus.enableAutoSizing = true;
                taskStatus.fontSizeMin = 9f;
                taskStatus.fontSizeMax = 15f;
                taskStatus.enableWordWrapping = false;
                taskStatus.overflowMode = TextOverflowModes.Ellipsis;
                taskStatus.raycastTarget = true;
            }
            BindRuntimes();
            RefreshQueries();
            Render();
        }

        private void Update()
        {
            BindRuntimes();
            NetworkManager network = NetworkManager.Instance;
            long character = session?.LocalCharacterDbId ?? 0;
            long epoch = network != null && network.IsConnected
                ? network.ConnectionEpoch : 0;
            if (character != lastCharacterDbId || epoch != lastEpoch)
            {
                ClosePending();
                EnsureSelectionCharacter(character);
                lastCharacterDbId = character;
                lastEpoch = epoch;
                Render();
            }
            bool ready = HasCurrentData();
            if (ready != shownReady
                || (ready && (shownCatalog != runtime.Catalog
                    || shownSnapshot != runtime.Snapshot
                    || shownItems != itemCatalog?.ItemCatalog)))
                Render();

            long revision = inventory?.CurrentSnapshot?.InventoryRevision ?? -1;
            if (revision != lastInventoryRevision)
            {
                lastInventoryRevision = revision;
                ClosePending();
                selection.SyncRestoredStackItems(inventory?.CurrentSnapshot);
                RefreshSlots();
            }
            RefreshStartButton();
            TickTask();
        }

        private void OnDisable()
        {
            ClosePending();
            ClearClaimDrag();
            if (quantityDialog != null)
            {
                quantityDialog.Confirmed -= ConfirmQuantity;
                quantityDialog.Dispose();
                quantityDialog = null;
            }
            ClearViews();
            shownReady = false;
            shownCatalog = null;
            shownSnapshot = null;
            shownItems = null;
            ClearTaskDisplay();
            if (startButton != null)
            {
                startButton.onClick.RemoveListener(StartAlchemy);
                startButton.interactable = false;
            }
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
        }

        private void Close()
        {
            gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            UnbindRuntimes();
        }

        private void EnsureSelectionCharacter(long characterDbId)
        {
            if (characterDbId <= 0 || selection.CharacterDbId <= 0
                || selection.CharacterDbId == characterDbId)
                return;
            selection = new AlchemyMaterialSelection();
            selectedRecipeId = null;
        }

        private void BindRuntimes()
        {
            AlchemySessionRuntime current = AlchemySessionRuntime.Instance;
            CultivationSessionRuntime items = CultivationSessionRuntime.Instance;
            if (runtime != current)
            {
                UnbindAlchemy();
                ClosePending();
                runtime = current;
                if (runtime != null)
                {
                    runtime.CatalogChanged += HandleCatalogChanged;
                    runtime.SnapshotChanged += HandleSnapshotChanged;
                    runtime.StartResultReceived += HandleStartResult;
                    runtime.StartSelectionResolved += HandleStartSelectionResolved;
                    runtime.StartRequestRejected += HandleStartRequestRejected;
                    runtime.StartTimedOut += HandleStartTimeout;
                    runtime.ClaimResultReceived += HandleClaimResult;
                    runtime.ClaimTimedOut += HandleClaimTimeout;
                }
                if (isActiveAndEnabled)
                    Render();
            }
            AdoptStartSelection();
            if (itemCatalog != items)
            {
                if (itemCatalog != null)
                    itemCatalog.ItemCatalogChanged -= HandleItemCatalogChanged;
                itemCatalog = items;
                if (itemCatalog != null)
                    itemCatalog.ItemCatalogChanged += HandleItemCatalogChanged;
                if (isActiveAndEnabled)
                    Render();
            }
        }

        private void AdoptStartSelection()
        {
            AlchemyMaterialSelection recovered = runtime?.StartSelection;
            if (recovered == null || ReferenceEquals(recovered, selection)
                || recovered.CharacterDbId != session?.LocalCharacterDbId)
                return;
            selection = recovered;
            selectedRecipeId = recovered.RecipeId;
            if (isActiveAndEnabled)
                Render();
        }

        private void UnbindRuntimes()
        {
            UnbindAlchemy();
            if (itemCatalog != null)
                itemCatalog.ItemCatalogChanged -= HandleItemCatalogChanged;
            itemCatalog = null;
        }

        private void UnbindAlchemy()
        {
            if (runtime != null)
            {
                runtime.CatalogChanged -= HandleCatalogChanged;
                runtime.SnapshotChanged -= HandleSnapshotChanged;
                runtime.StartResultReceived -= HandleStartResult;
                runtime.StartSelectionResolved -= HandleStartSelectionResolved;
                runtime.StartRequestRejected -= HandleStartRequestRejected;
                runtime.StartTimedOut -= HandleStartTimeout;
                runtime.ClaimResultReceived -= HandleClaimResult;
                runtime.ClaimTimedOut -= HandleClaimTimeout;
            }
            runtime = null;
        }

        private void RefreshQueries()
        {
            if (session == null || !session.IsWorldReady)
                return;
            runtime?.RequestCatalog();
            runtime?.RequestSnapshot();
            itemCatalog?.RequestItemCatalog();
            inventory?.RequestSnapshot();
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            ClosePending();
            EnsureSelectionCharacter(characterDbId);
            lastCharacterDbId = characterDbId;
            lastEpoch = epoch;
            BindRuntimes();
            RefreshQueries();
            if (isActiveAndEnabled)
                Render();
        }

        private void HandleCatalogChanged(AlchemyRecipeDto[] recipes)
        {
            if (isActiveAndEnabled)
                Render();
        }

        private void HandleSnapshotChanged(AlchemySnapshotDto snapshot)
        {
            EnsureSelectionCharacter(session?.LocalCharacterDbId ?? 0);
            if (isActiveAndEnabled)
                Render();
        }

        private void HandleStartResult(AlchemyResponseDto result)
        {
            ClosePending();
            if (!isActiveAndEnabled)
                return;
            RefreshSlots();
            RefreshStartButton();
            GameNoticeService.Show(result.idempotentReplay
                ? "正在同步开炉结果和背包"
                : !string.IsNullOrWhiteSpace(result.message)
                    ? result.message
                    : result.success ? "炼丹已开始，正在同步任务"
                    : "开炉失败，请重新确认材料与条件");
        }

        private void HandleStartSelectionResolved()
        {
            if (!isActiveAndEnabled)
                return;
            RefreshSlots();
            RefreshStartButton();
        }

        private void HandleStartRequestRejected(string code, string message)
        {
            if (isActiveAndEnabled)
                GameNoticeService.Show(!string.IsNullOrWhiteSpace(message)
                    ? message
                    : "开炉请求未确认，正在同步状态");
        }

        private void HandleStartTimeout()
        {
            if (!isActiveAndEnabled)
                return;
            RefreshStartButton();
            GameNoticeService.Show("开炉结果尚未确认，请等待同步或重新进场恢复");
        }

        private void HandleClaimResult(AlchemyResponseDto result)
        {
            if (!isActiveAndEnabled)
                return;
            Render();
            GameNoticeService.Show(!string.IsNullOrWhiteSpace(result.message)
                ? result.message
                : result.success ? "收炉已确认" : "收炉失败，请刷新状态");
        }

        private void HandleClaimTimeout()
        {
            if (!isActiveAndEnabled)
                return;
            GameNoticeService.Show("收炉结果尚未确认，请等待同步或重新进场恢复");
        }

        private void RefreshStartButton()
        {
            if (startButton != null)
                startButton.interactable = HasCurrentData()
                    && runtime.CanStart(selection, inventory?.CurrentSnapshot);
        }

        private void ClaimAlchemy()
        {
            if (runtime == null || !runtime.TryClaim(inventory?.CurrentSnapshot))
            {
                GameNoticeService.Show(runtime != null
                    && runtime.HasUnresolvedClaim
                    ? "收炉结果尚未确认，请等待状态同步"
                    : "任务或背包状态已变化，请重新同步");
            }
            else
                GameNoticeService.Show("正在确认收炉结果");
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            AlchemyTaskDto task = runtime?.Snapshot?.currentTask;
            GameObject pressed = eventData?.pointerPressRaycast.gameObject;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || pressed == null || task == null || task.status != "ready"
                || string.IsNullOrWhiteSpace(task.taskId)
                || !runtime.IsSessionActive || !runtime.IsSnapshotCurrent
                || task != displayedTask)
                return;
            bool hasIcon = resultIcon != null && resultIcon.isActiveAndEnabled
                && resultIcon.sprite != null;
            if (pressed != (hasIcon ? resultIcon.gameObject
                : taskStatus?.gameObject))
                return;
            InventoryPanelView bag = InventoryPanelView.ActivePanel;
            if (bag == null || bag.GridView == null
                || !bag.gameObject.activeInHierarchy)
            {
                GameNoticeService.Show("请先打开背包");
                return;
            }
            if (!InventoryDragPreview.TryCreate(this, "拖拽成品",
                out claimPreview, out claimDragCanvas))
                return;
            Image previewImage = claimPreview.GetComponent<Image>();
            if (hasIcon)
            {
                claimPreview.sizeDelta = resultIcon.rectTransform.rect.size;
                previewImage.sprite = resultIcon.sprite;
                previewImage.color = new Color(1f, 1f, 1f, .8f);
            }
            else
            {
                claimPreview.sizeDelta = new Vector2(160f, 36f);
                previewImage.color = Color.clear;
                TMP_Text label = Instantiate(taskStatus, claimPreview, false);
                label.text = task.ParsedSucceeded == false
                    ? "炼制失败" : TaskName(task);
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                RectTransform labelRect = label.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            }
            draggingClaimTaskId = task.taskId;
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null)
                InventoryDragPreview.Move(claimPreview, claimDragCanvas,
                    eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            string taskId = draggingClaimTaskId;
            bool overBagSlot = taskId != null && IsOverBagSlot(eventData);
            ClearClaimDrag();
            if (!overBagSlot || runtime?.Snapshot?.currentTask?.taskId != taskId
                || runtime.Snapshot.currentTask.status != "ready")
                return;
            ClaimAlchemy();
        }

        private bool IsOverBagSlot(PointerEventData eventData)
        {
            InventoryPanelView bag = InventoryPanelView.ActivePanel;
            if (eventData == null || EventSystem.current == null
                || bag?.GridView == null || !bag.gameObject.activeInHierarchy)
                return false;
            claimDropHits.Clear();
            EventSystem.current.RaycastAll(eventData, claimDropHits);
            if (claimDropHits.Count == 0)
                return false;
            InventorySlotView slot = claimDropHits[0].gameObject
                ?.GetComponentInParent<InventorySlotView>();
            return slot != null && slot.isActiveAndEnabled
                && slot.DisplayIndex >= 0
                && bag.GridView.GetSlot(slot.DisplayIndex) == slot;
        }

        private void ClearClaimDrag()
        {
            if (claimPreview != null)
                Destroy(claimPreview.gameObject);
            claimPreview = null;
            claimDragCanvas = null;
            draggingClaimTaskId = null;
            claimDropHits.Clear();
        }

        private void StartAlchemy()
        {
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            if (runtime != null && runtime.CanStart(selection, bag))
            {
                AlchemySelectionError error = selection.CheckReady(bag,
                    runtime.ConfigurationRevision);
                if (error != AlchemySelectionError.None)
                {
                    GameNoticeService.Show(error == AlchemySelectionError.UnsupportedItem
                        ? "该物品不可用于炼丹"
                        : error == AlchemySelectionError.EmptySelection
                            ? "请先放入材料"
                            : "材料数量或背包已变化，请重新确认");
                    return;
                }
            }
            if (runtime == null || !runtime.TryStart(selection,
                bag))
            {
                GameNoticeService.Show(runtime != null
                    && runtime.HasUnresolvedStart
                    ? "开炉结果尚未确认，请等待状态同步"
                    : "材料或炼丹条件已变化，请重新确认");
            }
            else
                GameNoticeService.Show("正在确认开炉结果");
            RefreshStartButton();
        }

        private void HandleItemCatalogChanged(CultivationItemCatalogResultDto catalog)
        {
            if (isActiveAndEnabled)
                Render();
        }

        private bool HasCurrentData()
        {
            NetworkManager network = NetworkManager.Instance;
            return session != null && session.IsWorldReady
                && session.LocalCharacterDbId > 0
                && network != null && network.IsConnected
                && network.ConnectionEpoch > 0
                && runtime != null && runtime.IsCatalogCurrent
                && runtime.IsSnapshotCurrent
                && runtime.Catalog != null && runtime.Snapshot != null
                && runtime.Snapshot.characterDbId == session.LocalCharacterDbId;
        }

        private bool IsLearned(string recipeId)
        {
            string[] learned = runtime?.Snapshot?.learnedRecipeIds;
            if (learned == null)
                return false;
            foreach (string id in learned)
                if (string.Equals(id, recipeId, StringComparison.Ordinal))
                    return true;
            return false;
        }

        private void Render()
        {
            ClearMedicineTexts();
            RenderTask();
            bool ready = HasCurrentData();
            shownReady = ready;
            shownCatalog = ready ? runtime.Catalog : null;
            shownSnapshot = ready ? runtime.Snapshot : null;
            shownItems = ready ? itemCatalog?.ItemCatalog : null;
            if (!ready || recipeContent == null || materialContent == null
                || recipePrefab == null || materialPrefab == null)
            {
                ClosePending();
                ClearViews();
                return;
            }
            AlchemyRecipeDto current = IsLearned(selectedRecipeId)
                ? FindRecipe(selectedRecipeId) : null;
            if (current == null && selectedRecipeId == null)
            {
                current = Array.Find(runtime.Catalog, recipe =>
                    recipe != null && IsLearned(recipe.recipeId));
                selectedRecipeId = current?.recipeId;
            }
            if (current != null && selection.RecipeId == null
                && !selection.IsLocked)
                selection.SelectRecipe(current, runtime.ConfigurationRevision,
                    session.LocalCharacterDbId);

            ClearRows();
            int visibleIndex = 0;
            foreach (AlchemyRecipeDto recipe in runtime.Catalog)
            {
                if (recipe == null || !IsLearned(recipe.recipeId))
                    continue;
                AlchemyRecipeRowView row = Instantiate(recipePrefab,
                    recipeContent, false);
                RectTransform rect = (RectTransform)row.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(
                    Mathf.Max(100f, recipeContent.rect.width - 6f), 60f);
                rect.anchoredPosition = new Vector2(3f, -3f - visibleIndex * 63f);
                row.Bind(recipe.recipeId, recipe.name, FindRecipeIcon(recipe),
                    true, recipe.recipeId == selectedRecipeId);
                row.Selected += SelectRecipe;
                rows.Add(row);
                visibleIndex++;
            }
            recipeContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(0f, 6f + visibleIndex * 63f));
            if (recipeName != null)
                recipeName.text = current != null ? current.name : string.Empty;
            if (recipeIcon != null)
            {
                recipeIcon.sprite = current != null
                    ? FindRecipeIcon(current) : null;
                recipeIcon.gameObject.SetActive(recipeIcon.sprite != null);
            }
            RenderRequiredMaterials(current);
            RebuildSlots();
        }

        private void RenderTask()
        {
            NetworkManager network = NetworkManager.Instance;
            AlchemySnapshotDto snapshot = runtime?.Snapshot;
            AlchemyTaskDto task = snapshot?.currentTask;
            if (session == null || !session.IsWorldReady
                || network == null || !network.IsConnected
                || runtime == null || !runtime.IsSessionActive
                || snapshot == null
                || snapshot.characterDbId != session.LocalCharacterDbId
                || task == null)
            {
                ClearTaskDisplay();
                return;
            }

            bool changed = displayedTask == null
                || displayedTask.taskId != task.taskId
                || displayedTask.status != task.status
                || displayedTask.startedAtUnixMilliseconds
                    != task.startedAtUnixMilliseconds
                || displayedTask.endsAtUnixMilliseconds
                    != task.endsAtUnixMilliseconds;
            if (changed)
            {
                completionQuerySent = false;
                invalidClockQuerySent = false;
                syncedServerTime = 0;
                displayedSeconds = -1;
            }
            displayedTask = task;
            if (task.status == "ready")
            {
                taskClock = null;
                if (taskFill != null)
                    taskFill.fillAmount = 1f;
                SetTaskStatus(task.ParsedSucceeded == true
                    ? TaskName(task) + " 成功 x"
                        + task.outputCount.ToString(CultureInfo.InvariantCulture)
                        + "（待收炉）"
                    : TaskName(task) + " 炼制失败（待收炉）");
                ShowResultIcon();
                return;
            }

            HideResultIcon();
            if (changed || syncedServerTime != runtime.LastServerTime)
            {
                if (AlchemyTaskClock.TryCreate(task, runtime.LastServerTime,
                    Time.realtimeSinceStartupAsDouble, out AlchemyTaskClock clock))
                {
                    taskClock = clock;
                    syncedServerTime = runtime.LastServerTime;
                    displayedSeconds = -1;
                    invalidClockQuerySent = false;
                }
                else
                {
                    taskClock = null;
                    if (taskFill != null)
                        taskFill.fillAmount = 0f;
                    SetTaskStatus(TaskName(task) + " --");
                    if (!invalidClockQuerySent)
                    {
                        invalidClockQuerySent = true;
                        runtime.RequestSnapshot();
                    }
                    return;
                }
            }
            TickTask();
        }

        private void TickTask()
        {
            if (displayedTask == null)
                return;
            if (displayedTask.status == "ready")
            {
                ShowResultIcon();
                return;
            }
            if (taskClock == null)
                return;
            double now = Time.realtimeSinceStartupAsDouble;
            long seconds = taskClock.RemainingSeconds(now);
            if (taskFill != null)
                taskFill.fillAmount = taskClock.Progress(now);
            if (seconds != displayedSeconds)
            {
                displayedSeconds = seconds;
                long hours = seconds / 3600;
                long minutes = seconds / 60 % 60;
                long remainder = seconds % 60;
                SetTaskStatus(seconds == 0
                    ? TaskName(displayedTask) + " 结算中"
                    : TaskName(displayedTask) + " "
                        + string.Format(CultureInfo.InvariantCulture,
                            "{0:00}:{1:00}:{2:00}", hours, minutes, remainder));
            }
            if (seconds == 0 && !completionQuerySent && runtime != null)
                completionQuerySent = runtime.RequestSnapshot();
        }

        private static string TaskName(AlchemyTaskDto task)
        {
            return string.IsNullOrWhiteSpace(task.recipeName)
                ? task.recipeId : task.recipeName;
        }

        private void SetTaskStatus(string value)
        {
            if (taskStatus == null)
                return;
            taskStatus.text = value;
            if (!taskStatus.gameObject.activeSelf)
                taskStatus.gameObject.SetActive(true);
        }

        private void ShowResultIcon()
        {
            if (resultIcon == null || displayedTask == null
                || displayedTask.ParsedSucceeded != true)
                return;
            Sprite sprite = FindItemIcon(displayedTask.outputItemId);
            resultIcon.sprite = sprite;
            resultIcon.preserveAspect = true;
            resultIcon.raycastTarget = true;
            resultIcon.gameObject.SetActive(sprite != null);
        }

        private void HideResultIcon()
        {
            if (resultIcon == null)
                return;
            resultIcon.sprite = null;
            resultIcon.gameObject.SetActive(false);
        }

        private void ClearTaskDisplay()
        {
            displayedTask = null;
            taskClock = null;
            syncedServerTime = 0;
            displayedSeconds = -1;
            completionQuerySent = false;
            invalidClockQuerySent = false;
            if (taskFill != null)
                taskFill.fillAmount = 0f;
            if (taskStatus != null)
            {
                taskStatus.text = string.Empty;
                taskStatus.gameObject.SetActive(false);
            }
            HideResultIcon();
        }

        private void SelectRecipe(string id)
        {
            if (!HasCurrentData() || selection.IsLocked
                || runtime.HasUnresolvedStart || runtime.HasUnresolvedClaim
                || runtime.IsInventoryRecovering)
                return;
            AlchemyRecipeDto recipe = FindRecipe(id);
            if (recipe == null || !IsLearned(id)
                || (selectedRecipeId == id && selection.RecipeId == id
                    && selection.ConfigurationRevision
                        == runtime.ConfigurationRevision))
                return;
            ClosePending();
            selection.Clear();
            selectedRecipeId = id;
            if (IsLearned(id))
                selection.SelectRecipe(recipe, runtime.ConfigurationRevision,
                    session.LocalCharacterDbId);
            Render();
        }

        private AlchemyRecipeDto FindRecipe(string id)
        {
            if (id == null || runtime?.Catalog == null)
                return null;
            foreach (AlchemyRecipeDto recipe in runtime.Catalog)
                if (recipe != null && recipe.recipeId == id)
                    return recipe;
            return null;
        }

        private void RebuildSlots()
        {
            ClearSlots();
            if (materialContent == null || materialPrefab == null)
                return;
            int index = 0;
            foreach (long stackId in selection.SelectedStackIds)
            {
                if (selection.TryGetSelectedStack(stackId, out string itemId,
                    out int count))
                {
                    string identity = "s:" + stackId.ToString(
                        CultureInfo.InvariantCulture);
                    AddMaterialSlot(identity, count, FindItemIcon(itemId),
                        index++);
                }
            }
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            foreach (string identity in selection.UnsupportedIdentities)
            {
                Sprite icon = null;
                if (bag?.Equipment != null)
                    foreach (InventoryEquipmentState equipment in bag.Equipment)
                        if (equipment != null && equipment.Identity == identity
                            && inventory?.ItemDefinitions != null
                            && inventory.ItemDefinitions.TryGetByEquipmentId(
                                equipment.EquipmentId, out ItemDefinition item))
                        {
                            icon = item.Icon;
                            break;
                        }
                AddMaterialSlot(identity, 1, icon, index++);
            }
            RectTransform viewport = materialContent.parent as RectTransform;
            float viewportWidth = viewport != null ? viewport.rect.width : 0f;
            float viewportHeight = viewport != null ? viewport.rect.height : 0f;
            int rows = (index + MaterialColumns - 1) / MaterialColumns;
            materialContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal,
                Mathf.Max(viewportWidth,
                    MaterialColumns * MaterialSlotSize
                        + (MaterialColumns + 1) * MaterialSpacing));
            materialContent.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(viewportHeight,
                    rows * MaterialSlotSize
                        + (rows + 1) * MaterialSpacing));
        }

        private void AddMaterialSlot(string identity, int count, Sprite icon,
            int index)
        {
            AlchemyMaterialSlotView slot = Instantiate(materialPrefab,
                materialContent, false);
            RectTransform rect = (RectTransform)slot.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(MaterialSlotSize, MaterialSlotSize);
            rect.anchoredPosition = new Vector2(
                MaterialSpacing + index % MaterialColumns
                    * (MaterialSlotSize + MaterialSpacing),
                -MaterialSpacing - index / MaterialColumns
                    * (MaterialSlotSize + MaterialSpacing));
            slot.DropRequested += HandleDrop;
            slot.RemoveRequested += RemoveMaterial;
            slot.ReturnToBagRequested += ReturnMaterialToBag;
            slots.Add(slot);
            slot.Bind(identity, count, icon);
        }

        private void ReturnMaterialToBag(string identity, PointerEventData eventData)
        {
            if (IsOverBagSlot(eventData))
                RemoveMaterial(identity);
        }

        private void RefreshSlots()
        {
            RebuildSlots();
        }

        public void OnDrop(PointerEventData eventData)
        {
            RectTransform viewport = materialContent?.parent as RectTransform;
            if (viewport == null || eventData == null
                || !RectTransformUtility.RectangleContainsScreenPoint(
                    viewport, eventData.position, eventData.pressEventCamera))
                return;
            InventoryBagDragSlot source = eventData.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryBagDragSlot>()
                : null;
            if (source != null && !string.IsNullOrEmpty(source.DragIdentity))
                HandleDrop(source);
        }

        private void HandleDrop(InventoryBagDragSlot source)
        {
            if (!HasCurrentData() || !IsLearned(selectedRecipeId)
                || runtime.Snapshot.currentTask != null
                || selection.IsLocked || runtime.HasUnresolvedStart
                || runtime.HasUnresolvedClaim
                || runtime.IsInventoryRecovering)
            {
                GameNoticeService.Show("炼丹条件尚未就绪或丹炉已占用");
                return;
            }
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            if (bag == null || bag.CharacterDbId != session.LocalCharacterDbId
                || bag.InventoryRevision < 0)
            {
                GameNoticeService.Show("背包尚未同步");
                return;
            }
            if (source == null || string.IsNullOrEmpty(source.DragIdentity))
            {
                return;
            }
            if (source.DragIdentity.StartsWith("e:", StringComparison.Ordinal))
            {
                if (selection.SetUnsupportedItem(bag, source.DragIdentity)
                    != AlchemySelectionError.None)
                    GameNoticeService.Show("背包装备已变化，请重新拖入");
                else
                    RefreshSlots();
                return;
            }
            InventoryStackState stack = null;
            foreach (InventoryStackState entry in bag.Stacks)
                if (entry != null && entry.Identity == source.DragIdentity
                    && entry.ItemId == source.ItemId && entry.Count > 0)
                {
                    stack = entry;
                    break;
                }
            if (stack == null)
            {
                GameNoticeService.Show("背包堆叠已变化，请重新拖入");
                return;
            }
            if (quantityDialog == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>(true);
                quantityDialog = AlchemyQuantityConfirmationView.Create(canvas,
                    recipeName != null ? recipeName.font : null);
                if (quantityDialog != null)
                    quantityDialog.Confirmed += ConfirmQuantity;
            }
            if (quantityDialog == null)
                return;
            int suggested = Mathf.Max(1,
                selection.SelectedCountForStack(stack.StackId));
            pendingStackId = stack.StackId;
            pendingInventoryRevision = bag.InventoryRevision;
            pendingConfigurationRevision = runtime.ConfigurationRevision;
            pendingRecipeId = selectedRecipeId;
            pendingItemId = stack.ItemId;
            string name = string.IsNullOrWhiteSpace(source.DisplayName)
                ? stack.ItemId : source.DisplayName;
            quantityDialog.Show(stack.Identity, name, stack.Count, suggested);
        }

        private void ConfirmQuantity(string identity, int amount)
        {
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            if (!HasCurrentData() || selection.IsLocked
                || runtime.HasUnresolvedStart || runtime.HasUnresolvedClaim
                || runtime.IsInventoryRecovering
                || runtime.Snapshot.currentTask != null
                || bag == null
                || bag.CharacterDbId != session.LocalCharacterDbId
                || bag.InventoryRevision != pendingInventoryRevision
                || runtime.ConfigurationRevision != pendingConfigurationRevision
                || selectedRecipeId != pendingRecipeId
                || !IsLearned(selectedRecipeId)
                || string.IsNullOrEmpty(pendingItemId))
            {
                quantityDialog.ShowError("目录或背包已变化，请重新拖入");
                return;
            }
            InventoryStackState stack = null;
            foreach (InventoryStackState entry in bag.Stacks)
                if (entry != null && entry.StackId == pendingStackId
                    && entry.Identity == identity && entry.ItemId == pendingItemId)
                {
                    stack = entry;
                    break;
                }
            if (stack == null || amount <= 0 || amount > stack.Count
                || selection.SetStack(bag, stack.StackId, amount)
                    != AlchemySelectionError.None)
            {
                quantityDialog.ShowError("数量或堆叠已变化，请重新拖入");
                return;
            }
            ClosePending();
            RefreshSlots();
        }

        private void RemoveMaterial(string identity)
        {
            if (!HasCurrentData() || selection.IsLocked
                || runtime.HasUnresolvedStart || runtime.HasUnresolvedClaim
                || runtime.IsInventoryRecovering
                || runtime.Snapshot.currentTask != null)
                return;
            bool removed = identity != null
                && identity.StartsWith("e:", StringComparison.Ordinal)
                ? selection.RemoveUnsupportedItem(identity)
                : identity != null
                    && identity.StartsWith("s:", StringComparison.Ordinal)
                    && long.TryParse(identity.Substring(2), NumberStyles.None,
                        CultureInfo.InvariantCulture, out long stackId)
                    && selection.RemoveStack(stackId);
            if (removed)
            {
                ClosePending();
                RefreshSlots();
            }
        }

        private Sprite FindItemIcon(string itemId)
        {
            return inventory?.ItemDefinitions != null
                && inventory.ItemDefinitions.TryGet(itemId,
                    out ItemDefinition definition)
                ? definition.Icon : null;
        }

        private void RenderRequiredMaterials(AlchemyRecipeDto recipe)
        {
            if (primaryMedicineText == null || recipe?.materials == null)
                return;

            var entries = new List<string>(recipe.materials.Length);
            foreach (AlchemyRecipeMaterialDto material in recipe.materials)
            {
                if (material == null)
                    continue;
                string name = material.itemId;
                if (inventory?.ItemDefinitions != null
                    && inventory.ItemDefinitions.TryGet(material.itemId,
                        out ItemDefinition definition)
                    && !string.IsNullOrWhiteSpace(definition.DisplayName))
                    name = definition.DisplayName;
                entries.Add(name + " x"
                    + material.count.ToString(CultureInfo.InvariantCulture));
            }
            primaryMedicineText.text = string.Join("、", entries);
        }

        private Sprite FindRecipeIcon(AlchemyRecipeDto recipe)
        {
            if (recipe == null || itemCatalog?.ItemCatalog?.items == null)
                return null;
            Sprite sprite = null;
            foreach (CultivationItemDefinitionDto item in itemCatalog.ItemCatalog.items)
            {
                if (item == null || item.kind != "recipebook"
                    || item.learnRecipeId != recipe.recipeId)
                    continue;
                Sprite book = FindItemIcon(item.itemId);
                if (book == null)
                    continue;
                if (sprite != null && sprite != book)
                    return null;
                sprite = book;
            }
            return sprite;
        }

        private void ClosePending()
        {
            if (quantityDialog != null)
                quantityDialog.Hide();
            pendingStackId = 0;
            pendingInventoryRevision = 0;
            pendingConfigurationRevision = 0;
            pendingItemId = null;
            pendingRecipeId = null;
        }

        private void ClearViews()
        {
            ClearRows();
            ClearSlots();
            ClearMedicineTexts();
            if (recipeIcon != null)
            {
                recipeIcon.sprite = null;
                recipeIcon.gameObject.SetActive(false);
            }
            if (recipeName != null)
                recipeName.text = string.Empty;
        }

        private void ClearMedicineTexts()
        {
            if (primaryMedicineText != null)
                primaryMedicineText.text = string.Empty;
            if (secondaryMedicineText != null)
                secondaryMedicineText.text = string.Empty;
            if (auxiliaryMedicineText != null)
                auxiliaryMedicineText.text = string.Empty;
        }

        private void ClearRows()
        {
            foreach (AlchemyRecipeRowView row in rows)
            {
                row.Selected -= SelectRecipe;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
        }

        private void ClearSlots()
        {
            foreach (AlchemyMaterialSlotView slot in slots)
            {
                slot.DropRequested -= HandleDrop;
                slot.RemoveRequested -= RemoveMaterial;
                slot.ReturnToBagRequested -= ReturnMaterialToBag;
                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }
            slots.Clear();
        }
    }
}
