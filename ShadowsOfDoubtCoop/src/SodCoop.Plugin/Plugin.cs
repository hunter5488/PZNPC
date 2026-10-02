using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace SodCoop
{
    [BepInPlugin(Guid, "Shadows of Doubt Co-op", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "sodcoop";
        public const string Version = "0.1.0";

        internal static ManualLogSource CoopLog;
        internal static CoopConfig Settings;

        public override void Load()
        {
            CoopLog = Log;
            Settings = new CoopConfig(Config);
            AddComponent<CoopBehaviour>();
            CoopLog.LogInfo($"Shadows of Doubt Co-op {Version} loaded. Mode: {Settings.Mode.Value}. Press {Settings.ToggleSessionKey.Value} in game to start or stop.");
        }
    }
}
