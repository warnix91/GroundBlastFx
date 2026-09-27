using System;

namespace GroundBlastFx.Model
{
    /// <summary>
    /// Front du nuage R_front d'un foyer, intégré dans le temps.
    /// Pendant la poussée : dR/dt = k · u(R) = k · u_i · (r_i / R)^n, plafonné à R_max.
    /// Intégration exacte pour u_i et r_i constants sur le pas : d(R^(n+1))/dt = (n+1) · k · u_i · r_i^n.
    /// Après la coupure : le front décélère (traînée), s'étale lentement, puis le nuage se dissipe.
    /// </summary>
    public static class CloudFrontModel
    {
        public static float AdvanceActive(float frontRadiusM, float impingementRadiusM, float wallJetVelocityMs,
                                          float maxRadiusM, float dt, PhysicsParams p)
        {
            float ri = GeMath.Max(impingementRadiusM, 0.01f);
            float r = GeMath.Max(frontRadiusM, ri);
            if (dt <= 0f || wallJetVelocityMs <= 0f) return r;
            // R_max peut baisser si la poussée baisse : le nuage déjà formé ne recule pas.
            if (r >= maxRadiusM) return r;
            double n = p.WallJetDecayExponent;
            double rn1 = Math.Pow(r, n + 1.0) + (n + 1.0) * p.CloudFrontSpeedK * wallJetVelocityMs * Math.Pow(ri, n) * dt;
            float next = (float)Math.Pow(rn1, 1.0 / (n + 1.0));
            return GeMath.Min(next, maxRadiusM);
        }

        /// <summary>Vitesse du front pendant la poussée (utile au moment de la coupure).</summary>
        public static float FrontSpeed(float frontRadiusM, float impingementRadiusM, float wallJetVelocityMs, float maxRadiusM, PhysicsParams p)
        {
            if (frontRadiusM >= maxRadiusM) return p.PostCutoffSpreadMs;
            return p.CloudFrontSpeedK * PlumeModel.WallJetVelocityAt(wallJetVelocityMs, impingementRadiusM, frontRadiusM, p);
        }

        /// <summary>Après coupure : v(t) = v0 · e^(−t/τ) + étalement résiduel ; R ≤ 1,5 · R_max.</summary>
        public static float AdvanceAfterCutoff(float frontRadiusM, float speedAtCutoffMs, float timeSinceCutoffS,
                                               float maxRadiusM, float dt, PhysicsParams p)
        {
            if (dt <= 0f) return frontRadiusM;
            float tau = GeMath.Max(p.PostCutoffFrontDecayS, 0.05f);
            float v = speedAtCutoffMs * (float)Math.Exp(-timeSinceCutoffS / tau) + p.PostCutoffSpreadMs;
            float cap = GeMath.Max(maxRadiusM * p.PostCutoffMaxGrowth, frontRadiusM);
            return GeMath.Min(frontRadiusM + v * dt, cap);
        }

        /// <summary>Durée de dissipation : 20 à 90 s selon la taille du nuage à la coupure.</summary>
        public static float DissipationTime(float frontRadiusAtCutoffM, PhysicsParams p)
        {
            float t = GeMath.Clamp01(frontRadiusAtCutoffM / GeMath.Max(p.DissipationRefRadiusM, 1f));
            return GeMath.Lerp(p.DissipationMinS, p.DissipationMaxS, t);
        }

        /// <summary>Facteur de fondu après coupure (1 → 0), plateau au début puis décroissance lisse.</summary>
        public static float CutoffFade(float timeSinceCutoffS, float dissipationTimeS)
        {
            if (dissipationTimeS <= 0f) return 0f;
            return 1f - GeMath.SmoothStep(0.15f * dissipationTimeS, dissipationTimeS, timeSinceCutoffS);
        }

        /// <summary>Fondu d'allumage 0 → 1.</summary>
        public static float IgnitionFade(float timeSinceIgnitionS, PhysicsParams p)
        {
            return GeMath.SmoothStep(0f, GeMath.Max(p.IgnitionFadeS, 0.01f), timeSinceIgnitionS);
        }

        /// <summary>Fondu de la nappe d'éjectas dans le vide à la coupure (arrêt net mais sans saut d'une frame).</summary>
        public static float VacuumCutoffFade(float timeSinceCutoffS, PhysicsParams p)
        {
            return 1f - GeMath.SmoothStep(0f, GeMath.Max(p.VacuumCutoffFadeS, 0.01f), timeSinceCutoffS);
        }
    }
}
