using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>
    /// The "Slender" game mode: an extra entry on the Survival experience screen (next to Pilgrim,
    /// Stalker, ...). It uses Pilgrim's settings, only allows Coastal Highway, and marks the new game as
    /// a Slender game so the mod's rules apply to that save, and to no other.
    ///
    /// Safety: no new Unity objects are created here. The menu entry reuses Pilgrim's settings asset and
    /// artwork; only a plain game data object (XPModeMenuItem) and a menu row are added.
    /// </summary>
    internal static class SlenderGame
    {
        public const string Label = Diff.ModeName;
        public const string Description =
            "Eight pages, one endless night on Coastal Highway, and something tall that only moves when you are not looking. Pilgrim survival rules.";

        /// <summary>True between clicking a Slender entry and the new game starting.</summary>
        public static bool PendingNewGame;
        /// <summary>True between starting ANY new survival game and that game being under way.</summary>
        public static bool NewGameStarting;
        /// <summary>Which difficulty row was clicked on the game-mode screen.</summary>
        public static int PendingDifficulty = (int)SlenderDifficultyChoice.Normal;

        static MelonLogger.Instance _log;
        static string _file;
        static readonly HashSet<uint> _ids = new HashSet<uint>();
        static readonly Dictionary<uint, int> _difficulty = new Dictionary<uint, int>();
        /// <summary>The save file each Slender game lives in. A slot id on its own is not proof of
        /// identity (the game reuses them), and the save name is a second, independent check.</summary>
        static readonly Dictionary<uint, string> _saveName = new Dictionary<uint, string>();
        /// <summary>
        /// The experience mode each Slender game reported when it was started. The Slender entry
        /// borrows Pilgrim's settings, so a Slender game calls itself Pilgrim - but rather than
        /// assume that, it is read off the game we started ourselves and remembered, and a later run
        /// in that slot has to report the same thing. Learning it means a change at the game's end
        /// cannot leave the mod refusing to run in its own games.
        /// </summary>
        static readonly Dictionary<uint, int> _modeType = new Dictionary<uint, int>();
        static readonly HashSet<uint> _gifted = new HashSet<uint>();
        static string _giftFile;

        /// <summary>
        /// The difficulty of the game being played. Each save remembers its own, so loading an old
        /// game brings its difficulty back; outside a Slender game the settings value is used.
        /// </summary>
        public static int CurrentDifficulty
        {
            get
            {
                try
                {
                    uint id = CurrentGameId();
                    if (id != 0 && _difficulty.TryGetValue(id, out int d)) return d;
                    var s = SlenderMod.Settings;
                    return s != null ? (int)s.Difficulty : (int)SlenderDifficultyChoice.Normal;
                }
                catch { return (int)SlenderDifficultyChoice.Normal; }
            }
        }

        /// <summary>Used when the player changes the difficulty from the settings page mid-game.</summary>
        public static void SetCurrentDifficulty(int d)
        {
            try
            {
                uint id = CurrentGameId();
                if (id == 0 || !_ids.Contains(id)) return;
                if (_difficulty.TryGetValue(id, out int cur) && cur == d) return;
                _difficulty[id] = d;
                SaveGames();
                _log?.Msg("Difficulty for this game set to " + Diff.Get(d).Name + ".");
            }
            catch { }
        }

        static void SaveGames()
        {
            try
            {
                var lines = new List<string>();
                foreach (uint id in _ids)
                    lines.Add(id + "|" + (_difficulty.TryGetValue(id, out int d) ? d : 1)
                                 + "|" + (_saveName.TryGetValue(id, out string nm) ? nm : "")
                                 + "|" + (_modeType.TryGetValue(id, out int mt) ? mt.ToString() : ""));
                File.WriteAllLines(_file, lines.ToArray());
            }
            catch (Exception e) { _log?.Warning("Could not save the Slender games list: " + e.Message); }
        }

        /// <summary>True the first time this is asked for the current Slender game (then remembered).</summary>
        public static bool TakeStartGift()
        {
            uint id = CurrentGameId();
            if (id == 0 || !_ids.Contains(id) || _gifted.Contains(id)) return false;
            _gifted.Add(id);
            try { File.AppendAllText(_giftFile, id + Environment.NewLine); } catch { }
            return true;
        }

        public static void Init(MelonLogger.Instance log, string modFolder)
        {
            _log = log;
            _file = Path.Combine(modFolder, "slender_games.txt");
            _giftFile = Path.Combine(modFolder, "slender_gifts.txt");
            try
            {
                if (File.Exists(_giftFile))
                    foreach (var line in File.ReadAllLines(_giftFile))
                        if (uint.TryParse(line.Trim(), out uint gid)) _gifted.Add(gid);
            }
            catch { }
            try
            {
                // "<id>" from older builds, then "<id>|<difficulty>",
                // now "<id>|<difficulty>|<save name>|<experience mode>" (later fields may be missing)
                if (File.Exists(_file))
                    foreach (var line in File.ReadAllLines(_file))
                    {
                        string t = line.Trim();
                        if (t.Length == 0) continue;
                        string[] p = t.Split('|');
                        if (!uint.TryParse(p[0].Trim(), out uint id)) continue;
                        _ids.Add(id);
                        if (p.Length > 1 && int.TryParse(p[1].Trim(), out int d)) _difficulty[id] = d;
                        if (p.Length > 2 && p[2].Trim().Length > 0) _saveName[id] = p[2].Trim();
                        if (p.Length > 3 && int.TryParse(p[3].Trim(), out int mt)) _modeType[id] = mt;
                    }
            }
            catch (Exception e) { _log.Warning("Could not read the Slender games list: " + e.Message); }
        }

        static uint CurrentGameId()
        {
            try { return SaveGameSystem.m_CurrentGameId; } catch { return 0; }
        }

        /// <summary>The slot being played, for things that have to be kept per game.</summary>
        internal static uint CurrentGame => CurrentGameId();

        static string CurrentSaveName()
        {
            try { return SaveGameSystem.GetCurrentSaveName() ?? ""; } catch { return ""; }
        }

        static void SaveGifts()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (uint g in _gifted) sb.Append(g).Append(Environment.NewLine);
                File.WriteAllText(_giftFile, sb.ToString());
            }
            catch { }
        }

        /// <summary>
        /// Is the game being played right now a Slender game?
        ///
        /// The recorded id is a save SLOT id and the game reuses those, so the slot alone is not
        /// proof: a Pilgrim run started in a slot that once held a Slender game would be claimed by
        /// it. Three things guard against that - ordinary new games clear the slot's record as they
        /// start (see TryClaimNewGame), the save file name has to match the one the record was
        /// written for, and the run has to report the same experience mode the Slender game in that
        /// slot reported when it was started. Any one of them failing settles it: not our game.
        ///
        /// Answered once per frame and remembered: this is asked from about twenty places, several of
        /// them patches on methods the game calls every frame.
        /// </summary>
        public static bool IsActive
        {
            get
            {
                int f;
                try { f = Time.frameCount; } catch { return Evaluate(); }
                if (f == _frame) return _frameValue;
                _frame = f;
                _frameValue = Evaluate();
                return _frameValue;
            }
        }

        static int _frame = -1;
        static bool _frameValue;
        static uint _settledId;
        static bool _settledValue, _settledKnown;
        /// <summary>The game TryClaimNewGame has already looked at. Nothing is answered before that.</summary>
        static uint _claimCheckedId = uint.MaxValue;

        /// <summary>Forget what was worked out about the game that was running (called on scene changes).</summary>
        public static void ForgetCurrentCheck() { _settledKnown = false; _frame = -1; }

        static bool Evaluate()
        {
            uint id = CurrentGameId();
            if (id == 0 || !_ids.Contains(id)) return false;
            if (_settledKnown && _settledId == id) return _settledValue;

            // Until the game is actually running, and until the new-game check below has had its
            // look at it, the one thing known for certain is whether WE are the ones starting it.
            // The slot id is not enough on its own: the game hands the same slot to whatever game
            // takes it next, so an ordinary Pilgrim run can land on a slot a Slender game used to
            // own. Trusting the record through the loading screen is what put the Slender loading
            // screen and the page counter in front of a Pilgrim game.
            if (!GameplaySettled() || _claimCheckedId != id) return PendingNewGame;

            bool ok = IdentityMatches(id) && ModeAllows(id);
            _settledId = id; _settledValue = ok; _settledKnown = true;
            return ok;
        }

        /// <summary>True once the game being loaded is the game that is running.</summary>
        static bool GameplaySettled()
        {
            try
            {
                if (GameManager.IsMainMenuActive()) return false;
                if (SaveGameSystem.IsRestoreInProgress()) return false;
                return GameManager.GetPlayerTransform() != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// The save file has to be the one this record was written for. Older records have no name
        /// recorded, and a name we cannot read is never treated as a mismatch, so neither of those
        /// can throw a real Slender game out. A mismatch means the slot was reused by another game:
        /// the record is dropped there and then, so it cannot claim any future game either.
        /// </summary>
        static bool IdentityMatches(uint id)
        {
            string have = CurrentSaveName();
            if (have.Length == 0) return true;      // cannot read it: never hold that against a game

            if (!_saveName.TryGetValue(id, out string want) || want.Length == 0)
            {
                // Written by an older build that recorded a slot number and nothing else. A slot
                // number cannot tell this game apart from whatever used the slot before it, so the
                // record can only ever do harm sitting there - it is dropped rather than left to
                // claim an ordinary game that happens to land on the same slot.
                _log?.Msg("Save slot " + id + " carries an old Slender record with no save attached to it" +
                          " - clearing it rather than letting it claim '" + have + "'.");
                Disown(id);
                return false;
            }
            if (have == want) return true;
            _log?.Msg("Save slot " + id + " holds '" + have + "' now, not the Slender game '" + want +
                      "' it was recorded for - clearing that record; Slender stays out of this game.");
            Disown(id);
            return false;
        }

        /// <summary>Drops a slot's Slender record for good.</summary>
        static void Disown(uint id)
        {
            try
            {
                if (!_ids.Remove(id)) return;
                _difficulty.Remove(id);
                _saveName.Remove(id);
                _modeType.Remove(id);
                SaveGames();
            }
            catch { }
        }

        /// <summary>
        /// Called when the player is back on the menus without a game having started. The
        /// "a new game is starting" flag must not survive that: left set, the next save they LOAD
        /// would look like a brand-new ordinary game and lose its Slender record.
        /// </summary>
        public static void CancelNewGame()
        {
            NewGameStarting = false;
            PendingNewGame = false;
            ForgetCurrentCheck();
        }

        /// <summary>
        /// The run has to call itself what this slot's Slender game called itself. For a record
        /// written before that was stored, Pilgrim and Custom are accepted - the two a Slender game
        /// can present as. Anything unreadable is never treated as a mismatch.
        /// </summary>
        static bool ModeAllows(uint id)
        {
            try
            {
                int t = (int)ExperienceModeManager.GetCurrentExperienceModeType();
                bool ok = _modeType.TryGetValue(id, out int want)
                    ? t == want
                    : t == (int)ExperienceModeType.Pilgrim || t == (int)ExperienceModeType.Custom;
                if (!ok)
                    _log?.Msg("Save slot " + id + " once held a Slender game, but this run is " +
                              (ExperienceModeType)t + " - Slender stays out of it.");
                return ok;
            }
            catch { return true; }
        }

        /// <summary>What the game we just started calls itself: by definition, what a Slender game is.</summary>
        static int CurrentModeType()
        {
            try { return (int)ExperienceModeManager.GetCurrentExperienceModeType(); }
            catch { return -1; }
        }

        /// <summary>Call while in gameplay: turns a just-started game into a Slender game.</summary>
        public static bool TryClaimNewGame()
        {
            uint id = CurrentGameId();
            if (id == 0) return false;
            // From here on this game has been looked at, so IsActive is allowed to answer for it.
            _claimCheckedId = id;

            // A brand-new ordinary game: the slot may still carry an old Slender game's record,
            // and leaving it there is what made Slender hunt in Pilgrim runs.
            if (NewGameStarting && !PendingNewGame)
            {
                NewGameStarting = false;
                ForgetCurrentCheck();
                if (_ids.Remove(id))
                {
                    _difficulty.Remove(id);
                    _saveName.Remove(id);
                    _modeType.Remove(id);
                    SaveGames();
                    _log?.Msg("Save slot " + id + " is an ordinary game now; its old Slender record was cleared.");
                }
                return false;
            }

            if (!PendingNewGame) return false;
            NewGameStarting = false;
            PendingNewGame = false;
            ForgetCurrentCheck();
            _ids.Add(id);
            _difficulty[id] = PendingDifficulty;      // the row they clicked on the game-mode screen
            _saveName[id] = CurrentSaveName();        // second check: slot ids get reused, this does not
            int mt = CurrentModeType();               // third check: what this game calls itself
            if (mt >= 0) _modeType[id] = mt; else _modeType.Remove(id);
            SaveGames();
            try { if (SlenderMod.Settings != null) { SlenderMod.Settings.Difficulty = (SlenderDifficultyChoice)PendingDifficulty; SlenderMod.Settings.Save(); } } catch { }
            // The game reuses save-slot ids, so a brand-new game can land on an id that already
            // had its starting gear. Without this, that game starts with nothing.
            if (_gifted.Remove(id)) SaveGifts();
            _log.Msg("New Slender game started (game id " + id + ", " + Diff.Get(PendingDifficulty).Name + ").");
            return true;
        }

        public static bool IsCoastal(SelectRegionItem item)
        {
            try { return item != null && item.gameObject.name.IndexOf("Coastal", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { return false; }
        }

        internal static void Log(string msg) => _log?.Msg(msg);
        internal static void Warn(string msg) => _log?.Warning(msg);
    }

    // ------------------------------------------------------------------ experience screen
    //
    // One SLENDER row on the Survival screen. It cannot live on the Challenges screen, and it
    // cannot be one row per difficulty: the menu has a fixed pool of eight row widgets. Survival
    // already uses six of them, and Challenges is full (it shows only eight of its twelve
    // challenges). So the difficulty is picked in Mod Settings, and this row is labelled with
    // whichever one a new game would start on.

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.ConfigureMenu))]
    internal static class Patch_SelectExperience_ConfigureMenu
    {
        static Panel_SelectExperience.XPModeMenuItem _item;
        internal static int SlenderIndex = -1;
        static bool _loggedConfigs;

        /// <summary>The difficulty a new game would start on, straight from the settings page.</summary>
        internal static DifficultyLevel Chosen
        {
            get
            {
                try
                {
                    var s = SlenderMod.Settings;
                    return s != null ? Diff.Get(s.Difficulty) : Diff.Get(1);
                }
                catch { return Diff.Get(1); }
            }
        }

        static void Postfix(Panel_SelectExperience __instance)
        {
            try
            {
                var items = __instance.m_MenuItems;
                if (items == null || items.Count == 0) return;

                if (!_loggedConfigs)
                {
                    _loggedConfigs = true;
                    for (int i = 0; i < items.Count; i++)
                    {
                        var cfg = items[i]?.m_SandboxConfig;
                        SlenderGame.Log("  experience entry " + i + ": " + (cfg != null ? cfg.name : "(none)"));
                    }
                }

                Relink(items);

                if (!items.Contains(_item)) items.Add(_item);
                SlenderIndex = items.IndexOf(_item);

                var menu = __instance.m_BasicMenu;
                // ConfigureMenu may already have added a row for it (the list persists between visits).
                if (FindModel(menu, SlenderIndex) == null) __instance.AddMenuItem(SlenderIndex);

                var model = FindModel(menu, SlenderIndex);
                if (model != null)
                {
                    var d = Chosen;
                    model.m_LabelText = Diff.ModeName;
                    model.m_SecondaryText = d.Name;
                    model.m_DescriptionText = d.Description + "   (difficulty: Options > Mod Settings > Slender Mode)";
                    model.m_IsLocked = false;
                    model.m_IsUpsell = false;
                    model.m_Selectable = true;
                    menu.Refresh();
                }
                else SlenderGame.Warn("Slender menu row could not be found after adding it.");
            }
            catch (Exception e) { SlenderGame.Warn("Adding the Slender entry failed: " + e.Message); }
        }

        /// <summary>
        /// Points the Slender entry at the CURRENT Pilgrim settings and artwork. Must run every time the
        /// screen is built or shown: returning from a game destroys the old artwork objects, and the game
        /// touches every entry's artwork when any mode is picked (a stale one sent you back to the menu).
        /// </summary>
        internal static void Relink(Il2CppSystem.Collections.Generic.List<Panel_SelectExperience.XPModeMenuItem> items)
        {
            if (items == null || items.Count == 0) return;
            Panel_SelectExperience.XPModeMenuItem pilgrim = null;
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (it == null || it == _item) continue;
                var cfg = it.m_SandboxConfig;
                if (cfg != null && cfg.name.IndexOf("Pilgrim", StringComparison.OrdinalIgnoreCase) >= 0) { pilgrim = it; break; }
            }
            if (pilgrim == null)
            {
                for (int i = 0; i < items.Count; i++) if (items[i] != null && items[i] != _item) { pilgrim = items[i]; break; }
                if (pilgrim == null) return;
                SlenderGame.Warn("Pilgrim entry not found by name; using the first entry as the template.");
            }
            if (_item == null) _item = new Panel_SelectExperience.XPModeMenuItem();
            _item.m_Display = pilgrim.m_Display;
            _item.m_SandboxConfig = pilgrim.m_SandboxConfig;
            _item.m_PlayOnSelect = pilgrim.m_PlayOnSelect;
            _item.m_PlayOnDeselect = pilgrim.m_PlayOnDeselect;
        }

        internal static bool HasItem => _item != null;

        static BasicMenu.BasicMenuItemModel FindModel(BasicMenu menu, int itemIndex)
        {
            var list = menu?.m_ItemModelList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].m_ItemIndex == itemIndex) return list[i];
            return null;
        }
    }

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.Enable))]
    internal static class Patch_SelectExperience_Enable
    {
        static void Prefix(Panel_SelectExperience __instance)
        {
            try { if (Patch_SelectExperience_ConfigureMenu.HasItem) Patch_SelectExperience_ConfigureMenu.Relink(__instance.m_MenuItems); }
            catch (Exception e) { SlenderGame.Warn("Re-linking the Slender entry failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.OnExperienceClicked))]
    internal static class Patch_SelectExperience_Clicked
    {
        static void Prefix(Panel_SelectExperience __instance)
        {
            try
            {
                bool slender = Patch_SelectExperience_ConfigureMenu.SlenderIndex >= 0 &&
                               __instance.GetSelectedIndex() == Patch_SelectExperience_ConfigureMenu.SlenderIndex;
                SlenderGame.PendingNewGame = slender;
                if (!slender) return;
                var d = Patch_SelectExperience_ConfigureMenu.Chosen;
                SlenderGame.PendingDifficulty = (int)d.Id;
                SlenderGame.Log("Slender game mode selected: " + d.Name + ".");
            }
            catch (Exception e) { SlenderGame.Warn("Experience click check failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.OnClickBack))]
    internal static class Patch_SelectExperience_Back
    {
        static void Prefix() { SlenderGame.CancelNewGame(); }
    }


    // ------------------------------------------------------------------ region screen: Coastal Highway only

    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.Update))]
    internal static class Patch_SelectRegion_Update
    {
        static readonly List<SelectRegionItem> _hidden = new List<SelectRegionItem>();

        static void Postfix(Panel_SelectRegion_Map __instance)
        {
            try
            {
                var items = __instance.m_Items;
                if (items == null) return;
                if (SlenderGame.PendingNewGame)
                {
                    for (int i = 0; i < items.Count; i++)
                    {
                        var it = items[i];
                        if (it == null || SlenderGame.IsCoastal(it) || !it.gameObject.activeSelf) continue;
                        it.gameObject.SetActive(false);
                        _hidden.Add(it);
                    }
                }
                else if (_hidden.Count > 0)
                {
                    foreach (var it in _hidden) { try { if (it != null) it.gameObject.SetActive(true); } catch { } }
                    _hidden.Clear();
                }
            }
            catch { }
        }

        internal static SelectRegionItem FindCoastal(Panel_SelectRegion_Map panel)
        {
            var items = panel.m_Items;
            if (items == null) return null;
            for (int i = 0; i < items.Count; i++) if (SlenderGame.IsCoastal(items[i])) return items[i];
            return null;
        }
    }

    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.OnItemSelected))]
    internal static class Patch_SelectRegion_ItemSelected
    {
        static bool Prefix(Panel_SelectRegion_Map __instance, SelectRegionItem item)
        {
            if (!SlenderGame.PendingNewGame || SlenderGame.IsCoastal(item)) return true;
            var coastal = Patch_SelectRegion_Update.FindCoastal(__instance);
            if (coastal != null) __instance.SelectItem(coastal, true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.OnRandomSelected))]
    internal static class Patch_SelectRegion_Random
    {
        static bool Prefix(Panel_SelectRegion_Map __instance)
        {
            if (!SlenderGame.PendingNewGame) return true;
            var coastal = Patch_SelectRegion_Update.FindCoastal(__instance);
            if (coastal != null) __instance.SelectItem(coastal, true);
            return false;
        }
    }

    /// <summary>Backing out of the region map is not the start of a new game.</summary>
    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.OnClickBack))]
    internal static class Patch_SelectRegion_Back
    {
        static void Prefix() { SlenderGame.NewGameStarting = false; }
    }

    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.OnSelectRegionContinue))]
    internal static class Patch_SelectRegion_Continue
    {
        static void Prefix(Panel_SelectRegion_Map __instance)
        {
            // Every new survival game passes through here, Slender or not.
            SlenderGame.NewGameStarting = true;
            if (!SlenderGame.PendingNewGame) return;
            try
            {
                if (!SlenderGame.IsCoastal(__instance.m_SelectedItem))
                {
                    var coastal = Patch_SelectRegion_Update.FindCoastal(__instance);
                    if (coastal != null) __instance.SelectItem(coastal, false);
                }
            }
            catch (Exception e) { SlenderGame.Warn("Forcing Coastal Highway failed: " + e.Message); }
        }
    }
}

