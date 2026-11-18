using System;
using Game.Items;
using UnityEngine;

/// <summary>
/// 绑定已学技能到技能栏，供 HUD 和释放入口读取。
/// </summary>
[DisallowMultipleComponent]
public sealed class 技能栏控制器 : MonoBehaviour
{
    public const int SlotCount = 4;

    [SerializeField] private GameSession session;
    [SerializeField] private ItemDefinitionTable 物品注册表;
    [SerializeField] private 技能注册表 技能注册表;

    private string[] 技能ID = new string[SlotCount];

    public event Action<int, string> 技能槽位已变更;

    private void OnEnable()
    {
        if (session != null)
        {
            session.LocalSkillBarSnapshotChanged += HandleSkillSnapshotChanged;
            HandleSkillSnapshotChanged(session.LocalSkillSnapshot);
        }
    }

    private void OnDisable()
    {
        if (session != null)
            session.LocalSkillBarSnapshotChanged -= HandleSkillSnapshotChanged;
    }

    public bool TryGetSkillId(int 槽位编号, out string skillId)
    {
        skillId = IsValidSlot(槽位编号) ? 技能ID[槽位编号 - 1] : null;
        return !string.IsNullOrEmpty(skillId);
    }

    public bool CanBindLearnedSkill(string skillId)
    {
        SkillSnapshotResult snapshot = session != null ? session.LocalSkillSnapshot : null;
        if (session == null || !session.CanEditSkillBar)
            return false;

        SkillSnapshotEntry learned = FindLearnedSkill(snapshot, skillId);
        return learned != null && (learned.progress == null || learned.progress.attributes != null)
            && 技能注册表 != null && 技能注册表.IsRegisteredAndValid(skillId);
    }

    public bool TryBindLearnedSkill(int 槽位编号, string skillId)
    {
        if (!IsValidSlot(槽位编号) || !CanBindLearnedSkill(skillId))
            return false;

        return session.TrySaveSkillBarBinding(槽位编号, skillId);
    }

    public bool TryGetSkillDetail(int 槽位编号, out ItemDetailData detail)
    {
        detail = null;
        return TryGetSkillId(槽位编号, out string skillId)
            && TryGetSkillDetail(skillId, out detail);
    }

    public bool TryGetSkillDetail(string skillId, out ItemDetailData detail)
    {
        detail = 技能注册表 != null && 技能注册表.TryGet(skillId, out var config)
            ? 技能详情适配器.转换(config) : null;
        if (物品注册表 != null && 物品注册表.TryGetSkillBook(skillId, out var book))
        {
            ItemDetailData bookDetail = ItemDetailDataResolver.FromItem(book);
            if (detail == null)
                detail = bookDetail;
            else if (bookDetail != null)
            {
                if (detail.Icon == null)
                    detail.Icon = bookDetail.Icon;
                if (string.IsNullOrWhiteSpace(detail.DisplayName))
                    detail.DisplayName = bookDetail.DisplayName;
                if (string.IsNullOrWhiteSpace(detail.Description))
                    detail.Description = bookDetail.Description;
                if (string.IsNullOrWhiteSpace(detail.DetailText)
                    && (detail.Entries == null || detail.Entries.Count == 0))
                {
                    detail.DetailText = bookDetail.DetailText;
                    detail.Entries = bookDetail.Entries;
                }
            }
        }

        return detail != null && detail.IsValid;
    }

    private void HandleSkillSnapshotChanged(SkillSnapshotResult snapshot)
    {
        var restoredSkills = new string[SlotCount];
        if (IsCurrentSnapshot(snapshot) && snapshot.skillBarRevision.HasValue)
            foreach (SkillSnapshotEntry skill in snapshot.skills)
                if (skill?.equippedSlot is int slot && IsValidSlot(slot))
                    restoredSkills[slot - 1] = skill.skillId;

        string[] previousSkills = 技能ID;
        技能ID = restoredSkills;
        for (int index = 0; index < 技能ID.Length; index++)
            if (previousSkills[index] != 技能ID[index])
                技能槽位已变更?.Invoke(index + 1, 技能ID[index] ?? string.Empty);
    }

    private SkillSnapshotEntry FindLearnedSkill(SkillSnapshotResult snapshot, string skillId)
    {
        if (!IsCurrentSnapshot(snapshot))
            return null;

        foreach (SkillSnapshotEntry skill in snapshot.skills)
            if (skill?.skillId == skillId)
                return skill;
        return null;
    }

    private bool IsCurrentSnapshot(SkillSnapshotResult snapshot) => session != null
        && snapshot != null && snapshot.success && snapshot.skills != null
        && snapshot.characterDbId == session.LocalCharacterDbId;

    private static bool IsValidSlot(int slot) => slot >= 1 && slot <= SlotCount;
}
