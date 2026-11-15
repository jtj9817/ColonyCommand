using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// The mod's single vanilla window. Phase 1: empty (no size, no chrome, no input), it exists to be called every OnGUI
    /// event and every frame by the window stack. Windows live in <c>UIRoot.windows</c>, which is never saved.
    /// </summary>
    public class ColonyCommandWindow : Window
    {
        public ColonyCommandWindow()
        {
            layer = WindowLayer.GameUI;
            doWindowBackground = false;
            drawShadow = false;
            preventCameraMotion = false;
            closeOnCancel = false;
            closeOnAccept = false;   // defaults to true: Enter would close it
            closeOnClickedOutside = false;
            focusWhenOpened = false;
            absorbInputAroundWindow = false;
            forcePause = false;
            soundAppear = null;
            soundClose = null;
        }

        public override Vector2 InitialSize => Vector2.zero;

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, 0f, 0f);
        }

        public override void DoWindowContents(Rect inRect)
        {
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            BarAdapter.Apply();
        }

        /// <summary>
        /// Runs on every OnGUI event, after the bar was handled for that event (UIRoot_Play draws the bar, then the window
        /// stack). A change made during OnGUI is therefore repaired before the next event reaches the bar.
        /// </summary>
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            BarAdapter.Apply();
        }
    }
}