namespace SlenderMode
{
    /// <summary>
    /// The world rules of a Slender game: endless night (the clock loops 22:00 -> 04:30 -> 22:00, so
    /// time keeps moving for the weather), no aurora, Arrival-style foggy dark night, 2 torches at the start.
    /// </summary>
    internal static class SlenderWorld
    {
        const float NightStart = 22f;   // hour the loop jumps back to
        const float NightEnd = 4.5f;    // hour at which it jumps back
        static float _nextCheck;
        static bool _loggedTime;

        /// <summary>Called once when a brand-new Slender game starts.</summary>
        public static void OnNewGame() { _nextCheck = 0f; }

        static void GiveTorches()
        {
            try
            {
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm == null) return;
                pm.AddItemCONSOLE("GEAR_Torch", 2, 1f);
                SlenderGame.Log("Added 2 torches to the inventory.");

                // The flashlight is the chaser's counter, so a Slender game starts with one.
                // It never runs out (see the charge patch below), aurora or not.
                string flash = FindGear("GEAR_Flashlight", "GEAR_FlashlightA", "GEAR_HandheldFlashlight");
                if (flash != null)
                {
                    pm.AddItemCONSOLE(flash, 1, 1f);
                    SlenderGame.Log("Added a flashlight to the inventory (" + flash + ").");
                }
                else SlenderGame.Warn("No flashlight gear prefab found (tried GEAR_Flashlight, GEAR_FlashlightA, " +
                                      "GEAR_HandheldFlashlight); starting without one.");
            }
            catch (Exception e) { SlenderGame.Warn("Could not add the starting gear: " + e.Message); }
        }

