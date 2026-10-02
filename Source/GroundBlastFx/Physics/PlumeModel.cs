using System;

namespace GroundBlastFx.Model
{
    /// <summary>
    /// Modèle d'impact jet/surface par moteur. Fonctions pures, sans allocation.
    /// </summary>
    public static class PlumeModel
    {
        /// <summary>v_e = Isp · g0.</summary>
        public static float ExhaustVelocity(float ispS)
        {
            return ispS > 0f ? ispS * GeMath.G0 : 0f;
        }

        /// <summary>ṁ = F / v_e.</summary>
        public static float MassFlow(float thrustN, float exhaustVelocityMs)
        {
            return exhaustVelocityMs > 1f ? thrustN / exhaustVelocityMs : 0f;
        }

        /// <summary>Part de vide : 0 au niveau de la mer, 1 dans le vide.</summary>
        public static float VacuumFactor(float ambientPressurePa)
        {
            return 1f - GeMath.Clamp01(ambientPressurePa / GeMath.SeaLevelPressurePa);
        }

        /// <summary>θ = lerp(θ_mer, θ_vide, 1 − clamp01(p_amb / 101 325)), en radians.</summary>
        public static float HalfAngleRad(float ambientPressurePa, PhysicsParams p)
        {
            float deg = GeMath.Lerp(p.PlumeHalfAngleSeaLevelDeg, p.PlumeHalfAngleVacuumDeg, VacuumFactor(ambientPressurePa));
            return deg * GeMath.Deg2Rad;
        }

        /// <summary>r_i = r_e + h · tan θ.</summary>
        public static float ImpingementRadius(float exitRadiusM, float standoffM, float halfAngleRad)
        {
            if (standoffM < 0f) standoffM = 0f;
            return exitRadiusM + standoffM * (float)Math.Tan(halfAngleRad);
        }

        /// <summary>p_s ≈ F · cos α / (π · r_i²), borné.</summary>
        public static float ImpingementPressure(float thrustN, float cosAlpha, float impingementRadiusM, PhysicsParams p)
        {
            float r = GeMath.Max(impingementRadiusM, p.ImpingementPressureMinRadiusM);
            float ps = thrustN * GeMath.Clamp01(cosAlpha) / ((float)Math.PI * r * r);
            return GeMath.Clamp(ps, 0f, p.ImpingementPressureMaxPa);
        }

        /// <summary>u_i ≈ k_u · v_e / (1 + h / (k_h · r_e)).</summary>
        public static float WallJetVelocity(float exhaustVelocityMs, float standoffM, float exitRadiusM, PhysicsParams p)
        {
            float re = GeMath.Max(exitRadiusM, 0.01f);
            float h = GeMath.Max(standoffM, 0f);
            return p.WallJetKu * exhaustVelocityMs / (1f + h / (p.WallJetKh * re));
        }

        /// <summary>
        /// Souffle au sol d'un foyer entier (tous ses moteurs). En atmosphère, loin des tuyères, un jet turbulent perd sa
        /// vitesse selon sa quantité de mouvement, c'est-à-dire selon sa POUSSÉE : u ≈ k_m · √(F / ρ_air) / h. Un gros
        /// lanceur souffle donc fort de bien plus haut qu'un petit moteur (neuf moteurs soufflent plus qu'un seul).
        /// Plafond près des tuyères : k_u · v_e. Dans le vide (jet libre, sans air) : formule par tuyère.
        /// </summary>
        public static float GroupWallJetVelocity(float thrustN, float exhaustVelocityMs, float standoffM, float ambientPressurePa,
                                                 float nozzleFormulaMs, PhysicsParams p)
        {
            float vac = VacuumFactor(ambientPressurePa);
            if (thrustN <= 0f || vac >= 0.999f) return nozzleFormulaMs;
            float rho = GeMath.Max(ambientPressurePa / (287f * 288f), 0.02f);   // masse volumique de l'air (≈ 15 °C)
            float far = p.WallJetMomentumK * (float)Math.Sqrt(thrustN / rho) / GeMath.Max(standoffM, 1f);
            float atm = GeMath.Min(far, p.WallJetKu * GeMath.Max(exhaustVelocityMs, 1f));
            return GeMath.Lerp(atm, nozzleFormulaMs, vac * vac);
        }

        /// <summary>u(r) ≈ u_i · (r_i / r)^n pour r &gt; r_i, u_i sinon.</summary>
        public static float WallJetVelocityAt(float wallJetVelocityMs, float impingementRadiusM, float radiusM, PhysicsParams p)
        {
            if (radiusM <= impingementRadiusM || radiusM <= 0f) return wallJetVelocityMs;
            return wallJetVelocityMs * (float)Math.Pow(impingementRadiusM / radiusM, p.WallJetDecayExponent);
        }

