using System.Globalization;
using System.Text;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CultivationProgressPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private TMP_Text progressText;
        [SerializeField] private Image cultivationFill;
        [SerializeField] private TMP_Text nextRealmText;

        private RefiningSessionRuntime refining;
        private ScrollRect materialScroll;
        private TMP_Text materialText;
        private string displayedMaterials;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (progressText != null)
            {
                progressText.enableAutoSizing = true;
                progressText.fontSizeMin = 8f;
                progressText.fontSizeMax = 36f;
                progressText.enableWordWrapping = false;
            }
            if (session != null)
            {
                session.LocalProgressionSnapshotChanged += HandleProgression;
                session.LocalWorldEntered += HandleWorldEntered;
            }
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected += HandleDisconnected;
            CreateMaterialList();
            BindRefining();
            Render();
            if (session != null && session.LocalProgressionSnapshot == null)
                session.RequestProgressionSnapshot();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.LocalProgressionSnapshotChanged -= HandleProgression;
                session.LocalWorldEntered -= HandleWorldEntered;
            }
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected -= HandleDisconnected;
            if (refining != null)
                refining.CatalogChanged -= HandleCatalog;
            refining = null;
            displayedMaterials = null;
        }

        private void Update()
        {
            BindRefining();
            Render();
        }

        private void OnDestroy()
        {
            if (materialScroll != null)
                Destroy(materialScroll.gameObject);
        }

        private void BindRefining()
        {
            RefiningSessionRuntime current = RefiningSessionRuntime.Instance;
            if (current == refining)
                return;
            if (refining != null)
                refining.CatalogChanged -= HandleCatalog;
            refining = current;
            if (refining != null)
                refining.CatalogChanged += HandleCatalog;
            Render();
        }

        private void Render()
        {
            ProgressionSnapshotResult progression = session != null
                ? session.LocalProgressionSnapshot : null;
            long characterDbId = session != null
                ? session.LocalCharacterDbId : 0;
            CultivationProgressDisplay display =
                CultivationProgressDisplay.FromSnapshot(progression,
                    characterDbId, session != null && session.IsWorldReady);
            bool valid = display.IsValid;
            RefiningBreakthroughDto path = null;
            if (valid && progression.hasNextJingjie)
            {
                RefiningBreakthroughConditions.TryFindPath(
                    refining != null ? refining.Catalog : null,
                    progression, characterDbId, out path);
                if (!RefiningBreakthroughConditions.MatchesNextRealm(
                    path, progression))
                    path = null;
            }
            if (progressText != null && progressText.text != display.Progress)
                progressText.text = display.Progress;
            if (cultivationFill != null)
            {
                if (cultivationFill.fillAmount != display.FillAmount)
                    cultivationFill.fillAmount = display.FillAmount;
            }
            ShowMaterials(path, characterDbId);
        }

        private void ShowMaterials(RefiningBreakthroughDto path, long characterDbId)
        {
            if (materialText == null)
                return;
            string label;
            if (path == null)
                label = "突破材料 --";
            else if (path.materials.Length == 0)
                label = string.Empty;
            else
            {
                var lines = new StringBuilder();
                InventoryOwnershipSnapshot ownership =
                    inventory != null ? inventory.CurrentSnapshot : null;
                ItemDefinitionTable definitions =
                    inventory != null ? inventory.ItemDefinitions : null;
                foreach (RefiningMaterialDto material in path.materials)
                {
                    if (material == null)
                        continue;
                    if (lines.Length > 0)
                        lines.Append('\n');
                    string name = material.itemId;
                    if (definitions != null
                        && definitions.TryGet(material.itemId,
                            out ItemDefinition definition)
                        && !string.IsNullOrWhiteSpace(definition.DisplayName))
                        name = definition.DisplayName;
                    lines.Append(name).Append(' ');
                    if (ownership != null
                        && ownership.CharacterDbId == characterDbId)
                    {
                        ownership.TryGetItemCount(material.itemId,
                            out long owned);
                        lines.Append(owned.ToString(CultureInfo.InvariantCulture));
                    }
                    else
                        lines.Append("--");
                    lines.Append('/').Append(material.count.ToString(
                        CultureInfo.InvariantCulture));
                }
                label = lines.ToString();
            }
            if (displayedMaterials == label)
                return;
            displayedMaterials = label;
            materialText.text = label;
            RectTransform content = materialText.rectTransform;
            float preferredHeight = materialText.GetPreferredValues(label,
                204f, 0f).y;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(36f, preferredHeight + 2f));
            materialScroll.verticalNormalizedPosition = 1f;
        }

        private void CreateMaterialList()
        {
            if (materialScroll != null || nextRealmText == null)
                return;
            GameObject view = new GameObject("突破材料列表", typeof(RectTransform),
                typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
            view.transform.SetParent(nextRealmText.transform.parent, false);
            RectTransform viewport = view.GetComponent<RectTransform>();
            viewport.anchorMin = viewport.anchorMax = new Vector2(0.5f, 0.5f);
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.anchoredPosition = new Vector2(
                nextRealmText.rectTransform.anchoredPosition.x, -52f);
            viewport.sizeDelta = new Vector2(210f, 36f);
            Image graphic = view.GetComponent<Image>();
            graphic.color = Color.clear;
            graphic.raycastTarget = true;
            materialText = Instantiate(nextRealmText, view.transform);
            materialText.gameObject.name = "突破材料文本";
            materialText.raycastTarget = false;
            materialText.alignment = TextAlignmentOptions.TopLeft;
            materialText.enableAutoSizing = false;
            materialText.fontSize = 11f;
            materialText.enableWordWrapping = true;
            materialText.overflowMode = TextOverflowModes.Overflow;
            RectTransform content = materialText.rectTransform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = new Vector2(3f, 0f);
            content.sizeDelta = new Vector2(-6f, 36f);
            materialScroll = view.GetComponent<ScrollRect>();
            materialScroll.viewport = viewport;
            materialScroll.content = content;
            materialScroll.horizontal = false;
            materialScroll.vertical = true;
            materialScroll.movementType = ScrollRect.MovementType.Clamped;
            materialScroll.scrollSensitivity = 30f;
        }

        private void HandleProgression(ProgressionSnapshotResult result)
        {
            Render();
        }

        private void HandleCatalog(RefiningResultDto result)
        {
            Render();
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            Render();
        }

        private void HandleDisconnected(string reason)
        {
            if (progressText != null)
                progressText.text = "--/--";
            if (cultivationFill != null)
                cultivationFill.fillAmount = 0f;
            displayedMaterials = null;
            if (materialText != null)
                materialText.text = "突破材料 --";
        }
    }
}