        /// <summary>First of these gear names that the game actually has a prefab for.</summary>
        static string FindGear(params string[] names)
        {
            foreach (string n in names)
            {
                try { if (GearItem.LoadGearItemPrefab(n) != null) return n; }
                catch { }
            }
            return null;
        }

        /// <summary>Call every frame while playing a Slender game.</summary>
        public static void Update(float now)
        {
            if (now < _nextCheck) return;
            _nextCheck = now + 2f;
            if (SlenderGame.TakeStartGift()) GiveTorches();
            KeepNight();
            KeepDark();
        }

        static void KeepNight()
        {
            try
            {
                var tod = GameManager.GetTimeOfDayComponent();
                if (tod == null) return;
                float hour = tod.GetHour() + tod.GetMinutes() / 60f;
                bool night = hour >= NightStart - 1f || hour < NightEnd;   // 21:00 .. 04:30 counts as night
                if (night) return;

                // Work out how normalized time maps to hours on this clock, then jump to 22:00.
                float n = tod.GetNormalizedTime();
                float offset = n - hour / 24f;
                float target = NightStart / 24f + offset;
                target -= (float)Math.Floor(target);
                tod.SetNormalizedTime(target, true);

                if (!_loggedTime)
                {
                    _loggedTime = true;
                    float after = tod.GetHour() + tod.GetMinutes() / 60f;
                    SlenderGame.Log("Endless night: clock moved from " + hour.ToString("0.0") + "h to " + after.ToString("0.0") + "h.");
                }
            }
            catch (Exception e) { SlenderGame.Warn("Endless night failed: " + e.Message); _nextCheck += 30f; }
        }

