using System;

namespace GroundBlastFx.Model
{
    /// <summary>Scénarios de référence utilisés par le mode Démo.</summary>
    public enum DemoKind
    {
        None = 0,
        S1Landing = 1,        // Falcon 9 sur terrain sec
        S2Barge = 2,          // 3 × BE-4 sur barge
        S3PadLaunch = 3,      // 9 moteurs kérolox, pas de tir, déluge
        S4Stress33 = 4,       // 33 moteurs méthalox
        S5Moon = 5,           // alunissage
        S7Water = 7,          // stationnaire à 20 m au-dessus de l'eau
    }

    /// <summary>Surface imposée par un scénario (Real = celle réellement sondée sous le point de démo).</summary>
    public enum DemoSurface { Real = 0, Terrain = 1, LaunchPad = 2, Water = 3, VesselDeck = 4 }

    /// <summary>Famille d'ergols du scénario (sert à choisir la couleur de flamme dans Propellants.cfg).</summary>
    public enum DemoPropellant { Kerolox = 0, Methalox = 1, Hypergolic = 2 }

    /// <summary>Paramètres fixes d'un scénario.</summary>
    public sealed class DemoScenario
    {
        public DemoKind Kind;
        public int EngineCount;
        public float ThrustPerEngineN;
        public float IspS;
        public float ExitRadiusM;
        public DemoPropellant Propellant;
        public DemoSurface Surface;
        public bool ForceVacuum;
        public float ForcedGravityMs2;      // 0 = gravité réelle
        public float DurationS = 30f;       // durée moteurs allumés
        public float[] OffsetX;             // disposition des tuyères dans le plan de la surface (m)
        public float[] OffsetY;
        public float NozzleSpanM;           // rayon de l'ensemble des tuyères (pour placer la démo à côté du vaisseau)
    }

    /// <summary>État d'un scénario à l'instant t (depuis le début de la démo).</summary>
    public struct DemoState
    {
        public bool EnginesOn;
        public float StandoffM;             // hauteur tuyère → surface
        public float Throttle01;            // multiplicateur de poussée
        public float LateralOffsetM;        // décalage horizontal du vaisseau virtuel (inclinaison, dérive)
    }

    public static class DemoProfiles
    {
        public static DemoScenario Create(DemoKind kind)
        {
            var s = new DemoScenario { Kind = kind };
            switch (kind)
            {
                case DemoKind.S1Landing:
                case DemoKind.S7Water:
                    s.EngineCount = 1; s.ThrustPerEngineN = 850e3f; s.IspS = 282f; s.ExitRadiusM = 0.46f;
                    s.Propellant = DemoPropellant.Kerolox;
                    s.Surface = kind == DemoKind.S7Water ? DemoSurface.Water : DemoSurface.Real;
                    break;
                case DemoKind.S2Barge:
                    s.EngineCount = 3; s.ThrustPerEngineN = 2400e3f; s.IspS = 310f; s.ExitRadiusM = 0.9f;
                    s.Propellant = DemoPropellant.Methalox; s.Surface = DemoSurface.VesselDeck;
                    break;
                case DemoKind.S3PadLaunch:
                    s.EngineCount = 9; s.ThrustPerEngineN = 7.6e6f / 9f; s.IspS = 282f; s.ExitRadiusM = 0.46f;
                    s.Propellant = DemoPropellant.Kerolox; s.Surface = DemoSurface.LaunchPad;
                    break;
                case DemoKind.S4Stress33:
                    s.EngineCount = 33; s.ThrustPerEngineN = 70e6f / 33f; s.IspS = 327f; s.ExitRadiusM = 0.65f;
                    s.Propellant = DemoPropellant.Methalox; s.Surface = DemoSurface.LaunchPad;
                    break;
                case DemoKind.S5Moon:
                    s.EngineCount = 1; s.ThrustPerEngineN = 45e3f; s.IspS = 311f; s.ExitRadiusM = 0.77f;
                    s.Propellant = DemoPropellant.Hypergolic; s.Surface = DemoSurface.Real;
                    s.ForceVacuum = true; s.ForcedGravityMs2 = 1.62f;
                    break;
                default:
                    throw new ArgumentException("Scénario inconnu : " + kind);
            }
            BuildLayout(s);
            return s;
        }

