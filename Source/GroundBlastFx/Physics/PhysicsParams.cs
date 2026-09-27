namespace GroundBlastFx.Model
{
    /// <summary>
    /// Constantes calibrables du modèle. Valeurs par défaut = Configs/Physics.cfg livré.
    /// Toutes les grandeurs sont en unités SI sauf mention (kN pour les formules en racine de la poussée).
    /// </summary>
    public sealed class PhysicsParams
    {
        // --- Jet ---
        public float PlumeHalfAngleSeaLevelDeg = 10f;   // θ au niveau de la mer
        public float PlumeHalfAngleVacuumDeg = 50f;     // θ dans le vide
        public float WallJetKu = 0.3f;                  // u_i = k_u · v_e / (1 + h / (k_h · r_e))
        public float WallJetKh = 8f;
        public float WallJetDecayExponent = 1.1f;       // u(r) = u_i · (r_i / r)^n
        public float WallJetMomentumK = 3.5f;           // souffle d'un foyer en atmosphère : u ≈ k · √(F / ρ_air) / h (dépend de la poussée)
        public float ImpingementPressureMaxPa = 5.0e6f; // borne de p_s
        public float ImpingementPressureMinRadiusM = 0.05f;

        // --- Tuyère ---
        public float NozzleExitRadiusFactor = 0.4f;     // r_e ≈ 0,4 × diamètre de la pièce
        public float NozzleExitRadiusMinM = 0.05f;
        public float NozzleExitRadiusMaxM = 3.0f;

        // --- Activation ---
        public float ActivationK = 4.5f;                // H_act = k · √(F kN), en atmosphère
        public float ActivationKVacuum = 9f;            // idem dans le vide (jet non confiné, régolithe sans cohésion)
        public float ActivationFullFraction = 0.6f;     // effet plein sous 0,6 · H_act

        // --- Levée de poussière ---
        public float LiftReferenceVelocityMs = 100f;    // u_ref
        public float SteamVisibilityThresholdMs = 15f;  // vitesse pariétale minimale pour une vapeur visible

        // --- Nuage ---
        public float CloudMaxRadiusK = 3f;              // R_max = k_R · √(F kN)
        public float CloudMaxRadiusMinM = 6f;
        public float CloudFrontSpeedK = 0.35f;          // vitesse du front = k · u(R) (entraînement, rouleau)
        public float PostCutoffFrontDecayS = 2.5f;      // décélération du front après coupure (constante de temps)
        public float PostCutoffSpreadMs = 0.4f;         // étalement lent résiduel
        public float PostCutoffMaxGrowth = 1.5f;        // R ≤ 1,5 · R_max après coupure
        public float DissipationMinS = 40f;             // 1.6 : dispersion moins rapide (retour de l'auteur)
        public float DissipationMaxS = 150f;
        public float DissipationRefRadiusM = 300f;      // R au moment de la coupure qui donne DissipationMaxS
        public float PadSteamDissipationFactor = 1.6f;  // pas de tir : la vapeur du déluge persiste 1 à 4 min (2,5 de la 1.6 à la 1.9.2 : jusqu'à ~6 min)
        public float IgnitionFadeS = 0.4f;
        public float IntensitySmoothingS = 0.3f;

        // --- Fusion multi-moteurs / suivi ---
        public float ClusterMergeFactor = 1.5f;         // fusion si distance < 1,5 × max(r_i)
        public float ClusterMatchMinM = 8f;             // rayon minimal d'appariement d'un foyer d'un sondage à l'autre
        public float ClusterRelightWindowS = 2f;        // une rallumage dans cette fenêtre garde le même Id

        // --- Pas de tir ---
        public float PadLatchAltitudeM = 300f;

        // --- Échantillonnage ---
        public float SampleRateHz = 25f;
        public int RingRaysLow = 6;
        public int RingRaysHigh = 8;

        // --- Milieu ---
        public float VacuumPressureThresholdPa = 1f;    // en dessous : vide

        // --- Vide ---
        public float VacuumEjectaAngleDeg = 2f;
        public float VacuumEjectaAngleMinDeg = 1f;
        public float VacuumEjectaAngleMaxDeg = 3f;
        public float VacuumEjectaSpeedK = 0.5f;         // vitesse caractéristique = k · u_i
        public float VacuumEjectaSpeedMinMs = 100f;
        public float VacuumEjectaSpeedMaxMs = 2000f;
        public float VacuumCutoffFadeS = 0.2f;          // arrêt net de la nappe
        public float VacuumLingerS = 8f;                // le foyer reste listé pour les débris en vol

        // --- Lumière de flamme ---
        public float FlameLightK = 1f;                  // intensité = k · √(F / 1 MN) · intensité ergol
        public float FlameLightFadeS = 0.15f;

        // --- Traces ---
        public float ScorchPressureRefPa = 20000f;      // p_s de référence pour la dose
        public float ScorchDoseRef = 6f;                // Strength = 1 − exp(−dose / ref)
        public float ScorchMinStrength = 0.02f;
        public float ScorchRadiusFactor = 2.0f;         // rayon = k · r_i (atmosphère)
        public float ScorchVacuumRadiusFactor = 0.6f;   // rayon du halo = k · R_front (vide)
        public float ScorchNewMarkDistanceFactor = 0.6f;// nouvelle trace si le centre s'éloigne de > k · rayon
        public int ScorchMaxMarks = 64;

        // --- Vent ---
        public float WindVariationPeriodS = 45f;
    }
}