        // Slender: The Arrival nights: low fog hanging between the trees, a faint cold-blue moonlight that
        // only shows silhouettes, everything else is your light.
        static readonly WeatherStage NightWeather = WeatherStage.LightFog;
        static readonly Color MoonColour = new Color(0.035f, 0.05f, 0.09f);
        static float _nextWeatherForce;
        static bool _loggedWeather, _loggedMoon;

        /// <summary>
        /// As dark as the game allows, styled after Slender: The Arrival: no aurora, low fog that never
        /// clears, and the moonlight turned down to a faint cold blue.
        /// </summary>
        static void KeepDark()
        {
            try { AuroraManager.m_ForceDisable = true; } catch { }

            try
            {
                var w = GameManager.GetWeatherComponent();
                var wt = GameManager.GetWeatherTransitionComponent();
                if (w != null && wt != null && w.GetWeatherStage() != NightWeather && Time.unscaledTime >= _nextWeatherForce)
                {
                    _nextWeatherForce = Time.unscaledTime + 60f;   // give the transition time to finish
                    wt.ForceUnmanagedWeatherStage(NightWeather, 0.1f);
                    if (!_loggedWeather) { _loggedWeather = true; SlenderGame.Log("Darkness: weather locked to low fog (Slender: The Arrival style)."); }
                }
            }
            catch (Exception e) { SlenderGame.Warn("Overcast weather failed: " + e.Message); _nextWeatherForce = Time.unscaledTime + 300f; }

            try
            {
                var us = GameManager.GetUniStorm();
                if (us != null)
                {
                    us.SetMoonColourOverride(true);
                    us.SetMoonColourOverrideColour(MoonColour);
                    if (!_loggedMoon) { _loggedMoon = true; SlenderGame.Log("Darkness: moonlight dimmed to a faint cold blue."); }
                }
            }
            catch (Exception e) { SlenderGame.Warn("Moonlight override failed: " + e.Message); _nextCheck += 30f; }
        }
    }
}

