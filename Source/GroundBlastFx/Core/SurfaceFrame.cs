using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Conversions entre l'espace monde Unity (origine flottante, Krakensbane) et le repère du corps céleste.
    /// Tout ce qui est fixé au sol est stocké en latitude / longitude / altitude et en directions « corps »,
    /// puis reconverti à chaque frame.
    /// </summary>
    public static class SurfaceFrame
    {
        /// <summary>Direction monde → repère du corps (tourne avec la planète). Même convention que CelestialBody.GetRelSurfaceDirection.</summary>
        public static Vector3d ToBody(CelestialBody body, Vector3 worldDir)
        {
            return body.GetRelSurfaceDirection(worldDir);
        }

        /// <summary>Direction repère du corps → monde (inverse de ToBody).</summary>
        public static Vector3 FromBody(CelestialBody body, Vector3d bodyDir)
        {
            return (Vector3)body.BodyFrame.LocalToWorld(bodyDir.xzy).xzy;
        }

        public static Vector3 WorldPosition(CelestialBody body, double lat, double lon, double alt)
        {
            return (Vector3)body.GetWorldSurfacePosition(lat, lon, alt);
        }

        /// <summary>Repère local (haut, nord, est) en un point, comme Vessel.upAxis/north/east.</summary>
        public static void Basis(CelestialBody body, Vector3 worldPoint, out Vector3 up, out Vector3 north, out Vector3 east)
        {
            Vector3d c = body.position;
            Vector3d p = worldPoint;
            Vector3d upd = (p - c).normalized;
            Vector3d pole = c + (Vector3d)body.transform.up * body.Radius;
            Vector3d northd = Vector3d.Exclude(upd, pole - p).normalized;
            Vector3d eastd = body.getRFrmVel(p);
            if (eastd.sqrMagnitude > 1e-12) eastd = eastd.normalized;
            else eastd = Vector3d.Cross(upd, northd).normalized;
            up = upd;
            north = northd;
            east = eastd;
        }

        /// <summary>Direction horizontale correspondant à un cap (degrés depuis le nord, sens horaire).</summary>
        public static Vector3 HeadingDirection(CelestialBody body, Vector3 worldPoint, float headingDeg)
        {
            Basis(body, worldPoint, out Vector3 _, out Vector3 north, out Vector3 east);
            float a = headingDeg * Mathf.Deg2Rad;
            return (north * Mathf.Cos(a) + east * Mathf.Sin(a)).normalized;
        }

        /// <summary>Cap (degrés 0..360 depuis le nord) d'une direction monde en un point.</summary>
        public static float HeadingOf(CelestialBody body, Vector3 worldPoint, Vector3 dir)
        {
            Basis(body, worldPoint, out Vector3 _, out Vector3 north, out Vector3 east);
            float h = Mathf.Atan2(Vector3.Dot(dir, east), Vector3.Dot(dir, north)) * Mathf.Rad2Deg;
            return h < 0f ? h + 360f : h;
        }
    }
}
