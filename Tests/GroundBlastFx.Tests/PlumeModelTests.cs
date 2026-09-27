using GroundBlastFx.Model;

namespace GroundBlastFx.Tests
{
    public static class PlumeModelTests
    {
        private static readonly PhysicsParams P = new PhysicsParams();

        [Test]
        public static void ExhaustVelocityAndMassFlow()
        {
            Assert.Near(2765.475, PlumeModel.ExhaustVelocity(282f), 0.01, "v_e = Isp · g0");
            Assert.Near(307.36, PlumeModel.MassFlow(850e3f, 2765.475f), 0.05, "ṁ = F / v_e");
            Assert.Near(0, PlumeModel.MassFlow(850e3f, 0f), 0, "v_e nulle : débit nul, pas de division par zéro");
        }

        [Test]
        public static void HalfAngleFollowsAmbientPressure()
        {
            Assert.Near(10, PlumeModel.HalfAngleRad(101325f, P) * GeMath.Rad2Deg, 1e-3, "θ au niveau de la mer");
            Assert.Near(50, PlumeModel.HalfAngleRad(0f, P) * GeMath.Rad2Deg, 1e-3, "θ dans le vide");
            Assert.Near(30, PlumeModel.HalfAngleRad(50662.5f, P) * GeMath.Rad2Deg, 1e-3, "θ à mi-pression");
            Assert.Near(10, PlumeModel.HalfAngleRad(200000f, P) * GeMath.Rad2Deg, 1e-3, "θ borné au-delà d'une atmosphère");
        }

        [Test]
        public static void ImpingementRadiusAndPressure()
        {
            float theta = 10f * GeMath.Deg2Rad;
            float ri = PlumeModel.ImpingementRadius(0.46f, 20f, theta);
            Assert.Near(3.9866, ri, 1e-3, "r_i = r_e + h · tan θ");
            Assert.Near(0.46, PlumeModel.ImpingementRadius(0.46f, -3f, theta), 1e-6, "h négatif ramené à 0");
            Assert.Near(17024, PlumeModel.ImpingementPressure(850e3f, 1f, ri, P), 20, "p_s = F cos α / (π r_i²)");
            Assert.Near(P.ImpingementPressureMaxPa, PlumeModel.ImpingementPressure(70e6f, 1f, 0.01f, P), 1, "p_s borné");
            Assert.True(PlumeModel.ImpingementPressure(850e3f, 0.5f, ri, P) < PlumeModel.ImpingementPressure(850e3f, 1f, ri, P), "p_s baisse avec l'inclinaison");
        }

        [Test]
        public static void WallJetVelocityDecaysWithHeightAndRadius()
        {
            float ve = 2765.475f;
            Assert.Near(829.64, PlumeModel.WallJetVelocity(ve, 0f, 0.46f, P), 0.05, "u_i à h = 0 : k_u · v_e");
            Assert.Near(128.93, PlumeModel.WallJetVelocity(ve, 20f, 0.46f, P), 0.05, "u_i à h = 20 m");
            float prev = float.MaxValue;
            for (int h = 0; h <= 200; h += 5)
            {
                float u = PlumeModel.WallJetVelocity(ve, h, 0.46f, P);
                Assert.True(u < prev, "u_i strictement décroissante avec h (h=" + h + ")");
                prev = u;
            }
            Assert.Near(100, PlumeModel.WallJetVelocityAt(100f, 4f, 4f, P), 1e-4, "u(r_i) = u_i");
            Assert.Near(100 * 0.466516, PlumeModel.WallJetVelocityAt(100f, 4f, 8f, P), 1e-3, "u(2 r_i) = u_i · 0,5^1,1");
            Assert.Near(100, PlumeModel.WallJetVelocityAt(100f, 4f, 2f, P), 1e-4, "u(r < r_i) = u_i");
        }

        [Test]
        public static void GroupWallJetDependsOnThrust()
        {
            float ve = 3000f, near = 60f;
            float small = PlumeModel.GroupWallJetVelocity(45e3f, ve, 30f, 101325f, near, P);
            float medium = PlumeModel.GroupWallJetVelocity(1.24e6f, ve, 30f, 101325f, near, P);
            float big = PlumeModel.GroupWallJetVelocity(32e6f, ve, 30f, 101325f, near, P);
            Assert.True(small < medium && medium < big, "plus de poussée : souffle plus fort à hauteur égale");
            Assert.Near(3.5 * System.Math.Sqrt(1.24e6 / (101325.0 / (287.0 * 288.0))) / 100.0,
                        PlumeModel.GroupWallJetVelocity(1.24e6f, ve, 100f, 101325f, near, P), 0.1, "u = k √(F/ρ) / h");
            Assert.True(PlumeModel.GroupWallJetVelocity(1.24e6f, ve, 100f, 101325f, near, P)
                        < PlumeModel.GroupWallJetVelocity(1.24e6f, ve, 50f, 101325f, near, P), "décroît avec la hauteur");
            Assert.Near(P.WallJetKu * ve, PlumeModel.GroupWallJetVelocity(32e6f, ve, 2f, 101325f, near, P), 1e-3, "plafond près des tuyères");
            Assert.Near(near, PlumeModel.GroupWallJetVelocity(1.24e6f, ve, 30f, 0f, near, P), 1e-4, "vide : formule par tuyère");
        }

