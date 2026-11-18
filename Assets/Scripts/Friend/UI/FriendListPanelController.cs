using System;
using System.Collections.Generic;
using Game.Friend.Protocol;
using Game.Friend.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendListPanelController : MonoBehaviour
    {
        [SerializeField] private ScrollRect friendsScroll;
        [SerializeField] private RectTransform friendsContent;
        [SerializeField] private FriendListItemView friendPrefab;

        private readonly Dictionary<long, FriendListItemView> rows =
            new Dictionary<long, FriendListItemView>();
        private FriendSessionRuntime runtime;
        private TextMeshProUGUI emptyText;
        private long boundEpoch;

        public event Action<FriendDto> FriendSelected;

        private void Awake()
        {
            if (friendsScroll == null || friendsContent == null
                || friendPrefab == null || friendsScroll.content != friendsContent
                || !friendsContent.IsChildOf(friendsScroll.transform))
            {
                Debug.LogError("[Friend] Friend list references are invalid.", this);
                enabled = false;
                return;
            }

            var hint = new GameObject("好友列表提示", typeof(RectTransform),
                typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)hint.transform;
            rect.SetParent(friendsScroll.transform.parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = ((RectTransform)friendsScroll.transform).anchoredPosition;
            rect.sizeDelta = new Vector2(75f, 28f);
            emptyText = hint.GetComponent<TextMeshProUGUI>();
            emptyText.font = friendPrefab.GetComponentInChildren<TMP_Text>().font;
            emptyText.fontSize = 11f;
            emptyText.color = new Color(0.28f, 0.33f, 0.35f);
            emptyText.alignment = TextAlignmentOptions.Center;
            emptyText.enableWordWrapping = true;
            emptyText.raycastTarget = false;
            hint.SetActive(false);
        }

        private void OnEnable()
        {
            if (emptyText == null) return;
            BindRuntime();
            RefreshRows();
        }

        private void OnDisable()
        {
            UnbindRuntime();
        }

        private void Update()
        {
            if (emptyText == null) return;
            if (!ReferenceEquals(runtime, FriendSessionRuntime.Instance))
            {
                BindRuntime();
                RefreshRows();
            }
            else if (runtime != null && boundEpoch != runtime.State.ConnectionEpoch)
                RefreshRows();
        }

        private void BindRuntime()
        {
            FriendSessionRuntime current = FriendSessionRuntime.Instance;
            if (ReferenceEquals(runtime, current)) return;
            UnbindRuntime();
            ClearRows();
            runtime = current;
            boundEpoch = runtime == null ? 0 : runtime.State.ConnectionEpoch;
            if (runtime != null) runtime.State.Changed += RefreshRows;
        }

        private void UnbindRuntime()
        {
            if (runtime == null) return;
            runtime.State.Changed -= RefreshRows;
            runtime = null;
        }

        private void RefreshRows()
        {
            if (emptyText == null) return;
            if (runtime != null && boundEpoch != runtime.State.ConnectionEpoch)
            {
                boundEpoch = runtime.State.ConnectionEpoch;
                ClearRows();
            }

            var friends = runtime != null && runtime.State.HasSnapshot
                ? runtime.State.Friends : null;
            var obsolete = new List<long>();
            foreach (long id in rows.Keys)
                if (friends == null || !friends.ContainsKey(id)) obsolete.Add(id);
            foreach (long id in obsolete)
            {
                FriendListItemView row = rows[id];
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
                rows.Remove(id);
            }

            if (friends != null)
            {
                var ids = new List<long>(friends.Keys);
                ids.Sort();
                for (int index = 0; index < ids.Count; index++)
                {
                    long id = ids[index];
                    FriendDto friend = friends[id];
                    if (!rows.TryGetValue(id, out FriendListItemView row))
                    {
                        row = Instantiate(friendPrefab, friendsContent, false);
                        rows.Add(id, row);
                        row.gameObject.SetActive(true);
                    }
                    row.Bind(friend, HandleFriendSelected);
                    row.transform.SetSiblingIndex(index);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(friendsContent);
            string hint = friends == null ? "正在获取好友..."
                : rows.Count == 0 ? "暂无好友" : null;
            emptyText.text = hint ?? string.Empty;
            emptyText.gameObject.SetActive(hint != null);
        }

        private void HandleFriendSelected(FriendDto friend)
        {
            if (friend != null && friend.characterDbId > 0)
                FriendSelected?.Invoke(friend);
        }

        private void ClearRows()
        {
            foreach (FriendListItemView row in rows.Values)
            {
                if (row == null) continue;
                row.gameObject.SetActive(false);
                Destroy(row.gameObject);
            }
            rows.Clear();
            friendsScroll.verticalNormalizedPosition = 1f;
        }
    }
}
