using System;
using System.Collections.Generic;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.UI.Notice;
using Newtonsoft.Json;
using UnityEngine;

public partial class GameSession
{
    private const float SkillRequestTimeout = 5f;

    private string skillLearnSessionId;
    private long skillConnectionEpoch;
    private long skillCharacterDbId;
    private long completedSkillLearnSequence;
    private SkillLearnRequest pendingSkillLearn;
    private float skillLearnRetryAt;
    private bool skillLearnRetried;

    private long nextSkillBarSaveSequence;
    private SkillBarSaveRequest pendingSkillBarSave;
    private float skillBarSaveDeadline;
    private bool skillBarSaveRetried;
    private long skillBarRefreshMinSequence;
    private long skillBarRefreshMinRevision;
    private float skillBarRefreshRetryAt;

    private long nextSkillSnapshotSequence;
    private long pendingSkillSnapshotSequence;
    private float skillSnapshotDeadline;
    private bool receivedSkillSnapshot;
    private readonly Dictionary<long, SkillSnapshotBatches> skillSnapshotBatches =
        new Dictionary<long, SkillSnapshotBatches>();
    private readonly HashSet<string> confirmedSkillLearns =
        new HashSet<string>(StringComparer.Ordinal);
    private readonly Dictionary<string, SkillProgressData> progressBeforeOwnership =
        new Dictionary<string, SkillProgressData>(StringComparer.Ordinal);

    public SkillSnapshotResult LocalSkillSnapshot { get; private set; }
    public bool CanEditSkillBar => LocalSkillSnapshot?.skillBarRevision != null
        && !string.IsNullOrWhiteSpace(skillLearnSessionId)
        && pendingSkillBarSave == null && skillBarRefreshMinSequence == 0;
    public event Action<SkillSnapshotResult> LocalSkillSnapshotChanged;
    public event Action<SkillSnapshotResult> LocalSkillBarSnapshotChanged;
    public event Action SkillBarEditAvailabilityChanged;
    public event Action<SkillSnapshotEntry> LocalSkillProgressChanged;
    public event Action<SkillLearnResult> SkillLearnCompleted;

    public bool TryLearnSkill(long stackId)
    {
        if (!IsWorldReady || !IsCurrentSkillSession() || mapTransferPaused
            || string.IsNullOrWhiteSpace(skillLearnSessionId) || stackId <= 0)
            return false;

        if (pendingSkillLearn != null)
            return pendingSkillLearn.stackId == stackId && ResendPendingSkillLearn();

        InventoryOwnershipSnapshot inventory = FindObjectOfType<InventoryModuleRuntime>()
            ?.CurrentSnapshot;
        if (inventory == null || inventory.CharacterDbId != skillCharacterDbId)
            return false;

        InventoryStackState selected = null;
        foreach (InventoryStackState stack in inventory.Stacks)
            if (stack.StackId == stackId && stack.Count > 0)
            {
                selected = stack;
                break;
            }

        CultivationSessionRuntime catalog = CultivationSessionRuntime.Instance;
        if (selected == null || catalog == null
            || !catalog.TryGetItem(selected.ItemId, out var definition)
            || !definition.IsSkillBook)
            return false;

        var request = new SkillLearnRequest(skillLearnSessionId,
            completedSkillLearnSequence + 1, inventory.InventoryRevision, stackId,
            Math.Max(1L, CurrentClientTick));
        if (!networkManager.Send(request))
            return false;

        pendingSkillLearn = request;
        skillLearnRetryAt = Time.realtimeSinceStartup + SkillRequestTimeout;
        skillLearnRetried = false;
        return true;
    }

