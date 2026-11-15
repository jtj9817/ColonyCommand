using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// What vanilla draws in the right column, read without reflection and without changing any of it: where the Learning
    /// Helper ends (the panel starts under it) and where the alerts and letters begin (the panel must end above them).
    /// Measured at most once per frame: <c>AlertsHeight</c> measures every alert's text.
    /// </summary>
    public static class RightColumn
    {
        /// <summary>
        /// The Learning Helper (<c>LearningReadout.LearningReadoutOnGUI</c>) is an <c>ImmediateWindow</c> with ID 76136312;
        /// <c>WindowStack.ImmediateWindow</c> stores the ID negated. It is in the window stack only while it is drawn
        /// (the stack drops an immediate window that was not requested in a repaint), so its presence means it is shown.
        /// </summary>
        public const int LearningHelperWindowId = -76136312;
        /// <summary>The Learning Helper's y (<c>LearningReadout</c> draws it at 8) and the air between it and the panel.</summary>
        public const float TopMargin = 8f, Gap = 4f;

        private static int measuredFrame = -1;
        private static float top = TopMargin, bottom;

        /// <summary>Top and bottom of the free part of the column (screen y), for the frame being drawn.</summary>
        public static void Measure(out float freeTop, out float freeBottom)
        {
            if (measuredFrame != Time.frameCount)
            {
                measuredFrame = Time.frameCount;
                top = LearningHelperBottom();
                bottom = AlertsTop();
            }
            freeTop = top;
            freeBottom = bottom;
        }

        /// <summary>Under the Learning Helper when it is showing, else the top margin.</summary>
        private static float LearningHelperBottom()
        {
            WindowStack windows = Find.WindowStack;
            for (int i = 0; i < windows.Count; i++)
            {
                Window w = windows[i];
                if (w.ID == LearningHelperWindowId && w is ImmediateWindow)
                    return w.windowRect.yMax + Gap;
            }
            return TopMargin;
        }

        /// <summary>
        /// Where the alerts start, going up from the letters (<c>AlertsReadout.AlertsReadoutOnGUI</c>: the stack is
        /// <c>LetterStack.LastTopY</c> minus <c>AlertsHeight</c> tall). With no alerts, the top of the letters.
        /// </summary>
        private static float AlertsTop()
        {
            float lettersTop = Find.LetterStack.LastTopY;
            float alertsHeight = 0f;
            if (Find.UIRoot is UIRoot_Play play)
            {
                using (new TextBlock(GameFont.Small))   // Alert.Height sets the font
                    alertsHeight = play.alerts.AlertsHeight;
            }
            return lettersTop - alertsHeight - Gap;
        }
    }
}
