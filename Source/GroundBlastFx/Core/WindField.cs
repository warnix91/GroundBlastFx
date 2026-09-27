using System;
using System.Collections.Generic;
using GroundBlastFx.Config;
using GroundBlastFx.Model;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Vent par corps (Configs/Bodies.cfg : windMin / windMax). Tiré une fois par session de jeu (graine fixée
    /// au premier appel), puis variation lente. Vecteur horizontal dans le repère local du point.
    /// </summary>
    public static class WindField
    {
        private static readonly Dictionary<string, WindModel> Models = new Dictionary<string, WindModel>(StringComparer.Ordinal);
        private static readonly int SessionSeed = Environment.TickCount;

        public static Vector3 At(CelestialBody body, Vector3 worldPoint)
        {
            if (body == null || !body.atmosphere) return Vector3.zero;
            if (!Models.TryGetValue(body.bodyName, out WindModel m))
            {
                BodyParams bp = GeConfig.Body(body.bodyName);
                m = new WindModel(SessionSeed ^ body.bodyName.GetHashCode(), bp.WindMinMs, bp.WindMaxMs);
                Models[body.bodyName] = m;
                GeLog.Info("Vent de session sur " + body.bodyName + " : " + m.BaseSpeedMs.ToString("0.0") + " m/s (plage " + bp.WindMinMs + "–" + bp.WindMaxMs + ")");
            }
            if (m.MaxSpeedMs <= 0f) return Vector3.zero;
            m.Sample(Planetarium.GetUniversalTime(), GeConfig.Physics.WindVariationPeriodS, out float speed, out float heading);
            SurfaceFrame.Basis(body, worldPoint, out Vector3 _, out Vector3 north, out Vector3 east);
            return (north * Mathf.Cos(heading) + east * Mathf.Sin(heading)) * speed;
        }

        /// <summary>Oublie les tirages (nouvelle session simulée, tests).</summary>
        public static void Reset() { Models.Clear(); }
    }
}
