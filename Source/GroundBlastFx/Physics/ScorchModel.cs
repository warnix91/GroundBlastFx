using System;

namespace GroundBlastFx.Model
{
    /// <summary>
    /// Accumulation des traces au sol. La « dose » intègre la pression d'impact dans le temps :
    /// un moteur qui reste longtemps au même endroit marque davantage. Strength = 1 − exp(−dose / ref).
    /// </summary>
    public static class ScorchModel
    {
        public static float AddDose(float dose, float impingementPressurePa, float activation01, float dt, PhysicsParams p)
        {
            if (dt <= 0f || activation01 <= 0f || impingementPressurePa <= 0f) return dose;
            float rate = impingementPressurePa / GeMath.Max(p.ScorchPressureRefPa, 1f);
            if (rate > 4f) rate = 4f; // un jet très concentré ne brûle pas instantanément
            return dose + rate * activation01 * dt;
        }

        public static float Strength(float dose, PhysicsParams p)
        {
            if (dose <= 0f) return 0f;
            return 1f - (float)Math.Exp(-dose / GeMath.Max(p.ScorchDoseRef, 0.01f));
        }

        /// <summary>Rayon d'une trace : brûlure ≈ tache d'impact en atmosphère, halo décapé bien plus large dans le vide.</summary>
        public static float Radius(bool vacuum, float impingementRadiusM, float frontRadiusM, PhysicsParams p)
        {
            // Halo du vide plafonné : autour des sites Apollo, la zone éclaircie visible fait ~100 m de rayon ;
            // au-delà, les grains fins partent loin mais ne changent plus l'aspect du sol.
            if (vacuum) return GeMath.Min(GeMath.Max(p.ScorchVacuumRadiusFactor * frontRadiusM, 3f * impingementRadiusM), 120f);
            return p.ScorchRadiusFactor * impingementRadiusM;
        }
    }
}
