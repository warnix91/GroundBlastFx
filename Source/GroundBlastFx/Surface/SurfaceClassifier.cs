using System;
using System.Collections.Generic;
using System.Text;
using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Core;
using UnityEngine;

namespace GroundBlastFx.Surface
{
    /// <summary>
    /// Classification d'un collider de décor (calques Local Scenery / TerrainColliders) en Terrain, LaunchPad ou Structure
    /// Le résultat est mis en cache par collider : le travail (noms, réflexion KK, logs) n'est fait
    /// qu'à la première rencontre.
    /// Signaux, du plus fort au plus faible :
    /// 1. tag « LaunchpadFX » ou composant LaunchPadFX (pas de tir du KSC, même test que ModuleSurfaceFX stock) ;
    /// 2. composant PQ ou collider Parallax sous PQStorage → Terrain ;
    /// 3. motifs de noms dans la hiérarchie (Configs/Surfaces.cfg, GROUNDBLASTFX_CLASSIFIER) ;
    /// 4. static Kerbal Konstructs (site de lancement → LaunchPad, autre → Structure) ;
    /// 5. sinon Structure.
    /// Le verrou PRELAUNCH (EngineSampler) est appliqué ensuite par le Core et a priorité sur Terrain/Structure.
    /// </summary>
    public sealed class SurfaceClassifier
    {
        private struct Cached
        {
            public SurfaceKind Kind;
            public string SiteKey;
        }

        private readonly Dictionary<int, Cached> _cache = new Dictionary<int, Cached>();
        private readonly HashSet<string> _logged = new HashSet<string>(StringComparer.Ordinal);
        private readonly KerbalKonstructsBridge _kk = new KerbalKonstructsBridge();
        private float _lastGlobalPadSearch = -999f;

        /// <summary>Pas de tir réels rencontrés (sorties du déflecteur, rayon du pas).</summary>
        public readonly LaunchPadRegistry Pads = new LaunchPadRegistry();

        public bool KerbalKonstructsAvailable => _kk.Available;

        public void Clear() { _cache.Clear(); Pads.Clear(); }

        /// <summary>
        /// Un collider classé « pas de tir » par son nom peut être le grand terrain qui entoure le pas (KSC) : on cherche
        /// le vrai pas (LaunchPadFX) dans la même installation pour pouvoir ensuite distinguer le pas de ses abords.
        /// Recherche faite une seule fois par collider (résultat mis en cache).
        /// </summary>
        private void FindPadNear(Collider c)
        {
            Transform t = c.transform;
            for (int depth = 0; depth < 6 && t != null; depth++, t = t.parent)
            {
                LaunchPadFX[] found = t.GetComponentsInChildren<LaunchPadFX>(true);
                if (found.Length == 0) continue;
                for (int i = 0; i < found.Length; i++) Pads.Register(found[i].GetComponent<Collider>());
                return;
            }
            // Hiérarchie inhabituelle : une recherche globale, au plus toutes les 5 s.
            if (Time.realtimeSinceStartup - _lastGlobalPadSearch < 5f) return;
            _lastGlobalPadSearch = Time.realtimeSinceStartup;
            LaunchPadFX[] all = UnityEngine.Object.FindObjectsOfType<LaunchPadFX>();
            for (int i = 0; i < all.Length; i++) Pads.Register(all[i].GetComponent<Collider>());
        }

        public SurfaceKind ClassifyScenery(Collider c, out string siteKey)
        {
            siteKey = null;
            if (c == null) return SurfaceKind.Unknown;
            int id = c.GetInstanceID();
            if (_cache.TryGetValue(id, out Cached cached))
            {
                siteKey = cached.SiteKey;
                return cached.Kind;
            }
            if (_cache.Count > 8192) _cache.Clear(); // les quads PQ apparaissent et disparaissent avec le LOD du terrain
            cached = Compute(c);
            _cache[id] = cached;
            siteKey = cached.SiteKey;
            return cached.Kind;
        }

        private Cached Compute(Collider c)
        {
            var result = new Cached { Kind = SurfaceKind.Structure };
            string reason;
            if (c.CompareTag("LaunchpadFX") || c.GetComponent<LaunchPadFX>() != null)
            {
                result.Kind = SurfaceKind.LaunchPad;
                result.SiteKey = "LaunchPad";
                reason = "tag/composant LaunchpadFX";
                Pads.Register(c);
            }
            else if (c.GetComponent<PQ>() != null)
            {
                result.Kind = SurfaceKind.Terrain;
                reason = "quad PQ";
            }
            else if (IsParallaxTerrainCollider(c.transform))
            {
                result.Kind = SurfaceKind.Terrain;
                reason = "collider terrain Parallax";
            }
            else if (MatchHierarchy(c.transform, GeConfig.PadPatterns, out string padName))
            {
                result.Kind = SurfaceKind.LaunchPad;
                result.SiteKey = padName;
                reason = "motif pas de tir « " + padName + " »";
                FindPadNear(c);
            }
            else if (MatchHierarchy(c.transform, GeConfig.TerrainPatterns, out string terrainName))
            {
                result.Kind = SurfaceKind.Terrain;
                reason = "motif terrain « " + terrainName + " »";
            }
            else if (_kk.TryClassify(c.transform, out string kkSite))
            {
                result.Kind = kkSite != null ? SurfaceKind.LaunchPad : SurfaceKind.Structure;
                result.SiteKey = kkSite;
                reason = kkSite != null ? "site de lancement Kerbal Konstructs « " + kkSite + " »" : "static Kerbal Konstructs";
            }
            else
            {
                reason = "décor non reconnu";
            }

            if (result.Kind != SurfaceKind.Terrain || (reason != "quad PQ" && reason != "collider terrain Parallax"))
            {
                string path = HierarchyPath(c.transform);
                if (_logged.Add(path))
                    GeLog.Info("Surface : « " + path + " » (calque " + c.gameObject.layer + " " + LayerMask.LayerToName(c.gameObject.layer) + ") → " + result.Kind + " (" + reason + ")");
            }
            return result;
        }

        // Parallax Continued remplace les colliders PQ par des objets « ParallaxCollider(Clone) »
        // sous PQStorage. Exiger les deux indices évite de classer ses éventuels décors comme terrain.
        private static bool IsParallaxTerrainCollider(Transform t)
        {
            if (t == null || !(string.Equals(t.name, "ParallaxCollider", StringComparison.OrdinalIgnoreCase)
                || t.name.StartsWith("ParallaxCollider(", StringComparison.OrdinalIgnoreCase))) return false;
            for (int depth = 0; t.parent != null && depth < 12; depth++)
            {
                t = t.parent;
                if (string.Equals(t.name, "PQStorage", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool MatchHierarchy(Transform t, List<string> patterns, out string matchedName)
        {
            matchedName = null;
            if (patterns.Count == 0) return false;
            for (int depth = 0; t != null && depth < 12; depth++, t = t.parent)
            {
                string name = t.name;
                for (int i = 0; i < patterns.Count; i++)
                {
                    if (name.IndexOf(patterns[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matchedName = name;
                        return true;
                    }
                }
            }
            return false;
        }

        private static string HierarchyPath(Transform t)
        {
            var sb = new StringBuilder();
            for (int depth = 0; t != null && depth < 6; depth++, t = t.parent)
            {
                if (sb.Length > 0) sb.Insert(0, '/');
                sb.Insert(0, t.name);
            }
            return sb.ToString();
        }
    }
}
