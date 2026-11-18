using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public sealed class RefiningStartButton : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private CultivationMaterialDropTarget materialDropTarget;

        private Button button;
        private RefiningSessionRuntime runtime;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
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
            if (runtime == null || materialDropTarget == null
                || !materialDropTarget.TryGetSelection(out long stackId,
                    out int count, out long configurationRevision))
            {
                GameNoticeService.Show("请先拖入炼化材料并确认数量");
                Refresh();
                return;
            }
            if (runtime.TryStart(stackId, count, configurationRevision))
            {
                Refresh();
                return;
            }
            GameNoticeService.Show(runtime.HasUnresolvedOperation
                ? "炼化结果尚未确认，请等待状态同步"
                : "无法开始炼化，请刷新材料与背包");
            Refresh();
        }

        private void BindRuntime()
        {
            RefiningSessionRuntime current = RefiningSessionRuntime.Instance;
            if (runtime == current)
                return;
            UnbindRuntime();
            runtime = current;
            if (runtime == null)
                return;
            runtime.StartResultReceived += HandleResult;
            runtime.RequestRejected += HandleRejection;
            runtime.RequestTimedOut += HandleTimeout;
        }

        private void UnbindRuntime()
        {
            if (runtime == null)
                return;
            runtime.StartResultReceived -= HandleResult;
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
                && network != null && network.IsConnected
                && runtime != null && !runtime.HasUnresolvedOperation
                && materialDropTarget != null
                && materialDropTarget.TryGetSelection(out long stackId,
                    out int count, out long revision)
                && runtime.CanStart(stackId, count, revision);
        }

        private void HandleResult(RefiningResultDto result)
        {
            materialDropTarget?.ClearSelectedMaterial();
            Refresh();
            if (result == null)
                return;
            GameNoticeService.Show(result.success
                ? result.idempotentReplay
                    ? "炼化结果已确认，正在同步背包"
                    : "炼化已开始"
                : DescribeError(result.errorCode));
        }

        private void HandleRejection(string errorCode)
        {
            Refresh();
            GameNoticeService.Show(DescribeError(errorCode));
        }

        private void HandleTimeout()
        {
            Refresh();
            GameNoticeService.Show("炼化结果尚未确认，请重新连接后确认");
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "refining_configuration_changed":
                    return "炼化规则已更新，请重新选择材料和数量";
                case "refining_inventory_conflict":
                case "refining_count_insufficient":
                case "refining_stack_missing":
                    return "背包材料已变化，请重新选择数量";
                case "refining_item_disabled":
                    return "该物品暂不能炼化";
                case "refining_task_active":
                    return "已有炼化任务，请等待当前任务完成";
                case "refining_duration_invalid":
                case "refining_overflow":
                    return "数量超出本次炼化上限";
                case "refining_request_conflict":
                    return "炼化请求身份冲突，已停止自动重试，请检查状态";
                case "refining_persist_failed":
                case "not_authenticated":
                case "refining_invalid_request":
                    return "炼化结果尚未确认，请重新连接后确认";
                default:
                    return "炼化失败，请刷新背包后重试";
            }
        }
    }
}
