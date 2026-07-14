using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.SceneManagement;

namespace FreeGhost
{
    [BepInAutoPlugin]
    public partial class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log { get; private set; } = null!;
        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony("yondev.tinytweaks");
            harmony.PatchAll();
            
            FreeGhost.Binds(Config);
            showNamesAlways.Binds(Config);
            Log.LogInfo($"Plugin {Name} is loaded!");
        }

        public static void log(string message)
        {
            Log.LogInfo($"{message}");
        }
    }
}
