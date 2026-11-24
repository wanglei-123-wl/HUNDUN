namespace Game.CharacterPanel.Presentation
{
    public sealed class CharacterPanelPointerCapture
    {
        private int capturedButtons;
        private int sampledFrame = int.MinValue;
        private int classifiedPresses;
        public int CapturedButtonsThisFrame { get; private set; }

        public bool Sample(bool open, bool pointerOnPanel,
            int heldButtons, int pressedButtons, int releasedButtons, int frame = -1)
        {
            if (frame < 0 || frame != sampledFrame)
            {
                sampledFrame = frame;
                classifiedPresses = 0;
                CapturedButtonsThisFrame = 0;
            }

            // Opening a window must not steal a button already held in the world.
            int newPresses = pressedButtons & ~classifiedPresses;
            classifiedPresses |= pressedButtons;
            if (open && pointerOnPanel) capturedButtons |= newPresses;

            CapturedButtonsThisFrame |= capturedButtons;
            bool blocked = (open && pointerOnPanel) || CapturedButtonsThisFrame != 0;
            capturedButtons &= heldButtons & ~releasedButtons;
            return blocked;
        }

        public void Reset()
        {
            capturedButtons = 0;
            CapturedButtonsThisFrame = 0;
            sampledFrame = int.MinValue;
            classifiedPresses = 0;
        }
    }
}
