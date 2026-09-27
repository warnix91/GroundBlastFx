using System;
using System.Collections.Generic;
using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Model;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Mode Démo : foyers virtuels (IsDemo = true) sous ou à côté du vaisseau actif, pendant 30 s,
    /// sans moteur réel. Les tuyères virtuelles passent par le même pipeline que les vrais moteurs (sondage, modèle,
    /// fusion, suivi), avec les profils des scénarios S1, S2, S3, S4, S5 et S7 (§4.4).
    /// </summary>
    public sealed class DemoDirector
    {
        private sealed class Run
        {
            public DemoScenario Scenario;
            public float T;
            public CelestialBody Body;
            public double Lat, Lon, Alt;
            public float HeadingDeg;      // orientation de la disposition des tuyères et de la dérive latérale
            public int Key;
            public PropellantLight Light;
            public string SiteKey;
            public bool PadLatched;
        }

        private const int MaxRuns = 4;
        private readonly List<Run> _runs = new List<Run>(MaxRuns);
        private int _nextKey;

        public int RunningCount => _runs.Count;

        public string Describe()
        {
            if (_runs.Count == 0) return "-";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < _runs.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_runs[i].Scenario.Kind).Append(" t=").Append(_runs[i].T.ToString("0.0")).Append(" s");
            }
            return sb.ToString();
        }

        public void StopAll() { _runs.Clear(); }

        /// <returns>null si la démo a démarré, sinon la raison de l'échec (texte pour l'UI).</returns>
        public string Start(DemoKind kind, Vessel active, int sceneryMask, string siteKeyHint)
        {
            if (active == null || active.mainBody == null) return "aucun vaisseau actif";
            if (_runs.Count >= MaxRuns) _runs.RemoveAt(0);
            DemoScenario sc = DemoProfiles.Create(kind);
            CelestialBody body = active.mainBody;

            // Placement : à côté du vaisseau s'il est posé ou très bas (sinon il masquerait l'effet), sinon dessous.
            Vector3 up = (active.transform.position - body.position).normalized;
            double hTerrain = active.heightFromTerrain > 0 ? active.heightFromTerrain : active.altitude;
            bool beside = active.Landed || active.Splashed || active.situation == Vessel.Situations.PRELAUNCH || hTerrain < 40.0;
            float maxCloud = PlumeModel.MaxCloudRadius(sc.ThrustPerEngineN * sc.EngineCount, GeConfig.Physics);
            Vector3 side = Vector3.zero;
            if (beside)
            {
                // Devant la caméra (au-delà du vaisseau) et un peu sur le côté : l'effet est dans le champ sans être masqué.
                Transform camT = FlightCamera.fetch != null ? FlightCamera.fetch.transform : null;
                Vector3 fwd = camT != null ? Vector3.ProjectOnPlane(camT.forward, up) : Vector3.zero;
                Vector3 right = camT != null ? Vector3.ProjectOnPlane(camT.right, up) : Vector3.zero;
                side = fwd.normalized + 0.6f * right.normalized;
                if (side.sqrMagnitude < 1e-4f) side = Vector3.ProjectOnPlane(Vector3.right, up);
                side = side.normalized * Mathf.Clamp(0.35f * maxCloud + 3f * sc.NozzleSpanM + 25f, 40f, 250f);
            }
            Vector3 probeFrom = active.transform.position + side + up * 1500f;
            Vector3 ground;
            if (Physics.Raycast(probeFrom, -up, out RaycastHit hit, 5000f, sceneryMask, QueryTriggerInteraction.Ignore)) ground = hit.point;
            else
            {
                body.GetLatLonAlt(active.transform.position + side, out double la, out double lo, out double _);
                double terrain = body.pqsController != null ? body.TerrainAltitude(la, lo, false) : 0.0;
                if (body.ocean && terrain < 0.0) terrain = 0.0;
                ground = (Vector3)body.GetWorldSurfacePosition(la, lo, terrain);
            }
            if (body.ocean)
            {
                // Au-dessus de l'eau : le niveau de la mer est la surface.
                double altGround = FlightGlobals.getAltitudeAtPos((Vector3d)ground, body);
                if (altGround < 0.0) ground -= up * (float)altGround;
            }

            var run = new Run
            {
                Scenario = sc,
                T = 0f,
                Body = body,
                Key = 1000 + (_nextKey++ % 1000),
                Light = FindLight(sc.Propellant),
                HeadingDeg = UnityEngine.Random.Range(0f, 360f),
            };
            body.GetLatLonAlt(ground, out run.Lat, out run.Lon, out run.Alt);
            if (sc.Surface == DemoSurface.LaunchPad)
            {
                run.PadLatched = true;
                run.SiteKey = siteKeyHint ?? "LaunchPad";
            }
            _runs.Add(run);
            GeLog.Info("Démo " + kind + " lancée sur " + body.bodyName + " à " + run.Lat.ToString("0.0000") + ", " + run.Lon.ToString("0.0000")
                       + (beside ? " (à côté du vaisseau)" : " (sous le vaisseau)") + ", " + sc.EngineCount + " moteur(s), "
                       + (sc.ThrustPerEngineN * sc.EngineCount / 1000f).ToString("0") + " kN");
            return null;
        }

        private static PropellantLight FindLight(DemoPropellant family)
        {
            string wanted = family == DemoPropellant.Kerolox ? "Kerosene" : (family == DemoPropellant.Methalox ? "LqdMethane" : "Aerozine50");
            List<PropellantLight> lights = GeConfig.Propellants;
            for (int i = 0; i < lights.Count; i++)
                if (wanted.IndexOf(lights[i].Match, StringComparison.OrdinalIgnoreCase) >= 0 || lights[i].Match.IndexOf(wanted, StringComparison.OrdinalIgnoreCase) >= 0)
                    return lights[i];
            return GeConfig.DefaultPropellant;
        }

        public void Update(float dt)
        {
            for (int i = _runs.Count - 1; i >= 0; i--)
            {
                Run r = _runs[i];
                r.T += dt;
                if (r.T > r.Scenario.DurationS + 0.5f) _runs.RemoveAt(i); // le foyer continue sa dissipation dans le tracker
            }
        }

        public void Collect(JetBuffer jets)
        {
            for (int i = 0; i < _runs.Count; i++)
            {
                Run r = _runs[i];
                DemoScenario sc = r.Scenario;
                DemoProfiles.Evaluate(sc, r.T, out DemoState st);
                if (!st.EnginesOn || r.Body == null) continue;
                Vector3 ground = SurfaceFrame.WorldPosition(r.Body, r.Lat, r.Lon, r.Alt);
                SurfaceFrame.Basis(r.Body, ground, out Vector3 up, out Vector3 north, out Vector3 east);
                float a = r.HeadingDeg * Mathf.Deg2Rad;
                Vector3 u = north * Mathf.Cos(a) + east * Mathf.Sin(a);
                Vector3 v = Vector3.Cross(up, u);
                Vector3 center = ground + up * st.StandoffM + u * st.LateralOffsetM;
                float ve = PlumeModel.ExhaustVelocity(sc.IspS);
                float thrust = sc.ThrustPerEngineN * st.Throttle01;
                SurfaceKind forced = MapSurface(sc.Surface);
                for (int k = 0; k < sc.EngineCount; k++)
                {
                    var s = new JetSample
                    {
                        NozzleWorld = center + u * sc.OffsetX[k] + v * sc.OffsetY[k],
                        AxisWorld = -up,
                        ThrustN = thrust,
                        ExhaustVelocityMs = ve,
                        ExitRadiusM = sc.ExitRadiusM,
                        FlameColor = r.Light.Color,
                        FlameIntensity = r.Light.Intensity,
                        SteamBonus = r.Light.SteamBonus,
                        Soot = r.Light.Soot,
                        ProbeLengthM = st.StandoffM + 60f,
                        AmbientPressureAtNozzlePa = sc.ForceVacuum || !r.Body.atmosphere ? 0f : (float)(r.Body.GetPressure(Math.Max(r.Alt + st.StandoffM, 0.0)) * 1000.0),
                        Vessel = null,
                        Part = null,
                        Key = r.Key,
                        IsDemo = true,
                        ForcedSurface = forced,
                        ForceVacuum = sc.ForceVacuum,
                        ForcedGravityMs2 = sc.ForcedGravityMs2,
                        PadLatched = r.PadLatched,
                        SiteKey = r.SiteKey,
                    };
                    jets.Add(ref s);
                }
            }
        }

        private static SurfaceKind MapSurface(DemoSurface s)
        {
            switch (s)
            {
                case DemoSurface.Terrain: return SurfaceKind.Terrain;
                case DemoSurface.LaunchPad: return SurfaceKind.LaunchPad;
                case DemoSurface.Water: return SurfaceKind.Water;
                case DemoSurface.VesselDeck: return SurfaceKind.VesselDeck;
                default: return SurfaceKind.Unknown;
            }
        }

        /// <summary>Positions des démos en cours (overlay : étiquettes).</summary>
        public int GetLabels(Vector3[] positions, string[] names)
        {
            int n = 0;
            for (int i = 0; i < _runs.Count && n < positions.Length; i++)
            {
                Run r = _runs[i];
                if (r.Body == null) continue;
                positions[n] = SurfaceFrame.WorldPosition(r.Body, r.Lat, r.Lon, r.Alt);
                names[n] = r.Scenario.Kind.ToString();
                n++;
            }
            return n;
        }
    }
}
