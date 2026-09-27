using System;

namespace GroundBlastFx.Model
{
    /// <summary>
    /// Éjectas dans le vide. Nappe rasante (1 à 3° au-dessus de l'horizon),
    /// trajectoires balistiques sans traînée, ~100 m/s pour les grains grossiers jusqu'au km/s pour les fins.
    /// Le Core fournit une vitesse caractéristique et un angle ; le Rendering répartit les vitesses autour.
    /// </summary>
    public static class VacuumEjectaModel
    {
        /// <summary>Vitesse caractéristique des éjectas : k · u_i, bornée [100 ; 2000] m/s par défaut.</summary>
        public static float CharacteristicSpeed(float wallJetVelocityMs, PhysicsParams p)
        {
            return GeMath.Clamp(p.VacuumEjectaSpeedK * wallJetVelocityMs, p.VacuumEjectaSpeedMinMs, p.VacuumEjectaSpeedMaxMs);
        }

        /// <summary>
        /// Élévation de la nappe. Plus la tuyère est basse, plus la nappe est rasante (le jet pariétal colle au sol).
        /// Angle nominal à h = 5 · r_e, de 0,5× à 1,5× le nominal, borné [1 ; 3]°.
        /// </summary>
        public static float SheetAngleDeg(float standoffM, float exitRadiusM, PhysicsParams p)
        {
            float t = GeMath.Clamp01(standoffM / GeMath.Max(10f * exitRadiusM, 0.1f));
            float a = p.VacuumEjectaAngleDeg * GeMath.Lerp(0.5f, 1.5f, t);
            return GeMath.Clamp(a, p.VacuumEjectaAngleMinDeg, p.VacuumEjectaAngleMaxDeg);
        }

        /// <summary>Portée balistique sans traînée sur sol plat : v² · sin(2θ) / g.</summary>
        public static float BallisticRange(float speedMs, float angleDeg, float gravityMs2)
        {
            if (gravityMs2 <= 0f) return float.PositiveInfinity;
            return speedMs * speedMs * (float)Math.Sin(2f * angleDeg * GeMath.Deg2Rad) / gravityMs2;
        }

        /// <summary>Temps de vol balistique : 2 · v · sin θ / g.</summary>
        public static float FlightTime(float speedMs, float angleDeg, float gravityMs2)
        {
            if (gravityMs2 <= 0f) return float.PositiveInfinity;
            return 2f * speedMs * (float)Math.Sin(angleDeg * GeMath.Deg2Rad) / gravityMs2;
        }
    }
}
