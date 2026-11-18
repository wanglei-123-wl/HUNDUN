using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CharacterRoleSwitcher : MonoBehaviour
{
    [Header("UI组件")]
    [SerializeField] private Image characterImage;
    [SerializeField] private Button leftArrowButton;
    [SerializeField] private Button rightArrowButton;

    [Header("角色图片")]
    [SerializeField] private List<Sprite> characterSprites = new List<Sprite>();

    [Header("切换设置")]
    [SerializeField] private bool loopSelection = true;
    [SerializeField] private float animationDuration = 0.2f;
    [SerializeField] private float slideDistance = 80f;

    private RectTransform characterRectTransform;
    private CanvasGroup characterCanvasGroup;

    private Vector2 originalPosition;
    private int currentIndex;
    private bool isSwitching;

    public event Action<int> OnIndexChanged;

    public int CurrentIndex => currentIndex;
    public int ItemCount => characterSprites == null ? 0 : characterSprites.Count;
    public bool HasItems => ItemCount > 0;
    public IReadOnlyList<Sprite> Sprites => characterSprites;

    public Sprite CurrentCharacterSprite
    {
        get
        {
            if (characterSprites == null ||
                characterSprites.Count == 0 ||
                currentIndex < 0 ||
                currentIndex >= characterSprites.Count)
            {
                return null;
            }

            return characterSprites[currentIndex];
        }
    }

    private void Awake()
    {
        if (characterImage != null)
        {
            characterRectTransform =
                characterImage.GetComponent<RectTransform>();

            originalPosition =
                characterRectTransform.anchoredPosition;

            characterCanvasGroup =
                characterImage.GetComponent<CanvasGroup>();

            if (characterCanvasGroup == null)
            {
                characterCanvasGroup =
                    characterImage.gameObject.AddComponent<CanvasGroup>();
            }
        }
    }

    private void OnEnable()
    {
        if (leftArrowButton != null)
        {
            leftArrowButton.onClick.AddListener(SelectPrevious);
        }

        if (rightArrowButton != null)
        {
            rightArrowButton.onClick.AddListener(SelectNext);
        }

        RefreshImmediately();
    }

    public void SetSprites(IReadOnlyList<Sprite> newSprites, int initialIndex = 0)
    {
        StopAllCoroutines();
        isSwitching = false;
        characterSprites.Clear();

        if (newSprites != null)
        {
            for (int index = 0; index < newSprites.Count; index++)
            {
                characterSprites.Add(newSprites[index]);
            }
        }

        currentIndex = characterSprites.Count == 0
            ? -1
            : Mathf.Clamp(initialIndex, 0, characterSprites.Count - 1);

        RefreshImmediately();
        OnIndexChanged?.Invoke(currentIndex);
    }

    public void Clear()
    {
        SetSprites(null);
    }

    public void SelectIndex(int index, bool animate)
    {
        if (isSwitching || index < 0 || index >= ItemCount)
        {
            return;
        }

        if (!animate || index == currentIndex)
        {
            currentIndex = index;
            RefreshImmediately();
            OnIndexChanged?.Invoke(currentIndex);
            return;
        }

        int direction = index > currentIndex ? 1 : -1;
        StartCoroutine(SwitchCharacter(index, direction));
    }

    private void OnDisable()
    {
        if (leftArrowButton != null)
        {
            leftArrowButton.onClick.RemoveListener(SelectPrevious);
        }

        if (rightArrowButton != null)
        {
            rightArrowButton.onClick.RemoveListener(SelectNext);
        }

        StopAllCoroutines();
        isSwitching = false;
    }

    public void SelectPrevious()
    {
        if (!CanSwitch())
        {
            return;
        }

        int targetIndex = currentIndex - 1;

        if (targetIndex < 0)
        {
            if (!loopSelection)
            {
                return;
            }

            targetIndex = characterSprites.Count - 1;
        }

        StartCoroutine(SwitchCharacter(targetIndex, -1));
    }

    public void SelectNext()
    {
        if (!CanSwitch())
        {
            return;
        }

        int targetIndex = currentIndex + 1;

        if (targetIndex >= characterSprites.Count)
        {
            if (!loopSelection)
            {
                return;
            }

            targetIndex = 0;
        }

        StartCoroutine(SwitchCharacter(targetIndex, 1));
    }

    private bool CanSwitch()
    {
        return !isSwitching &&
               characterImage != null &&
               characterSprites != null &&
               characterSprites.Count > 1;
    }

    private IEnumerator SwitchCharacter(
        int targetIndex,
        int direction)
    {
        isSwitching = true;
        SetButtonsInteractable(false);

        Vector2 exitPosition =
            originalPosition +
            Vector2.left * direction * slideDistance;

        yield return Animate(
            originalPosition,
            exitPosition,
            1f,
            0f);

        currentIndex = targetIndex;
        characterImage.sprite = characterSprites[currentIndex];

        characterRectTransform.anchoredPosition =
            originalPosition +
            Vector2.right * direction * slideDistance;

        characterCanvasGroup.alpha = 0f;

        yield return Animate(
            characterRectTransform.anchoredPosition,
            originalPosition,
            0f,
            1f);

        characterRectTransform.anchoredPosition = originalPosition;
        characterCanvasGroup.alpha = 1f;

        isSwitching = false;
        RefreshButtonState();
        OnIndexChanged?.Invoke(currentIndex);
    }

    private IEnumerator Animate(
        Vector2 startPosition,
        Vector2 endPosition,
        float startAlpha,
        float endAlpha)
    {
        float elapsedTime = 0f;

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.unscaledDeltaTime;

            float progress = animationDuration <= 0f
                ? 1f
                : Mathf.Clamp01(elapsedTime / animationDuration);

            float smoothProgress =
                progress * progress * (3f - 2f * progress);

            characterRectTransform.anchoredPosition =
                Vector2.Lerp(
                    startPosition,
                    endPosition,
                    smoothProgress);

            characterCanvasGroup.alpha =
                Mathf.Lerp(
                    startAlpha,
                    endAlpha,
                    smoothProgress);

            yield return null;
        }

        characterRectTransform.anchoredPosition = endPosition;
        characterCanvasGroup.alpha = endAlpha;
    }

    private void RefreshImmediately()
    {
        if (characterImage == null)
        {
            Debug.LogWarning(
                "CharacterRoleSwitcher：没有绑定 Character Image。",
                this);
            return;
        }

        if (characterSprites == null ||
            characterSprites.Count == 0)
        {
            currentIndex = -1;
            characterImage.sprite = null;
            characterImage.enabled = false;
            SetButtonsInteractable(false);
            return;
        }

        currentIndex = Mathf.Clamp(
            currentIndex,
            0,
            characterSprites.Count - 1);

        characterImage.enabled = true;
        characterImage.sprite = characterSprites[currentIndex];

        if (characterRectTransform != null)
        {
            characterRectTransform.anchoredPosition = originalPosition;
        }

        if (characterCanvasGroup != null)
        {
            characterCanvasGroup.alpha = 1f;
        }

        RefreshButtonState();
    }

    private void RefreshButtonState()
    {
        if (characterSprites == null ||
            characterSprites.Count <= 1)
        {
            SetButtonsInteractable(false);
            return;
        }

        if (loopSelection)
        {
            SetButtonsInteractable(true);
            return;
        }

        if (leftArrowButton != null)
        {
            leftArrowButton.interactable = currentIndex > 0;
        }

        if (rightArrowButton != null)
        {
            rightArrowButton.interactable =
                currentIndex < characterSprites.Count - 1;
        }
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (leftArrowButton != null)
        {
            leftArrowButton.interactable = interactable;
        }

        if (rightArrowButton != null)
        {
            rightArrowButton.interactable = interactable;
        }
    }
}
