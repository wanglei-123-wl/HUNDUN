using System;
using System.Collections.Generic;
using Game.Friend.Protocol;
using Game.Friend.Runtime;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendRequestsPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject dialogRoot;
        [SerializeField] private ScrollRect requestScroll;
        [SerializeField] private RectTransform requestContent;
        [SerializeField] private FriendRequestItemView requestPrefab;

        private readonly Dictionary<string, FriendRequestItemView> rows =
            new Dictionary<string, FriendRequestItemView>(StringComparer.Ordinal);
        private readonly HashSet<string> resolvedLocally =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> unavailable =
            new HashSet<string>(StringComparer.Ordinal);
        private FriendSessionRuntime runtime;
        private TextMeshProUGUI emptyStateText;
        private long boundEpoch;
        private long pendingSequence;
        private string pendingRequestId;
        private string pendingMsgId;
        private double nextExpiryCheck;
        private bool lastBusy;

        private struct IncomingRequest
        {
            public string nickname;
            public long expireAt;
        }

        private void Awake()
        {
            if (dialogRoot == null || requestScroll == null
                || requestContent == null || requestPrefab == null
                || requestScroll.content != requestContent
                || !requestScroll.transform.IsChildOf(dialogRoot.transform)
                || !requestContent.IsChildOf(requestScroll.transform))
            {
                Debug.LogError("[Friend] Received requests panel references are invalid.", this);
                enabled = false;
                return;
            }

            var hint = new GameObject("好友申请空状态", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)hint.transform;
            rect.SetParent(requestScroll.transform.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(-14f, 164f);
            rect.sizeDelta = new Vector2(275f, 22f);
            emptyStateText = hint.GetComponent<TextMeshProUGUI>();
            emptyStateText.font = requestPrefab.GetComponentInChildren<TMP_Text>().font;
            emptyStateText.fontSize = 13f;
            emptyStateText.color = new Color(0.92f, 0.96f, 0.96f);
            emptyStateText.alignment = TextAlignmentOptions.Center;
            emptyStateText.enableWordWrapping = false;
            emptyStateText.overflowMode = TextOverflowModes.Ellipsis;
            emptyStateText.raycastTarget = false;
            hint.SetActive(false);
        }

        private void OnEnable()
        {
            if (emptyStateText == null) return;
            BindRuntime();
            RefreshRows();
        }

        private void OnDisable()
        {
            UnbindRuntime();
        }

        private void Update()
        {
            if (emptyStateText == null) return;
            if (!ReferenceEquals(runtime, FriendSessionRuntime.Instance))
                BindRuntime();
            if (runtime != null && boundEpoch != runtime.Requests.ConnectionEpoch)
            {
                boundEpoch = runtime.Requests.ConnectionEpoch;
                ClearRows();
                resolvedLocally.Clear();
                unavailable.Clear();
                pendingSequence = 0;
                pendingRequestId = pendingMsgId = null;
                RefreshRows();
            }
            if (Time.realtimeSinceStartupAsDouble >= nextExpiryCheck
                || IsBusy() != lastBusy)
            {
                nextExpiryCheck = Time.realtimeSinceStartupAsDouble + 1d;
                RefreshControls();
            }
        }

        private void BindRuntime()
        {
            FriendSessionRuntime current = FriendSessionRuntime.Instance;
            if (ReferenceEquals(runtime, current)) return;
            UnbindRuntime();
            ClearRows();
            resolvedLocally.Clear();
            unavailable.Clear();
            pendingSequence = 0;
            pendingRequestId = pendingMsgId = null;
            runtime = current;
            boundEpoch = runtime == null ? 0 : runtime.Requests.ConnectionEpoch;
            if (runtime == null) return;
            runtime.State.Changed += RefreshRows;
            runtime.OperationCompleted += HandleOperationResult;
        }

        private void UnbindRuntime()
        {
            if (runtime == null) return;
            runtime.State.Changed -= RefreshRows;
            runtime.OperationCompleted -= HandleOperationResult;
            runtime = null;
        }

        private void RefreshRows()
        {
            if (emptyStateText == null) return;
            var incoming = new Dictionary<string, IncomingRequest>(StringComparer.Ordinal);
            if (runtime != null && runtime.State.HasSnapshot)
            {
                foreach (FriendRequestDto request in runtime.State.PendingReceived.Values)
                {
                    if (request == null || request.status != "pending"
                        || string.IsNullOrWhiteSpace(request.requestId)
                        || resolvedLocally.Contains(request.requestId))
                        continue;
                    incoming[request.requestId] = new IncomingRequest {
                        nickname = request.fromNickname,
                        expireAt = request.expireAt
                    };
                }
                foreach (FriendReceived notice in runtime.State.ReceivedNotices.Values)
                {
                    if (notice == null || string.IsNullOrWhiteSpace(notice.requestId)
                        || incoming.ContainsKey(notice.requestId)
                        || resolvedLocally.Contains(notice.requestId))
                        continue;
                    incoming[notice.requestId] = new IncomingRequest {
                        nickname = notice.fromNickname,
                        expireAt = notice.expireAt
                    };
                }
            }

            var obsolete = new List<string>();
            foreach (string id in rows.Keys)
                if (!incoming.ContainsKey(id)) obsolete.Add(id);
            foreach (string id in obsolete)
            {
                FriendRequestItemView row = rows[id];
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
                rows.Remove(id);
            }

            var ids = new List<string>(incoming.Keys);
            ids.Sort(StringComparer.Ordinal);
            for (int index = 0; index < ids.Count; index++)
            {
                string id = ids[index];
                if (!rows.TryGetValue(id, out FriendRequestItemView row))
                {
                    row = Instantiate(requestPrefab, requestContent, false);
                    rows.Add(id, row);
                    row.gameObject.SetActive(true);
                }
                row.Bind(id, incoming[id].nickname, incoming[id].expireAt, HandleRequest);
                row.transform.SetSiblingIndex(index);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(requestContent);
            RefreshControls();
        }

        private void ClearRows()
        {
            foreach (FriendRequestItemView row in rows.Values)
            {
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
            requestScroll.verticalNormalizedPosition = 1f;
        }

        private void HandleRequest(string id, bool accept)
        {
            if (string.IsNullOrWhiteSpace(id)
                || !rows.TryGetValue(id, out var row))
                return;
            if (row.IsExpired(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            {
                GameNoticeService.Show("申请已过期");
                return;
            }
            if (runtime == null || boundEpoch != runtime.Requests.ConnectionEpoch
                || !runtime.State.HasSnapshot || runtime.Requests.NeedsRecovery
                || runtime.Requests.Pending != null || runtime.Requests.WaitingCount > 0
                || pendingSequence > 0 || resolvedLocally.Contains(id)
                || unavailable.Contains(id))
                return;
            if (!runtime.HandleRequest(id, accept))
            {
                GameNoticeService.Show("申请暂不可处理");
                RefreshControls();
                return;
            }
            pendingRequestId = id;
            pendingMsgId = accept ? "friend_request_accept" : "friend_request_reject";
            pendingSequence = runtime.Requests.Pending == null
                ? 0 : runtime.Requests.Pending.requestSequence;
            GameNoticeService.Show(accept ? "正在同意申请..." : "正在拒绝申请...");
            RefreshControls();
        }

        private void HandleOperationResult(FriendOperationResult result)
        {
            if (runtime == null || boundEpoch != runtime.Requests.ConnectionEpoch
                || result == null || result.requestSequence != pendingSequence
                || result.requestMsgId != pendingMsgId)
                return;
            string id = pendingRequestId;
            bool accepted = pendingMsgId == "friend_request_accept";
            pendingRequestId = pendingMsgId = null;
            pendingSequence = 0;
            if (result.success)
            {
                resolvedLocally.Add(id);
                GameNoticeService.Show(accepted ? "已同意好友申请" : "已拒绝好友申请");
                RefreshRows();
                runtime.RequestList();
                RefreshControls();
            }
            else
            {
                if (result.errorCode == "request_expired")
                    unavailable.Add(id);
                else if (result.errorCode == "request_not_found")
                    resolvedLocally.Add(id);
                GameNoticeService.Show(DescribeError(result.errorCode));
                RefreshRows();
                if ((result.errorCode == "request_expired"
                    || result.errorCode == "request_not_found")
                    && !runtime.Requests.NeedsRecovery)
                    runtime.RequestList();
                RefreshControls();
            }
        }

        private bool IsBusy()
        {
            return runtime == null || !runtime.State.HasSnapshot
                || runtime.Requests.NeedsRecovery || runtime.Requests.Pending != null
                || runtime.Requests.WaitingCount > 0 || pendingSequence > 0;
        }

        private void RefreshControls()
        {
            if (emptyStateText == null) return;
            bool busy = IsBusy();
            lastBusy = busy;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            foreach (FriendRequestItemView row in rows.Values)
                if (row != null)
                    row.UpdateState(busy, unavailable.Contains(row.RequestId), now);

            string hint;
            if (runtime != null && runtime.Requests.NeedsRecovery)
                hint = "好友状态不明，请重新连接";
            else if (runtime == null || !runtime.State.HasSnapshot)
                hint = "正在获取好友申请...";
            else
                hint = string.Empty;
            if (emptyStateText.text != hint) emptyStateText.text = hint;
            if (emptyStateText.gameObject.activeSelf != (hint.Length > 0))
                emptyStateText.gameObject.SetActive(hint.Length > 0);
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "request_expired": return "申请已过期";
                case "request_not_found": return "申请已被处理或不存在";
                case "not_request_receiver": return "无法处理该申请";
                case "blocked_by_self":
                case "blocked_by_target": return "当前无法同意该申请";
                case "invalid_request":
                case "duplicate_request":
                case "sequence_gap": return "好友状态不明，请重新连接";
                default: return "处理失败，请稍后再试";
            }
        }
    }
}
