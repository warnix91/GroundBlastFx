using System;
using GroundBlastFx.Model;

namespace GroundBlastFx.Tests
{
    public static class CloudFrontTests
    {
        private static readonly PhysicsParams P = new PhysicsParams();

        [Test]
        public static void ActiveFrontGrowsMonotonicallyAndIsCapped()
        {
            float ri = 1.342f, ui = 351.6f, rmax = 87.46f;
            float r = ri, prev = 0f;
            for (int i = 0; i < 60 * 60; i++)
            {
                r = CloudFrontModel.AdvanceActive(r, ri, ui, rmax, 1f / 60f, P);
                Assert.True(r >= prev, "R_front ne recule pas");
                Assert.True(r <= rmax + 1e-3f, "R_front ≤ R_max");
                prev = r;
            }
            Assert.Near(rmax, r, 1e-3, "R_max atteint au bout d'une minute");
            Assert.Near(ri, CloudFrontModel.AdvanceActive(0f, ri, ui, rmax, 0f, P), 1e-6, "R part de r_i");
        }

        [Test]
        public static void ActiveFrontMatchesAnalyticSolution()
        {
            // S1 à h = 5 m : R^2,1 = r_i^2,1 + 2,1 · k · u_i · r_i^1,1 · t → R = 50 m vers t ≈ 10,4 s.
            float ri = 1.342f, ui = 351.6f;
            float coarse = CloudFrontModel.AdvanceActive(ri, ri, ui, 1000f, 10.4f, P);
            float fine = ri;
            for (int i = 0; i < 10400; i++) fine = CloudFrontModel.AdvanceActive(fine, ri, ui, 1000f, 0.001f, P);
            Assert.Near(coarse, fine, 0.05, "l'intégration exacte ne dépend pas du pas");
            Assert.Near(50, coarse, 1.0, "R ≈ 50 m après 10,4 s");
        }

        [Test]
        public static void AfterCutoffSlowSpreadBoundedAndFades()
        {
            float r = 80f, rmax = 87.46f;
            float v0 = CloudFrontModel.FrontSpeed(r, 1.342f, 351.6f, rmax, P);
            Assert.True(v0 > 0f, "vitesse du front positive à la coupure");
            float prev = r;
            for (int i = 0; i < 90 * 30; i++)
            {
                float t = i / 30f;
                r = CloudFrontModel.AdvanceAfterCutoff(r, v0, t, rmax, 1f / 30f, P);
                Assert.True(r >= prev, "le nuage continue de s'étaler");
                prev = r;
            }
            Assert.True(r <= rmax * P.PostCutoffMaxGrowth + 1e-3f, "étalement borné à 1,5 R_max");
            Assert.Near(40, CloudFrontModel.DissipationTime(0f, P), 1e-4, "petit nuage : 40 s");
            Assert.Near(150, CloudFrontModel.DissipationTime(1000f, P), 1e-4, "gros nuage : 150 s");
            Assert.Near(72.07, CloudFrontModel.DissipationTime(87.46f, P), 0.1, "S1 : ~72 s");
            float td = 40f, fprev = 2f;
            for (float t = 0; t <= 45f; t += 0.5f)
            {
                float f = CloudFrontModel.CutoffFade(t, td);
                Assert.True(f <= fprev + 1e-6f && f >= 0f && f <= 1f, "fondu de dissipation monotone dans [0,1]");
                fprev = f;
            }
            Assert.Near(1, CloudFrontModel.CutoffFade(0f, td), 1e-6, "plein à la coupure");
            Assert.Near(0, CloudFrontModel.CutoffFade(td, td), 1e-6, "vide à la fin");
            Assert.Near(0, CloudFrontModel.VacuumCutoffFade(P.VacuumCutoffFadeS, P), 1e-6, "vide : arrêt net en 0,2 s");
            Assert.Near(0, CloudFrontModel.IgnitionFade(0f, P), 1e-6, "allumage : départ à 0");
            Assert.Near(1, CloudFrontModel.IgnitionFade(1f, P), 1e-6, "allumage : plein après 0,4 s");
        }
    }

    public static class ClustererTests
    {
        private static int Run(float[] x, float[] y, float[] r, int[] key, int[] groups)
        {
            var c = new JetClusterer();
            var z = new float[x.Length];
            return c.Cluster(x.Length, x, y, z, r, key, 1.5f, groups);
        }

        [Test]
        public static void NineEnginesMergeIntoOneCluster()
        {
            var s = DemoProfiles.Create(DemoKind.S3PadLaunch);
            int n = s.EngineCount;
            var r = new float[n]; var key = new int[n]; var g = new int[n];
            for (int i = 0; i < n; i++) r[i] = 2f;
            Assert.Equal(1, Run(s.OffsetX, s.OffsetY, r, key, g), "octaweb = 1 foyer");
        }

