using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Image), typeof(AspectRatioFitter))]
public sealed class LoadingBackgroundSlideshow : MonoBehaviour
{
    [Tooltip("按播放顺序排列；每次显示加载页面时随机选择起始图片。")]
    [SerializeField] private Sprite[] backgrounds = new Sprite[0];
    [Tooltip("每张图片的显示时间，到时直接切换下一张。")]
    [Min(0f)] [SerializeField] private float holdSeconds = 3f;

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
        elapsedSeconds = 0d;
        ShowCurrentBackground();
    }

    private void Update()
    {
        if (playableBackgrounds.Count <= 1)
            return;

        double cycleSeconds = Mathf.Max(0f, holdSeconds);
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
    }

    private void ShowCurrentBackground()
    {
        Sprite background = playableBackgrounds[currentIndex];
        backgroundImage.sprite = background;
        // 直接换图，始终保持不透明，避免切换时露出底色。
        backgroundImage.color = Color.white;
        aspectFitter.aspectRatio = background.rect.width / background.rect.height;
    }
}
