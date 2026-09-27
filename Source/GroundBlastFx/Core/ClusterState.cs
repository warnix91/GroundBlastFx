using GroundBlastFx.Contracts;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// État persistant d'un foyer, de l'allumage à la fin de la dissipation. Les objets sont pré-alloués
    /// par le ClusterTracker et réutilisés (aucune allocation en régime établi).
    /// </summary>
    public sealed class ClusterState
    {
        public bool InUse;
        public int Id;
        public int Key;
        public bool IsDemo;
        public bool EnginesActive;
        public bool MatchedThisTick;
        public float TimeSinceIgnitionS;
        public float TimeSinceCutoffS;
        public float DissipationTimeS;
        public float FrontSpeedAtCutoffMs;
        public float IntensityAtCutoff;

        // --- Ancrage ---
        public CelestialBody Body;
        public double Lat, Lon, Alt;             // cible du dernier sondage
        public double SLat, SLon, SAlt;          // valeur lissée utilisée pour le rendu
        public Transform AnchorTransform;        // pont de barge (surface mobile)
        public bool AnchoredToDeck;              // true tant que l'ancrage est le Transform
        public Vector3 AnchorLocalPoint;         // cible, repère du Transform
        public Vector3 SAnchorLocalPoint;        // lissé
        public Vector3 NormalLocal, AxisLocal;   // repère du Transform (pont)
        public Vector3d NormalBody, AxisBody;    // repère du corps (sol)
        public Vessel LeadVessel;                // vaisseau qui porte la plus grande poussée du foyer
        public Vector3 NozzleLocal;              // centre de poussée dans le repère du vaisseau
        public bool HasNozzleLocal;
        public float SlantM;                     // distance tuyère → impact le long du jet
        public Vector3 LastImpactWorld;
        public Vector3 LastNormalWorld;
        public Vector3 LastAxisWorld;
        public Vector3 LastNozzleWorld;

        // --- Cibles calculées au sondage ---
        public SurfaceKind Surface;
        public MediumKind Medium;
        public int EngineCount;
        public float ThrustN;
        public float MassFlowKgS;
        public float ExhaustVelocityMs;
        public float ImpingementRadiusM;
        public float ImpingementPressurePa;
        public float WallJetVelocityMs;
        public float AmbientPressurePa;
        public float AmbientDensityKgM3;
        public float GravityMs2;
        public float Erodibility;
        public float SteamFraction;
        public float Soot = 0.6f;               // noircissement par les ergols (Propellants.cfg)
        public float Lift;
        public float SteamVisibility;
        public float Activation;
        public float ActivationHeightM;
        public float MaxCloudRadiusM;
        public float FlameIntensityTarget;
        public float EjectaSpeedMs;
        public float EjectaAngleDeg;
        public float ExitRadiusM;
        public float StandoffM;
        public Color DustA, DustB, FlameColor;
        public string SiteKey;
        public float TrenchHeadingDeg = -1f;
        public bool Deluge;
        public string BiomeName;
        public double BiomeLat, BiomeLon;
        public float BiomeAgeS = 999f;

        // --- Intégré à chaque frame ---
        public float FrontM;
        public float Intensity;
        public float FlameIntensity;
        public float PriorityFade;
        public float CameraDistanceM;
        public Vector3 WorldImpact;              // position monde de la frame courante (après lissage)
        public Vector3 WorldNormal;
        public Vector3 WorldAxis;
        public Vector3 WorldNozzle;

        // --- Repère fixe du nuage déposé (D-CL-014) : ne suit pas le jet ---
        public bool CloudAnchorSet;
        public double CALat, CALon, CAAlt;       // sol
        public Vector3 CloudAnchorLocal;         // pont de barge (repère du Transform)
        public Vector3 WorldCloudAnchor;
        public Vector3 WorldCloudNorth;
        public Vector3 WorldCloudUp;             // verticale fixe du nuage (verticale du lieu ; pont : repère du pont)
        public Vector3 CloudUpLocal;             // pont de barge : verticale dans le repère du Transform
        public float Source;                     // apport instantané du jet (sans maintien)

        // --- Surface figée à la naissance (1.7) : un nuage de vapeur ne devient pas de la poussière ---
        public bool SurfaceSet;
        public int SurfaceMismatchTicks;         // sondages consécutifs où le jet touche une autre surface

        // --- Traces ---
        public int ScorchSlot = -1;

        public void Reset()
        {
            InUse = false;
            MatchedThisTick = false;
            EnginesActive = false;
            TimeSinceIgnitionS = TimeSinceCutoffS = DissipationTimeS = FrontSpeedAtCutoffMs = IntensityAtCutoff = 0f;
            Body = null;
            AnchorTransform = null;
            AnchoredToDeck = false;
            LeadVessel = null;
            HasNozzleLocal = false;
            FrontM = Intensity = FlameIntensity = PriorityFade = 0f;
            MaxCloudRadiusM = 0f;
            Activation = Lift = SteamVisibility = 0f;
            SiteKey = null;
            TrenchHeadingDeg = -1f;
            BiomeName = null;
            BiomeAgeS = 999f;
            ScorchSlot = -1;
            IsDemo = false;
            CloudAnchorSet = false;
            Source = 0f;
            SurfaceSet = false;
            SurfaceMismatchTicks = 0;
        }
    }
}
