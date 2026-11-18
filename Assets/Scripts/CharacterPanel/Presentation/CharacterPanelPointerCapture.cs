namespace Game.CharacterPanel.Presentation
{
    public sealed class CharacterPanelPointerCapture
    {
        private int capturedButtons;
        private bool wasOpen;

        public bool Sample(bool open, bool pointerOnPanel,
            int heldButtons, int pressedButtons, int releasedButtons)
        {
            if (open && (!wasOpen || pointerOnPanel))
                capturedButtons |= heldButtons | pressedButtons;

            bool blocked = (open && pointerOnPanel) || capturedButtons != 0;
            capturedButtons &= heldButtons & ~releasedButtons;
            wasOpen = open;
            return blocked;
        }

        public void Reset()
        {
            capturedButtons = 0;
            wasOpen = false;
        }
    }
}
