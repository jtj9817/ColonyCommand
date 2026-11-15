using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace ColonyCommand
{
    /// <summary>
    /// The mod's single vanilla window and its controls. The window's rect is exactly the search field, so it can never cover
    /// a portrait or steal a click from one; the field is the window's <see cref="CommonSearchWidget"/>, which makes the base
    /// window draw it and makes vanilla suppress the game's hotkeys while it is focused. The chip row and the empty-state line
    /// are drawn in screen space from <see cref="ExtraOnGUI"/> (windows live in <c>UIRoot.windows</c>, never saved). Placement
    /// is <see cref="ControlsLayout"/>, recomputed on every event from the bar's own lists, so it follows the bar.
    /// </summary>
    public class ColonyCommandWindow : Window
    {
        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private int lastSeenVersion = -1;
        private Placement placement;

        private LoadedLanguage textLanguage;
        private readonly string[] chipNames = new string[4], chipTips = new string[4], chipLabels = new string[4];
        private readonly int[] chipLabelCounts = { -1, -1, -1, -1 };
        private string placeholder, noMatches, reset, resetTip, filters;

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

        /// <summary>Where the controls are this event (the search field is its <c>Search</c> rect). Tests read it to click what the user sees.</summary>
        public Placement CurrentPlacement => placement;

        public QuickSearchWidget Search => search;

        /// <summary>The chip at this index (0 All, 1 Drafted, 2 Injured, 3 Idle) as last placed; meaningless while the strip is compact.</summary>
        public Rect ChipRect(int index) => chipRects[index];

        /// <summary>The Reset button of the empty-state line as last placed.</summary>
        public Rect ResetRect => resetRect;

        public override Vector2 InitialSize => Vector2.zero;

        protected override float Margin => 0f;

        /// <summary>Null while the controls are hidden, so a hidden field can neither hold focus nor swallow the game's hotkeys.</summary>
        public override QuickSearchWidget CommonSearchWidget => placement.Visible ? search : null;

        protected override void SetInitialSizeAndPosition()
        {
            windowRect = new Rect(0f, 0f, 0f, 0f);
        }

        /// <summary>The base window passes its own local rect: the field fills the window.</summary>
        protected override Rect QuickSearchWidgetRect(Rect winRect, Rect inRect) => winRect;

        /// <summary>The field changed (typing or its × button): the state follows it, never the other way round.</summary>
        public override void Notify_CommonSearchChanged()
        {
            ViewState.Query = search.filter.Text;
            lastSeenVersion = ViewState.Version;
        }

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            BarAdapter.Apply();
        }

        /// <summary>
        /// Runs on every OnGUI event, after the bar was handled for that event (UIRoot_Play draws the bar, then the window
        /// stack): a change made during OnGUI is repaired before the next event reaches the bar, and the controls are placed
        /// from the bar as it is now.
        /// </summary>
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            BarAdapter.Apply();
            EnsureTexts();
            UpdatePlacement();
            windowRect = placement.Visible ? placement.Search : new Rect(0f, 0f, 0f, 0f);
            if (!placement.Visible)
            {
                search.Unfocus();
                return;
            }
            SyncSearchFromState();
            RosterCounts counts = BarAdapter.Counts;
            search.noResultsMatched = ViewState.Snapshot.HasQuery && counts.Eligible > 0 && counts.All == 0;   // the text turns red
            HandleEnter();
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
            {
                if (placement.ShowEmpty)
                    DrawEmptyState();
                DrawStrip();
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            // The vanilla widget draws no placeholder: it is ours, over the empty, unfocused field.
            if (!search.filter.Active && !search.CurrentlyFocused())
            {
                Rect text = new Rect(inRect.x + 4f + QuickSearchWidget.IconSize + 4f + 4f, inRect.y, inRect.width - 34f, inRect.height);
                using (new TextBlock(GameFont.Small, TextAnchor.MiddleLeft))
                {
                    GUI.color = ColoredText.SubtleGrayColor;
                    Widgets.Label(text, placeholder);
                    GUI.color = Color.white;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ state

        private void EnsureTexts()
        {
            if (ReferenceEquals(textLanguage, LanguageDatabase.activeLanguage) && placeholder != null)
                return;
            textLanguage = LanguageDatabase.activeLanguage;
            placeholder = "ColonyCommand_SearchPlaceholder".Translate();
            noMatches = "ColonyCommand_NoMatches".Translate();
            reset = "ColonyCommand_Reset".Translate();
            resetTip = "ColonyCommand_ResetTip".Translate();
            filters = "ColonyCommand_Filters".Translate();
            chipNames[0] = "ColonyCommand_ChipAll".Translate();
            chipNames[1] = "ColonyCommand_ChipDrafted".Translate();
            chipNames[2] = "ColonyCommand_ChipInjured".Translate();
            chipNames[3] = "ColonyCommand_ChipIdle".Translate();
            chipTips[0] = "ColonyCommand_ChipAllTip".Translate();
            chipTips[1] = "ColonyCommand_ChipDraftedTip".Translate();
            chipTips[2] = "ColonyCommand_ChipInjuredTip".Translate();
            chipTips[3] = "ColonyCommand_ChipIdleTip".Translate();
            for (int i = 0; i < chipLabelCounts.Length; i++)
                chipLabelCounts[i] = -1;
        }

        /// <summary>The field shows what the state holds when something else changed it (Reset, a new game).</summary>
        private void SyncSearchFromState()
        {
            if (lastSeenVersion == ViewState.Version)
                return;
            lastSeenVersion = ViewState.Version;
            if (search.filter.Text != ViewState.Query)
                search.filter.Text = ViewState.Query;
        }

        // --------------------------------------------------------------------------------------- placement

        private void UpdatePlacement()
        {
            bool barShown = !Find.TilePicker.Active && Find.PlaySettings.showColonistBar;
            Criteria criteria = ViewState.Snapshot;
            bool empty = criteria.FilterActive && BarAdapter.Applied && BarAdapter.Shown == 0 && BarAdapter.Counts.Eligible > 0;
            ColonistBar bar = Find.ColonistBar;
            List<ColonistBar.Entry> entries = bar.Entries;
            BarMetrics metrics = ControlsLayout.Measure(bar, entries.Count == 0 ? 0 : entries[entries.Count - 1].group + 1);
            ChipWidths(out float stripWidth);
            Vector2 resetSize, filtersSize, emptySize;
            using (new TextBlock(GameFont.Small))
            {
                resetSize = Text.CalcSize(reset);
                filtersSize = Text.CalcSize(filters);
                emptySize = Text.CalcSize(noMatches);
            }
            float emptyWidth = emptySize.x + ControlsLayout.Gap * 3f + resetSize.x + 24f;
            placement = ControlsLayout.Compute(UI.screenWidth, UI.screenHeight, metrics, empty, stripWidth, filtersSize.x + 24f, emptyWidth);
            if (!barShown)
                placement.Visible = false;
            float x = placement.Strip.x;
            for (int i = 0; i < 4; i++)
            {
                chipRects[i] = new Rect(x, placement.Strip.y, chipWidths[i], placement.Strip.height);
                x += chipWidths[i] + ControlsLayout.Gap;
            }
            resetRect = new Rect(placement.Empty.xMax - resetSize.x - 16f, placement.Empty.y, resetSize.x + 16f, placement.Empty.height);
        }

        private readonly float[] chipWidths = new float[4];
        private readonly Rect[] chipRects = new Rect[4];
        private Rect resetRect;

        /// <summary>Fills the four chips' widths and returns the width of the whole row (counts refresh the labels).</summary>
        private void ChipWidths(out float total)
        {
            RosterCounts counts = BarAdapter.Counts;
            int[] values = { counts.All, counts.Drafted, counts.Injured, counts.Idle };
            total = 0f;
            using (new TextBlock(GameFont.Small))
            {
                for (int i = 0; i < 4; i++)
                {
                    if (chipLabelCounts[i] != values[i])
                    {
                        chipLabelCounts[i] = values[i];
                        chipLabels[i] = "ColonyCommand_ChipWithCount".Translate(chipNames[i], values[i]);
                    }
                    chipWidths[i] = Mathf.Max(48f, Text.CalcSize(chipLabels[i]).x + 16f);
                    total += chipWidths[i] + (i > 0 ? ControlsLayout.Gap : 0f);
                }
            }
        }

        // ---------------------------------------------------------------------------------------- drawing

        private void DrawStrip()
        {
            Rect strip = placement.Strip;
            Criteria c = ViewState.Snapshot;
            if (placement.StripCompact)
            {
                if (Widgets.ButtonText(strip, filters))
                    Find.WindowStack.Add(new FloatMenu(FilterMenu(c)));
                return;
            }
            for (int i = 0; i < 4; i++)
            {
                if (DrawChip(chipRects[i], chipLabels[i], IsActive(i, c), chipTips[i]))
                    Click(i, c);
            }
        }

        private static bool IsActive(int chip, Criteria c)
        {
            switch (chip)
            {
                case 0: return c.Status == StatusFilter.None && !c.HasSkillCriterion;
                case 1: return c.Status == StatusFilter.Drafted;
                case 2: return c.Status == StatusFilter.Injured;
                default: return c.Status == StatusFilter.Idle;
            }
        }

        /// <summary>All clears the status and the skill criterion (the search stays); another chip toggles itself, one at a time.</summary>
        private static void Click(int chip, Criteria c)
        {
            if (chip == 0)
            {
                ViewState.ClearStatusAndSkill();
                return;
            }
            StatusFilter wanted = chip == 1 ? StatusFilter.Drafted : chip == 2 ? StatusFilter.Injured : StatusFilter.Idle;
            ViewState.Status = c.Status == wanted ? StatusFilter.None : wanted;
        }

        private List<FloatMenuOption> FilterMenu(Criteria c)
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < 4; i++)
            {
                int chip = i;
                options.Add(new FloatMenuOption(chipLabels[i], () => Click(chip, ViewState.Snapshot)));
            }
            if (ViewState.AnythingSet)
                options.Add(new FloatMenuOption(reset, ResetAll));
            return options;
        }

        private static bool DrawChip(Rect rect, string label, bool active, string tip)
        {
            Widgets.DrawOptionBackground(rect, active);
            Widgets.Label(rect, label);
            TooltipHandler.TipRegion(rect, tip);
            return Widgets.ButtonInvisible(rect);
        }

        private void DrawEmptyState()
        {
            Rect line = placement.Empty;
            GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(new Rect(line.x, line.y, resetRect.x - ControlsLayout.Gap - line.x, line.height), noMatches);
            GUI.color = Color.white;
            TooltipHandler.TipRegion(resetRect, resetTip);
            if (Widgets.ButtonText(resetRect, reset))
                ResetAll();
        }

        /// <summary>Back to the vanilla bar: the state is cleared and the field follows on the next event.</summary>
        private void ResetAll()
        {
            ViewState.Reset();
            search.Unfocus();
        }

        // ------------------------------------------------------------------------------------------ Enter

        /// <summary>Enter in a focused field with something typed selects the first result as a click would, and is consumed.</summary>
        private void HandleEnter()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown || (e.keyCode != KeyCode.Return && e.keyCode != KeyCode.KeypadEnter))
                return;
            if (!search.CurrentlyFocused() || !ViewState.Snapshot.HasQuery)
                return;
            FirstResult.Select();
            e.Use();
        }
    }
}
