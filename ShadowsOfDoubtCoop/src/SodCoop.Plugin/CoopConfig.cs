using BepInEx.Configuration;
using UnityEngine;

namespace SodCoop
{
    public enum CoopMode
    {
        Off,
        Host,
        Join,
    }

    /// <summary>Settings in BepInEx/config/sodcoop.cfg (created on first launch).</summary>
    internal sealed class CoopConfig
    {
        public readonly ConfigEntry<CoopMode> Mode;
        public readonly ConfigEntry<string> HostAddress;
        public readonly ConfigEntry<int> Port;
        public readonly ConfigEntry<string> PlayerName;
        public readonly ConfigEntry<int> MaxPlayers;
        public readonly ConfigEntry<bool> AutoStart;

        public readonly ConfigEntry<int> SendRate;
        public readonly ConfigEntry<int> InterpolationDelayMs;

        public readonly ConfigEntry<bool> SyncTime;
        public readonly ConfigEntry<float> TimeSyncThreshold;

        public readonly ConfigEntry<string> CityIdOverride;

        public readonly ConfigEntry<float> AvatarHeightOffset;
        public readonly ConfigEntry<KeyCode> ToggleSessionKey;
        public readonly ConfigEntry<KeyCode> ToggleOverlayKey;

        public CoopConfig(ConfigFile cfg)
        {
            Mode = cfg.Bind("Session", "Mode", CoopMode.Off,
                "Off, Host (others join you) or Join (connect to HostAddress).");
            HostAddress = cfg.Bind("Session", "HostAddress", "127.0.0.1",
                "Join mode: the host's IP or hostname. Over the internet use the host's public IP with the port forwarded, or a VPN such as Tailscale, ZeroTier or Radmin.");
            Port = cfg.Bind("Session", "Port", 27777, "TCP port. The host must allow it through the firewall.");
            PlayerName = cfg.Bind("Session", "PlayerName", "Detective", "Name shown above you on other players' screens.");
            MaxPlayers = cfg.Bind("Session", "MaxPlayers", 4, "Host mode: maximum players including the host.");
            AutoStart = cfg.Bind("Session", "AutoStart", true,
                "Start hosting or joining automatically once a game has loaded. Otherwise press ToggleSessionKey.");

            SendRate = cfg.Bind("Network", "SendRate", 20, "Position updates sent per second.");
            InterpolationDelayMs = cfg.Bind("Network", "InterpolationDelayMs", 100,
                "How far in the past other players are drawn. Higher is smoother on bad connections but laggier.");

            SyncTime = cfg.Bind("World", "SyncTime", true, "Clients follow the host's in-game clock.");
            TimeSyncThreshold = cfg.Bind("World", "TimeSyncThreshold", 0.05f,
                "Clients only snap their clock when it differs from the host's by more than this (in the game's time units).");

            CityIdOverride = cfg.Bind("World", "CityIdOverride", "",
                "Leave empty to detect the loaded city. If detection does not work in your game version, set the same text (e.g. the city name) on every player.");

            AvatarHeightOffset = cfg.Bind("Display", "AvatarHeightOffset", 0.9f,
                "Raises other players' figures. Adjust if they float or sink into the floor.");
            ToggleSessionKey = cfg.Bind("Display", "ToggleSessionKey", KeyCode.F8, "Start or stop the co-op session.");
            ToggleOverlayKey = cfg.Bind("Display", "ToggleOverlayKey", KeyCode.F9, "Show or hide the co-op status overlay.");
        }
    }
}
