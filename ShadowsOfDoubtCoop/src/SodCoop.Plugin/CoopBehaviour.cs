using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using SodCoop.Net;
using UnityEngine;

namespace SodCoop
{
    /// <summary>
    /// Runs every frame on the Unity main thread: starts and stops the session, sends the local
    /// player's position, applies what arrives from the network, and draws the overlay.
    /// </summary>
    public class CoopBehaviour : MonoBehaviour
    {
        const int MaxMessages = 6;
        const float MessageSeconds = 8f;

        CoopSession _session;
        readonly Dictionary<byte, RemoteAvatar> _remotes = new Dictionary<byte, RemoteAvatar>();
        readonly List<(float until, string text)> _messages = new List<(float, string)>();
        float _nextSend;
        float _nextTimeSync;
        bool _autoStartUsed;
        bool _wasInGame;
        bool _showOverlay = true;
        bool _hotkeysBroken;
        string _status = "Co-op: off";

        public CoopBehaviour(IntPtr ptr) : base(ptr) { }

        // Unity calls Update, OnGUI and OnDestroy. Everything else is plain C# and is hidden from
        // IL2CPP class injection, which cannot register methods using managed-only types.

        [HideFromIl2Cpp]
        static CoopConfig Cfg => Plugin.Settings;

        void Update()
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Plugin.CoopLog.LogError("Co-op update failed, stopping session: " + ex);
                StopSession("Error: " + ex.GetBaseException().Message);
            }
        }

        [HideFromIl2Cpp]
        void Tick()
        {
            HandleHotkeys();

            bool inGame = GameBridge.IsInGame;
            if (!inGame && _wasInGame)
            {
                // Back at the main menu or loading: the world we were syncing is gone.
                if (_session != null)
                    StopSession("Left the game.");
                _autoStartUsed = false;
            }
            _wasInGame = inGame;

            if (inGame && _session == null && Cfg.AutoStart.Value && !_autoStartUsed && Cfg.Mode.Value != CoopMode.Off)
            {
                _autoStartUsed = true;
                StartSession();
            }

            if (_session == null)
                return;

            _session.Poll(HandleEvent);
            if (_session == null)
                return; // An event ended the session.

            float now = Time.realtimeSinceStartup;
            if (inGame && now >= _nextSend)
            {
                _nextSend = now + 1f / Mathf.Clamp(Cfg.SendRate.Value, 1, 60);
                if (GameBridge.TryGetLocalPose(out var pos, out var yaw, out var pitch))
                    _session.SendLocalState(new PlayerState { X = pos.x, Y = pos.y, Z = pos.z, Yaw = yaw, Pitch = pitch });
            }

            if (_session is CoopHost host && Cfg.SyncTime.Value && now >= _nextTimeSync)
            {
                _nextTimeSync = now + 1f;
                var gameTime = GameBridge.GameTime;
                if (gameTime.HasValue)
                    host.SendWorldTime(gameTime.Value);
            }

            double renderTime = now - Cfg.InterpolationDelayMs.Value / 1000.0;
            foreach (var remote in _remotes.Values)
                remote.Update(renderTime, Cfg.AvatarHeightOffset.Value);
        }

        [HideFromIl2Cpp]
        void HandleHotkeys()
        {
            if (_hotkeysBroken)
                return;
            try
            {
                if (Input.GetKeyDown(Cfg.ToggleOverlayKey.Value))
                    _showOverlay = !_showOverlay;

                if (Input.GetKeyDown(Cfg.ToggleSessionKey.Value))
                {
                    if (_session != null)
                        StopSession("Stopped.");
                    else if (!GameBridge.IsInGame)
                        Notify("Load a game before starting co-op.");
                    else if (Cfg.Mode.Value == CoopMode.Off)
                        Notify("Set Mode to Host or Join in BepInEx/config/sodcoop.cfg first.");
                    else
                        StartSession();
                }
            }
            catch (Exception ex)
            {
                // The game may not expose Unity's legacy input; AutoStart still works without hotkeys.
                _hotkeysBroken = true;
                Plugin.CoopLog.LogWarning("Hotkeys unavailable, use AutoStart instead: " + ex.GetBaseException().Message);
            }
        }

        [HideFromIl2Cpp]
        void StartSession()
        {
            string city = GameBridge.CityId;
            try
            {
                if (Cfg.Mode.Value == CoopMode.Host)
                {
                    var host = new CoopHost(Cfg.Port.Value, Cfg.PlayerName.Value, city, Cfg.MaxPlayers.Value);
                    _session = host;
                    _status = $"Co-op: hosting on port {host.Port}";
                    Notify($"Hosting on port {host.Port}. City: {city}");
                }
                else
                {
                    _session = new CoopClient(Cfg.HostAddress.Value, Cfg.Port.Value, Cfg.PlayerName.Value, city);
                    _status = $"Co-op: connecting to {Cfg.HostAddress.Value}:{Cfg.Port.Value}...";
                }
                _nextSend = 0;
                _nextTimeSync = 0;
            }
            catch (Exception ex)
            {
                _session = null;
                _status = "Co-op: off";
                Notify("Could not start: " + ex.GetBaseException().Message);
            }
        }

        [HideFromIl2Cpp]
        void StopSession(string reason)
        {
            _session?.Dispose();
            _session = null;
            foreach (var remote in _remotes.Values)
                remote.Destroy();
            _remotes.Clear();
            _status = "Co-op: off";
            Notify(reason);
        }

        [HideFromIl2Cpp]
        void HandleEvent(NetEvent e)
        {
            switch (e.Kind)
            {
                case NetEventKind.Connected:
                    _status = $"Co-op: connected as player {e.PlayerId}";
                    Notify("Connected to host.");
                    break;

                case NetEventKind.Disconnected:
                    StopSession(e.Text);
                    break;

                case NetEventKind.PlayerJoined:
                    if (e.PlayerId == _session.LocalPlayerId)
                        break;
                    if (_remotes.TryGetValue(e.PlayerId, out var existing))
                        existing.Name = e.Text;
                    else
                        _remotes[e.PlayerId] = new RemoteAvatar(e.PlayerId, e.Text);
                    Notify($"{e.Text} joined.");
                    break;

                case NetEventKind.PlayerLeft:
                    if (_remotes.TryGetValue(e.PlayerId, out var leaving))
                    {
                        leaving.Destroy();
                        _remotes.Remove(e.PlayerId);
                        Notify($"{leaving.Name} left.");
                    }
                    break;

                case NetEventKind.PlayerState:
                    if (_remotes.TryGetValue(e.PlayerId, out var remote))
                        remote.Buffer.Add(Time.realtimeSinceStartup, e.State);
                    break;

                case NetEventKind.WorldTime:
                    ApplyWorldTime(e.GameTime);
                    break;

                case NetEventKind.Chat:
                    var who = e.PlayerId == CoopSession.HostPlayerId && _session.IsHost ? Cfg.PlayerName.Value
                        : _remotes.TryGetValue(e.PlayerId, out var sender) ? sender.Name : $"Player {e.PlayerId}";
                    Notify($"{who}: {e.Text}");
                    break;

                case NetEventKind.Warning:
                    Notify(e.Text);
                    break;
            }
        }

        [HideFromIl2Cpp]
        void ApplyWorldTime(double hostTime)
        {
            if (!Cfg.SyncTime.Value)
                return;
            var local = GameBridge.GameTime;
            if (local.HasValue && Math.Abs(local.Value - hostTime) > Cfg.TimeSyncThreshold.Value)
                GameBridge.GameTime = hostTime;
        }

        [HideFromIl2Cpp]
        void Notify(string text)
        {
            Plugin.CoopLog.LogInfo(text);
            _messages.Add((Time.realtimeSinceStartup + MessageSeconds, text));
            if (_messages.Count > MaxMessages)
                _messages.RemoveAt(0);
        }

        void OnGUI()
        {
            if (!_showOverlay)
                return;

            float now = Time.realtimeSinceStartup;
            _messages.RemoveAll(m => m.until < now);

            float y = 10;
            GUI.Label(new Rect(10, y, 600, 22), $"{_status}   [{Cfg.ToggleSessionKey.Value}] start/stop   [{Cfg.ToggleOverlayKey.Value}] hide");
            y += 22;
            foreach (var m in _messages)
            {
                GUI.Label(new Rect(10, y, 900, 22), m.text);
                y += 20;
            }

            var cam = Camera.main;
            if (cam == null)
                return;
            foreach (var remote in _remotes.Values)
            {
                if (!remote.HasPosition)
                    continue;
                var screen = cam.WorldToScreenPoint(remote.HeadPosition + Vector3.up * 0.4f);
                if (screen.z <= 0)
                    continue; // Behind the camera.
                // GUI space has y pointing down; screen space has it pointing up.
                GUI.Label(new Rect(screen.x - 60, Screen.height - screen.y - 10, 120, 22), remote.Name);
            }
        }

        void OnDestroy()
        {
            _session?.Dispose();
            _session = null;
        }
    }
}
