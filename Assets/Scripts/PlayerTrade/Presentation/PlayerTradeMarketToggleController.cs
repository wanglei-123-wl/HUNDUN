using UnityEngine;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeMarketToggleController : MonoBehaviour
    {
        [Header("玩家市场显示控制")]
        [SerializeField, InspectorName("切换按键")]
        private KeyCode toggleKey = KeyCode.V;
        [SerializeField, InspectorName("玩家市场界面")]
        private GameObject marketPanel;

        public static PlayerTradeMarketToggleController ActiveMarket { get; private set; }
        public bool IsVisible => marketPanel != null && marketPanel.activeInHierarchy;
        public Transform MarketRoot => marketPanel != null ? marketPanel.transform : null;

        private void Awake()
        {
            if (marketPanel == null || marketPanel == gameObject)
            {
                Debug.LogError("[PlayerTrade] Market panel must be bound to a separate object.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            ActiveMarket = this;
        }

        private void OnDisable()
        {
            if (ActiveMarket == this) ActiveMarket = null;
        }

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Toggle();
        }

        public void Toggle()
        {
            if (marketPanel != null) SetVisible(!marketPanel.activeSelf);
        }

        public void Show()
        {
            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (marketPanel != null && marketPanel.activeSelf != visible)
                marketPanel.SetActive(visible);
        }
    }
}
