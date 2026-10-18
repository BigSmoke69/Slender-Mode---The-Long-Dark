using System;
using System.IO;
using HarmonyLib;
using Il2Cpp;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace SlenderMode
{
    /// <summary>
    /// Slender games get their own loading screen: the intro text ("How long can you survive?") is
    /// replaced, and the background picture becomes Mods/SlenderMode/loading.jpg.
    /// Only while a Slender game is starting or loaded; normal games keep the game's own screen.
    /// </summary>
    internal static class SlenderLoadingScreen
    {
        // Written for this mod. The passage that used to sit here was lifted from the game that
        // inspired it, which is not ours to ship.
        public const string Text =
            "Eight pages, and a night that does not end. Walk softly, keep the light in your hand, " +
            "and whatever is standing out there among the trees\n\ndo not stop and look at it.";

        static string _imageFile, _guardFile, _offFile, _failFile;
        static Texture2D _image;
        static bool _imageFailed, _disabled, _guardOpen, _loggedOriginal;
        static int _guardFrames;
        static string _original;           // what the game wanted to show (to find its label)
        static int _scan;

        public static void Init(string modFolder)
        {
            _imageFile = Path.Combine(modFolder, "loading.jpg");
            _guardFile = Path.Combine(modFolder, "loading_crashguard.txt");
            _offFile = Path.Combine(modFolder, "loading_image_off.txt");
            _failFile = Path.Combine(modFolder, "loading_fails.txt");
            try
            {
                // Quitting the game while a loading screen is up leaves the guard open, which is not a
                // crash. Only switch the image off after it has happened twice in a row.
                if (File.Exists(_guardFile))
                {
                    File.Delete(_guardFile);
                    int fails = 0;
                    try { if (File.Exists(_failFile)) int.TryParse(File.ReadAllText(_failFile).Trim(), out fails); } catch { }
                    fails++;
                    try { File.WriteAllText(_failFile, fails.ToString()); } catch { }
                    if (fails >= 2)
                    {
                        File.WriteAllText(_offFile, "The Slender loading image was on screen twice when the game closed; delete this file to try again.");
                        SlenderGame.Warn("The game closed twice while the Slender loading image was being shown. " +
                                         "The image is off from now on (the text still changes). Delete Mods\\SlenderMode\\loading_image_off.txt to try again.");
                    }
                    else
                    {
                        SlenderGame.Log("The game closed while the Slender loading image was on screen. If that was a crash and it happens again, the image turns itself off.");
                    }
                }
                _disabled = File.Exists(_offFile);
            }
            catch { }
        }

        /// <summary>Quitting normally is not a crash: close the guard so it never counts as one.</summary>
        public static void OnQuit()
        {
            try { if (_guardFile != null && File.Exists(_guardFile)) File.Delete(_guardFile); } catch { }
        }

        public static bool Wanted
        {
            get { try { return SlenderGame.PendingNewGame || SlenderGame.IsActive; } catch { return false; } }
        }

        internal static void OnFoundText(ref string result)
        {
            if (!Wanted) return;
            if (!_loggedOriginal) { _loggedOriginal = true; SlenderGame.Log("Loading screen: replacing the survival intro text."); }
            if (!string.IsNullOrEmpty(result) && result != Text) _original = result;
            result = Text;
        }

        internal static void OnUpdate(Panel_Loading panel)
        {
            if (panel == null || !Wanted) return;
            ApplyImage(panel);
            if (++_scan % 10 == 0) ApplyText(panel);
            if (_guardOpen && ++_guardFrames > 60)
            {
                _guardOpen = false;
                // the image survived: clear the guard and forget any earlier false alarm
                try { File.Delete(_guardFile); } catch { }
                try { if (File.Exists(_failFile)) File.Delete(_failFile); } catch { }
            }
        }

        static void ApplyImage(Panel_Loading panel)
        {
            if (_disabled || _imageFailed) return;
            try
            {
                var bg = panel.m_BackgroundTexture;
                if (bg == null) return;
                if (_image == null)
                {
                    if (!File.Exists(_imageFile)) { _imageFailed = true; return; }
                    var t = new Texture2D(2, 2);
                    Il2CppStructArray<byte> bytes = File.ReadAllBytes(_imageFile);
                    if (!ImageConversion.LoadImage(t, bytes)) { _imageFailed = true; SlenderGame.Warn("Loading image could not be read."); return; }
                    t.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    _image = t;
                }
                var cur = bg.mainTexture;
                if (cur != null && cur.Pointer == _image.Pointer) return;
                if (!_guardOpen)
                {
                    _guardOpen = true; _guardFrames = 0;
                    try { File.WriteAllText(_guardFile, "loading image"); } catch { }
                }
                bg.mainTexture = _image;
            }
            catch (Exception e) { _imageFailed = true; SlenderGame.Warn("Loading image disabled after error: " + e.Message); }
        }

        static void ApplyText(Panel_Loading panel)
        {
            try
            {
                var labels = panel.GetComponentsInChildren<UILabel>(true);
                foreach (var l in labels)
                {
                    if (l == null) continue;
                    string s = l.text;
                    if (string.IsNullOrEmpty(s) || s == Text) continue;
                    bool match = (_original != null && s == _original)
                                 || s.StartsWith("A mysterious geomagnetic storm", StringComparison.OrdinalIgnoreCase)
                                 || s.Contains("So often are we guided");   // our text, but mangled by the game's lookup
                    if (match) l.text = Text;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.FindChallengeOrSurvivalText))]
    internal static class Patch_Loading_Text
    {
        static void Postfix(ref string __result)
        {
            try { SlenderLoadingScreen.OnFoundText(ref __result); } catch { }
        }
    }

    [HarmonyPatch(typeof(Panel_Loading), nameof(Panel_Loading.Update))]
    internal static class Patch_Loading_Update
    {
        static void Postfix(Panel_Loading __instance)
        {
            try { SlenderLoadingScreen.OnUpdate(__instance); } catch { }
        }
    }
}
