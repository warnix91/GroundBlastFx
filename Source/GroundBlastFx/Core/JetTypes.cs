using GroundBlastFx.Contracts;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>Un jet (un thrustTransform d'un moteur allumé, ou une tuyère virtuelle de démo) à l'instant du sondage.</summary>
    public struct JetSample
    {
        public Vector3 NozzleWorld;
        public Vector3 AxisWorld;            // direction du jet (+forward du thrustTransform), normalisée
        public float ThrustN;
        public float ExhaustVelocityMs;
        public float ExitRadiusM;
        public Color FlameColor;
        public float FlameIntensity;
        public float SteamBonus;
        public float Soot;
        public float ProbeLengthM;
        public float AmbientPressureAtNozzlePa;
        public Vessel Vessel;                // null pour la démo
        public Part Part;
        public int Key;                      // 0 = réel ; 1000 + n = démo n (pas de fusion entre clés différentes)
        public bool IsDemo;
        public SurfaceKind ForcedSurface;    // Unknown = surface réellement sondée
        public bool ForceVacuum;
        public float ForcedGravityMs2;       // 0 = gravité réelle
        public bool PadLatched;              // vaisseau dans la fenêtre « pas de tir » (PRELAUNCH + altitude)
        public string SiteKey;               // nom du site de lancement associé (latch), sinon null
    }

    /// <summary>Résultat du sondage de surface pour un jet.</summary>
    public struct JetHit
    {
        public bool Hit;
        public Vector3 PointWorld;
        public Vector3 NormalWorld;
        public float DistanceM;              // h le long du rayon central
        public SurfaceKind Surface;
        public Transform AnchorTransform;    // pièce d'un autre vaisseau (pont de barge)
        public Collider Collider;
        public int RingHits;                 // nombre de rayons de la couronne qui ont touché
        public string SiteKey;               // site de lancement reconnu sur le collider, sinon null
    }

    /// <summary>Jet évalué par le modèle physique.</summary>
    public struct JetEval
    {
        public float ImpingementRadiusM;
        public float WallJetVelocityMs;
        public float CosAlpha;
        public float MassFlowKgS;
    }

    /// <summary>Foyer d'un sondage (fusion des jets proches), avant appariement avec les foyers persistants.</summary>
    public struct GroupData
    {
        public int Key;
        public bool IsDemo;
        public int EngineCount;
        public float ThrustN;
        public float MassFlowKgS;
        public Vector3 ImpactWorld;
        public Vector3 NormalWorld;
        public Vector3 AxisWorld;
        public Vector3 NozzleWorld;
        public float StandoffM;
        public float ExitRadiusM;
        public float EnvelopeRadiusM;        // r_i du foyer (enveloppe des taches)
        public float WallJetVelocityMs;
        public float CosAlpha;
        public SurfaceKind Surface;
        public Transform AnchorTransform;
        public Vessel LeadVessel;
        public Color FlameColor;
        public float FlameIntensity;
        public float SteamBonus;
        public float Soot;
        public bool ForceVacuum;
        public float ForcedGravityMs2;
        public string SiteKey;
        public float WeightSum;              // somme des poussées (interne)
        public float SurfaceWeightBest;      // interne : poussée qui a déterminé Surface
    }
}
