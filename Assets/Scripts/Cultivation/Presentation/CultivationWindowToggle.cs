using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CultivationWindowToggle : MonoBehaviour
    {
        [SerializeField] private GameObject cultivationWindow;
        [SerializeField] private Button hudToggleButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private GameSession session;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private CultivationPanelToggle panels;
        [SerializeField] private LearnedSkillListView learnedSkills;

        public bool IsOpen => cultivationWindow != null && cultivationWindow.activeSelf;

        private void OnEnable()
        {
            if (hudToggleButton != null)
                hudToggleButton.onClick.AddListener(Toggle);
            if (closeButton != null)
                closeButton.onClick.AddListener(Close);
            if (session != null)
                session.SkillLearnCompleted += HandleSkillLearnCompleted;
        }

        private void OnDisable()
        {
            if (hudToggleButton != null)
                hudToggleButton.onClick.RemoveListener(Toggle);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            if (session != null)
                session.SkillLearnCompleted -= HandleSkillLearnCompleted;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.C) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Toggle();
        }

        public void Toggle()
        {
            if (cultivationWindow != null && cultivationWindow != gameObject)
            {
                cultivationWindow.SetActive(!cultivationWindow.activeSelf);
                if (cultivationWindow.activeSelf)
                    RefiningSessionRuntime.Instance?.Refresh();
            }
        }

        private void HandleSkillLearnCompleted(SkillLearnResult result)
        {
            if (!result.success)
            {
                GameNoticeService.Show(result.errorCode switch
                {
                    "skill_already_learned" => "该技能已经学会",
                    "skill_book_level_required" => "未达到技能书的等级要求",
                    "skill_book_role_required" => "当前职业无法学习该技能",
                    "inventory_revision_conflict" => "背包已变化，请重新选择技能书",
                    "skill_learn_persist_failed" => "技能学习结果待确认，请稍后重试",
                    _ => "技能学习失败，请稍后重试"
                });
                return;
            }

            if (cultivationWindow == null || cultivationWindow == gameObject || panels == null)
                return;

            inventory?.Close();
            panels.OpenSkills();
            cultivationWindow.SetActive(true);
            learnedSkills?.SelectSkill(result.learnedSkill.skillId);
            GameNoticeService.Show("技能学习成功");
        }

        private void Close()
        {
            if (cultivationWindow != null && cultivationWindow != gameObject
                && cultivationWindow.activeSelf)
                cultivationWindow.SetActive(false);
        }
    }
}