namespace SlenderMode
{
    // ------------------------------------------------------------------ no game music in Slender games
    // The game's own music (arrival in an area, exploring, aurora, sorrow/stalked/success stingers) is
    // skipped while a Slender game is running. The mod's ambience is the only score.

    [HarmonyPatch(typeof(SceneMusicManager), nameof(SceneMusicManager.PlaySceneEnterMusic))]
    internal static class Patch_NoMusic_SceneEnter { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(SceneMusicManager), nameof(SceneMusicManager.PlayExploreMusic))]
    internal static class Patch_NoMusic_Explore { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.PlaySorrowMusic))]
    internal static class Patch_NoMusic_Sorrow { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.CheckForHappySuccess))]
    internal static class Patch_NoMusic_Success { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.CheckForSorrow))]
    internal static class Patch_NoMusic_CheckSorrow { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.CheckForRopeClimbStress))]
    internal static class Patch_NoMusic_Rope { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.CheckForBeingStalked))]
    internal static class Patch_NoMusic_Stalked { static bool Prefix() => !SlenderGame.IsActive; }

    [HarmonyPatch(typeof(MusicEventManager), nameof(MusicEventManager.PlayLocationSound))]
    internal static class Patch_NoMusic_Location { static bool Prefix() => !SlenderGame.IsActive; }
}