        [Test]
        public static void DustLiftAndSteam()
        {
            Assert.Near(0.712, PlumeModel.DustLift(129f, 40f, 0.8f, P), 1e-3, "levée S1 à 20 m");
            Assert.Near(0, PlumeModel.DustLift(30f, 40f, 0.8f, P), 0, "sous le seuil : pas de levée");
            Assert.Near(0.8, PlumeModel.DustLift(900f, 40f, 0.8f, P), 1e-6, "levée saturée × érodabilité");
            Assert.Near(0, PlumeModel.DustLift(900f, 40f, 0f, P), 0, "eau : érodabilité nulle");
            Assert.True(PlumeModel.SteamVisibility(300f, 0.8f, P) > 0.7f, "vapeur visible au-dessus de l'eau");
        }

        [Test]
        public static void ActivationHeightAndFade()
        {
            float hAct = PlumeModel.ActivationHeight(850e3f, 101325f, P);
            Assert.Near(131.196, hAct, 0.01, "H_act = 4,5 √(850 kN)");
            Assert.Near(9 * System.Math.Sqrt(15), PlumeModel.ActivationHeight(15e3f, 0f, P), 0.01, "H_act vide, 15 kN");
            Assert.Near(1, PlumeModel.ActivationFade(0f, hAct, P), 1e-6, "effet plein au sol");
            Assert.Near(1, PlumeModel.ActivationFade(0.6f * hAct, hAct, P), 1e-6, "effet plein à 0,6 H_act");
            Assert.Near(0, PlumeModel.ActivationFade(hAct, hAct, P), 1e-6, "aucun effet à H_act");
            Assert.Near(0, PlumeModel.ActivationFade(hAct * 2f, hAct, P), 0, "aucun effet au-dessus");
            float prev = 2f;
            for (float h = 0; h < hAct * 1.2f; h += 1f)
            {
                float f = PlumeModel.ActivationFade(h, hAct, P);
                Assert.True(f <= prev + 1e-6f, "fondu monotone");
                prev = f;
            }
            Assert.Near(0, PlumeModel.ActivationFade(1f, 0f, P), 0, "poussée nulle : aucun effet");
        }

        [Test]
        public static void MaxCloudRadiusAndExitRadius()
        {
            Assert.Near(87.464, PlumeModel.MaxCloudRadius(850e3f, P), 0.01, "R_max S1");
            Assert.Near(261.53, PlumeModel.MaxCloudRadius(7.6e6f, P), 0.05, "R_max S3");
            Assert.Near(793.73, PlumeModel.MaxCloudRadius(70e6f, P), 0.1, "R_max S4");
            Assert.Near(P.CloudMaxRadiusMinM, PlumeModel.MaxCloudRadius(100f, P), 1e-6, "R_max minimal");
            Assert.Near(0.5, PlumeModel.EstimateExitRadius(1.25f, 1, P), 1e-6, "r_e = 0,4 × diamètre");
            Assert.Near(0.5 / System.Math.Sqrt(9), PlumeModel.EstimateExitRadius(1.25f, 9, P), 1e-6, "9 tuyères dans une pièce");
            Assert.Near(P.NozzleExitRadiusMaxM, PlumeModel.EstimateExitRadius(50f, 1, P), 1e-6, "r_e borné");
        }

        [Test]
        public static void EvaluateMatchesIndividualFunctions()
        {
            var input = new JetInput { ThrustN = 850e3f, ExhaustVelocityMs = 2765.475f, ExitRadiusM = 0.46f, StandoffM = 20f, CosAlpha = 1f, AmbientPressurePa = 101325f };
            PlumeModel.Evaluate(ref input, P, out JetImpingement r);
            Assert.Near(3.9866, r.ImpingementRadiusM, 1e-3, "r_i");
            Assert.Near(128.93, r.WallJetVelocityMs, 0.05, "u_i");
            Assert.Near(131.196, r.ActivationHeightM, 0.01, "H_act");
            Assert.Near(1, r.ActivationFade01, 1e-6, "fondu à 20 m");
            Assert.Near(307.36, r.MassFlowKgS, 0.05, "ṁ");
        }

        [Test]
        public static void FlameLight()
        {
            Assert.Near(1, PlumeModel.FlameLightIntensity(1e6f, 1f, P), 1e-5, "1 MN kérolox ≈ 1");
            Assert.Near(0, PlumeModel.FlameLightIntensity(0f, 1f, P), 0, "moteur coupé");
            Assert.True(PlumeModel.FlameLightIntensity(7.6e6f, 1f, P) > PlumeModel.FlameLightIntensity(850e3f, 1f, P), "croît avec la poussée");
        }
    }
}
