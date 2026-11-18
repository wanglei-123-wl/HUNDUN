using System;
using System.Globalization;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class HeartbeatLatencyTextPresenter : MonoBehaviour
{
    private const float RefreshIntervalSeconds = 0.25f;

    [SerializeField] private TMP_Text latencyText;

    private float nextRefreshAt;
    private string displayedText;

    private void Awake()
    {
        if (latencyText == null)
            latencyText = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        nextRefreshAt = 0;
        Refresh();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefreshAt)
            return;

        Refresh();
    }

    private void Refresh()
    {
        nextRefreshAt = Time.unscaledTime + RefreshIntervalSeconds;
        if (latencyText == null)
            return;

        NetworkManager network = NetworkManager.Instance;
        string text = network != null && network.TryGetHeartbeatRoundTripMs(out double roundTripMs)
            ? "\u5EF6\u8FDF: " + Math.Round(roundTripMs).ToString("0", CultureInfo.InvariantCulture) + " ms"
            : "\u5EF6\u8FDF: -- ms";

        if (!string.Equals(displayedText, text, StringComparison.Ordinal))
        {
            latencyText.text = text;
            displayedText = text;
        }
    }
}