namespace SlenderMode
{
    /// <summary>
    /// This flashlight is aurora-powered: with no aurora the game reports no charge and holds the
    /// light Off, whatever we write into the battery field. A Slender game kills the aurora to keep
    /// the night black, which would leave the flashlight permanently dead.
    ///
    /// So the aurora is faked for the flashlight alone: while any of the flashlight's own methods are
    /// running, the aurora checks answer "yes". Everything else - the sky, the lighting, powered
    /// buildings - still sees no aurora, so the night stays exactly as dark as it was.
    /// </summary>
    // The flashlight is aurora-powered, and it is gated in three separate places, not one:
    // GetNormalizedCharge reports 0, IsLit returns false, and EnableLights refuses to switch the
    // beam on. All three checks live inside the game's own native code, so nothing we do from the
    // outside (faking the aurora, writing the battery field) can reach them - the methods have to
    // be taken over instead.
    //
    // Which three they are was learned by reading Deaadman's Universal Tweaks (GPL-3.0). The code
    // below is our own, written against the game's API, and every patch is limited to Slender games
    // so ordinary saves behave exactly as the game intends.
    //
    // m_CurrentBatteryCharge is a plain 0..1 value, so a full battery is 1f.

    [HarmonyPatch(typeof(FlashlightItem), nameof(FlashlightItem.GetNormalizedCharge))]
    internal static class Patch_FL_Charge
    {
        static bool Prefix(FlashlightItem __instance, ref float __result)
        {
            if (!SlenderGame.IsActive || __instance == null) return true;
            __result = __instance.m_CurrentBatteryCharge;
            return false;
        }
    }

