using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image), typeof(AspectRatioFitter))]
public sealed class LoadingBackgroundSlideshow : MonoBehaviour
{
    [Tooltip("按播放顺序排列；每次显示加载页面时随机选择起始图片。")]
    [SerializeField] private Sprite[] backgrounds = new Sprite[0];
    [Tooltip("每张图片完全显示的停留时间，不含淡入淡出。")]
    [Min(0f)] [SerializeField] private float holdSeconds = 3f;
    [Tooltip("淡出和淡入各自使用的时间。")]
    [Min(0f)] [SerializeField] private float fadeSeconds = 0.6f;

    private readonly List<Sprite> playableBackgrounds = new List<Sprite>();
    private Image backgroundImage;
    private AspectRatioFitter aspectFitter;
    private int currentIndex;
    private double elapsedSeconds;

    private void OnEnable()
    {
        backgroundImage = GetComponent<Image>();
        aspectFitter = GetComponent<AspectRatioFitter>();
        aspectFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        backgroundImage.raycastTarget = false;

        playableBackgrounds.Clear();
        if (backgrounds != null)
        {
            foreach (Sprite background in backgrounds)
            {
                if (background != null)
                    playableBackgrounds.Add(background);
            }
        }

        backgroundImage.enabled = playableBackgrounds.Count > 0;
        if (playableBackgrounds.Count == 0)
            return;

        currentIndex = Random.Range(0, playableBackgrounds.Count);
        // 首张直接完整显示，后续图片依次淡入、停留、淡出。
        elapsedSeconds = Mathf.Max(0f, fadeSeconds);
        ShowCurrentBackground();
        SetAlpha(1f);
    }

    private void Update()
    {
        if (playableBackgrounds.Count <= 1)
            return;

        float hold = Mathf.Max(0f, holdSeconds);
        float fade = Mathf.Max(0f, fadeSeconds);
        double cycleSeconds = hold + 2d * fade;
        if (cycleSeconds <= 0d)
            return;

        // 不受游戏暂停或 Time.timeScale 影响；掉帧时保留超出的时间。
        elapsedSeconds += Time.unscaledDeltaTime;
        if (elapsedSeconds >= cycleSeconds)
        {
            int advanced = (int)((elapsedSeconds / cycleSeconds) % playableBackgrounds.Count);
            currentIndex = (currentIndex + advanced) % playableBackgrounds.Count;
            elapsedSeconds %= cycleSeconds;
            ShowCurrentBackground();
        }

        float alpha = 1f;
        if (fade > 0f)
        {
            if (elapsedSeconds < fade)
                alpha = (float)(elapsedSeconds / fade);
            else if (elapsedSeconds > fade + hold)
                alpha = (float)((cycleSeconds - elapsedSeconds) / fade);
        }
        SetAlpha(alpha);
    }

    private void ShowCurrentBackground()
    {
        Sprite background = playableBackgrounds[currentIndex];
        backgroundImage.sprite = background;
        aspectFitter.aspectRatio = background.rect.width / background.rect.height;
    }

    private void SetAlpha(float alpha)
    {
        backgroundImage.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
    }
}
