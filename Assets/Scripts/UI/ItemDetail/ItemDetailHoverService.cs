using System;
using System.Collections.Generic;
using Game.Items;
using Game.UI.Notice;
using UnityEngine;

namespace Game.UI.ItemDetail
{
    [DisallowMultipleComponent]
    public sealed class ItemDetailHoverService : MonoBehaviour
    {
        private const int MaxOpenPanels = 5;

        public static ItemDetailHoverService Active { get; private set; }

        [SerializeField] private ItemDetailPanelController detailPanel;

        public static bool IsConfiguredInScene(Component source)
        {
            if (source == null)
                return false;

            ItemDetailHoverService[] services =
                FindObjectsOfType<ItemDetailHoverService>(true);
            foreach (ItemDetailHoverService service in services)
                if (service != null
                    && service.gameObject.scene == source.gameObject.scene)
                    return true;

            return false;
        }

        private readonly Dictionary<ItemDetailHoverTrigger, ItemDetailPanelController>
            openPanels = new Dictionary<ItemDetailHoverTrigger, ItemDetailPanelController>();
        private readonly List<ItemDetailPanelController> panels =
            new List<ItemDetailPanelController>();

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Active = this;
        }

        private void Update()
        {
            if (openPanels.Count > 0 && Input.GetKeyDown(KeyCode.Escape))
                HideAll();
        }

        private void OnDisable()
        {
            foreach (ItemDetailHoverTrigger trigger in openPanels.Keys)
                if (trigger != null)
                    trigger.NotifyServiceHidden(this);
            HideAll();
            foreach (ItemDetailPanelController panel in panels)
            {
                if (panel == null)
                    continue;
                panel.CloseRequested -= HandleCloseRequested;
                if (panel != detailPanel)
                    Destroy(panel.gameObject);
            }
            panels.Clear();
            if (Active == this)
                Active = null;
        }

        public void Show(
            ItemDetailHoverTrigger trigger,
            ItemDetailData detail,
            Vector2 screenPosition)
        {
            if (trigger == null || detail == null || !detail.IsValid)
                return;

            ResolveReferences();
            if (detailPanel == null)
                return;

            if (openPanels.ContainsKey(trigger))
            {
                Hide(trigger);
                return;
            }

            ItemDetailPanelController panel = GetAvailablePanel();
            if (panel == null)
            {
                if (openPanels.Count >= MaxOpenPanels)
                    GameNoticeService.Show("最多同时打开 5 个详情窗口");
                return;
            }

            panel.Show(detail, screenPosition);
            if (!panel.IsVisible)
                return;
            panel.transform.SetAsLastSibling();
            openPanels.Add(trigger, panel);
        }

        public void Hide(ItemDetailHoverTrigger trigger)
        {
            if (trigger == null
                || !openPanels.TryGetValue(trigger, out ItemDetailPanelController panel))
                return;

            openPanels.Remove(trigger);
            if (panel != null)
                panel.Hide();
        }

        public void HideIfDetailChanged(
            ItemDetailHoverTrigger trigger, ItemDetailData detail)
        {
            if (trigger == null
                || !openPanels.TryGetValue(trigger, out ItemDetailPanelController panel))
                return;

            if (panel == null || detail == null || !detail.IsValid
                || !string.Equals(panel.CurrentDetail?.Identity, detail.Identity,
                    StringComparison.Ordinal))
                Hide(trigger);
        }

        public void HideIfDetailChanged(
            ItemDetailHoverTrigger trigger, Func<ItemDetailData> provider)
        {
            if (trigger != null && openPanels.ContainsKey(trigger))
                HideIfDetailChanged(trigger, provider == null ? null : provider());
        }

        public void HideAll()
        {
            foreach (ItemDetailPanelController panel in openPanels.Values)
                if (panel != null)
                    panel.Hide();
            openPanels.Clear();
        }

        private void HandleCloseRequested(ItemDetailPanelController panel)
        {
            ItemDetailHoverTrigger trigger = null;
            foreach (KeyValuePair<ItemDetailHoverTrigger, ItemDetailPanelController> entry
                in openPanels)
            {
                if (entry.Value != panel)
                    continue;
                trigger = entry.Key;
                break;
            }
            if (trigger != null)
                Hide(trigger);
        }

        private ItemDetailPanelController GetAvailablePanel()
        {
            if (detailPanel == null)
                return null;

            if (panels.Count == 0)
            {
                panels.Add(detailPanel);
                detailPanel.CloseRequested += HandleCloseRequested;
            }
            foreach (ItemDetailPanelController panel in panels)
                if (panel != null && !openPanels.ContainsValue(panel))
                    return panel;

            if (panels.Count >= MaxOpenPanels)
                return null;

            ItemDetailPanelController copy =
                Instantiate(detailPanel, detailPanel.transform.parent);
            copy.CloseRequested += HandleCloseRequested;
            panels.Add(copy);
            return copy;
        }

        private void ResolveReferences()
        {
            if (detailPanel == null)
                detailPanel = GetComponent<ItemDetailPanelController>();

            if (detailPanel != null)
                return;

            ItemDetailPanelController[] panels =
                FindObjectsOfType<ItemDetailPanelController>(true);
            for (int index = 0; index < panels.Length; index++)
            {
                ItemDetailPanelController candidate = panels[index];
                if (candidate == null
                    || candidate.gameObject.name.EndsWith("(Clone)",
                        StringComparison.Ordinal))
                    continue;

                detailPanel = candidate;
                break;
            }
        }
    }
}
