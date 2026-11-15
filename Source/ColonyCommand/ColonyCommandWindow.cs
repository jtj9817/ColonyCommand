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
    /// window draw it and makes vanilla suppress the game's hotkeys while it is focused. Everything else is drawn in screen
    /// space from <see cref="ExtraOnGUI"/> (windows live in <c>UIRoot.windows</c>, never saved): one panel in the right column,
    /// as wide as the Learning Helper and right under it. Placement is <see cref="ControlsLayout"/> fed by
    /// <see cref="RightColumn"/>, recomputed on every event, so it follows the Learning Helper and the alerts.
    /// </summary>
    public class ColonyCommandWindow : Window
    {
        /// <summary>All, Drafted, Injured, Idle, Inspired: the index is the chip's number everywhere below.</summary>
        public const int ChipCount = 5;

        private readonly QuickSearchWidget search = new QuickSearchWidget();
        private int lastSeenVersion = -1;
        private Placement placement;
        private bool wasCompact;

        private LoadedLanguage textLanguage;
        private readonly string[] chipNames = new string[ChipCount], chipTips = new string[ChipCount], chipLabels = new string[ChipCount];
        private readonly int[] chipLabelCounts = { -1, -1, -1, -1, -1 };
        private string placeholder, searchTip, noMatches, reset, resetTip, filters, filtersTip, shownPlaceholder;
        private float shownPlaceholderWidth;

        // Labels of the Skill, Level, Sort and direction buttons: rebuilt when the criteria change.
        private int labelVersion = -1;
        private string skillLabel, levelLabel, sortLabel, dirLabel, skillTip, levelTip, sortTip, dirTip;

        // Where each control is, as last placed. A zero-width rect means the control is not shown.
        private readonly Rect[] chipRects = new Rect[ChipCount];
        private Rect skillRect, levelRect, sortRect, dirRect, resetRect, emptyRect;

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

        /// <summary>The chip at this index (0 All, 1 Drafted, 2 Injured, 3 Idle, 4 Inspired) as last placed; zero-width while the panel is compact.</summary>
        public Rect ChipRect(int index) => chipRects[index];

        /// <summary>
        /// The search field held keyboard focus at the start of the latest event. <c>GUI.GetNameOfFocusedControl</c> may only be
        /// asked during OnGUI, so tests read this copy instead.
        /// </summary>
        public bool SearchFocused { get; private set; }

        /// <summary>
        /// Where the advanced controls are as last placed: Skill, minimum Level (only with a skill chosen), Sort, its direction
        /// (only while sorting) and Reset (only when something is set). A zero-width rect means the control is not shown.
        /// </summary>
        public Rect SkillRect => skillRect;
        public Rect LevelRect => levelRect;
        public Rect SortRect => sortRect;
        public Rect DirectionRect => dirRect;
        public Rect ResetButtonRect => resetRect;
        /// <summary>The "No matching colonists" line (zero-width unless there are no results).</summary>
        public Rect EmptyLineRect => emptyRect;

        /// <summary>What the buttons say right now.</summary>
        public string SkillLabel => skillLabel;
        public string LevelLabel => levelLabel;
        public string SortLabel => sortLabel;
        public string DirectionLabel => dirLabel;

        /// <summary>The options of the Filters menu the compact panel opens (the same actions as the buttons it stands in for).</summary>
        public List<FloatMenuOption> FoldedMenuOptions() => FilterMenu(ViewState.Snapshot);

        /// <summary>The text of the chip at this index, count included.</summary>
        public string ChipLabel(int index) => chipLabels[index];

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
        /// from the right column as it is now.
        /// </summary>
        public override void ExtraOnGUI()
        {
            base.ExtraOnGUI();
            BarAdapter.Apply();
            EnsureTexts();
            SearchFocused = search.CurrentlyFocused();
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
            Look.DrawPanel(placement.Panel);
            TooltipHandler.TipRegion(placement.Search, searchTip);
            using (new TextBlock(GameFont.Small, TextAnchor.MiddleCenter))
                DrawControls();
            AbsorbPanelClicks();
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
                    Widgets.Label(text, PlaceholderFor(text.width));
                    GUI.color = Color.white;
                }
            }
        }

        /// <summary>The placeholder cut to the field (the compact field is narrow); recomputed only when the width or the text changes.</summary>
        private string PlaceholderFor(float width)
        {
            if (shownPlaceholder == null || Mathf.Abs(width - shownPlaceholderWidth) > 0.5f)
            {
                shownPlaceholderWidth = width;
                shownPlaceholder = placeholder.Truncate(width);
            }
            return shownPlaceholder;
        }

        // ------------------------------------------------------------------------------------------ state

        private void EnsureTexts()
        {
            if (ReferenceEquals(textLanguage, LanguageDatabase.activeLanguage) && placeholder != null)
                return;
            textLanguage = LanguageDatabase.activeLanguage;
            placeholder = "ColonyCommand_SearchPlaceholder".Translate();
            searchTip = "ColonyCommand_SearchTip".Translate();
            shownPlaceholder = null;
            noMatches = "ColonyCommand_NoMatches".Translate();
            reset = "ColonyCommand_Reset".Translate();
            resetTip = "ColonyCommand_ResetTip".Translate();
            filters = "ColonyCommand_Filters".Translate();
            filtersTip = "ColonyCommand_FiltersTip".Translate();
            chipNames[0] = "ColonyCommand_ChipAll".Translate();
            chipNames[1] = "ColonyCommand_ChipDrafted".Translate();
            chipNames[2] = "ColonyCommand_ChipInjured".Translate();
            chipNames[3] = "ColonyCommand_ChipIdle".Translate();
            chipNames[4] = "ColonyCommand_ChipInspired".Translate();
            chipTips[0] = "ColonyCommand_ChipAllTip".Translate();
            chipTips[1] = "ColonyCommand_ChipDraftedTip".Translate();
            chipTips[2] = "ColonyCommand_ChipInjuredTip".Translate();
            chipTips[3] = "ColonyCommand_ChipIdleTip".Translate();
            chipTips[4] = "ColonyCommand_ChipInspiredTip".Translate();
            for (int i = 0; i < chipLabelCounts.Length; i++)
                chipLabelCounts[i] = -1;
            labelVersion = -1;
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
            RightColumn.Measure(out float top, out float bottom);
            UpdateChipLabels();
            EnsureAdvancedLabels(criteria);
            float filtersWidth;
            using (new TextBlock(GameFont.Small))
                filtersWidth = Text.CalcSize(filters).x + 24f;
            placement = ControlsLayout.Compute(UI.screenWidth, UI.screenHeight, top, bottom, ViewState.AnythingSet, empty, filtersWidth, wasCompact);
            if (placement.Visible)
                wasCompact = placement.Compact;
            if (!barShown)
                placement.Visible = false;
            PlaceControls(criteria);
        }

        private static readonly Rect None = new Rect(0f, 0f, 0f, 0f);

        /// <summary>Rects of every control from the placement; the ones that do not exist now (a compact panel, no skill chosen) are zero-width.</summary>
        private void PlaceControls(Criteria c)
        {
            if (placement.Compact)
            {
                for (int i = 0; i < ChipCount; i++)
                    chipRects[i] = None;
                skillRect = levelRect = sortRect = dirRect = resetRect = None;
            }
            else
            {
                chipRects[0] = placement.Row(1);
                chipRects[1] = placement.Cell(2, 0);
                chipRects[2] = placement.Cell(2, 1);
                chipRects[3] = placement.Cell(3, 0);
                chipRects[4] = placement.Cell(3, 1);
                bool levelShown = c.Skill != null, dirShown = c.EffectiveSort != SortKey.Vanilla;
                skillRect = levelShown ? placement.Cell(4, 0) : placement.Row(4);
                levelRect = levelShown ? placement.Cell(4, 1) : None;
                sortRect = dirShown ? placement.Cell(5, 0) : placement.Row(5);
                dirRect = dirShown ? placement.Cell(5, 1) : None;
                resetRect = placement.ResetRow >= 0 ? placement.Row(placement.ResetRow) : None;
            }
            emptyRect = placement.EmptyRow >= 0 ? placement.Row(placement.EmptyRow) : None;
        }

        /// <summary>The chips' texts, with the live counts (translated again only when a count changes).</summary>
        private void UpdateChipLabels()
        {
            RosterCounts counts = BarAdapter.Counts;
            int[] values = { counts.All, counts.Drafted, counts.Injured, counts.Idle, counts.Inspired };
            for (int i = 0; i < ChipCount; i++)
            {
                if (chipLabelCounts[i] == values[i])
                    continue;
                chipLabelCounts[i] = values[i];
                chipLabels[i] = "ColonyCommand_ChipWithCount".Translate(chipNames[i], values[i]);
            }
        }

        /// <summary>Labels of Skill, Level, Sort and the direction for the current criteria (translated again only when they change).</summary>
        private void EnsureAdvancedLabels(Criteria c)
        {
            if (labelVersion == ViewState.Version && skillLabel != null)
                return;
            labelVersion = ViewState.Version;
            // Short on purpose: two buttons share a row of 186 px. The skill is named alone, the level shows as "10+"; the
            // tooltips say what each button is.
            skillLabel = c.Skill == null ? "ColonyCommand_SkillButton".Translate() : c.Skill.skillLabel.CapitalizeFirst();
            levelLabel = c.MinLevel == ViewState.AnyLevel ? "ColonyCommand_LevelButton".Translate() : "ColonyCommand_LevelValue".Translate(c.MinLevel);
            SortKey sort = c.EffectiveSort;
            string sortName = sort == SortKey.Name ? "ColonyCommand_SortName".Translate()
                : sort == SortKey.Mood ? "ColonyCommand_SortMood".Translate()
                : sort == SortKey.Skill ? c.Skill.skillLabel
                : "ColonyCommand_SortVanilla".Translate();
            sortLabel = "ColonyCommand_SortButton".Translate(sortName);
            dirLabel = sort == SortKey.Name ? (c.Descending ? "ColonyCommand_DirNameDesc" : "ColonyCommand_DirNameAsc").Translate()
                : (c.Descending ? "ColonyCommand_DirHigh" : "ColonyCommand_DirLow").Translate();
            skillTip = "ColonyCommand_SkillTip".Translate();
            levelTip = "ColonyCommand_LevelTip".Translate();
            sortTip = "ColonyCommand_SortTip".Translate();
            dirTip = "ColonyCommand_DirTip".Translate();
        }

        // ---------------------------------------------------------------------------------------- drawing

        private void DrawControls()
        {
            Criteria c = ViewState.Snapshot;
            if (placement.Compact)
            {
                if (Look.Button(placement.Filters, filters, filtersTip, ViewState.AnythingSet))
                    Find.WindowStack.Add(new FloatMenu(FilterMenu(c)));
            }
            else
            {
                for (int i = 0; i < ChipCount; i++)
                {
                    if (Look.Button(chipRects[i], chipLabels[i], chipTips[i], IsActive(i, c)))
                        Click(i, c);
                }
                if (Look.Button(skillRect, skillLabel, skillTip, c.Skill != null))
                    OpenSkillMenu();
                if (levelRect.width > 0f && Look.Button(levelRect, levelLabel, levelTip, c.HasSkillCriterion))
                    OpenLevelMenu();
                if (Look.Button(sortRect, sortLabel, sortTip, c.EffectiveSort != SortKey.Vanilla))
                    OpenSortMenu();
                if (dirRect.width > 0f && Look.Button(dirRect, dirLabel, dirTip, false))
                    ViewState.Descending = !c.Descending;
                if (resetRect.width > 0f && Look.Button(resetRect, reset, resetTip, false))
                    ResetAll();
            }
            if (emptyRect.width > 0f)
                Look.DrawLabel(emptyRect, noMatches, Look.Warning);
        }

        /// <summary>
        /// A click that lands on the panel between its controls must not fall through to the map beneath (it would deselect or
        /// select a colonist, or, with a right click, give the selected colonist an order). A window over the panel, such as
        /// a dropdown, keeps its clicks; the search field is a window of ours and gets its own.
        /// </summary>
        private void AbsorbPanelClicks()
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown && e.type != EventType.MouseUp)
                return;
            Vector2 mouse = e.mousePosition;
            if (placement.Panel.Contains(mouse) && !placement.Search.Contains(mouse) && Find.WindowStack.GetWindowAt(UI.GUIToScreenPoint(mouse)) == null)
                e.Use();
        }

        // ------------------------------------------------------------------------------------------- menus

        /// <summary>
        /// Vanilla FloatMenus (Escape and a click elsewhere close them, as everywhere in the game). Choosing a skill while no
        /// level is set goes straight on to the level menu: a skill alone filters nobody.
        /// </summary>
        private void OpenSkillMenu()
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("ColonyCommand_SkillNone".Translate(), () => ViewState.Skill = null) };
            foreach (SkillDef def in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                SkillDef chosen = def;
                options.Add(new FloatMenuOption(def.skillLabel.CapitalizeFirst(), () =>
                {
                    ViewState.Skill = chosen;
                    if (ViewState.MinLevel == ViewState.AnyLevel)
                        OpenLevelMenu();
                }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenLevelMenu()
        {
            var options = new List<FloatMenuOption> { new FloatMenuOption("ColonyCommand_LevelMenuAny".Translate(), () => ViewState.MinLevel = ViewState.AnyLevel) };
            foreach (int level in LevelPresets)
            {
                int chosen = level;
                options.Add(new FloatMenuOption("ColonyCommand_LevelValue".Translate(level), () => ViewState.MinLevel = chosen));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>Spec section 4.1: Any (switches the criterion off), 0, 5, 10, 15.</summary>
        public static readonly int[] LevelPresets = { 0, 5, 10, 15 };

        private void OpenSortMenu()
        {
            var options = new List<FloatMenuOption>
            {
                new FloatMenuOption("ColonyCommand_SortMenuVanilla".Translate(), () => ChooseSort(SortKey.Vanilla)),
                new FloatMenuOption("ColonyCommand_SortMenuName".Translate(), () => ChooseSort(SortKey.Name)),
                new FloatMenuOption("ColonyCommand_SortMenuMood".Translate(), () => ChooseSort(SortKey.Mood)),
            };
            if (ViewState.Skill != null)   // the skill sort exists only once a skill is chosen
                options.Add(new FloatMenuOption("ColonyCommand_SortMenuSkill".Translate(ViewState.Skill.skillLabel), () => ChooseSort(SortKey.Skill)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        /// <summary>A new key starts in its natural direction: names A-Z, mood and skill high to low (spec section 4.2 lists High-low first).</summary>
        private static void ChooseSort(SortKey key)
        {
            ViewState.Sort = key;
            ViewState.Descending = key == SortKey.Mood || key == SortKey.Skill;
        }

        /// <summary>The status each chip stands for (chip 0, All, is none).</summary>
        private static StatusFilter StatusOf(int chip)
        {
            switch (chip)
            {
                case 1: return StatusFilter.Drafted;
                case 2: return StatusFilter.Injured;
                case 3: return StatusFilter.Idle;
                case 4: return StatusFilter.Inspired;
                default: return StatusFilter.None;
            }
        }

        private static bool IsActive(int chip, Criteria c) =>
            chip == 0 ? c.Status == StatusFilter.None && !c.HasSkillCriterion : c.Status == StatusOf(chip);

        /// <summary>All clears the status and the skill criterion (the search stays); another chip toggles itself, one at a time.</summary>
        private static void Click(int chip, Criteria c)
        {
            if (chip == 0)
            {
                ViewState.ClearStatusAndSkill();
                return;
            }
            StatusFilter wanted = StatusOf(chip);
            ViewState.Status = c.Status == wanted ? StatusFilter.None : wanted;
        }

        private List<FloatMenuOption> FilterMenu(Criteria c)
        {
            var options = new List<FloatMenuOption>();
            for (int i = 0; i < ChipCount; i++)
            {
                int chip = i;
                options.Add(new FloatMenuOption(chipLabels[i], () => Click(chip, ViewState.Snapshot)));
            }
            EnsureAdvancedLabels(c);
            options.Add(new FloatMenuOption(skillLabel + "...", OpenSkillMenu));
            if (c.Skill != null)
                options.Add(new FloatMenuOption(levelLabel + "...", OpenLevelMenu));
            options.Add(new FloatMenuOption(sortLabel + "...", OpenSortMenu));
            if (c.EffectiveSort != SortKey.Vanilla)
                options.Add(new FloatMenuOption(dirLabel, () => ViewState.Descending = !ViewState.Descending));
            if (ViewState.AnythingSet)
                options.Add(new FloatMenuOption(reset, ResetAll));
            return options;
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