    public bool TrySaveSkillBarBinding(int slotIndex, string skillId)
    {
        if (!CanEditSkillBar || !IsWorldReady || !IsCurrentSkillSession() || mapTransferPaused
            || slotIndex < 1 || slotIndex > 技能栏控制器.SlotCount || string.IsNullOrWhiteSpace(skillId))
            return false;

        var snapshot = LocalSkillSnapshot;
        SkillSnapshotEntry learned = Array.Find(snapshot.skills, skill => skill.skillId == skillId);
        if (learned == null)
            return false;
        if (learned.equippedSlot == slotIndex)
            return true;

        var slots = new List<SkillBarSlot>(技能栏控制器.SlotCount);
        foreach (SkillSnapshotEntry skill in snapshot.skills)
            if (skill.equippedSlot is int equippedSlot && equippedSlot != slotIndex && skill.skillId != skillId)
                slots.Add(new SkillBarSlot { slotIndex = equippedSlot, skillId = skill.skillId });
        slots.Add(new SkillBarSlot { slotIndex = slotIndex, skillId = skillId });
        slots.Sort((left, right) => left.slotIndex.CompareTo(right.slotIndex));

        var request = new SkillBarSaveRequest(skillLearnSessionId,
            nextSkillBarSaveSequence + 1, snapshot.skillBarRevision.Value, slots.ToArray());
        if (!networkManager.Send(request))
        {
            GameNoticeService.Show("技能栏保存请求发送失败，请重试。");
            return false;
        }

        nextSkillBarSaveSequence = request.requestSequence;
        pendingSkillBarSave = request;
        skillBarSaveDeadline = Time.realtimeSinceStartup + SkillRequestTimeout;
        skillBarSaveRetried = false;
        SkillBarEditAvailabilityChanged?.Invoke();
        return true;
    }

    private void UpdateSkillBarSave()
    {
        if ((pendingSkillBarSave == null && skillBarRefreshMinSequence == 0)
            || mapTransferPaused || !IsCurrentSkillSession())
            return;

        float now = Time.realtimeSinceStartup;
        if (pendingSkillBarSave != null && now >= skillBarSaveDeadline)
        {
            if (!skillBarSaveRetried)
            {
                skillBarSaveRetried = true;
                skillBarSaveDeadline = now + SkillRequestTimeout;
                networkManager.Send(pendingSkillBarSave);
            }
            else
            {
                pendingSkillBarSave = null;
                QueueSkillBarRefresh();
                GameNoticeService.Show("技能栏保存结果尚未确认，正在同步。");
            }
        }

        if (skillBarRefreshMinSequence != 0 && now >= skillBarRefreshRetryAt)
        {
            RequestSkillSnapshot();
            skillBarRefreshRetryAt = now + SkillRequestTimeout;
        }
    }

    private void HandleSkillBarSaveResult(string json)
    {
        SkillBarSaveRequest request = pendingSkillBarSave;
        if (request == null || !IsCurrentSkillSession())
            return;

        SkillBarSaveResult result = ReadSkillMessage<SkillBarSaveResult>(json);
        if (result == null || result.characterDbId != skillCharacterDbId
            || result.learnSessionId != request.learnSessionId || result.requestSequence != request.requestSequence
            || (!result.success && string.IsNullOrWhiteSpace(result.errorCode)))
            return;

        if (!result.success && result.errorCode == "skill_bar_persist_failed" && !skillBarSaveRetried)
            return;

        pendingSkillBarSave = null;
        if (!result.skillBarRevision.HasValue || result.skillBarRevision < 0
            || (result.success && result.skillBarRevision < request.expectedSkillBarRevision))
        {
            QueueSkillBarRefresh();
        }
        else if (result.skillBarRevision >= LocalSkillSnapshot?.skillBarRevision
            && !TryApplySkillBarLayout(result.skillBarRevision.Value, result.slots))
        {
            QueueSkillBarRefresh(result.skillBarRevision.Value);
        }

        if (!result.success)
        {
            string notice = "技能栏保存失败，请确认槽位后重试。";
            if (result.errorCode == "skill_bar_revision_conflict")
                notice = skillBarRefreshMinSequence == 0
                    ? "技能栏布局已更新，请确认槽位后重新装备。"
                    : "技能栏布局已变化，正在同步当前槽位。";
            GameNoticeService.Show(notice);
            Debug.LogWarning("[SkillBar][RX] 保存失败：" + result.errorCode);
        }
        else if (skillBarRefreshMinSequence != 0)
        {
            GameNoticeService.Show("正在同步技能栏布局。");
        }
        SkillBarEditAvailabilityChanged?.Invoke();
    }