        /// <summary>Disposition des tuyères : 1 centrale, octaweb 8+1, BE-4 en ligne, Super Heavy 3+10+20.</summary>
        private static void BuildLayout(DemoScenario s)
        {
            int n = s.EngineCount;
            s.OffsetX = new float[n];
            s.OffsetY = new float[n];
            switch (n)
            {
                case 1:
                    break;
                case 3:
                    Ring(s, 0, 3, 2.2f, 0f);
                    break;
                case 9:
                    Ring(s, 1, 8, 1.5f, 0f);
                    break;
                case 33:
                    Ring(s, 0, 3, 0.9f, 0.3f);
                    Ring(s, 3, 10, 2.7f, 0.1f);
                    Ring(s, 13, 20, 4.2f, 0f);
                    break;
                default:
                    Ring(s, 0, n, 1.5f, 0f);
                    break;
            }
            float span = 0f;
            for (int i = 0; i < n; i++)
            {
                float r = (float)Math.Sqrt(s.OffsetX[i] * s.OffsetX[i] + s.OffsetY[i] * s.OffsetY[i]);
                if (r > span) span = r;
            }
            s.NozzleSpanM = span + s.ExitRadiusM;
        }

        private static void Ring(DemoScenario s, int first, int count, float radius, float phase)
        {
            for (int k = 0; k < count; k++)
            {
                double a = phase + 2.0 * Math.PI * k / count;
                s.OffsetX[first + k] = radius * (float)Math.Cos(a);
                s.OffsetY[first + k] = radius * (float)Math.Sin(a);
            }
        }

        /// <summary>Profil temporel du scénario. t en secondes depuis le lancement de la démo.</summary>
        public static void Evaluate(DemoScenario s, float t, out DemoState state)
        {
            state.EnginesOn = t >= 0f && t < s.DurationS;
            state.Throttle01 = 1f;
            state.LateralOffsetM = 0f;
            state.StandoffM = 0f;
            switch (s.Kind)
            {
                case DemoKind.S1Landing:
                case DemoKind.S2Barge:
                {
                    // Descente de 150 m à ~2 m en 25 s à décélération constante, puis 5 s posé, coupure à 30 s.
                    float hMin = s.Kind == DemoKind.S2Barge ? 3f : 2f;
                    float u = GeMath.Clamp01(t / 25f);
                    state.StandoffM = hMin + (150f - hMin) * (1f - u) * (1f - u);
                    state.Throttle01 = t < 25f ? 1f : 0.6f;
                    if (s.Kind == DemoKind.S2Barge) state.Throttle01 *= 0.55f; // 3 moteurs rallumés à poussée réduite
                    break;
                }
                case DemoKind.S3PadLaunch:
                case DemoKind.S4Stress33:
                {
                    // 4 s au sol (maintien), puis décollage à accélération constante.
                    float h0 = s.Kind == DemoKind.S4Stress33 ? 12f : 6f;
                    float a = s.Kind == DemoKind.S4Stress33 ? 4f : 3f;
                    float tl = t - 4f;
                    state.StandoffM = tl <= 0f ? h0 : h0 + 0.5f * a * tl * tl;
                    state.Throttle01 = GeMath.SmoothStep(0f, 1.5f, t) * 0.9f + 0.1f; // montée en poussée à l'allumage
                    break;
                }
                case DemoKind.S5Moon:
                {
                    // Descente finale de 40 m à 1,5 m en 26 s, poussée 45 → 15 kN, puis 4 s posé.
                    float u = GeMath.Clamp01(t / 26f);
                    state.StandoffM = 1.5f + 38.5f * (1f - u);
                    state.Throttle01 = GeMath.Lerp(1f, 15f / 45f, u);
                    state.LateralOffsetM = 3f * (1f - u); // légère translation, comme un LM qui cherche son site
                    break;
                }
                case DemoKind.S7Water:
                    state.StandoffM = 20f;
                    state.Throttle01 = 0.85f;
                    break;
            }
        }
    }
}
