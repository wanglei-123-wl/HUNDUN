using UnityEngine;
using UnityEngine.UI;

namespace Game.Chat.UI
{
    [DisallowMultipleComponent]
    public sealed class 添加好友弹窗控制器 : MonoBehaviour
    {
        [Header("界面引用")]
        [SerializeField] private Button 添加按钮;
        [SerializeField] private Button 搜索玩家界面关闭按钮;
        [SerializeField] private GameObject 添加好友弹窗;
        [SerializeField] private GameObject 添加好友弹窗画布;

        private void Awake()
        {
            if (添加按钮 == null || 搜索玩家界面关闭按钮 == null
                || 添加好友弹窗 == null || 添加好友弹窗画布 == null
                || 添加好友弹窗画布.transform.parent != 添加好友弹窗.transform
                || !搜索玩家界面关闭按钮.transform.IsChildOf(添加好友弹窗画布.transform))
            {
                Debug.LogError("[FriendChat] 添加好友弹窗的按钮或画布未绑定。", this);
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (添加按钮 == null || 搜索玩家界面关闭按钮 == null
                || 添加好友弹窗 == null || 添加好友弹窗画布 == null) return;

            添加按钮.onClick.AddListener(打开添加好友弹窗);
            搜索玩家界面关闭按钮.onClick.AddListener(关闭添加好友弹窗);
            GameplayUiInputGuard.RegisterModal(添加好友弹窗);
        }

        private void OnDisable()
        {
            if (添加按钮 != null)
                添加按钮.onClick.RemoveListener(打开添加好友弹窗);
            if (搜索玩家界面关闭按钮 != null)
                搜索玩家界面关闭按钮.onClick.RemoveListener(关闭添加好友弹窗);
            GameplayUiInputGuard.UnregisterModal(添加好友弹窗);
        }

        private void 打开添加好友弹窗()
        {
            添加好友弹窗.SetActive(true);
        }

        private void 关闭添加好友弹窗()
        {
            添加好友弹窗.SetActive(false);
        }
    }
}
