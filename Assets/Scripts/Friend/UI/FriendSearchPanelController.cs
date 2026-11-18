using System;
using System.Collections.Generic;
using System.Text;
using Game.Friend.Protocol;
using Game.Friend.Runtime;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendSearchPanelController : MonoBehaviour
    {
        [SerializeField] private GameObject dialogRoot;
        [SerializeField] private TMP_InputField nicknameInput;
        [SerializeField] private Button searchButton;
        [SerializeField] private ScrollRect resultsScroll;
        [SerializeField] private RectTransform resultsContent;
        [SerializeField] private FriendSearchResultItemView resultPrefab;

        private static FriendSearchPanelController activePanel;
        private readonly List<FriendSearchResultItemView> rows =
            new List<FriendSearchResultItemView>();
        private readonly HashSet<long> unavailableTargets = new HashSet<long>();
        private FriendSessionRuntime runtime;
        private long boundEpoch;
        private long pendingSearchSequence;
        private long pendingSendSequence;
        private long pendingTargetId;
        private string searchedKeyword;
        private double nextSearchAt;

        public static bool IsSearchInputFocused =>
            activePanel != null && activePanel.dialogRoot != null
            && activePanel.dialogRoot.activeInHierarchy
            && activePanel.nicknameInput != null && activePanel.nicknameInput.isFocused;

        private void Awake()
        {
            if (dialogRoot == null || nicknameInput == null || searchButton == null
                || resultsScroll == null || resultsContent == null || resultPrefab == null
                || nicknameInput.textComponent == null
                || resultsScroll.content != resultsContent
                || !nicknameInput.transform.IsChildOf(dialogRoot.transform)
                || !searchButton.transform.IsChildOf(dialogRoot.transform)
                || !resultsScroll.transform.IsChildOf(dialogRoot.transform))
            {
                Debug.LogError("[Friend] Search panel references do not belong to the friend dialog.", this);
                enabled = false;
                return;
            }

            if (nicknameInput.placeholder == null)
            {
                TMP_Text placeholder = Instantiate(nicknameInput.textComponent,
                    nicknameInput.textComponent.transform.parent);
                placeholder.name = "好友昵称占位";
                placeholder.text = "输入好友昵称";
                placeholder.color = new Color(1f, 1f, 1f, 0.55f);
                placeholder.raycastTarget = false;
                placeholder.transform.SetAsFirstSibling();
                nicknameInput.placeholder = placeholder;
                nicknameInput.ForceLabelUpdate();
            }

        }

        private void OnEnable()
        {
            activePanel = this;
            searchButton.onClick.AddListener(Search);
            nicknameInput.onSubmit.AddListener(Submit);
            nicknameInput.onValueChanged.AddListener(HandleInputChanged);
            BindRuntime();
            Refresh();
        }

        private void OnDisable()
        {
            if (activePanel == this) activePanel = null;
            searchButton?.onClick.RemoveListener(Search);
            nicknameInput?.onSubmit.RemoveListener(Submit);
            nicknameInput?.onValueChanged.RemoveListener(HandleInputChanged);
            UnbindRuntime();
        }

        private void Update()
        {
            if (!ReferenceEquals(runtime, FriendSessionRuntime.Instance))
                BindRuntime();
            if (runtime != null && boundEpoch != runtime.Requests.ConnectionEpoch)
            {
                boundEpoch = runtime.Requests.ConnectionEpoch;
                ResetResults();
                nextSearchAt = 0;
            }
            Refresh();
        }

        private void BindRuntime()
        {
            FriendSessionRuntime current = FriendSessionRuntime.Instance;
            if (ReferenceEquals(runtime, current)) return;
            UnbindRuntime();
            ResetResults();
            boundEpoch = current == null ? 0 : current.Requests.ConnectionEpoch;
            runtime = current;
            if (runtime == null) return;
            runtime.State.Changed += Refresh;
            runtime.SearchCompleted += HandleSearchResult;
            runtime.SendCompleted += HandleSendResult;
            runtime.OperationCompleted += HandleOperationResult;
        }

        private void UnbindRuntime()
        {
            if (runtime == null) return;
            runtime.State.Changed -= Refresh;
            runtime.SearchCompleted -= HandleSearchResult;
            runtime.SendCompleted -= HandleSendResult;
            runtime.OperationCompleted -= HandleOperationResult;
            runtime = null;
        }

        private void ResetResults()
        {
            foreach (FriendSearchResultItemView row in rows)
            {
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
            unavailableTargets.Clear();
            pendingSearchSequence = 0;
            pendingSendSequence = 0;
            pendingTargetId = 0;
            searchedKeyword = null;
        }

        private void Submit(string value)
        {
            Search();
        }

        private void Search()
        {
            string keyword = nicknameInput.text == null
                ? string.Empty : nicknameInput.text.Trim();
            if (CanRetryList())
            {
                if (!runtime.RequestList())
                    GameNoticeService.Show("好友列表暂不可用，请稍后重试");
                else
                    GameNoticeService.Show("正在获取好友信息...");
                Refresh();
                return;
            }
            if (!CanSearch(keyword))
            {
                ShowSearchValidationNotice(keyword);
                return;
            }
            if (!runtime.Search(keyword))
            {
                GameNoticeService.Show("搜索暂不可用");
                Refresh();
                return;
            }

            ResetRows();
            unavailableTargets.Clear();
            searchedKeyword = keyword;
            pendingSearchSequence = runtime.Requests.Pending == null
                ? 0 : runtime.Requests.Pending.requestSequence;
            nextSearchAt = Time.realtimeSinceStartupAsDouble + 1d;
            GameNoticeService.Show("正在搜索...");
            Refresh();
        }

        private bool CanSearch(string keyword)
        {
            return dialogRoot.activeInHierarchy && runtime != null
                && runtime.State.HasSnapshot && !runtime.Requests.NeedsRecovery
                && runtime.Requests.Pending == null && runtime.Requests.WaitingCount == 0
                && Time.realtimeSinceStartupAsDouble >= nextSearchAt
                && IsValidKeyword(keyword);
        }

        private bool CanRetryList()
        {
            return dialogRoot.activeInHierarchy && runtime != null
                && !runtime.State.HasSnapshot && runtime.State.NeedsRefresh
                && !runtime.Requests.NeedsRecovery
                && runtime.Requests.Pending == null && runtime.Requests.WaitingCount == 0;
        }

        private static bool IsValidKeyword(string keyword)
        {
            if (string.IsNullOrEmpty(keyword) || Encoding.UTF8.GetByteCount(keyword) > 16)
                return false;
            foreach (char character in keyword)
                if (char.IsControl(character)) return false;
            return true;
        }

        private void HandleInputChanged(string value)
        {
            if (searchedKeyword != null && value.Trim() != searchedKeyword)
            {
                ResetRows();
            }
            Refresh();
        }

        private void HandleSearchResult(FriendSearchResult result)
        {
            if (runtime == null || boundEpoch != runtime.Requests.ConnectionEpoch
                || result == null || result.requestSequence != pendingSearchSequence)
                return;
            pendingSearchSequence = 0;
            if (nicknameInput.text.Trim() != searchedKeyword)
            {
                Refresh();
                return;
            }

            if (!result.success)
                GameNoticeService.Show(DescribeError(result.errorCode));
            else
            {
                FriendSearchPlayer[] players = result.players;
                for (int index = 0; index < Mathf.Min(players.Length, 50); index++)
                {
                    FriendSearchPlayer player = players[index];
                    if (player == null || player.characterDbId <= 0) continue;
                    FriendSearchResultItemView row =
                        Instantiate(resultPrefab, resultsContent, false);
                    row.gameObject.SetActive(true);
                    row.Bind(player, SendRequest);
                    rows.Add(row);
                }
                LayoutRebuilder.ForceRebuildLayoutImmediate(resultsContent);
                resultsScroll.verticalNormalizedPosition = 1f;
                if (result.truncated)
                    GameNoticeService.Show("结果较多，请输入更完整的昵称");
                else if (rows.Count == 0)
                    GameNoticeService.Show("没有找到玩家");
            }
            Refresh();
        }

        private void SendRequest(FriendSearchPlayer player)
        {
            if (player == null || runtime == null
                || boundEpoch != runtime.Requests.ConnectionEpoch
                || searchedKeyword != nicknameInput.text.Trim()
                || !runtime.State.HasSnapshot || runtime.Requests.NeedsRecovery
                || runtime.Requests.Pending != null || runtime.Requests.WaitingCount > 0
                || IsFriend(player.characterDbId)
                || IsRequested(player.characterDbId)
                || unavailableTargets.Contains(player.characterDbId))
                return;
            if (!runtime.SendRequest(player.characterDbId))
            {
                GameNoticeService.Show("申请暂不可发送");
                Refresh();
                return;
            }
            pendingTargetId = player.characterDbId;
            pendingSendSequence = runtime.Requests.Pending == null
                ? 0 : runtime.Requests.Pending.requestSequence;
            GameNoticeService.Show("正在发送申请...");
            Refresh();
        }

        private void HandleSendResult(FriendSendResult result)
        {
            if (runtime == null || boundEpoch != runtime.Requests.ConnectionEpoch
                || result == null || result.requestSequence != pendingSendSequence)
                return;
            if (!result.success && result.errorCode == "target_not_found")
                unavailableTargets.Add(pendingTargetId);
            pendingTargetId = 0;
            pendingSendSequence = 0;
            GameNoticeService.Show(result.success
                ? "申请已发送，等待对方处理"
                : DescribeError(result.errorCode));
            if (!result.success && result.errorCode == "already_friends")
                runtime.RequestList();
            Refresh();
        }

        private void HandleOperationResult(FriendOperationResult result)
        {
            if (runtime == null || boundEpoch != runtime.Requests.ConnectionEpoch
                || result == null) return;
            if (result.requestMsgId == "friend_search"
                && result.requestSequence == pendingSearchSequence)
            {
                pendingSearchSequence = 0;
                if (!result.success)
                    GameNoticeService.Show(DescribeError(result.errorCode));
            }
            else if (result.requestMsgId == "friend_request_send"
                && result.requestSequence == pendingSendSequence)
            {
                pendingSendSequence = 0;
                pendingTargetId = 0;
                if (!result.success)
                    GameNoticeService.Show(DescribeError(result.errorCode));
            }
            Refresh();
        }

        private void ResetRows()
        {
            foreach (FriendSearchResultItemView row in rows)
            {
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
            resultsScroll.verticalNormalizedPosition = 1f;
        }

        private bool IsFriend(long id)
        {
            return runtime != null && runtime.State.Friends.ContainsKey(id);
        }

        private bool IsRequested(long id)
        {
            if (runtime == null) return false;
            if (runtime.State.SentRequestIds.ContainsKey(id)) return true;
            foreach (FriendRequestDto request in runtime.State.PendingSent.Values)
                if (request.toCharacterDbId == id) return true;
            return false;
        }

        private void Refresh()
        {
            string keyword = nicknameInput.text == null
                ? string.Empty : nicknameInput.text.Trim();
            searchButton.interactable = CanSearch(keyword) || CanRetryList();
            bool busy = runtime == null || runtime.Requests.NeedsRecovery
                || runtime.Requests.Pending != null || runtime.Requests.WaitingCount > 0;
            foreach (FriendSearchResultItemView row in rows)
            {
                if (row == null) continue;
                long id = row.CharacterDbId;
                row.UpdateState(IsFriend(id), IsRequested(id),
                    unavailableTargets.Contains(id), busy);
            }
        }

        private void ShowSearchValidationNotice(string keyword)
        {
            if (runtime == null || !runtime.State.HasSnapshot)
            {
                GameNoticeService.Show("好友信息尚未加载完成");
                return;
            }

            if (runtime.Requests.NeedsRecovery)
            {
                GameNoticeService.Show("好友状态不明，请重新连接");
                return;
            }

            if (runtime.Requests.Pending != null
                || runtime.Requests.WaitingCount > 0)
            {
                GameNoticeService.Show("好友请求处理中，请稍后");
                return;
            }

            if (Time.realtimeSinceStartupAsDouble < nextSearchAt)
            {
                GameNoticeService.Show("搜索冷却中");
                return;
            }

            if (string.IsNullOrEmpty(keyword))
            {
                GameNoticeService.Show("请输入好友昵称");
                return;
            }

            if (!IsValidKeyword(keyword))
                GameNoticeService.Show("昵称需为 1-16 个 UTF8 字节");
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "friend_search_rate_limited": return "搜索太频繁，请稍后再试";
                case "target_not_found": return "该角色不存在或已删除";
                case "target_offline": return "目标当前不可申请，请稍后再试";
                case "already_friends": return "已经是好友";
                case "blocked_by_self":
                case "blocked_by_target": return "当前无法向该玩家发送申请";
                case "invalid_request": return "请求内容无效";
                case "friend_search_unavailable":
                case "friend_search_failed": return "搜索暂不可用，请稍后再试";
                case "duplicate_request":
                case "sequence_gap": return "好友状态不明，请重新连接";
                default: return "操作失败，请稍后再试";
            }
        }
    }
}