    [HarmonyPatch(typeof(FlashlightItem), nameof(FlashlightItem.IsLit))]
    internal static class Patch_FL_IsLit
    {
        static bool Prefix(FlashlightItem __instance, ref bool __result)
        {
            if (!SlenderGame.IsActive || __instance == null) return true;
            __result = __instance.IsOn();
            return false;
        }
    }

    /// <summary>Switches the beam objects and lights on directly, without asking about the aurora.</summary>
    [HarmonyPatch(typeof(FlashlightItem), nameof(FlashlightItem.EnableLights))]
    internal static class Patch_FL_EnableLights
    {
        static bool _broken;

        static bool Prefix(FlashlightItem __instance, FlashlightItem.State state)
        {
            if (_broken || !SlenderGame.IsActive || __instance == null) return true;
            try
            {
                // While it is in your hands the first-person rig draws the beam, so the world
                // object's own lights stay off and the two do not stack.
                // (Compared by pointer: two wrappers can stand for the same game object, so
                // reference equality on the wrappers is not the same question.)
                var pm = GameManager.GetPlayerManagerComponent();
                var mine = __instance.m_GearItem;
                var held = pm != null ? pm.m_ItemInHands : null;
                if (mine != null && held != null && mine.Pointer == held.Pointer) state = FlashlightItem.State.Off;

                bool low = state == FlashlightItem.State.Low;
                bool high = state == FlashlightItem.State.High;
                bool on = low || high;

                if (__instance.m_FxObjectLow != null) __instance.m_FxObjectLow.SetActive(low);
                if (__instance.m_FxObjectHigh != null) __instance.m_FxObjectHigh.SetActive(high);

                bool outdoor = false;
                try { outdoor = GameManager.GetWeatherComponent().UseOutdoorLightingForLightSources(); } catch { }

                if (__instance.m_LightIndoor != null) __instance.m_LightIndoor.enabled = !outdoor && on;
                if (__instance.m_LightIndoorHigh != null) __instance.m_LightIndoorHigh.enabled = !outdoor && high;
                if (__instance.m_LightOutdoor != null) __instance.m_LightOutdoor.enabled = outdoor && on;
                if (__instance.m_LightOutdoorHigh != null) __instance.m_LightOutdoorHigh.enabled = outdoor && high;
                return false;
            }
            catch (Exception e)
            {
                _broken = true;
                SlenderGame.Warn("Flashlight light patch disabled, falling back to the game's own: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// Without an aurora the game's own flicker makes the beam strobe, so it is stopped.
    ///
    /// This runs for every flickering light in the scene, every frame, so the name - which costs a
    /// string every time it is read across the interop boundary - is read once per light and the
    /// answer kept against its pointer.
    /// </summary>
    [HarmonyPatch(typeof(LightRandomIntensity), nameof(LightRandomIntensity.Update))]
    internal static class Patch_FL_NoFlicker
    {
        static readonly Dictionary<IntPtr, bool> _isBeam = new Dictionary<IntPtr, bool>();

        internal static void Forget() { _isBeam.Clear(); }

        static bool Prefix(LightRandomIntensity __instance)
        {
            try
            {
                if (!SlenderGame.IsActive || __instance == null) return true;
                IntPtr key = __instance.Pointer;
                if (!_isBeam.TryGetValue(key, out bool beam))
                {
                    if (_isBeam.Count > 2048) _isBeam.Clear();   // scene churn: start the list again
                    string n = __instance.gameObject.name;
                    beam = n == "LightIndoors" || n == "LightOutdoors" || n == "LightExtend";
                    _isBeam[key] = beam;
                }
                return !beam;
            }
            catch { return true; }
        }
    }

    /// <summary>
    /// The battery in a Slender game. The game's own flashlight is charged by the aurora and there
    /// is no aurora here, so the charge is run by hand instead: it burns down while the light is
    /// on and comes back while it is off.
    ///
    /// It is deliberately generous. The light is the only thing that drives the chaser off, so a
    /// battery that runs dry quickly would not make the game tense, it would make it unplayable -
    /// the point is that you cannot simply leave it burning all night.
    /// </summary>
    [HarmonyPatch(typeof(FlashlightItem), nameof(FlashlightItem.Update))]
    internal static class Patch_Flashlight_Battery
    {
        static bool _broken, _logged;

        /// <summary>Seconds of continuous light a full battery gives.</summary>
        const float DrainSeconds = 420f;    // 7 minutes
        /// <summary>Seconds switched off to go from flat back to full.</summary>
        const float ChargeSeconds = 300f;   // 5 minutes
        /// <summary>Below this the beam gives out and the light switches itself off.</summary>
        const float DeadCharge = 0.001f;

        // The charge is kept here rather than on the flashlight object, and belongs to the GAME
        // rather than to any one instance of the item. Going through a door can hand you a freshly
        // built flashlight object, and a charge that lived on the object would come back full every
        // time you stepped inside - an hour of light for anyone willing to open a door.
        static float _charge = 1f;
        static uint _chargeFor;
        static int _tickFrame = -1;

        static void Postfix(FlashlightItem __instance)
        {
            if (_broken || __instance == null || !SlenderGame.IsActive) return;
            try
            {
                // Several flashlights can be ticking; the battery only moves once a frame.
                int f = Time.frameCount;
                if (f != _tickFrame)
                {
                    _tickFrame = f;
                    uint id = SlenderGame.CurrentGame;
                    if (id != _chargeFor) { _chargeFor = id; _charge = 1f; }   // a new game starts charged

                    float dt = Time.deltaTime;      // zero while paused, which is what we want
                    if (dt > 0f)
                    {
                        if (__instance.IsOn())
                        {
                            _charge -= dt / DrainSeconds;
                            if (_charge <= DeadCharge)
                            {
                                _charge = 0f;
                                try { __instance.TurnOff(); } catch { }   // the beam gives out
                            }
                        }
                        else _charge += dt / ChargeSeconds;
                        _charge = Mathf.Clamp01(_charge);
                    }

                    if (!_logged)
                    {
                        _logged = true;
                        SlenderGame.Log("Flashlight: works without an aurora. Battery gives about " +
                                        (int)(DrainSeconds / 60f) + " minutes of light and fills again over about " +
                                        (int)(ChargeSeconds / 60f) + " minutes switched off.");
                    }
                }
                __instance.m_CurrentBatteryCharge = _charge;
            }
            catch (Exception e) { _broken = true; SlenderGame.Warn("Flashlight battery patch disabled: " + e.Message); }
        }
    }
}