    private bool TryApplySkillBarLayout(long revision, SkillBarSlot[] slots)
    {
        if (slots == null || slots.Length > 技能栏控制器.SlotCount)
            return false;

        var snapshot = LocalSkillSnapshot;
        var layout = new Dictionary<string, int>(StringComparer.Ordinal);
        var occupiedSlots = new HashSet<int>();
        foreach (SkillBarSlot slot in slots)
        {
            if (slot == null || slot.slotIndex < 1 || slot.slotIndex > 技能栏控制器.SlotCount
                || string.IsNullOrWhiteSpace(slot.skillId) || !occupiedSlots.Add(slot.slotIndex)
                || !layout.TryAdd(slot.skillId, slot.slotIndex)
                || !Array.Exists(snapshot.skills, skill => skill.skillId == slot.skillId))
                return false;
        }

        foreach (SkillSnapshotEntry skill in snapshot.skills)
            skill.equippedSlot = layout.TryGetValue(skill.skillId, out int slotIndex) ? slotIndex : (int?)null;
        snapshot.skillBarRevision = revision;
        PublishSkillSnapshot(snapshot, refreshSkillBar: true);
        return true;
    }

    private void QueueSkillBarRefresh(long minimumRevision = 0)
    {
        // A snapshot already in flight may have been read before the save committed.
        skillBarRefreshMinSequence = nextSkillSnapshotSequence + 1;
        skillBarRefreshMinRevision = minimumRevision;
        skillBarRefreshRetryAt = Time.realtimeSinceStartup;
    }

    public bool RequestSkillSnapshot()
    {
        if (!IsCurrentSkillSession()
            || (pendingSkillSnapshotSequence != 0 && Time.realtimeSinceStartup < skillSnapshotDeadline))
            return false;

        var request = new SkillSnapshotRequest(nextSkillSnapshotSequence + 1);
        if (!networkManager.Send(request))
            return false;

        if (pendingSkillSnapshotSequence != 0)
            skillSnapshotBatches.Remove(pendingSkillSnapshotSequence);
        pendingSkillSnapshotSequence = nextSkillSnapshotSequence = request.requestSequence;
        skillSnapshotDeadline = Time.realtimeSinceStartup + SkillRequestTimeout;
        return true;
    }

    private void BeginSkillSession(long characterDbId, string learnSessionId)
    {
        if (skillCharacterDbId == characterDbId
            && skillConnectionEpoch == networkManager.ConnectionEpoch
            && skillLearnSessionId == learnSessionId)
            return;

        ResetSkillSnapshot();
        skillCharacterDbId = characterDbId;
        skillConnectionEpoch = networkManager.ConnectionEpoch;
        skillLearnSessionId = learnSessionId;
    }

    private bool IsCurrentSkillSession() => sessionStarted && gameSessionAccepted
        && !returningToLogin && !worldRecoveryPending && skillCharacterDbId > 0
        && skillCharacterDbId == localCharacterDbId
        && networkManager != null && networkManager.IsConnected
        && networkManager.ConnectionEpoch == skillConnectionEpoch;

    private void RetryTimedOutSkillLearn()
    {
        if (pendingSkillLearn != null && !skillLearnRetried && !mapTransferPaused
            && IsCurrentSkillSession())
            ResendPendingSkillLearn();
    }

    private bool ResendPendingSkillLearn()
    {
        if (Time.realtimeSinceStartup < skillLearnRetryAt)
            return false;

        // The server checks the original revision, stack and tick when replaying a request.
        skillLearnRetryAt = Time.realtimeSinceStartup + SkillRequestTimeout;
        skillLearnRetried = true;
        return networkManager.Send(pendingSkillLearn);
    }

