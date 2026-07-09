using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace TinyTweaks
{
    [BepInAutoPlugin]
    public partial class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log { get; private set; } = null!;

        private void Awake()
        {
            Log = Logger;
            Harmony harmony = new Harmony("yondev.tinytweaks");
            harmony.PatchAll();
            FreeGhost.Binds(Config);
            Log.LogInfo($"Plugin {Name} is loaded!");
        }
    }
}
