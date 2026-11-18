using Game.CharacterPanel.Protocol;
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

        private Button button;
        private JingjieBreakthroughRuntime runtime;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
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
            if (CanAttempt() && runtime.TryBreakthrough())
            {
                Refresh();
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
            runtime.ResultConfirmed += HandleResult;
            runtime.RequestRejected += HandleRejection;
            runtime.RequestTimedOut += HandleTimeout;
        }

        private void UnbindRuntime()
        {
            if (runtime == null)
                return;
            runtime.ResultConfirmed -= HandleResult;
            runtime.RequestRejected -= HandleRejection;
            runtime.RequestTimedOut -= HandleTimeout;
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

        private void HandleResult(JingjieBreakthroughResultDto result)
        {
            Refresh();
            if (result == null || result.idempotentReplay)
                return;

            GameNoticeService.Show(result.success
                ? "突破成功" : DescribeError(result.errorCode));
        }

        private void HandleRejection(string errorCode)
        {
            Refresh();
            GameNoticeService.Show(DescribeError(errorCode));
        }

        private void HandleTimeout()
        {
            Refresh();
            GameNoticeService.Show("突破结果尚未确认，请重新连接后确认");
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "jingjie_exp_insufficient":
                    return "修为不足";
                case "required_items_insufficient":
                    return "突破材料不足";
                case "max_jingjie":
                    return "当前境界暂无可突破路径";
                case "jingjie_disabled":
                    return "境界突破暂不可用";
                case "jingjie_operation_busy":
                    return "突破处理中，请稍后";
                case "jingjie_operation_conflict":
                case "jingjie_persist_failed":
                case "invalid_jingjie_request":
                case "not_authenticated":
                    return "突破结果尚未确认，请重新连接后确认";
                default:
                    return "突破失败，请稍后再试";
            }
        }
    }
}