        [Test]
        public static void ThirtyThreeEnginesMergeIntoOneCluster()
        {
            var s = DemoProfiles.Create(DemoKind.S4Stress33);
            int n = s.EngineCount;
            var r = new float[n]; var key = new int[n]; var g = new int[n];
            for (int i = 0; i < n; i++) r[i] = 2.5f;
            Assert.Equal(1, Run(s.OffsetX, s.OffsetY, r, key, g), "Super Heavy = 1 foyer");
        }

        [Test]
        public static void DistantJetsAndDifferentKeysStaySeparate()
        {
            var g = new int[3];
            Assert.Equal(2, Run(new float[] { 0, 1, 100 }, new float[] { 0, 0, 0 }, new float[] { 2, 2, 2 }, new int[3], g), "deux zones éloignées");
            Assert.Equal(g[0], g[1], "les deux proches ensemble");
            Assert.True(g[0] != g[2], "le lointain à part");
            Assert.Equal(2, Run(new float[] { 0, 1 }, new float[] { 0, 0 }, new float[] { 2, 2 }, new int[] { 0, 7 }, new int[2]), "clés différentes : pas de fusion");
        }

        [Test]
        public static void MergeIsTransitive()
        {
            // A–B et B–C à 5 m (limite 6 m), A–C à 10 m : un seul foyer par transitivité.
            Assert.Equal(1, Run(new float[] { 0, 5, 10 }, new float[3], new float[] { 4, 4, 4 }, new int[3], new int[3]), "chaîne A–B–C");
        }

        [Test]
        public static void GroupIndicesAreStableAndDense()
        {
            var g = new int[4];
            int n = Run(new float[] { 100, 0, 100.5f, 0.5f }, new float[4], new float[] { 1, 1, 1, 1 }, new int[4], g);
            Assert.Equal(2, n, "2 groupes");
            Assert.Equal(0, g[0], "le premier jet ouvre le groupe 0");
            Assert.Equal(1, g[1], "le deuxième groupe vaut 1");
            Assert.Equal(0, g[2], "même groupe que le premier");
            Assert.Equal(1, g[3], "même groupe que le deuxième");
        }
    }

    public static class VacuumAndScorchTests
    {
        private static readonly PhysicsParams P = new PhysicsParams();

        [Test]
        public static void VacuumEjecta()
        {
            Assert.Near(349, VacuumEjectaModel.CharacteristicSpeed(698f, P), 1e-3, "v = 0,5 u_i");
            Assert.Near(100, VacuumEjectaModel.CharacteristicSpeed(50f, P), 1e-6, "borne basse 100 m/s");
            Assert.Near(2000, VacuumEjectaModel.CharacteristicSpeed(1e5f, P), 1e-6, "borne haute 2 km/s");
            for (float h = 0; h < 60; h += 1)
            {
                float a = VacuumEjectaModel.SheetAngleDeg(h, 0.77f, P);
                Assert.True(a >= 1f && a <= 3f, "nappe rasante entre 1 et 3°");
            }
            Assert.Near(4.309, VacuumEjectaModel.FlightTime(100f, 2f, 1.62f), 0.01, "temps de vol balistique lunaire");
            Assert.Near(430.6, VacuumEjectaModel.BallisticRange(100f, 2f, 1.62f), 0.5, "portée balistique lunaire");
        }

        [Test]
        public static void ScorchAccumulates()
        {
            float dose = 0f, prev = 0f;
            for (int i = 0; i < 600; i++)
            {
                dose = ScorchModel.AddDose(dose, 17000f, 1f, 0.1f, P);
                float s = ScorchModel.Strength(dose, P);
                Assert.True(s >= prev && s <= 1f, "force croissante et ≤ 1");
                prev = s;
            }
            Assert.True(prev > 0.99f, "un moteur longtemps au même endroit marque fortement");
            Assert.Near(0, ScorchModel.Strength(0f, P), 0, "pas de dose, pas de trace");
            Assert.True(ScorchModel.Radius(true, 0.9f, 40f, P) > ScorchModel.Radius(false, 0.9f, 40f, P), "halo du vide plus large que la brûlure");
        }
    }

    public static class WindAndDemoTests
    {
        [Test]
        public static void WindStaysInRangeAndIsDeterministic()
        {
            var a = new WindModel(42, 1f, 6f);
            var b = new WindModel(42, 1f, 6f);
            for (double t = 0; t < 600; t += 0.7)
            {
                a.Sample(t, 45f, out float sa, out float ha);
                b.Sample(t, 45f, out float sb, out float hb);
                Assert.True(sa >= 1f && sa <= 6f, "vitesse de vent dans [1 ; 6] m/s");
                Assert.Near(sa, sb, 0, "même graine, même vent");
                Assert.Near(ha, hb, 0, "même graine, même cap");
            }
        }

