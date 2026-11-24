using UnityEngine;

namespace Game.CharacterPanel.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CharacterPanelInputGuard : MonoBehaviour
    {
        [SerializeField] private CharacterPanelToggle panelToggle;
        [SerializeField] private RectTransform panelHitArea;

        private static CharacterPanelInputGuard active;
        private readonly CharacterPanelPointerCapture capture = new CharacterPanelPointerCapture();

        public static bool BlocksGameplayInput =>
            active != null && active.SampleInput();

        public static bool PointerOnPanel => active != null
            && active.panelToggle != null && active.panelToggle.IsOpen && active.IsPointerOverPanel();

        private void OnEnable()
        {
            active = this;
            capture.Reset();
        }

        private void OnDisable()
        {
            if (active == this) active = null;
            capture.Reset();
        }

        private bool SampleInput()
        {
            bool open = panelToggle != null && panelToggle.IsOpen;
            bool overPanel = open && IsPointerOverPanel();
            int held = 0;
            int pressed = 0;
            int released = 0;
            for (int button = 0; button <= 2; button++)
            {
                int mask = 1 << button;
                if (Input.GetMouseButton(button)) held |= mask;
                if (Input.GetMouseButtonDown(button)) pressed |= mask;
                if (Input.GetMouseButtonUp(button)) released |= mask;
            }

            return capture.Sample(open, overPanel, held, pressed, released, Time.frameCount);
        }

        private bool IsPointerOverPanel()
        {
            return panelHitArea != null
                && RectTransformUtility.RectangleContainsScreenPoint(
                    panelHitArea, Input.mousePosition);
        }
    }
}
