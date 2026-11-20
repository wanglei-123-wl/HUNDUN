using Game.CharacterPanel.Protocol;
using Game.CharacterPanel.Runtime;
using Game.UI.Notice;
using UnityEngine;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class JingjieBreakthroughPresentation : MonoBehaviour
    {
        private const string EffectName = "MeshFX_Wind";
        private const float EffectDurationSeconds = 5f;

        private JingjieBreakthroughRuntime runtime;
        private GameSession session;
        private Transform player;
        private PlayerEffectPresenter effects;
        private long characterDbId;
        private long connectionEpoch;
        private string operationSessionId;
        private float effectEndsAt;
        private bool effectFinished;
        private bool successConfirmed;

        public bool IsBusy { get; private set; }

        public static JingjieBreakthroughPresentation Ensure(JingjieBreakthroughRuntime runtime)
        {
            if (runtime == null) return null;
            JingjieBreakthroughPresentation presentation = runtime.GetComponent<JingjieBreakthroughPresentation>();
            if (presentation == null)
                presentation = runtime.gameObject.AddComponent<JingjieBreakthroughPresentation>();
            if (presentation.runtime != runtime)
            {
                presentation.Unbind();
                presentation.runtime = runtime;
                runtime.ResultConfirmed += presentation.HandleResult;
                runtime.RequestRejected += presentation.HandleRejection;
                runtime.RequestTimedOut += presentation.HandleTimeout;
            }
            return presentation;
        }

        public bool TryBreakthrough(GameSession owner)
        {
            if (IsBusy || !isActiveAndEnabled || runtime == null || owner == null
                || !owner.IsWorldReady || owner.IsLocalPlayerDead)
                return false;

            Transform localPlayer = LocalPlayerProvider.Instance?.PlayerTransform;
            NetworkManager network = NetworkManager.Instance;
            if (localPlayer == null || network == null || !network.IsConnected
                || !localPlayer.TryGetComponent(out PlayerEffectPresenter presenter))
                return false;

            session = owner;
            player = localPlayer;
            effects = presenter;
            characterDbId = owner.LocalCharacterDbId;
            operationSessionId = owner.JingjieOperationSessionId;
            connectionEpoch = network.ConnectionEpoch;
            effectEndsAt = Time.unscaledTime + EffectDurationSeconds;
            effectFinished = false;
            successConfirmed = false;
            IsBusy = true;

            // Subscribe and capture scope before sending; replies outlive the UI.
            if (!runtime.TryBreakthrough())
            {
                ResetPresentation();
                return false;
            }

            if (IsBusy && !effects.TryPlay(EffectName))
                Debug.LogWarning("[Jingjie] Could not play registered breakthrough effect: " + EffectName, this);
            return true;
        }

        private bool IsCurrentScope()
        {
            NetworkManager network = NetworkManager.Instance;
            return session != null && session.IsWorldReady && !session.IsLocalPlayerDead
                && session.LocalCharacterDbId == characterDbId
                && session.JingjieOperationSessionId == operationSessionId
                && network != null && network.IsConnected && network.ConnectionEpoch == connectionEpoch
                && player != null && LocalPlayerProvider.Instance?.PlayerTransform == player;
        }

        private void Update()
        {
            if (!IsBusy) return;
            if (!IsCurrentScope())
            {
                ResetPresentation();
                return;
            }

            if (!effectFinished && Time.unscaledTime >= effectEndsAt)
            {
                StopEffect();
                effectFinished = true;
            }
            if (effectFinished && successConfirmed)
            {
                ResetPresentation();
                GameNoticeService.Show("突破成功");
            }
        }

        private void HandleResult(JingjieBreakthroughResultDto result)
        {
            if (!IsBusy || result == null) return;
            if (!IsCurrentScope() || result.idempotentReplay)
            {
                ResetPresentation();
                return;
            }
            if (!result.success)
            {
                ResetPresentation();
                GameNoticeService.Show(DescribeError(result.errorCode));
                return;
            }

            successConfirmed = true;
            Update();
        }

        private void HandleRejection(string errorCode)
        {
            if (IsBusy && IsCurrentScope())
                GameNoticeService.Show(DescribeError(errorCode));
        }

        private void HandleTimeout()
        {
            if (IsBusy && IsCurrentScope())
                GameNoticeService.Show("突破结果尚未确认，请重新连接后确认");
        }

        private void StopEffect()
        {
            if (effects != null) effects.Stop(EffectName);
            effects = null;
        }

        private void ResetPresentation()
        {
            StopEffect();
            IsBusy = false;
            session = null;
            player = null;
            successConfirmed = false;
        }

        private void Unbind()
        {
            ResetPresentation();
            if (runtime == null) return;
            runtime.ResultConfirmed -= HandleResult;
            runtime.RequestRejected -= HandleRejection;
            runtime.RequestTimedOut -= HandleTimeout;
            runtime = null;
        }

        private void OnDisable() => Unbind();

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "jingjie_exp_insufficient": return "修为不足";
                case "required_items_insufficient": return "突破材料不足";
                case "max_jingjie": return "当前境界暂无可突破路径";
                case "jingjie_disabled": return "境界突破暂不可用";
                case "jingjie_operation_busy": return "突破处理中，请稍后";
                case "jingjie_operation_conflict":
                case "jingjie_persist_failed":
                case "invalid_jingjie_request":
                case "not_authenticated": return "突破结果尚未确认，请重新连接后确认";
                default: return "突破失败，请稍后再试";
            }
        }
    }
}