        [Test]
        public static void DemoProfilesFollowScenarios()
        {
            var s1 = DemoProfiles.Create(DemoKind.S1Landing);
            DemoProfiles.Evaluate(s1, 0f, out DemoState st);
            Assert.Near(150, st.StandoffM, 1e-3, "S1 commence à 150 m");
            DemoProfiles.Evaluate(s1, 25f, out st);
            Assert.Near(2, st.StandoffM, 1e-3, "S1 posé à 25 s");
            Assert.True(st.EnginesOn, "moteur allumé à 25 s");
            DemoProfiles.Evaluate(s1, 30f, out st);
            Assert.True(!st.EnginesOn, "coupure à 30 s (début de S8)");

            var s3 = DemoProfiles.Create(DemoKind.S3PadLaunch);
            Assert.Equal(9, s3.EngineCount, "S3 : 9 moteurs");
            Assert.Near(7.6e6, s3.ThrustPerEngineN * s3.EngineCount, 1, "S3 : 7,6 MN");
            DemoProfiles.Evaluate(s3, 20f, out st);
            Assert.True(st.StandoffM > 300f, "S3 : au-dessus de H_act vers 20 s");

            var s4 = DemoProfiles.Create(DemoKind.S4Stress33);
            Assert.Equal(33, s4.EngineCount, "S4 : 33 moteurs");
            Assert.Near(70e6, s4.ThrustPerEngineN * s4.EngineCount, 10, "S4 : 70 MN");

            var s5 = DemoProfiles.Create(DemoKind.S5Moon);
            Assert.True(s5.ForceVacuum && s5.ForcedGravityMs2 == 1.62f, "S5 : vide, 1,62 m/s²");
            DemoProfiles.Evaluate(s5, 29f, out st);
            Assert.Near(15e3, s5.ThrustPerEngineN * st.Throttle01, 1, "S5 : 15 kN en fin de descente");

            var s7 = DemoProfiles.Create(DemoKind.S7Water);
            DemoProfiles.Evaluate(s7, 10f, out st);
            Assert.Near(20, st.StandoffM, 1e-6, "S7 : stationnaire à 20 m");
            Assert.True(s7.Surface == DemoSurface.Water, "S7 : eau");
        }
    }

    public static class AllocationTests
    {
        /// <summary>Exigence §5 : zéro allocation par frame dans les chemins chauds du modèle.</summary>
        [Test]
        public static void HotPathsDoNotAllocate()
        {
            var p = new PhysicsParams();
            var clusterer = new JetClusterer();
            var s = DemoProfiles.Create(DemoKind.S4Stress33);
            int n = s.EngineCount;
            var z = new float[n]; var r = new float[n]; var key = new int[n]; var g = new int[n];
            for (int i = 0; i < n; i++) r[i] = 2.5f;
            var wind = new WindModel(1, 1f, 6f);
            var input = new JetInput { ThrustN = 2e6f, ExhaustVelocityMs = 3200f, ExitRadiusM = 0.65f, StandoffM = 30f, CosAlpha = 0.98f, AmbientPressurePa = 101325f };
            float sink = 0f;

            void Body(int i)
            {
                PlumeModel.Evaluate(ref input, p, out JetImpingement res);
                sink += res.WallJetVelocityMs;
                sink += CloudFrontModel.AdvanceActive(10f + i % 50, 3f, 300f, 200f, 0.016f, p);
                sink += CloudFrontModel.AdvanceAfterCutoff(100f, 5f, i * 0.01f, 200f, 0.016f, p);
                sink += CloudFrontModel.CutoffFade(i * 0.01f, 40f);
                sink += clusterer.Cluster(n, s.OffsetX, s.OffsetY, z, r, key, 1.5f, g);
                sink += VacuumEjectaModel.CharacteristicSpeed(500f, p) + VacuumEjectaModel.SheetAngleDeg(3f, 0.7f, p);
                sink += ScorchModel.Strength(ScorchModel.AddDose(1f, 2e4f, 1f, 0.04f, p), p);
                wind.Sample(i * 0.016, 45f, out float ws, out float wh);
                sink += ws + wh;
                DemoProfiles.Evaluate(s, i * 0.01f, out DemoState st);
                sink += st.StandoffM;
            }

            for (int i = 0; i < 100; i++) Body(i); // chauffe (JIT, capacités)
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20000; i++) Body(i);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0, after - before, "octets alloués sur 20 000 itérations");
            Assert.True(!float.IsNaN(sink), "résultat utilisé");
        }
    }
}
