using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Journalisation.
    /// - Tout message passe par Debug.Log avec le préfixe « [GroundBlastFx] » (donc dans KSP.log).
    /// - Tous les messages portant ce préfixe, y compris ceux du Rendering (CODEX), sont recopiés dans
    ///   PluginData/GroundBlastFx.log, écrasé à chaque lancement du jeu.
    /// - Zéro spam : une exception identique n'est loguée qu'une fois, ensuite on ne tient qu'un compteur.
    /// </summary>
    public static class GeLog
    {
        public const string Prefix = "[GroundBlastFx] ";

        private static readonly object FileLock = new object();
        private static StreamWriter _file;
        private static bool _initialized;
        private static readonly Dictionary<int, RepeatedError> Repeats = new Dictionary<int, RepeatedError>();

        private sealed class RepeatedError
        {
            public string Context;
            public string Summary;
            public int Count;
        }

        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                Directory.CreateDirectory(GePaths.PluginData);
                _file = new StreamWriter(GePaths.LogFile, false, new UTF8Encoding(false)) { AutoFlush = true };
                _file.WriteLine("GroundBlastFx.log — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception e)
            {
                _file = null;
                Debug.LogWarning(Prefix + "Impossible d'ouvrir " + GePaths.LogFile + " : " + e.Message);
            }
            Application.logMessageReceivedThreaded += OnUnityLog;
        }

        public static void Info(string message) { Debug.Log(Prefix + message); }

        public static void Warn(string message) { Debug.LogWarning(Prefix + message); }

        public static void Error(string message) { Debug.LogError(Prefix + message); }

        /// <summary>Écrit uniquement dans GroundBlastFx.log (détails volumineux : en-tête, listes).</summary>
        public static void FileOnly(string message)
        {
            WriteFile("INFO", message);
        }

        /// <summary>
        /// Exception dans un chemin répété (Update, LateUpdate, rendu…) : loguée complètement la première fois,
        /// ensuite seulement comptée. Sans allocation sur les répétitions (hors l'exception elle-même).
        /// </summary>
        /// <returns>true si c'est la première occurrence.</returns>
        public static bool ExceptionOnce(string context, Exception e)
        {
            int key = context.GetHashCode() * 31 + e.GetType().GetHashCode();
            string msg = e.Message;
            if (msg != null) key = key * 31 + msg.GetHashCode();
            lock (Repeats)
            {
                if (Repeats.TryGetValue(key, out RepeatedError r))
                {
                    r.Count++;
                    return false;
                }
                Repeats[key] = new RepeatedError { Context = context, Summary = e.GetType().Name + ": " + msg, Count = 1 };
            }
            Debug.LogError(Prefix + context + " : " + e + "\n(les répétitions de cette erreur ne seront plus loguées, seulement comptées)");
            return true;
        }

        /// <summary>Résumé des erreurs répétées (fenêtre Debug, fin de session).</summary>
        public static string RepeatedErrorsSummary()
        {
            lock (Repeats)
            {
                if (Repeats.Count == 0) return "aucune erreur";
                var sb = new StringBuilder();
                foreach (var kv in Repeats)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(kv.Value.Context).Append(" : ").Append(kv.Value.Summary).Append(" ×").Append(kv.Value.Count);
                }
                return sb.ToString();
            }
        }

        public static int RepeatedErrorCount
        {
            get { lock (Repeats) { return Repeats.Count; } }
        }

        private static void OnUnityLog(string condition, string stackTrace, LogType type)
        {
            if (_file == null || condition == null) return;
            if (!condition.StartsWith("[GroundBlastFx", StringComparison.Ordinal)) return;
            string level = type == LogType.Log ? "INFO" : (type == LogType.Warning ? "WARN" : "ERR ");
            if (type == LogType.Exception && !string.IsNullOrEmpty(stackTrace))
                WriteFile(level, condition + "\n" + stackTrace);
            else
                WriteFile(level, condition);
        }

        private static void WriteFile(string level, string message)
        {
            if (_file == null) return;
            lock (FileLock)
            {
                try
                {
                    _file.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
                    _file.Write(' ');
                    _file.Write(level);
                    _file.Write(' ');
                    _file.WriteLine(message);
                }
                catch (Exception)
                {
                    // Disque plein ou fichier verrouillé : on renonce au fichier, KSP.log garde tout.
                    _file = null;
                }
            }
        }
    }
}
