using System.Collections.Generic;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class LearnedSkillListView : MonoBehaviour
{
    [SerializeField] private GameSession session;
    [SerializeField] private 技能栏控制器 skillBar;
    [SerializeField] private RectTransform content;
    [SerializeField] private RectTransform slotPrefab;
    [SerializeField] private Image detailIcon;
    [SerializeField] private TMP_Text detailName;
    [SerializeField] private TMP_Text detailLevel;
    [SerializeField] private TMP_Text detailDescription;
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text progressValue;
    [SerializeField] private Button[] slotButtons = new Button[技能栏控制器.SlotCount];

    private readonly List<RectTransform> slots = new List<RectTransform>();
    private readonly List<SkillSnapshotEntry> visibleSkills = new List<SkillSnapshotEntry>();
    private UnityAction[] slotActions;
    private string selectedSkillId;
    private ItemDetailData selectedDetail;
    private ScrollRect descriptionScroll;

    private void OnEnable()
    {
        CreateDescriptionScroll();
        if (slotActions == null)
        {
            slotActions = new UnityAction[slotButtons.Length];
            for (int index = 0; index < slotActions.Length; index++)
            {
                int slotNumber = index + 1;
                slotActions[index] = () => skillBar?.TryBindLearnedSkill(slotNumber, selectedSkillId);
            }
        }
        for (int index = 0; index < slotButtons.Length; index++)
            if (slotButtons[index] != null)
                slotButtons[index].onClick.AddListener(slotActions[index]);

        if (session != null)
        {
            session.LocalSkillSnapshotChanged += Render;
            session.LocalSkillProgressChanged += HandleSkillProgressChanged;
            session.SkillBarEditAvailabilityChanged += RefreshSlotButtons;
        }
        Render(session != null ? session.LocalSkillSnapshot : null);
    }

    private void OnDisable()
    {
        if (session != null)
        {
            session.LocalSkillSnapshotChanged -= Render;
            session.LocalSkillProgressChanged -= HandleSkillProgressChanged;
            session.SkillBarEditAvailabilityChanged -= RefreshSlotButtons;
        }
        if (slotActions != null)
            for (int index = 0; index < slotButtons.Length; index++)
                if (slotButtons[index] != null)
                    slotButtons[index].onClick.RemoveListener(slotActions[index]);
        selectedSkillId = null;
        selectedDetail = null;
    }

    public void SelectSkill(string skillId)
    {
        SkillSnapshotEntry skill = visibleSkills.Find(entry => entry.skillId == skillId);
        if (skill != null)
            SetSelectedSkill(skill);
    }

    private void Render(SkillSnapshotResult snapshot)
    {
        visibleSkills.Clear();
        if (content != null && slotPrefab != null && session != null
            && session.LocalCharacterDbId > 0 && snapshot != null && snapshot.success
            && snapshot.characterDbId == session.LocalCharacterDbId && snapshot.skills != null)
        {
            foreach (SkillSnapshotEntry skill in snapshot.skills)
            {
                if (string.IsNullOrWhiteSpace(skill?.skillId))
                    continue;

                int index = visibleSkills.Count;
                visibleSkills.Add(skill);
                if (index == slots.Count)
                    slots.Add(CreateSlot(index));
                Sprite icon = null;
                if (skillBar != null && skillBar.TryGetSkillDetail(skill.skillId, out var detail))
                    icon = detail.Icon;
                SetIcon(slots[index].Find("图标")?.GetComponent<Image>(), icon);
            }
        }

        for (int index = slots.Count - 1; index >= visibleSkills.Count; index--)
        {
            GameObject slot = slots[index].gameObject;
            slot.SetActive(false);
            Destroy(slot);
            slots.RemoveAt(index);
        }

        SkillSnapshotEntry selected = visibleSkills.Find(entry => entry.skillId == selectedSkillId);
        SetSelectedSkill(selected ?? (visibleSkills.Count == 0 ? null : visibleSkills[0]));
    }

    private RectTransform CreateSlot(int index)
    {
        RectTransform slot = Instantiate(slotPrefab, content, false);
        Button button = slot.GetComponent<Button>();
        if (button == null)
        {
            button = slot.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
        }
        button.onClick.AddListener(() => SetSelectedSkill(visibleSkills[index]));
        return slot;
    }

    private void SetSelectedSkill(SkillSnapshotEntry skill)
    {
        bool changed = selectedSkillId != skill?.skillId;
        selectedSkillId = skill?.skillId;
        for (int index = 0; index < slots.Count; index++)
        {
            Transform highlight = slots[index].Find("图标/选中效果");
            if (highlight != null)
                highlight.gameObject.SetActive(visibleSkills[index].skillId == selectedSkillId);
        }

        selectedDetail = null;
        if (selectedSkillId != null && skillBar != null)
            skillBar.TryGetSkillDetail(selectedSkillId, out selectedDetail);
        SetIcon(detailIcon, selectedDetail?.Icon);
        RefreshDetails(skill, changed);
    }

    private void HandleSkillProgressChanged(SkillSnapshotEntry skill)
    {
        if (skill.skillId == selectedSkillId)
            RefreshDetails(skill, false);
    }

    private void RefreshDetails(SkillSnapshotEntry skill, bool resetScroll)
    {
        SkillProgressData progress = skill?.progress;
        string serverName = progress?.attributes?.name;
        if (detailName != null)
            detailName.text = !string.IsNullOrWhiteSpace(serverName) ? serverName
                : !string.IsNullOrWhiteSpace(selectedDetail?.DisplayName)
                    ? selectedDetail.DisplayName : selectedSkillId ?? string.Empty;
        if (detailLevel != null)
            detailLevel.text = SkillProgressText.FormatLevel(skill);
        RefreshProgress(progress);
        SetDescription(SkillProgressText.FormatDetails(progress, selectedDetail?.Description), resetScroll);
        RefreshSlotButtons();
    }

    private void RefreshSlotButtons()
    {
        bool canBind = selectedSkillId != null && skillBar != null
            && skillBar.CanBindLearnedSkill(selectedSkillId);
        foreach (Button button in slotButtons)
            if (button != null)
                button.interactable = canBind;
    }

    private void RefreshProgress(SkillProgressData progress)
    {
        float fill = 0f;
        string value = "—";
        if (progress?.attributes != null)
        {
            if (progress.requiredCasts is long requiredCasts && requiredCasts > 0)
            {
                fill = Mathf.Clamp01((float)progress.practiceCount / requiredCasts);
                value = $"{progress.practiceCount}/{requiredCasts}";
            }
            else if (!progress.requiredCasts.HasValue)
            {
                fill = 1f;
                value = "已满级";
            }
        }

        if (progressFill != null)
            progressFill.fillAmount = fill;
        if (progressValue != null)
            progressValue.text = value;
    }

    private void CreateDescriptionScroll()
    {
        if (detailDescription == null || descriptionScroll != null)
            return;

        RectTransform text = detailDescription.rectTransform;
        var view = new GameObject("技能详情滚动区", typeof(RectTransform),
            typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        var viewport = (RectTransform)view.transform;
        viewport.SetParent(text.parent, false);
        viewport.SetSiblingIndex(text.GetSiblingIndex());
        viewport.anchorMin = text.anchorMin;
        viewport.anchorMax = text.anchorMax;
        viewport.pivot = text.pivot;
        viewport.sizeDelta = text.sizeDelta;
        viewport.anchoredPosition3D = text.anchoredPosition3D;
        viewport.localScale = text.localScale;
        viewport.localRotation = text.localRotation;
        view.GetComponent<Image>().color = Color.clear;

        text.SetParent(viewport, false);
        text.anchorMin = Vector2.up;
        text.anchorMax = Vector2.one;
        text.pivot = new Vector2(0.5f, 1f);
        text.anchoredPosition3D = Vector3.zero;
        text.localScale = Vector3.one;
        text.localRotation = Quaternion.identity;
        text.sizeDelta = new Vector2(0f, viewport.rect.height);
        detailDescription.raycastTarget = false;

        descriptionScroll = view.GetComponent<ScrollRect>();
        descriptionScroll.content = text;
        descriptionScroll.viewport = viewport;
        descriptionScroll.horizontal = false;
        descriptionScroll.vertical = true;
        descriptionScroll.movementType = ScrollRect.MovementType.Clamped;
        descriptionScroll.scrollSensitivity = 30f;
    }

    private void SetDescription(string value, bool resetScroll)
    {
        if (detailDescription == null || (detailDescription.text == value && !resetScroll))
            return;

        float position = 1f;
        RectTransform text = detailDescription.rectTransform;
        if (descriptionScroll != null && !resetScroll && text.rect.height > descriptionScroll.viewport.rect.height)
            position = descriptionScroll.verticalNormalizedPosition;

        detailDescription.text = value;
        if (descriptionScroll == null)
            return;

        float height = detailDescription.GetPreferredValues(value, text.rect.width, 0f).y;
        height = Mathf.Max(descriptionScroll.viewport.rect.height, height);
        if (!Mathf.Approximately(text.rect.height, height))
        {
            text.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            descriptionScroll.verticalNormalizedPosition = position;
        }
        if (resetScroll)
        {
            descriptionScroll.StopMovement();
            descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    private static void SetIcon(Image image, Sprite icon)
    {
        if (image == null)
            return;
        image.sprite = icon;
        image.enabled = icon != null;
    }
}
