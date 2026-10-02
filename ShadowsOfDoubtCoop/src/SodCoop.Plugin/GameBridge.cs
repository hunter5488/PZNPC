using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SodCoop
{
    /// <summary>
    /// Every place the mod touches Shadows of Doubt's own classes. The game's classes are reached
    /// by name through reflection, so the mod still compiles and loads if a game update renames a
    /// member; the affected feature just switches off with a warning in the BepInEx log.
    ///
    /// If something here stops working, open UnityExplorer in game, find the right class/member
    /// and update the names below.
    /// </summary>
    internal static class GameBridge
    {
        const string GameAssembly = "Assembly-CSharp";

        static readonly Dictionary<string, Type> TypeCache = new Dictionary<string, Type>();
        static readonly HashSet<string> Warned = new HashSet<string>();

        /// <summary>The local player's character, or null when not in a loaded game.</summary>
        public static Component Player => GetSingleton("Player") as Component;

        public static bool IsInGame
        {
            get
            {
                try
                {
                    var player = Player;
                    return player != null && player.gameObject.activeInHierarchy && Camera.main != null;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool TryGetLocalPose(out Vector3 position, out float yaw, out float pitch)
        {
            position = default;
            yaw = pitch = 0;
            var player = Player;
            var cam = Camera.main;
            if (player == null || cam == null)
                return false;

            position = player.transform.position;
            var euler = cam.transform.eulerAngles;
            yaw = euler.y;
            pitch = euler.x;
            return true;
        }

        /// <summary>Identifies the loaded city so players can check they are in the same world.</summary>
        public static string CityId
        {
            get
            {
                var overrideId = Plugin.Settings.CityIdOverride.Value;
                if (!string.IsNullOrWhiteSpace(overrideId))
                    return overrideId.Trim();

                var city = GetSingleton("CityData");
                if (city == null)
                    return "unknown";
                var name = GetMember(city, "cityName")?.ToString();
                var seed = GetMember(city, "seed")?.ToString();
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(seed))
                    return "unknown";
                return $"{name}|{seed}";
            }
        }

        /// <summary>The in-game clock, or null if it could not be read.</summary>
        public static double? GameTime
        {
            get
            {
                var session = GetSingleton("SessionData");
                var value = session == null ? null : GetMember(session, "gameTime");
                return value == null ? (double?)null : Convert.ToDouble(value);
            }
            set
            {
                var session = GetSingleton("SessionData");
                if (session != null && value.HasValue)
                    SetMember(session, "gameTime", value.Value);
            }
        }

        // ---- reflection helpers ----

        static Type FindType(string name)
        {
            if (TypeCache.TryGetValue(name, out var cached))
                return cached;

            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == GameAssembly);
            var type = asm?.GetType(name) ?? asm?.GetType("Il2Cpp." + name);
            // Only cache hits: the game assembly may not be loaded yet on the first call.
            if (type != null)
                TypeCache[name] = type;
            else
                WarnOnce("type:" + name, $"Could not find game class '{name}'.");
            return type;
        }

        static object GetSingleton(string typeName)
        {
            var type = FindType(typeName);
            if (type == null)
                return null;
            try
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var prop = type.GetProperty("Instance", flags);
                if (prop != null)
                    return prop.GetValue(null);
                var field = type.GetField("Instance", flags) ?? type.GetField("_instance", flags);
                if (field != null)
                    return field.GetValue(null);
                WarnOnce("instance:" + typeName, $"Game class '{typeName}' has no Instance.");
            }
            catch (Exception ex)
            {
                WarnOnce("instance-error:" + typeName, $"Reading {typeName}.Instance failed: {ex.GetBaseException().Message}");
            }
            return null;
        }

        static MemberInfo FindMember(Type type, string name)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            return (MemberInfo)type.GetProperty(name, flags) ?? type.GetField(name, flags);
        }

        static object GetMember(object target, string name)
        {
            var member = FindMember(target.GetType(), name);
            try
            {
                switch (member)
                {
                    case PropertyInfo p: return p.GetValue(target);
                    case FieldInfo f: return f.GetValue(target);
                }
            }
            catch (Exception ex)
            {
                WarnOnce("get-error:" + name, $"Reading {target.GetType().Name}.{name} failed: {ex.GetBaseException().Message}");
                return null;
            }
            WarnOnce("member:" + target.GetType().Name + "." + name, $"Game class '{target.GetType().Name}' has no member '{name}'.");
            return null;
        }

        static void SetMember(object target, string name, double value)
        {
            var member = FindMember(target.GetType(), name);
            try
            {
                switch (member)
                {
                    case PropertyInfo p when p.CanWrite:
                        p.SetValue(target, Convert.ChangeType(value, p.PropertyType));
                        return;
                    case FieldInfo f:
                        f.SetValue(target, Convert.ChangeType(value, f.FieldType));
                        return;
                }
                WarnOnce("set:" + name, $"Cannot write {target.GetType().Name}.{name}; time sync disabled.");
            }
            catch (Exception ex)
            {
                WarnOnce("set-error:" + name, $"Writing {target.GetType().Name}.{name} failed: {ex.GetBaseException().Message}");
            }
        }

        static void WarnOnce(string key, string message)
        {
            if (Warned.Add(key))
                Plugin.CoopLog.LogWarning(message);
        }
    }
}
