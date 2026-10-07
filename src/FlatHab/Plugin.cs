using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FlatHab
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Shipbreaker.exe")]
    public class FlatHabPlugin : BaseUnityPlugin
    {
        public const string Guid = "structed.shipbreaker.flathab";
        public const string Name = "Flat Hab";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> StartInFlatMode;
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static ConfigEntry<bool> InstantCameraCuts;
        internal static ConfigEntry<float> BackgroundOpacity;
        internal static ConfigEntry<float> MenuScale;
        internal static ConfigEntry<bool> VerboseLogging;

        private void Awake()
        {
            Log = Logger;

            StartInFlatMode = Config.Bind("General", "StartInFlatMode", true,
                "Show the flat hab menu instead of the 3D hab navigation when the game starts.");
            ToggleKey = Config.Bind("General", "ToggleKey", new KeyboardShortcut(KeyCode.F6),
                "Switches between the flat menu and the original 3D hab navigation.");
            InstantCameraCuts = Config.Bind("General", "InstantCameraCuts", true,
                "Replace the hab camera fly-throughs with instant cuts.");
            BackgroundOpacity = Config.Bind("Appearance", "BackgroundOpacity", 0.65f,
                new ConfigDescription("How much the hab behind the menu is darkened (0 = not at all, 1 = black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            MenuScale = Config.Bind("Appearance", "MenuScale", 1f,
                new ConfigDescription("Menu size multiplier.", new AcceptableValueRange<float>(0.5f, 2.5f)));
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", true,
                "Log the hab layout and every menu action to BepInEx\\LogOutput.log.");

            new Harmony(Guid).PatchAll(typeof(FlatHabPlugin).Assembly);

            var host = new GameObject("FlatHab");
            host.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(host);
            host.AddComponent<HabMenu>();

            Log.LogInfo($"{Name} {Version} loaded. Press {ToggleKey.Value} in the hab to switch between menu and 3D view.");
        }
    }
}
