using System;
using System.Globalization;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LocalPlayerCoordinateTextPresenter : MonoBehaviour
{
    private const float UpdateDistance = 1f;
    [SerializeField] private GameSession session;
    [SerializeField] private TMP_Text coordinateText;
    private bool hasDisplayedPosition;
    private Vector2 displayedPosition;
    private GameSession displayedSession;
    private long displayedCharacterDbId;
    private string displayedSceneSessionId;

    private void Awake()
    {
        if (coordinateText == null) coordinateText = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        hasDisplayedPosition = false;
        Refresh();
    }

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        if (coordinateText == null) return;
        if (session == null) session = FindObjectOfType<GameSession>();
        if (session == null || !session.TryGetLocalPlayerCoordinates(out Vector2 coordinates))
        {
            hasDisplayedPosition = false;
            SetText("X（--）Z（--）");
            return;
        }

        bool samePlayerSession = hasDisplayedPosition && displayedSession == session
            && displayedCharacterDbId == session.LocalCharacterDbId
            && displayedSceneSessionId == session.SceneSessionId;
        // Compare unrounded X/Z against the last published position, not the
        // previous frame. Exactly one unit does not refresh the display.
        if (samePlayerSession && (coordinates - displayedPosition).sqrMagnitude <= UpdateDistance * UpdateDistance)
            return;

        double x = Math.Round(coordinates.x, 0, MidpointRounding.AwayFromZero);
        double z = Math.Round(coordinates.y, 0, MidpointRounding.AwayFromZero);
        // Normalize negative zero, and keep the user's full-width parentheses.
        SetText("X（" + (x == 0 ? 0 : x).ToString("0", CultureInfo.InvariantCulture)
            + "）Z（" + (z == 0 ? 0 : z).ToString("0", CultureInfo.InvariantCulture) + "）");
        displayedPosition = coordinates;
        displayedSession = session;
        displayedCharacterDbId = session.LocalCharacterDbId;
        displayedSceneSessionId = session.SceneSessionId;
        hasDisplayedPosition = true;
    }

    private void SetText(string value)
    {
        if (!string.Equals(coordinateText.text, value, StringComparison.Ordinal))
            coordinateText.text = value;
    }
}
