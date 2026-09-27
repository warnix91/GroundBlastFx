using System;
using System.Collections.Generic;
using GroundBlastFx.Core;

namespace GroundBlastFx.Config
{
    /// <summary>
    /// Textes de l'interface, lus dans Configs/Strings_*.cfg (nœuds GROUNDBLASTFX_STRINGS { language = fr … }).
    /// Une clé absente affiche la version anglaise, puis la française, puis la clé elle-même.
    /// Langue « auto » (toujours, 1.9.2) : celle du jeu (KSP 1.12 : anglais, espagnol, russe, chinois, japonais, allemand,
    /// français, italien, portugais), anglais si elle n'est pas traduite.
    /// </summary>
    public static class GeStrings
    {
        private static readonly Dictionary<string, string> Current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> English = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> French = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Langue réellement chargée (« auto » résolu).</summary>
        public static string Active { get; private set; } = "en";

        public static void Load(string language)
        {
            Current.Clear();
            English.Clear();
            French.Clear();
            GameDatabase db = GameDatabase.Instance;
            if (db == null) return;
            ConfigNode[] nodes = db.GetConfigNodes("GROUNDBLASTFX_STRINGS");
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ConfigNode n in nodes) found.Add((n.GetValue("language") ?? "fr").Trim().ToLowerInvariant());

            string wanted = (language ?? "auto").Trim().ToLowerInvariant();
            if (wanted == "auto" || wanted.Length == 0) wanted = GameLanguage();
            if (!found.Contains(wanted)) wanted = found.Contains("en") ? "en" : "fr";
            Active = wanted;

            foreach (ConfigNode n in nodes)
            {
                string lang = (n.GetValue("language") ?? "fr").Trim().ToLowerInvariant();
                foreach (ConfigNode.Value v in n.values)
                {
                    if (v.name == "language" || v.name == "languageName") continue;
                    string text = v.value.Replace("\\n", "\n");
                    if (lang == "fr") French[v.name] = text;
                    if (lang == "en") English[v.name] = text;
                    if (lang == wanted) Current[v.name] = text;
                }
            }
            GeLog.Info("Textes de l'interface : " + Current.Count + " clés (" + wanted + (language == wanted ? "" : ", demandé : " + language)
                       + "), " + found.Count + " langue(s) installée(s)");
        }

        /// <summary>Code de la langue du jeu (Localizer de KSP : « fr-fr », « zh-cn »… → « fr », « zh »).</summary>
        public static string GameLanguage()
        {
            try
            {
                string l = KSP.Localization.Localizer.CurrentLanguage;
                if (!string.IsNullOrEmpty(l)) return l.Trim().ToLowerInvariant().Substring(0, Math.Min(2, l.Trim().Length));
            }
            catch (Exception e)
            {
                GeLog.ExceptionOnce("GeStrings.GameLanguage", e);
            }
            return "en";
        }

        public static string Get(string key)
        {
            if (Current.TryGetValue(key, out string s)) return s;
            if (English.TryGetValue(key, out s)) return s;
            if (French.TryGetValue(key, out s)) return s;
            return key;
        }
    }
}