    private void HandleSkillLearnResult(string json)
    {
        if (pendingSkillLearn == null || !IsCurrentSkillSession())
            return;

        SkillLearnResult result = ReadSkillMessage<SkillLearnResult>(json);
        SkillLearnRequest request = pendingSkillLearn;
        if (result == null || result.requestSequence != request.requestSequence
            || result.learnSessionId != request.learnSessionId
            || (!result.success && string.IsNullOrWhiteSpace(result.errorCode)))
            return;

        if (!result.success && result.errorCode == "skill_learn_persist_failed")
        {
            // The server may have committed before returning the error. Retry the same request.
            skillLearnRetryAt = Time.realtimeSinceStartup + SkillRequestTimeout;
            skillLearnRetried = true;
        }
        else
        {
            pendingSkillLearn = null;
            if (result.success || SkillLearnRejectionWasRecorded(result.errorCode))
                completedSkillLearnSequence = request.requestSequence;
        }

        var inventory = FindObjectOfType<InventoryModuleRuntime>();
        if (result.success && inventory != null)
        {
            bool requiresSnapshot = result.requiresInventorySnapshot
                || result.inventoryRevision <= request.expectedInventoryRevision
                || inventory.CurrentSnapshot?.CharacterDbId != skillCharacterDbId;
            inventory.ApplySkillLearnInventoryChanges(result.inventoryRevision,
                requiresSnapshot, result.stackItemChanges);
        }
        else if (result.requiresInventorySnapshot || result.errorCode == "inventory_revision_conflict")
        {
            inventory?.RequestReconciliation();
        }

        if (result.success)
        {
            SkillSnapshotEntry learned = result.learnedSkill;
            if (learned == null || string.IsNullOrWhiteSpace(learned.skillId))
            {
                Debug.LogWarning("[SkillLearn][RX] 学习回执缺少已学技能，正在刷新技能列表。");
                RequestSkillSnapshot();
                return;
            }

            confirmedSkillLearns.Add(learned.skillId);
            var skills = new List<SkillSnapshotEntry>(
                LocalSkillSnapshot?.skills ?? Array.Empty<SkillSnapshotEntry>());
            int existingIndex = skills.FindIndex(skill => skill.skillId == learned.skillId);
            learned.equippedSlot = existingIndex >= 0 ? skills[existingIndex].equippedSlot : null;
            NormalizeSkillProgress(learned);
            if (existingIndex >= 0)
                TryApplySkillProgress(learned, skills[existingIndex].progress, true);
            ApplyProgressBeforeOwnership(learned, true);
            if (existingIndex < 0)
                skills.Add(learned);
            else
                skills[existingIndex] = learned;

            PublishSkillSnapshot(new SkillSnapshotResult
            {
                success = true,
                characterDbId = skillCharacterDbId,
                skillRevision = LocalSkillSnapshot?.skillRevision ?? 0,
                skillBarRevision = LocalSkillSnapshot?.skillBarRevision,
                skills = skills.ToArray()
            });
        }
        SkillLearnCompleted?.Invoke(result);
    }

    private static bool SkillLearnRejectionWasRecorded(string errorCode)
    {
        // These rejections leave the server sequence available for a fresh request.
        switch (errorCode)
        {
            case "invalid_skill_learn_request":
            case "skill_learn_session_invalid":
            case "skill_learning_disabled":
            case "skill_learn_sequence_gap":
            case "skill_learn_sequence_conflict":
            case "inventory_not_found":
            case "character_not_found":
                return false;
            default:
                return true;
        }
    }

    private void HandleSkillSnapshotResult(string json)
    {
        if (!IsCurrentSkillSession())
            return;

        SkillSnapshotResult result = ReadSkillMessage<SkillSnapshotResult>(json);
        if (result == null || result.characterDbId != skillCharacterDbId
            || result.requestSequence < 0)
            return;

        long sequence = result.requestSequence;
        if (sequence == 0 ? receivedSkillSnapshot : sequence != pendingSkillSnapshotSequence)
            return;

        if (!result.success)
        {
            FinishSkillSnapshot(sequence);
            Debug.LogWarning("[SkillSnapshot][RX] 请求被拒绝：" + (result.errorCode ?? result.message));
            return;
        }

        if (!TryCollectSkillSnapshot(result, out SkillSnapshotEntry[] entries))
            return;

        FinishSkillSnapshot(sequence);
        long currentRevision = LocalSkillSnapshot?.skillRevision ?? 0;
        if (result.skillRevision > 0 && result.skillRevision < currentRevision)
            return;
        long? currentBarRevision = LocalSkillSnapshot?.skillBarRevision;
        if (result.skillBarRevision < 0 || (currentBarRevision.HasValue
            && (!result.skillBarRevision.HasValue || result.skillBarRevision < currentBarRevision)))
            return;
        if (skillBarRefreshMinSequence != 0 && result.skillBarRevision < skillBarRefreshMinRevision)
            return;

        var skills = new Dictionary<string, SkillSnapshotEntry>(StringComparer.Ordinal);
        var occupiedSlots = new HashSet<int>();
        foreach (SkillSnapshotEntry entry in entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.skillId)
                || !skills.TryAdd(entry.skillId, entry))
            {
                Debug.LogWarning("[SkillSnapshot][RX] 快照含无效或重复的技能 ID。");
                return;
            }

            if (!result.skillBarRevision.HasValue || !entry.equippedSlot.HasValue)
                continue;

