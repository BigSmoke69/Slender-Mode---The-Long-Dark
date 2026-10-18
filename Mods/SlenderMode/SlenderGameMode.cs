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
        public const string Label = "SLENDER";
        public const string Description =
            "Something hunts you in Coastal Highway. The night never ends. Pilgrim survival rules.";

        /// <summary>True between clicking the Slender entry and the new game starting.</summary>
        public static bool PendingNewGame;

        static MelonLogger.Instance _log;
        static string _file;
        static readonly HashSet<uint> _ids = new HashSet<uint>();
        static readonly HashSet<uint> _gifted = new HashSet<uint>();
        static string _giftFile;

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
                if (File.Exists(_file))
                    foreach (var line in File.ReadAllLines(_file))
                        if (uint.TryParse(line.Trim(), out uint id)) _ids.Add(id);
            }
            catch (Exception e) { _log.Warning("Could not read the Slender games list: " + e.Message); }
        }

        static uint CurrentGameId()
        {
            try { return SaveGameSystem.m_CurrentGameId; } catch { return 0; }
        }

        /// <summary>Is the game being played right now a Slender game?</summary>
        public static bool IsActive
        {
            get { uint id = CurrentGameId(); return id != 0 && _ids.Contains(id); }
        }

        /// <summary>Call while in gameplay: turns a just-started game into a Slender game.</summary>
        public static bool TryClaimNewGame()
        {
            if (!PendingNewGame) return false;
            uint id = CurrentGameId();
            if (id == 0) return false;
            PendingNewGame = false;
            if (_ids.Add(id))
            {
                try { File.AppendAllText(_file, id + Environment.NewLine); } catch (Exception e) { _log.Warning("Could not save the Slender games list: " + e.Message); }
            }
            _log.Msg("New Slender game started (game id " + id + ").");
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

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.ConfigureMenu))]
    internal static class Patch_SelectExperience_ConfigureMenu
    {
        static Panel_SelectExperience.XPModeMenuItem _item;
        internal static int SlenderIndex = -1;
        static bool _loggedConfigs;

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
                    model.m_LabelText = SlenderGame.Label;
                    model.m_DescriptionText = SlenderGame.Description;
                    model.m_SecondaryText = "";
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
                if (slender) SlenderGame.Log("Slender game mode selected.");
            }
            catch (Exception e) { SlenderGame.Warn("Experience click check failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(Panel_SelectExperience), nameof(Panel_SelectExperience.OnClickBack))]
    internal static class Patch_SelectExperience_Back
    {
        static void Prefix() { SlenderGame.PendingNewGame = false; }
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

    [HarmonyPatch(typeof(Panel_SelectRegion_Map), nameof(Panel_SelectRegion_Map.OnSelectRegionContinue))]
    internal static class Patch_SelectRegion_Continue
    {
        static void Prefix(Panel_SelectRegion_Map __instance)
        {
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
    /// time keeps moving for the aurora and weather), a permanent aurora, and a flashlight at the start.
    /// </summary>
    internal static class SlenderWorld
    {
        const float NightStart = 22f;   // hour the loop jumps back to
        const float NightEnd = 4.5f;    // hour at which it jumps back
        static float _nextCheck;
        static bool _loggedTime, _loggedAurora, _loggedForceDisable;

        /// <summary>Called once when a brand-new Slender game starts.</summary>
        public static void OnNewGame() { _nextCheck = 0f; }

        static void GiveFlashlight()
        {
            try
            {
                var pm = GameManager.GetPlayerManagerComponent();
                if (pm != null)
                {
                    pm.AddItemCONSOLE("GEAR_Flashlight", 1, 1f);
                    SlenderGame.Log("Added a flashlight to the inventory.");
                }
            }
            catch (Exception e) { SlenderGame.Warn("Could not add the flashlight: " + e.Message); }
        }

        /// <summary>Call every frame while playing a Slender game.</summary>
        public static void Update(float now)
        {
            if (now < _nextCheck) return;
            _nextCheck = now + 2f;
            if (SlenderGame.TakeStartGift()) GiveFlashlight();
            KeepNight();
            KeepAurora();
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

        static void KeepAurora()
        {
            try
            {
                if (AuroraManager.m_ForceDisable)
                {
                    AuroraManager.m_ForceDisable = false;
                    if (!_loggedForceDisable) { _loggedForceDisable = true; SlenderGame.Log("Aurora: lifted the new-game aurora block."); }
                }
                var am = GameManager.GetAuroraManager();
                if (am == null || am.AuroraIsActive()) return;
                // Long duration (game hours); re-forced whenever it ends.
                am.ForceAuroraNextOpportunity(true, true, 8f);
                if (!_loggedAurora) { _loggedAurora = true; SlenderGame.Log("Aurora: forced on."); }
            }
            catch (Exception e) { SlenderGame.Warn("Permanent aurora failed: " + e.Message); _nextCheck += 30f; }
        }
    }
}
