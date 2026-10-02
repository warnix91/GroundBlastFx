using System;
using System.Globalization;
using UnityEngine;

namespace GroundBlastFx.Config
{
    /// <summary>Lecture robuste des valeurs de ConfigNode (culture invariante : le jeu de l'auteur est en fr-fr).</summary>
    public static class CfgParse
    {
        public static bool TryFloat(string s, out float value)
        {
            return float.TryParse(s?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static float Float(ConfigNode node, string key, float fallback)
        {
            string s = node.GetValue(key);
            return s != null && TryFloat(s, out float v) ? v : fallback;
        }

        public static int Int(ConfigNode node, string key, int fallback)
        {
            string s = node.GetValue(key);
            return s != null && int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;
        }

        public static bool Bool(ConfigNode node, string key, bool fallback)
        {
            string s = node.GetValue(key);
            if (s == null) return fallback;
            s = s.Trim();
            if (s.Equals("true", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (s.Equals("false", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("no", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }

        /// <summary>« r, g, b[, a] » avec des valeurs 0..1.</summary>
        public static bool TryColor(string s, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            string[] parts = s.Split(',');
            if (parts.Length < 3) return false;
            if (!TryFloat(parts[0], out float r) || !TryFloat(parts[1], out float g) || !TryFloat(parts[2], out float b)) return false;
            float a = 1f;
            if (parts.Length > 3 && !TryFloat(parts[3], out a)) a = 1f;
            color = new Color(r, g, b, a);
            return true;
        }

        public static Color ColorValue(ConfigNode node, string key, Color fallback)
        {
            return TryColor(node.GetValue(key), out Color c) ? c : fallback;
        }

        public static string Format(float v)
        {
            return v.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static string Format(Color c)
        {
            return Format(c.r) + ", " + Format(c.g) + ", " + Format(c.b) + ", " + Format(c.a);
        }
    }
}
