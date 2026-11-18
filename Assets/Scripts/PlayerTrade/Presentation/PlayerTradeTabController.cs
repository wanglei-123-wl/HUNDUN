using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeTabController : MonoBehaviour
    {
        [Serializable]
        private sealed class ExtendedPageRoute
        {
            [SerializeField, InspectorName("页面名称")]
            private string pageName = "新页面";
            [SerializeField, InspectorName("页面按钮")]
            private Button tabButton;
            [SerializeField, InspectorName("页面内容区域")]
            private GameObject pagePanel;
            [SerializeField, InspectorName("页面按钮名称")]
            private string tabButtonName;
            [SerializeField, InspectorName("页面内容名称")]
            private string pagePanelName;

            public Button TabButton => tabButton;
            public GameObject PagePanel => pagePanel;
            public string PageName => pageName;

            public void Resolve(Transform root)
            {
                if (tabButton == null)
                    tabButton = FindChild<Button>(root, tabButtonName);
                if (pagePanel == null)
                    pagePanel = FindObjectByName(root, pagePanelName);
            }
        }

        [Header("页签按钮")]
        [SerializeField, InspectorName("购买按钮")]
        private Button marketTabButton;
        [SerializeField, InspectorName("我的挂单按钮")]
        private Button ownedTabButton;

        [Header("页签内容")]
        [SerializeField, InspectorName("市场购买区域")]
        private GameObject marketPanel;
        [SerializeField, InspectorName("我的挂单区域")]
        private GameObject ownedPanel;
        [SerializeField, InspectorName("市场购买区域名称")]
        private string marketPanelName = "市场购买滚动区域";
        [SerializeField, InspectorName("我的挂单区域名称")]
        private string ownedPanelName = "挂单滚动区域";

        [Header("可扩展页面")]
        [SerializeField, InspectorName("扩展页面列表")]
        private List<ExtendedPageRoute> extendedPages =
            new List<ExtendedPageRoute>();

        private bool subscribed;
        private PlayerTradeSellPanelController sellPanelController;
        private readonly Dictionary<ExtendedPageRoute, UnityAction>
            extendedPageHandlers =
                new Dictionary<ExtendedPageRoute, UnityAction>();

        private void Awake()
        {
            ResolveReferences();
            Subscribe();
            Debug.Log("[PlayerTrade][UI][Awake] controller=" + name
                + " marketPanel=" + DescribeObject(marketPanel)
                + " ownedPanel=" + DescribeObject(ownedPanel),
                this);
            ShowMarket();
        }

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            Debug.Log("[PlayerTrade][UI][OnEnable] controller=" + name
                + " activePage=" + DescribeActivePage(), this);
        }

        private void OnDisable()
        {
            Debug.Log("[PlayerTrade][UI][OnDisable] controller=" + name
                + " activePage=" + DescribeActivePage(), this);
            Unsubscribe();
        }

        public void ShowMarket()
        {
            Debug.Log("[PlayerTrade][UI][ShowMarket] caller=explicit",
                this);
            ShowPage(marketPanel);
        }

        public void ShowOwned()
        {
            Debug.Log("[PlayerTrade][UI][ShowOwned] caller=explicit",
                this);
            ShowPage(ownedPanel);
        }

        public void ShowExtendedPage(int index)
        {
            ResolveReferences();
            if (extendedPages == null
                || index < 0
                || index >= extendedPages.Count
                || extendedPages[index] == null)
            {
                Debug.LogWarning("[玩家交易] 找不到扩展页面，索引=" + index,
                    this);
                return;
            }

            ShowPage(extendedPages[index].PagePanel);
        }

        public void CloseSellPanelForNavigation()
        {
            ResolveSellPanel();
            if (sellPanelController != null)
                sellPanelController.CloseForNavigation();
        }

        public void BringNavigationToFront(Transform overlayParent)
        {
            if (overlayParent == null)
                return;

            BringToFrontIfSibling(marketTabButton, overlayParent);
            BringToFrontIfSibling(ownedTabButton, overlayParent);
            if (extendedPages == null)
                return;

            for (int index = 0; index < extendedPages.Count; index++)
            {
                ExtendedPageRoute route = extendedPages[index];
                if (route != null)
                    BringToFrontIfSibling(route.TabButton, overlayParent);
            }
        }

        private void ShowPage(GameObject activePanel)
        {
            ResolveReferences();
            Debug.Log("[PlayerTrade][UI][ShowPage] requested="
                + DescribeObject(activePanel)
                + " market=" + DescribeObject(marketPanel)
                + " owned=" + DescribeObject(ownedPanel),
                this);
            if (activePanel == null)
            {
                Debug.LogWarning("[玩家交易] 找不到要显示的页面内容区域。",
                    this);
                return;
            }

            CloseSellPanelForNavigation();

            if (marketPanel != null)
                marketPanel.SetActive(activePanel == marketPanel);
            if (ownedPanel != null)
                ownedPanel.SetActive(activePanel == ownedPanel);

            if (extendedPages == null)
                return;

            for (int index = 0; index < extendedPages.Count; index++)
            {
                ExtendedPageRoute route = extendedPages[index];
                if (route?.PagePanel != null)
                    route.PagePanel.SetActive(activePanel == route.PagePanel);
            }

            Debug.Log("[PlayerTrade][UI][ShowPage][Applied] activePage="
                + DescribeActivePage()
                + " marketActive=" + IsActive(marketPanel)
                + " ownedActive=" + IsActive(ownedPanel),
                this);
        }

        private void ResolveReferences()
        {
            if (marketTabButton == null)
                marketTabButton = FindChild<Button>(transform, "购买");
            if (ownedTabButton == null)
                ownedTabButton = FindChild<Button>(transform, "我的挂单");
            if (marketPanel == null)
                marketPanel = FindObjectByName(transform, marketPanelName);
            if (ownedPanel == null)
                ownedPanel = FindObjectByName(transform, ownedPanelName);
            if (extendedPages != null)
            {
                for (int index = 0; index < extendedPages.Count; index++)
                    extendedPages[index]?.Resolve(transform);
            }
            ResolveSellPanel();
            Debug.Log("[PlayerTrade][UI][Resolve] controller=" + name
                + " market=" + DescribeObject(marketPanel)
                + " owned=" + DescribeObject(ownedPanel)
                + " sellController=" + (sellPanelController == null
                    ? "<null>" : sellPanelController.name),
                this);
        }

        private void Subscribe()
        {
            if (subscribed)
                return;

            if (marketTabButton != null)
                marketTabButton.onClick.AddListener(ShowMarket);
            if (ownedTabButton != null)
                ownedTabButton.onClick.AddListener(ShowOwned);
            if (extendedPages != null)
            {
                extendedPageHandlers.Clear();
                for (int index = 0; index < extendedPages.Count; index++)
                {
                    int capturedIndex = index;
                    ExtendedPageRoute route = extendedPages[index];
                    if (route?.TabButton != null)
                    {
                        UnityAction handler =
                            () => ShowExtendedPage(capturedIndex);
                        extendedPageHandlers[route] = handler;
                        route.TabButton.onClick.AddListener(handler);
                    }
                }
            }
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (marketTabButton != null)
                marketTabButton.onClick.RemoveListener(ShowMarket);
            if (ownedTabButton != null)
                ownedTabButton.onClick.RemoveListener(ShowOwned);
            foreach (KeyValuePair<ExtendedPageRoute, UnityAction> entry
                in extendedPageHandlers)
            {
                if (entry.Key?.TabButton != null && entry.Value != null)
                    entry.Key.TabButton.onClick.RemoveListener(entry.Value);
            }
            extendedPageHandlers.Clear();
            subscribed = false;
        }

        private void ResolveSellPanel()
        {
            if (sellPanelController == null)
                sellPanelController =
                    FindObjectOfType<PlayerTradeSellPanelController>(true);
        }

        private static GameObject FindObjectByName(
            Transform root,
            string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            Transform[] transforms = root == null
                ? Array.Empty<Transform>()
                : root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate != null
                    && string.Equals(candidate.name, objectName,
                        StringComparison.Ordinal))
                    return candidate.gameObject;
            }

            return null;
        }

        private static T FindChild<T>(
            Transform root,
            string objectName) where T : Component
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            T[] components = root == null
                ? Array.Empty<T>()
                : root.GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                T component = components[index];
                if (component != null
                    && string.Equals(component.gameObject.name, objectName,
                        StringComparison.Ordinal))
                    return component;
            }

            return null;
        }

        private static void BringToFrontIfSibling(
            Component component,
            Transform overlayParent)
        {
            if (component == null || overlayParent == null)
                return;

            Transform navigationRoot = component.transform;
            while (navigationRoot.parent != null
                && navigationRoot.parent != overlayParent)
                navigationRoot = navigationRoot.parent;

            if (navigationRoot.parent == overlayParent)
                navigationRoot.SetAsLastSibling();
        }

        private string DescribeActivePage()
        {
            if (marketPanel != null && marketPanel.activeSelf)
                return "market:" + marketPanel.name;
            if (ownedPanel != null && ownedPanel.activeSelf)
                return "owned:" + ownedPanel.name;
            return "<none>";
        }

        private static string DescribeObject(GameObject target)
        {
            if (target == null)
                return "<null>";
            return target.name + "(active=" + target.activeSelf
                + ",id=" + target.GetInstanceID() + ")";
        }

        private static bool IsActive(GameObject target)
        {
            return target != null && target.activeSelf;
        }
    }
}