        /// <summary>levée = clamp01((u_i − u_seuil) / u_ref) × érodabilité.</summary>
        public static float DustLift(float wallJetVelocityMs, float thresholdVelocityMs, float erodibility01, PhysicsParams p)
        {
            float uref = GeMath.Max(p.LiftReferenceVelocityMs, 1f);
            return GeMath.Clamp01((wallJetVelocityMs - thresholdVelocityMs) / uref) * GeMath.Clamp01(erodibility01);
        }

        /// <summary>Visibilité de la vapeur / du souffle chaud (eau, pas de tir, pont) : ne dépend pas de l'érodabilité.</summary>
        public static float SteamVisibility(float wallJetVelocityMs, float steamFraction01, PhysicsParams p)
        {
            float uref = GeMath.Max(p.LiftReferenceVelocityMs, 1f);
            return GeMath.Clamp01((wallJetVelocityMs - p.SteamVisibilityThresholdMs) / uref) * GeMath.Clamp01(steamFraction01);
        }

        /// <summary>H_act = k_act · √(F en kN), k_act interpolé entre atmosphère et vide.</summary>
        public static float ActivationHeight(float thrustN, float ambientPressurePa, PhysicsParams p)
        {
            if (thrustN <= 0f) return 0f;
            float k = GeMath.Lerp(p.ActivationK, p.ActivationKVacuum, VacuumFactor(ambientPressurePa));
            return k * (float)Math.Sqrt(thrustN / 1000f);
        }

        /// <summary>0 au-dessus de H_act, 1 sous 0,6 · H_act, fondu lisse entre les deux.</summary>
        public static float ActivationFade(float standoffM, float activationHeightM, PhysicsParams p)
        {
            if (activationHeightM <= 0f) return 0f;
            return 1f - GeMath.SmoothStep(p.ActivationFullFraction * activationHeightM, activationHeightM, standoffM);
        }

        /// <summary>R_max = k_R · √(F en kN).</summary>
        public static float MaxCloudRadius(float thrustN, PhysicsParams p)
        {
            if (thrustN <= 0f) return p.CloudMaxRadiusMinM;
            return GeMath.Max(p.CloudMaxRadiusMinM, p.CloudMaxRadiusK * (float)Math.Sqrt(thrustN / 1000f));
        }

        /// <summary>Rayon de sortie estimé depuis le diamètre de la pièce, réparti sur plusieurs tuyères (à surface égale).</summary>
        public static float EstimateExitRadius(float partDiameterM, int nozzleCount, PhysicsParams p)
        {
            if (nozzleCount < 1) nozzleCount = 1;
            float r = p.NozzleExitRadiusFactor * partDiameterM / (float)Math.Sqrt(nozzleCount);
            return GeMath.Clamp(r, p.NozzleExitRadiusMinM, p.NozzleExitRadiusMaxM);
        }

        /// <summary>Intensité relative de la lumière de flamme au sol (sans unité, ~1 pour 1 MN).</summary>
        public static float FlameLightIntensity(float thrustN, float propellantIntensity, PhysicsParams p)
        {
            if (thrustN <= 0f) return 0f;
            return p.FlameLightK * (float)Math.Sqrt(thrustN / 1.0e6f) * GeMath.Max(propellantIntensity, 0f);
        }

        /// <summary>
        /// Calcule en une fois l'impact d'un jet. <paramref name="result"/> est une structure : aucune allocation.
        /// </summary>
        public static void Evaluate(ref JetInput input, PhysicsParams p, out JetImpingement result)
        {
            result.ExhaustVelocityMs = input.ExhaustVelocityMs;
            result.MassFlowKgS = MassFlow(input.ThrustN, input.ExhaustVelocityMs);
            result.HalfAngleRad = HalfAngleRad(input.AmbientPressurePa, p);
            result.ImpingementRadiusM = ImpingementRadius(input.ExitRadiusM, input.StandoffM, result.HalfAngleRad);
            result.ImpingementPressurePa = ImpingementPressure(input.ThrustN, input.CosAlpha, result.ImpingementRadiusM, p);
            result.WallJetVelocityMs = WallJetVelocity(input.ExhaustVelocityMs, input.StandoffM, input.ExitRadiusM, p);
            result.ActivationHeightM = ActivationHeight(input.ThrustN, input.AmbientPressurePa, p);
            result.ActivationFade01 = ActivationFade(input.StandoffM, result.ActivationHeightM, p);
        }
    }

    /// <summary>Entrée du modèle pour un jet (un thrustTransform).</summary>
    public struct JetInput
    {
        public float ThrustN;
        public float ExhaustVelocityMs;
        public float ExitRadiusM;
        public float StandoffM;
        public float CosAlpha;
        public float AmbientPressurePa;
    }

    /// <summary>Résultat du modèle pour un jet.</summary>
    public struct JetImpingement
    {
        public float ExhaustVelocityMs;
        public float MassFlowKgS;
        public float HalfAngleRad;
        public float ImpingementRadiusM;
        public float ImpingementPressurePa;
        public float WallJetVelocityMs;
        public float ActivationHeightM;
        public float ActivationFade01;
    }
}
