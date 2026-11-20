using Game.CharacterPanel.Runtime;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public sealed class CultivationBreakthroughButton : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private CultivationWindowToggle window;

        private Button button;
        private JingjieBreakthroughRuntime runtime;
        private JingjieBreakthroughPresentation presentation;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (window == null)
                window = FindObjectOfType<CultivationWindowToggle>();
            button = GetComponent<Button>();
            button.onClick.AddListener(Click);
            BindRuntime();
            Refresh();
        }

        private void Update()
        {
            BindRuntime();
            Refresh();
        }

        private void OnDisable()
        {
            if (button != null)
                button.onClick.RemoveListener(Click);
            UnbindRuntime();
        }

        private void Click()
        {
            if (CanAttempt() && presentation != null && presentation.TryBreakthrough(session))
            {
                Refresh();
                window?.Close();
                return;
            }

            GameNoticeService.Show(runtime != null && runtime.HasUnresolvedOperation
                ? "突破结果尚未确认，请重新连接后确认"
                : "当前无法突破，请稍后再试");
            Refresh();
        }

        private void BindRuntime()
        {
            JingjieBreakthroughRuntime current = JingjieBreakthroughRuntime.Instance;
            if (runtime == current)
                return;

            UnbindRuntime();
            runtime = current;
            if (runtime == null)
                return;
            presentation = JingjieBreakthroughPresentation.Ensure(runtime);
        }

        private void UnbindRuntime()
        {
            presentation = null;
            runtime = null;
        }

        private void Refresh()
        {
            if (button == null)
                return;
            NetworkManager network = NetworkManager.Instance;
            button.interactable = session != null && session.IsWorldReady
                && !string.IsNullOrWhiteSpace(session.JingjieOperationSessionId)
                && network != null && network.IsConnected
                && runtime != null && !runtime.HasUnresolvedOperation
                && presentation != null && !presentation.IsBusy
                && CanAttempt();
        }

        private bool CanAttempt()
        {
            if (session == null || !session.IsWorldReady)
                return false;
            ProgressionSnapshotResult progression =
                session.LocalProgressionSnapshot;
            if (progression == null || !progression.hasNextJingjie
                || progression.requiredJingjieExp < 0
                || progression.jingjieExp < progression.requiredJingjieExp)
                return false;
            if (!RefiningBreakthroughConditions.TryFindPath(
                RefiningSessionRuntime.Instance != null
                    ? RefiningSessionRuntime.Instance.Catalog : null,
                progression, session.LocalCharacterDbId,
                out RefiningBreakthroughDto path))
                return false;
            return RefiningBreakthroughConditions.MatchesNextRealm(
                    path, progression)
                && RefiningBreakthroughConditions.CanAttempt(
                path, progression.jingjieExp,
                inventory != null ? inventory.CurrentSnapshot : null,
                session.LocalCharacterDbId);
        }

    }
}
