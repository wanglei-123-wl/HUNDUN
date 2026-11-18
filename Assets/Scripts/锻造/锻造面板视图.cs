using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Forging;
using Game.Forging.Protocol;
using Game.Forging.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Forging.Presentation
{
    [DisallowMultipleComponent]
    public sealed class 锻造面板视图 : MonoBehaviour, IDropHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform recipeContent;
        [SerializeField] private 锻造图谱行视图 recipePrefab;
        [SerializeField] private RectTransform materialContent;
        [SerializeField] private 锻造材料格子视图 materialPrefab;
        [SerializeField] private Image recipeIcon;
        [SerializeField] private TMP_Text recipeName;
        [SerializeField] private TMP_Text requiredMaterials;
        [SerializeField] private Button startButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Image taskFill;
        [SerializeField] private TMP_Text taskStatus;
        [SerializeField] private Image resultIcon;

        private readonly 锻造本地选择 selection = new 锻造本地选择();
        private readonly List<锻造图谱> recipes = new List<锻造图谱>();
        private readonly List<锻造图谱行视图> rows = new List<锻造图谱行视图>();
        private readonly List<锻造材料格子视图> slots =
            new List<锻造材料格子视图>();
        private readonly List<RaycastResult> hits = new List<RaycastResult>();

        private GameSession session;
        private InventoryModuleRuntime inventory;
        private 锻造会话运行时 runtime;
        private 锻造配方[] shownCatalog;
        private CultivationItemCatalogResultDto shownItemCatalog;
        private IReadOnlyList<string> shownLearnedRecipeIds;
        private 锻造快照 shownSnapshot;
        private 锻造数量确认弹窗 quantityDialog;
        private 锻造任务展示 task;
        private string recipeId;
        private long lastCharacterId;
        private long lastEpoch;
        private long lastInventoryRevision = -1;
        private string pendingIdentity;
        private string pendingItemId;
        private long pendingInventoryRevision;
        private long pendingCharacterId;
        private string draggingResultTaskId;
        private RectTransform resultPreview;
        private Canvas dragCanvas;
        private Canvas windowCanvas;
        private int normalSortingOrder;
        private TMP_Text generatedTaskStatus;
        private Image generatedTaskFill;

        public event Action<string> 领取请求;
        public IReadOnlyList<锻造已选材料> 已选材料 => selection.Materials;

        public void 设置图谱(IReadOnlyList<锻造图谱> catalog)
        {
            SetBlueprintRows(catalog, true);
        }

        private void SetBlueprintRows(IReadOnlyList<锻造图谱> catalog,
            bool resetSelection)
        {
            recipes.Clear();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (catalog != null)
                foreach (锻造图谱 recipe in catalog)
                    if (recipe != null
                        && !string.IsNullOrWhiteSpace(recipe.recipeId)
                        && ids.Add(recipe.recipeId))
                        recipes.Add(recipe);
            if (FindRecipe(recipeId) == null)
            {
                recipeId = recipes.Find(recipe => recipe.canForge)?.recipeId
                    ?? (recipes.Count > 0 ? recipes[0].recipeId : null);
                resetSelection = true;
            }
            if (resetSelection)
            {
                ClosePending();
                selection.Clear();
            }
            if (isActiveAndEnabled)
                Render();
        }

        public void 设置任务(锻造任务展示 display)
        {
            if (display != null)
                ClosePending();
            task = display;
            if (isActiveAndEnabled)
            {
                RenderTask();
                RefreshStartButton();
            }
        }

        private void OnEnable()
        {
            windowCanvas = GetComponent<Canvas>();
            normalSortingOrder = windowCanvas != null
                ? windowCanvas.sortingOrder : 0;
            EnsureTaskFeedback();
            session = FindObjectOfType<GameSession>();
            inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (session != null)
                session.LocalWorldEntered += HandleWorldEntered;
            long character = session?.LocalCharacterDbId ?? 0;
            long epoch = NetworkManager.Instance != null
                && NetworkManager.Instance.IsConnected
                ? NetworkManager.Instance.ConnectionEpoch : 0;
            if (character != lastCharacterId || epoch != lastEpoch)
            {
                lastEpoch = epoch;
                ClearForCharacter(character);
            }
            BindRuntime();
            领取请求 += ClaimForging;
            if (startButton != null)
                startButton.onClick.AddListener(StartForging);
            if (closeButton != null)
            {
                closeButton.interactable = true;
                closeButton.onClick.AddListener(Close);
            }
            RefreshCanvasSorting();
            Render();
        }

        private void Update()
        {
            RefreshCanvasSorting();
            if (inventory == null)
            {
                inventory = FindObjectOfType<InventoryModuleRuntime>();
                if (inventory != null && shownCatalog != null)
                    HandleCatalogChanged(shownCatalog);
            }
            BindRuntime();
            long character = session?.LocalCharacterDbId ?? 0;
            long epoch = NetworkManager.Instance != null
                && NetworkManager.Instance.IsConnected
                ? NetworkManager.Instance.ConnectionEpoch : 0;
            if (character != lastCharacterId || epoch != lastEpoch)
            {
                lastEpoch = epoch;
                ClearForCharacter(character);
                SyncRuntime();
                Render();
            }
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            long revision = bag?.InventoryRevision ?? -1;
            if (revision != lastInventoryRevision)
            {
                lastInventoryRevision = revision;
                ClosePending();
                if (selection.Sync(bag, character))
                    RenderSlots();
                if (requiredMaterials != null)
                    requiredMaterials.text =
                        FormatRequirements(FindRecipe(recipeId));
                RefreshStartButton();
            }
            TickTask();
            RefreshStartButton();
            if (Input.GetKeyDown(KeyCode.Escape)
                && (quantityDialog == null
                    || (!quantityDialog.gameObject.activeSelf
                        && quantityDialog.LastEscapeFrame != Time.frameCount)))
                Close();
        }

        private void OnDisable()
        {
            if (windowCanvas != null)
                windowCanvas.sortingOrder = normalSortingOrder;
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            if (startButton != null)
                startButton.onClick.RemoveListener(StartForging);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            领取请求 -= ClaimForging;
            ClosePending();
            ClearResultDrag();
            if (generatedTaskStatus != null)
            {
                Destroy(generatedTaskStatus.gameObject);
                taskStatus = null;
                generatedTaskStatus = null;
            }
            if (generatedTaskFill != null)
            {
                Destroy(generatedTaskFill.gameObject);
                taskFill = null;
                generatedTaskFill = null;
            }
            ClearRows();
            ClearSlots();
            UnbindRuntime();
        }

        private void EnsureTaskFeedback()
        {
            if (resultIcon == null || recipeName == null)
                return;
            Transform parent = resultIcon.rectTransform.parent;
            if (parent == null)
                return;
            if (taskStatus == null)
            {
                generatedTaskStatus = Instantiate(recipeName, parent, false);
                generatedTaskStatus.gameObject.name = "锻造任务状态";
                RectTransform rect = generatedTaskStatus.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(440f, 134f);
                rect.sizeDelta = new Vector2(210f, 56f);
                generatedTaskStatus.fontSize = 18f;
                generatedTaskStatus.fontSizeMin = 12f;
                generatedTaskStatus.fontSizeMax = 18f;
                generatedTaskStatus.enableAutoSizing = true;
                generatedTaskStatus.enableWordWrapping = true;
                generatedTaskStatus.alignment = TextAlignmentOptions.Center;
                generatedTaskStatus.raycastTarget = false;
                taskStatus = generatedTaskStatus;
            }
            if (taskFill == null)
            {
                generatedTaskFill = Instantiate(resultIcon, parent, false);
                generatedTaskFill.gameObject.name = "锻造任务进度";
                RectTransform rect = generatedTaskFill.rectTransform;
                rect.anchorMin = rect.anchorMax =
                    new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(350f, 97f);
                rect.sizeDelta = new Vector2(180f, 6f);
                generatedTaskFill.color = new Color(0.12f, 0.48f, 0.42f, 1f);
                generatedTaskFill.type = Image.Type.Simple;
                generatedTaskFill.raycastTarget = false;
                taskFill = generatedTaskFill;
            }
        }

        private void RefreshCanvasSorting()
        {
            if (windowCanvas == null)
                return;
            InventoryPanelView bag = InventoryPanelView.ActivePanel;
            Canvas bagCanvas = bag != null && bag.isActiveAndEnabled
                ? bag.GetComponentInParent<Canvas>()?.rootCanvas : null;
            int order = bagCanvas != null
                ? Mathf.Min(normalSortingOrder, bagCanvas.sortingOrder - 1)
                : normalSortingOrder;
            if (windowCanvas.sortingOrder != order)
                windowCanvas.sortingOrder = order;
        }

        private void OnDestroy()
        {
            if (quantityDialog == null)
                return;
            quantityDialog.Confirmed -= ConfirmQuantity;
            quantityDialog.Dispose();
            quantityDialog = null;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            lastEpoch = epoch;
            ClearForCharacter(characterDbId);
            SyncRuntime();
            Render();
        }

        private void ClearForCharacter(long characterDbId)
        {
            lastCharacterId = characterDbId;
            lastInventoryRevision = -1;
            selection.Clear();
            task = null;
            recipes.Clear();
            recipeId = null;
            shownCatalog = null;
            shownItemCatalog = null;
            shownLearnedRecipeIds = null;
            shownSnapshot = null;
            ClosePending();
        }

        private void BindRuntime()
        {
            锻造会话运行时 current = 锻造会话运行时.Instance;
            if (runtime != current)
            {
                UnbindRuntime();
                runtime = current;
                if (runtime != null)
                {
                    runtime.CatalogChanged += HandleCatalogChanged;
                    runtime.SnapshotChanged += HandleSnapshotChanged;
                    runtime.LearnedRecipesChanged += HandleLearnedRecipesChanged;
                    runtime.StartStateChanged += RefreshStartButton;
                    runtime.ClaimStateChanged += HandleClaimStateChanged;
                }
            }
            SyncRuntime();
        }

        private void UnbindRuntime()
        {
            if (runtime != null)
            {
                runtime.CatalogChanged -= HandleCatalogChanged;
                runtime.SnapshotChanged -= HandleSnapshotChanged;
                runtime.LearnedRecipesChanged -= HandleLearnedRecipesChanged;
                runtime.StartStateChanged -= RefreshStartButton;
                runtime.ClaimStateChanged -= HandleClaimStateChanged;
            }
            runtime = null;
        }

        private void SyncRuntime()
        {
            if (runtime == null || !runtime.IsSessionActive
                || (session?.LocalCharacterDbId ?? 0) <= 0)
                return;
            if (runtime.IsCatalogCurrent && (shownCatalog != runtime.Catalog
                || (runtime.UsesBlueprintLearning
                    && (shownLearnedRecipeIds != runtime.LearnedRecipeIds
                        || shownItemCatalog != CultivationSessionRuntime.Instance?.ItemCatalog))))
                HandleCatalogChanged(runtime.Catalog);
            if (runtime.IsSnapshotCurrent && shownSnapshot != runtime.Snapshot)
                HandleSnapshotChanged(runtime.Snapshot);
        }

        private void HandleClaimStateChanged()
        {
            RefreshStartButton();
        }

        private void ClaimForging(string taskId)
        {
            if (runtime == null
                || !runtime.TryClaim(taskId, inventory?.CurrentSnapshot))
                GameNoticeService.Show("锻造任务或背包已变化，请刷新后重新领取");
        }

        private void HandleCatalogChanged(锻造配方[] catalog)
        {
            bool changed = shownCatalog != catalog;
            shownCatalog = catalog;
            RefreshBlueprints(changed);
        }

        private void RefreshBlueprints(bool resetSelection)
        {
            CultivationSessionRuntime itemRuntime = CultivationSessionRuntime.Instance;
            shownItemCatalog = itemRuntime?.ItemCatalog;
            shownLearnedRecipeIds = runtime?.LearnedRecipeIds;
            bool usesBlueprintLearning = runtime?.UsesBlueprintLearning == true;
            IReadOnlyList<锻造图谱> display = 锻造图谱列表.Build(shownCatalog,
                shownLearnedRecipeIds, usesBlueprintLearning, shownItemCatalog?.items,
                inventory?.ItemDefinitions);
            SetBlueprintRows(display, resetSelection);
            if (usesBlueprintLearning && runtime.IsCatalogCurrent && shownItemCatalog != null)
                foreach (锻造图谱 row in display)
                    if (row.canForge && !row.hasBlueprintAssociation)
                        itemRuntime.QueueMissingForgingBlueprint(row.recipeId);
        }

        private void HandleSnapshotChanged(锻造快照 snapshot)
        {
            shownSnapshot = snapshot;
            锻造任务 current = snapshot?.currentTask;
            if (current == null)
            {
                设置任务(null);
                return;
            }
            selection.Clear();
            RenderSlots();
            bool ready = current.status == "ready";
            设置任务(new 锻造任务展示
            {
                taskId = current.taskId,
                ready = ready,
                succeeded = ready && current.succeeded == true,
                resultName = string.IsNullOrWhiteSpace(current.recipeName)
                    ? current.recipeId : current.recipeName,
                resultIcon = ready && current.succeeded == true
                    ? FindIcon(current.outputItemId) : null,
                outputCount = current.outputCount
            });
            TickTask();
        }

        private void HandleLearnedRecipesChanged()
        {
            RefreshBlueprints(false);
        }

        private void TickTask()
        {
            锻造任务 current = shownSnapshot?.currentTask;
            if (task == null || current == null || current.status != "active")
                return;
            long now = runtime?.EstimatedServerTime ?? 0;
            if (now <= 0)
                return;
            long remaining = Math.Max(0,
                (current.endsAtUnixMilliseconds - now + 999) / 1000);
            long duration = current.endsAtUnixMilliseconds
                - current.startedAtUnixMilliseconds;
            task.progress = duration > 0
                ? Mathf.Clamp01((float)(now
                    - current.startedAtUnixMilliseconds) / duration) : 0f;
            task.status = "锻造中 "
                + (remaining / 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (remaining % 60).ToString("00",
                    CultureInfo.InvariantCulture);
            RenderTask();
        }

        private void Render()
        {
            ClearRows();
            if (recipeContent != null && recipePrefab != null)
            {
                for (int index = 0; index < recipes.Count; index++)
                {
                    锻造图谱 recipe = recipes[index];
                    锻造图谱行视图 row = Instantiate(recipePrefab,
                        recipeContent, false);
                    row.gameObject.name = "锻造图谱行";
                    RectTransform rect = (RectTransform)row.transform;
                    rect.anchorMin = rect.anchorMax = rect.pivot =
                        new Vector2(0f, 1f);
                    rect.sizeDelta = new Vector2(
                        Mathf.Max(100f, recipeContent.rect.width - 8f), 56f);
                    rect.anchoredPosition = new Vector2(4f, -4f - index * 60f);
                    row.Bind(recipe, recipe.recipeId == recipeId);
                    row.Selected += SelectRecipe;
                    rows.Add(row);
                }
                recipeContent.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    Mathf.Max(0f, 8f + recipes.Count * 60f));
            }

            锻造图谱 current = FindRecipe(recipeId);
            if (recipeName != null)
            {
                if (current != null)
                    recipeName.text = current.name;
                else if (shownCatalog == null || runtime == null
                    || (runtime.UsesBlueprintLearning && runtime.LearnedRecipeIds == null))
                    recipeName.text = "图谱同步中";
                else
                    recipeName.text = runtime.UsesBlueprintLearning
                        ? "暂无已学图谱" : "暂无可用图谱";
            }
            if (recipeIcon != null)
            {
                recipeIcon.sprite = current?.icon;
                recipeIcon.color = Color.white;
                recipeIcon.preserveAspect = true;
                recipeIcon.gameObject.SetActive(recipeIcon.sprite != null);
            }
            if (requiredMaterials != null)
                requiredMaterials.text = FormatRequirements(current);
            RenderSlots();
            RenderTask();
            RefreshStartButton();
        }

        private string FormatRequirements(锻造图谱 recipe)
        {
            if (recipe?.materials == null)
                return string.Empty;
            var lines = new List<string>();
            foreach (锻造材料需求 material in recipe.materials)
            {
                if (material == null)
                    continue;
                string name = material.itemId;
                if (inventory?.ItemDefinitions != null
                    && inventory.ItemDefinitions.TryGet(material.itemId,
                        out ItemDefinition item)
                    && !string.IsNullOrWhiteSpace(item.DisplayName))
                    name = item.DisplayName;
                string balance = string.Empty;
                if (material.isCurrency)
                {
                    long amount = 0;
                    bool available = inventory?.CurrentSnapshot != null
                        && inventory.CurrentSnapshot.CharacterDbId
                            == (session?.LocalCharacterDbId ?? 0)
                        && inventory.CurrentSnapshot.Wallets.TryGetValue(
                            material.itemId, out amount);
                    balance = "（钱包 "
                        + (available
                            ? amount.ToString(CultureInfo.InvariantCulture)
                            : "--") + "）";
                }
                lines.Add(name + " x"
                    + material.count.ToString(CultureInfo.InvariantCulture)
                    + balance);
            }
            return string.Join("\n", lines);
        }

        private void SelectRecipe(string id)
        {
            if (task != null || recipeId == id || FindRecipe(id) == null)
                return;
            recipeId = id;
            selection.Clear();
            ClosePending();
            Render();
        }

        private 锻造图谱 FindRecipe(string id)
        {
            if (id == null)
                return null;
            foreach (锻造图谱 recipe in recipes)
                if (recipe.recipeId == id)
                    return recipe;
            return null;
        }

        private void RenderSlots()
        {
            ClearSlots();
            if (materialContent == null || materialPrefab == null)
                return;
            IReadOnlyList<锻造已选材料> materials = selection.Materials;
            for (int index = 0; index < materials.Count; index++)
            {
                锻造已选材料 material = materials[index];
                锻造材料格子视图 slot = Instantiate(materialPrefab,
                    materialContent, false);
                slot.gameObject.name = "锻造材料格子";
                RectTransform rect = (RectTransform)slot.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(50f, 50f);
                rect.anchoredPosition = new Vector2(
                    8f + index % 8 * 58f, -8f - index / 8 * 58f);
                slot.Bind(material.Identity, material.Count,
                    FindIcon(material.ItemId));
                slot.DropRequested += HandleDrop;
                slot.RemoveRequested += RemoveMaterial;
                slot.ReturnRequested += ReturnMaterial;
                slots.Add(slot);
            }
            materialContent.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(144f, 16f + (materials.Count + 7) / 8 * 58f));
            RefreshStartButton();
        }

        private Sprite FindIcon(string itemId)
        {
            return inventory?.ItemDefinitions != null
                && inventory.ItemDefinitions.TryGet(itemId,
                    out ItemDefinition item) ? item.Icon : null;
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
            if (source != null)
                HandleDrop(source);
        }

        private void HandleDrop(InventoryBagDragSlot source)
        {
            if (FindRecipe(recipeId) == null || task != null
                || !FindRecipe(recipeId).canForge
                || runtime == null || !runtime.IsCatalogCurrent
                || runtime.HasUnresolvedStart
                || runtime.HasUnresolvedLearn
                || !runtime.IsRecipeLearned(recipeId))
            {
                GameNoticeService.Show("图谱尚未就绪或锻造任务已占用");
                return;
            }
            if (source == null || source.DragIdentity == null
                || !source.DragIdentity.StartsWith("s:", StringComparison.Ordinal))
            {
                GameNoticeService.Show("请从背包拖入材料");
                return;
            }
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            long character = session?.LocalCharacterDbId ?? 0;
            InventoryStackState stack = null;
            if (character > 0 && bag?.CharacterDbId == character)
                foreach (InventoryStackState candidate in bag.Stacks)
                    if (candidate != null
                        && candidate.Identity == source.DragIdentity
                        && candidate.ItemId == source.ItemId
                        && candidate.Count > 0)
                    {
                        stack = candidate;
                        break;
                    }
            if (stack == null)
            {
                GameNoticeService.Show("背包材料已变化，请重新拖入");
                return;
            }
            bool required = false;
            foreach (锻造材料需求 material in FindRecipe(recipeId).materials)
                if (!material.isCurrency && material.itemId == stack.ItemId)
                {
                    required = true;
                    break;
                }
            if (!required)
            {
                GameNoticeService.Show("该物品不是当前锻造图谱的背包材料");
                return;
            }
            if (quantityDialog == null)
            {
                quantityDialog = 锻造数量确认弹窗.Create(
                    GetComponentInParent<Canvas>(true), recipeName?.font);
                if (quantityDialog != null)
                    quantityDialog.Confirmed += ConfirmQuantity;
            }
            if (quantityDialog == null)
                return;
            pendingIdentity = stack.Identity;
            pendingItemId = stack.ItemId;
            pendingInventoryRevision = bag.InventoryRevision;
            pendingCharacterId = character;
            int suggested = 1;
            foreach (锻造已选材料 material in selection.Materials)
                if (material.Identity == stack.Identity)
                    suggested = material.Count;
            quantityDialog.Show(stack.Identity,
                string.IsNullOrWhiteSpace(source.DisplayName)
                    ? stack.ItemId : source.DisplayName,
                stack.Count, suggested);
        }

        private void ConfirmQuantity(string identity, int amount)
        {
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            if (task != null || FindRecipe(recipeId) == null
                || !FindRecipe(recipeId).canForge
                || runtime == null || runtime.HasUnresolvedStart
                || runtime.HasUnresolvedLearn
                || !runtime.IsRecipeLearned(recipeId)
                || identity != pendingIdentity || bag == null
                || bag.InventoryRevision != pendingInventoryRevision
                || bag.CharacterDbId != pendingCharacterId
                || (session?.LocalCharacterDbId ?? 0) != pendingCharacterId
                || !selection.TrySet(bag, pendingCharacterId,
                    identity, pendingItemId, amount))
            {
                quantityDialog.ShowError("数量或背包已变化，请重新拖入");
                return;
            }
            ClosePending();
            RenderSlots();
        }

        private void RemoveMaterial(string identity)
        {
            if (task != null || runtime?.HasUnresolvedStart == true
                || !selection.Remove(identity))
                return;
            ClosePending();
            RenderSlots();
        }

        private void ReturnMaterial(string identity, PointerEventData eventData)
        {
            if (IsOverBagSlot(eventData))
                RemoveMaterial(identity);
        }

        private bool IsOverBagSlot(PointerEventData eventData)
        {
            InventoryPanelView bag = InventoryPanelView.ActivePanel;
            if (eventData == null || EventSystem.current == null
                || bag?.GridView == null || !bag.gameObject.activeInHierarchy)
                return false;
            hits.Clear();
            EventSystem.current.RaycastAll(eventData, hits);
            if (hits.Count == 0)
                return false;
            InventorySlotView slot = hits[0].gameObject
                ?.GetComponentInParent<InventorySlotView>();
            return slot != null && slot.isActiveAndEnabled
                && slot.DisplayIndex >= 0
                && bag.GridView.GetSlot(slot.DisplayIndex) == slot;
        }

        private void RefreshStartButton()
        {
            if (startButton == null)
                return;
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            long character = session?.LocalCharacterDbId ?? 0;
            startButton.interactable = character > 0
                && bag != null && bag.CharacterDbId == character
                && runtime != null && runtime.IsCatalogCurrent
                && runtime.ConfigurationRevision > 0
                && runtime.IsSnapshotCurrent
                && !runtime.HasUnresolvedStart
                && !runtime.HasUnresolvedClaim
                && !runtime.HasUnresolvedLearn
                && runtime.IsRecipeLearned(recipeId)
                && task == null && FindRecipe(recipeId)?.canForge == true
                && selection.IsCurrent(bag, character)
                && selection.Matches(FindRecipe(recipeId), bag);
        }

        private void StartForging()
        {
            InventoryOwnershipSnapshot bag = inventory?.CurrentSnapshot;
            锻造图谱 recipe = FindRecipe(recipeId);
            long character = session?.LocalCharacterDbId ?? 0;
            if (bag == null || bag.CharacterDbId != character
                || recipe == null || !recipe.canForge || runtime == null
                || !runtime.IsRecipeLearned(recipeId)
                || selection.Sync(bag, character)
                || !selection.Matches(recipe, bag))
            {
                RenderSlots();
                GameNoticeService.Show("图谱或材料已变化，请重新确认");
                return;
            }
            if (runtime == null || !runtime.TryStart(new 锻造开始意向(character,
                bag.InventoryRevision, recipe.recipeId,
                new List<锻造已选材料>(selection.Materials).AsReadOnly()), bag))
                GameNoticeService.Show("锻造状态已变化，请刷新后重新确认");
        }

        private void RenderTask()
        {
            if (taskFill != null)
            {
                taskFill.gameObject.SetActive(task != null && !task.ready);
                taskFill.fillAmount = task != null
                    ? Mathf.Clamp01(task.progress) : 0f;
                if (generatedTaskFill != null)
                    generatedTaskFill.rectTransform.SetSizeWithCurrentAnchors(
                        RectTransform.Axis.Horizontal,
                        180f * taskFill.fillAmount);
            }
            if (taskStatus != null)
            {
                taskStatus.text = task == null ? string.Empty
                    : task.ready
                        ? task.succeeded
                            ? task.resultName + " x"
                                + task.outputCount.ToString(
                                    CultureInfo.InvariantCulture) + "（待领取）"
                            : "锻造失败（待确认）"
                    : task.status ?? string.Empty;
                taskStatus.gameObject.SetActive(task != null);
            }
            if (resultIcon != null)
            {
                resultIcon.sprite = task != null && task.ready && task.succeeded
                    ? task.resultIcon : null;
                resultIcon.color = Color.white;
                resultIcon.preserveAspect = true;
                resultIcon.raycastTarget = resultIcon.sprite != null;
                resultIcon.gameObject.SetActive(resultIcon.sprite != null);
            }
            if (taskStatus != null)
                taskStatus.raycastTarget = task != null && task.ready
                    && (resultIcon == null || resultIcon.sprite == null);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            bool hasIcon = resultIcon != null && resultIcon.sprite != null;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || task == null || !task.ready
                || runtime == null || runtime.HasUnresolvedClaim
                || string.IsNullOrWhiteSpace(task.taskId)
                || eventData.pointerPressRaycast.gameObject
                    != (hasIcon ? resultIcon.gameObject : taskStatus?.gameObject)
                || InventoryPanelView.ActivePanel == null
                || !InventoryDragPreview.TryCreate(this, "拖拽锻造成品",
                    out resultPreview, out dragCanvas))
                return;
            draggingResultTaskId = task.taskId;
            Image image = resultPreview.GetComponent<Image>();
            if (hasIcon)
            {
                resultPreview.sizeDelta = resultIcon.rectTransform.rect.size;
                image.sprite = resultIcon.sprite;
                image.color = new Color(1f, 1f, 1f, 0.8f);
            }
            else
            {
                resultPreview.sizeDelta = new Vector2(160f, 36f);
                image.color = Color.clear;
                TMP_Text label = Instantiate(taskStatus, resultPreview, false);
                label.name = "锻造成品状态";
                label.text = task.succeeded ? task.resultName : "锻造失败";
                label.alignment = TextAlignmentOptions.Center;
                label.raycastTarget = false;
                RectTransform rect = label.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
            }
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null)
                InventoryDragPreview.Move(resultPreview, dragCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            string taskId = draggingResultTaskId;
            bool overBag = taskId != null && IsOverBagSlot(eventData);
            ClearResultDrag();
            if (!overBag || task == null || !task.ready || task.taskId != taskId)
                return;
            if (领取请求 == null)
                GameNoticeService.Show("锻造领取服务尚未接入");
            else
                领取请求.Invoke(taskId);
        }

        private void ClearResultDrag()
        {
            if (resultPreview != null)
                Destroy(resultPreview.gameObject);
            resultPreview = null;
            dragCanvas = null;
            draggingResultTaskId = null;
        }

        private void ClosePending()
        {
            if (quantityDialog != null)
                quantityDialog.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingInventoryRevision = 0;
            pendingCharacterId = 0;
        }

        private void Close()
        {
            gameObject.SetActive(false);
        }

        private void ClearRows()
        {
            foreach (锻造图谱行视图 row in rows)
            {
                row.Selected -= SelectRecipe;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
        }

        private void ClearSlots()
        {
            foreach (锻造材料格子视图 slot in slots)
            {
                slot.DropRequested -= HandleDrop;
                slot.RemoveRequested -= RemoveMaterial;
                slot.ReturnRequested -= ReturnMaterial;
                slot.gameObject.SetActive(false);
                Destroy(slot.gameObject);
            }
            slots.Clear();
        }
    }
}
