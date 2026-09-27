using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Core;
using UnityEngine;

namespace GroundBlastFx.UI
{
    /// <summary>
    /// Réglage en jeu de la tranchée de déflexion d'un pas de tir.
    /// Une flèche est dessinée au sol (overlay) ; les boutons ± tournent le cap ; « Enregistrer » écrit
    /// PluginData/LaunchSites_user.cfg. Le cap est appliqué en direct aux foyers (aperçu) ; « Annuler » restaure.
    /// </summary>
    public sealed class TrenchTool
    {
        private readonly GroundBlastFxAddon _addon;
        private float _savedHeading = -1f;

        public TrenchTool(GroundBlastFxAddon addon) { _addon = addon; }

        public bool Active { get; private set; }
        public string SiteKey { get; private set; }
        public float HeadingDeg { get; private set; } = -1f;
        public Vector3 CenterWorld { get; private set; }
        public CelestialBody Body { get; private set; }
        public string Message { get; private set; }

        public void Begin()
        {
            Message = null;
            if (!FindSite(out string site, out Vector3 center))
            {
                Message = GeStrings.Get("trenchNoPad");
                Active = false;
                return;
            }
            SiteKey = site;
            CenterWorld = center;
            Body = FlightGlobals.currentMainBody;
            HeadingDeg = GeConfig.TrenchHeading(site, GeConfig.FindLaunchSite(site));
            _savedHeading = HeadingDeg;
            Active = true;
        }

        public void Cancel()
        {
            if (Active && SiteKey != null) GeConfig.SetUserTrench(SiteKey, _savedHeading);
            Active = false;
        }

        public void Rotate(float deltaDeg)
        {
            if (!Active) return;
            float h = HeadingDeg < 0f ? 0f : HeadingDeg;
            h = Mathf.Repeat(h + deltaDeg, 360f);
            HeadingDeg = h;
            GeConfig.SetUserTrench(SiteKey, h);
        }

        public void SetRadial()
        {
            if (!Active) return;
            HeadingDeg = -1f;
            GeConfig.SetUserTrench(SiteKey, -1f);
        }

        public void Save()
        {
            if (!Active) return;
            GeConfig.SetUserTrench(SiteKey, HeadingDeg);
            Message = GeConfig.SaveUserLaunchSites() ? GeStrings.Get("trenchSaved") : GeStrings.Get("trenchSaveFailed");
            _savedHeading = HeadingDeg;
        }

        /// <summary>Met à jour le centre (le vaisseau peut bouger, l'origine flottante aussi).</summary>
        public void Refresh()
        {
            if (!Active) return;
            if (FindSite(out string site, out Vector3 center) && site == SiteKey) CenterWorld = center;
        }

        private bool FindSite(out string site, out Vector3 center)
        {
            site = null;
            center = Vector3.zero;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != null && _addon.Engines.IsPadLatched(v, out site))
            {
                center = v.transform.position - (v.transform.position - v.mainBody.position).normalized * (float)System.Math.Max(v.heightFromTerrain, 0.0);
                if (site == null) site = "LaunchPad";
                return true;
            }
            // Pas de vaisseau verrouillé : premier foyer « pas de tir » actif (démo S3/S4 comprise).
            ImpingementCluster[] cl = _addon.Clusters;
            for (int i = 0; i < _addon.ClusterCount; i++)
            {
                if (cl[i].Surface != SurfaceKind.LaunchPad) continue;
                ClusterState s = FindState(cl[i].Id);
                site = s != null && s.SiteKey != null ? s.SiteKey : "LaunchPad";
                center = cl[i].ImpactPointWorld;
                return true;
            }
            // Posé sur un site connu (landedAt).
            if (v != null && v.Landed && !string.IsNullOrEmpty(v.landedAt) && GeConfig.FindLaunchSite(v.landedAt) != null)
            {
                site = v.landedAt;
                center = v.transform.position;
                return true;
            }
            return false;
        }

        private ClusterState FindState(int id)
        {
            ClusterTracker t = _addon.Tracker;
            for (int i = 0; i < t.Capacity; i++) if (t[i].InUse && t[i].Id == id) return t[i];
            return null;
        }
    }
}