            int slot = entry.equippedSlot.Value;
            if (slot < 1 || slot > 技能栏控制器.SlotCount || !occupiedSlots.Add(slot))
            {
                Debug.LogWarning("[SkillSnapshot][RX] 快照含无效或重复的技能栏槽位。");
                return;
            }
        }

        var orderedSkills = new List<SkillSnapshotEntry>(entries);
        foreach (SkillSnapshotEntry entry in entries)
            NormalizeSkillProgress(entry);

        bool preserveCurrentOnEqualRevision = sequence == 0;
        if (LocalSkillSnapshot != null)
            foreach (SkillSnapshotEntry previous in LocalSkillSnapshot.skills)
            {
                if (skills.TryGetValue(previous.skillId, out SkillSnapshotEntry incoming))
                    TryApplySkillProgress(incoming, previous.progress, preserveCurrentOnEqualRevision);
                // A snapshot can have been read before a confirmed learn committed.
                else if (confirmedSkillLearns.Contains(previous.skillId))
                {
                    previous.equippedSlot = null;
                    orderedSkills.Add(previous);
                }
            }
        foreach (SkillSnapshotEntry entry in entries)
            ApplyProgressBeforeOwnership(entry, preserveCurrentOnEqualRevision);

        receivedSkillSnapshot = true;
        skillSnapshotBatches.Remove(0);
        result.skillRevision = Math.Max(currentRevision, result.skillRevision);
        result.skills = orderedSkills.ToArray();
        if (skillBarRefreshMinSequence != 0 && sequence >= skillBarRefreshMinSequence
            && result.skillBarRevision.HasValue)
            skillBarRefreshMinSequence = 0;
        PublishSkillSnapshot(result, refreshSkillBar: true);
    }

    private bool TryCollectSkillSnapshot(SkillSnapshotResult result, out SkillSnapshotEntry[] entries)
    {
        entries = null;
        long sequence = result.requestSequence;
        bool hasBatch = skillSnapshotBatches.TryGetValue(sequence, out SkillSnapshotBatches batches);
        if (result.batchCount == null)
        {
            if (hasBatch || result.batchIndex != null || result.totalCount != null
                || !string.IsNullOrEmpty(result.snapshotId))
                return false;
            entries = result.skills ?? Array.Empty<SkillSnapshotEntry>();
            return true;
        }

        if (result.batchCount == 1)
        {
            if (hasBatch || result.batchIndex != 1 || !string.IsNullOrEmpty(result.snapshotId)
                || result.skills == null || (result.totalCount.HasValue && result.totalCount != result.skills.Length))
                return false;
            entries = result.skills;
            return true;
        }

        if (result.batchCount < 2 || !result.batchIndex.HasValue || result.batchIndex < 1
            || result.batchIndex > result.batchCount || !result.totalCount.HasValue
            || result.totalCount < 1 || string.IsNullOrWhiteSpace(result.snapshotId)
            || result.skills == null || result.skills.Length > result.totalCount)
            return false;

        if (!hasBatch)
        {
            batches = new SkillSnapshotBatches(result);
            skillSnapshotBatches.Add(sequence, batches);
        }

        return batches.TryAdd(result, out entries);
    }

    private void FinishSkillSnapshot(long sequence)
    {
        skillSnapshotBatches.Remove(sequence);
        if (sequence != 0)
            pendingSkillSnapshotSequence = 0;
    }

    private void PublishSkillSnapshot(SkillSnapshotResult snapshot, bool refreshSkillBar = false)
    {
        snapshot.snapshotId = string.Empty;
        snapshot.batchIndex = 1;
        snapshot.batchCount = 1;
        snapshot.totalCount = snapshot.skills.Length;
        LocalSkillSnapshot = snapshot;
        if (refreshSkillBar)
            LocalSkillBarSnapshotChanged?.Invoke(snapshot);
        LocalSkillSnapshotChanged?.Invoke(snapshot);
    }

    private void HandleSkillProgressResult(string json)
    {
        if (!IsCurrentSkillSession())
            return;

        SkillProgressResult result = ReadSkillMessage<SkillProgressResult>(json);
        if (result == null || result.characterDbId != skillCharacterDbId || !IsValidSkillProgress(result.skill))
            return;

        SkillProgressData progress = result.skill;
        if (LocalSkillSnapshot != null)
            foreach (SkillSnapshotEntry learned in LocalSkillSnapshot.skills)
            {
                if (learned.skillId != progress.skillId)
                    continue;
                if (TryApplySkillProgress(learned, progress, false))
                    LocalSkillProgressChanged?.Invoke(learned);
                return;
            }

        // The owning skill may still be in a pending snapshot batch or learn result.
        if (!progressBeforeOwnership.TryGetValue(progress.skillId, out SkillProgressData pending)
            || progress.practiceRevision > pending.practiceRevision)
            progressBeforeOwnership[progress.skillId] = progress;
    }

    private static void NormalizeSkillProgress(SkillSnapshotEntry entry)
    {
        if (!IsValidSkillProgress(entry.progress) || entry.progress.skillId != entry.skillId)
            entry.progress = null;
        else
            entry.skillLevel = entry.progress.skillLevel;
    }

    private void ApplyProgressBeforeOwnership(SkillSnapshotEntry entry, bool allowEqualRevision)
    {
        if (progressBeforeOwnership.Remove(entry.skillId, out SkillProgressData pending))
            TryApplySkillProgress(entry, pending, allowEqualRevision);
    }

    private static bool TryApplySkillProgress(SkillSnapshotEntry entry, SkillProgressData progress,
        bool allowEqualRevision)
    {
        if (progress == null || (entry.progress != null
            && (progress.practiceRevision < entry.progress.practiceRevision
                || (!allowEqualRevision && progress.practiceRevision == entry.progress.practiceRevision))))
            return false;

        entry.progress = progress;
        entry.skillLevel = progress.skillLevel;
        return true;
    }

    private static bool IsValidSkillProgress(SkillProgressData progress)
    {
        return progress != null && !string.IsNullOrWhiteSpace(progress.skillId)
            && progress.skillLevel > 0 && progress.practiceRevision >= 0
            && progress.practiceCount >= 0 && (!progress.requiredCasts.HasValue || progress.requiredCasts > 0);
    }

    private static T ReadSkillMessage<T>(string json) where T : NetMessageBase
    {
        try { return JsonConvert.DeserializeObject<T>(json); }
        catch (JsonException exception)
        {
            Debug.LogWarning("[Skills][RX] 无法解析回包：" + exception.Message);
            return null;
        }
    }

    private void ResetSkillSnapshot()
    {
        skillLearnSessionId = null;
        skillConnectionEpoch = 0;
        skillCharacterDbId = 0;
        completedSkillLearnSequence = 0;
        pendingSkillLearn = null;
        nextSkillBarSaveSequence = 0;
        pendingSkillBarSave = null;
        skillBarRefreshMinSequence = 0;
        skillBarRefreshMinRevision = 0;
        nextSkillSnapshotSequence = 0;
        pendingSkillSnapshotSequence = 0;
        receivedSkillSnapshot = false;
        skillSnapshotBatches.Clear();
        confirmedSkillLearns.Clear();
        progressBeforeOwnership.Clear();
        if (LocalSkillSnapshot == null)
            return;
        LocalSkillSnapshot = null;
        LocalSkillBarSnapshotChanged?.Invoke(null);
        LocalSkillSnapshotChanged?.Invoke(null);
    }

    private sealed class SkillSnapshotBatches
    {
        private readonly string id;
        private readonly int batchCount;
        private readonly int totalCount;
        private readonly long skillRevision;
        private readonly long? skillBarRevision;
        private readonly Dictionary<int, SkillSnapshotEntry[]> parts =
            new Dictionary<int, SkillSnapshotEntry[]>();
        private int entryCount;

        public SkillSnapshotBatches(SkillSnapshotResult first)
        {
            id = first.snapshotId;
            batchCount = first.batchCount.Value;
            totalCount = first.totalCount.Value;
            skillRevision = first.skillRevision;
            skillBarRevision = first.skillBarRevision;
        }

        public bool TryAdd(SkillSnapshotResult batch, out SkillSnapshotEntry[] entries)
        {
            entries = null;
            if (batch.snapshotId != id || batch.batchCount != batchCount
                || batch.totalCount != totalCount || batch.skillRevision != skillRevision
                || batch.skillBarRevision != skillBarRevision
                || batch.skills.Length > totalCount - entryCount
                || !parts.TryAdd(batch.batchIndex.Value, batch.skills))
                return false;

            entryCount += batch.skills.Length;
            if (parts.Count != batchCount || entryCount != totalCount)
                return false;

            entries = new SkillSnapshotEntry[entryCount];
            int offset = 0;
            for (int index = 1; index <= batchCount; index++)
            {
                SkillSnapshotEntry[] part = parts[index];
                part.CopyTo(entries, offset);
                offset += part.Length;
            }
            return true;
        }
    }
}
