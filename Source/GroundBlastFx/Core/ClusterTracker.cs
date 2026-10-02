using System;
using System.Collections.Generic;
using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Model;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Foyers persistants :
    /// - appariement des foyers d'un sondage avec ceux du sondage précédent → identifiant stable ;
    /// - ancrage lat/lon/alt (sol) ou Transform de pièce (pont de barge), lissé entre deux sondages ;
    /// - front R_front intégré à chaque frame, dissipation après coupure (20 à 90 s), arrêt net dans le vide ;
    /// - priorité caméra : au-delà de MaxRenderedClusters, fondu de sortie des foyers les plus lointains ;
    /// - LOD proposé, vent, couleurs du sol (corps / biome / surface).
    /// </summary>
    public sealed class ClusterTracker
    {
        /// <summary>Marge (m) au-delà du rayon d'un pas de tir pour garder ses vraies bouches de déflecteur.</summary>
        private const float PadOutletMarginM = 40f;

        public const int MaxClusters = 32;

        private readonly ClusterState[] _states = new ClusterState[MaxClusters];
        private readonly int[] _order = new int[MaxClusters];
        private int _nextId = 1;
        private bool _warnedFull;
        private readonly Dictionary<string, HashSet<string>> _biomesSeen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public ClusterTracker()
        {
            for (int i = 0; i < MaxClusters; i++) _states[i] = new ClusterState();
        }

        public int Capacity => MaxClusters;

        public ClusterState this[int index] => _states[index];

        public int InUseCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxClusters; i++) if (_states[i].InUse) n++;
                return n;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < MaxClusters; i++) _states[i].Reset();
        }

        /// <summary>Supprime immédiatement les foyers de démonstration (bouton « Arrêter la démo »).</summary>
        public void ClearDemo()
        {
            for (int i = 0; i < MaxClusters; i++) if (_states[i].InUse && _states[i].IsDemo) _states[i].Reset();
        }

        public void BeginTick()
        {
            for (int i = 0; i < MaxClusters; i++) _states[i].MatchedThisTick = false;
        }

        /// <summary>Intègre un foyer du sondage courant (déjà filtré : activation &gt; 0).</summary>
        public void ApplyGroup(ref GroupData g, CelestialBody body)
        {
            PhysicsParams p = GeConfig.Physics;
            ClusterState best = null, revive = null;
            float bestD = float.MaxValue, reviveD = float.MaxValue;
            for (int i = 0; i < MaxClusters; i++)
            {
                ClusterState s = _states[i];
                if (!s.InUse || s.MatchedThisTick || s.Key != g.Key || s.Body != body) continue;
                // Surface différente depuis plus de 0,6 s (fusée qui dérive du pas vers l'herbe, de l'eau vers la plage) :
                // le nuage existant garde sa nature et se dissipe ; un nouveau foyer naît sur la nouvelle surface (1.7).
                if (s.SurfaceSet && s.Surface != g.Surface && s.SurfaceMismatchTicks >= SurfaceMismatchLimit) continue;
                // Le nuage déposé est ancré au sol : si le point d'impact s'éloigne trop de son origine (fusée qui part
                // en biais), un nouveau foyer naît et l'ancien se dissipe sur place (D-CL-014). Zone portée à 0,65 R_max
                // en 1.6 (la grille couvre 1,45 R_max) : moins de foyers, donc moins de nuages qui repartent de zéro.
                float anchorD = s.CloudAnchorSet ? Vector3.Distance(s.WorldCloudAnchor, g.ImpactWorld) : 0f;
                float domain = Mathf.Max(p.ClusterMatchMinM, 0.65f * Mathf.Max(s.MaxCloudRadiusM, 1.5f * g.EnvelopeRadiusM));
                if (anchorD > domain) continue;
                if (!s.EnginesActive && s.TimeSinceCutoffS > p.ClusterRelightWindowS)
                {
                    // Nuage encore présent : le jet qui revient dessus le reprend (il est de nouveau poussé et alimenté)
                    // au lieu de créer à côté un nouveau nuage vide qui l'ignore (1.6).
                    // 1.9.2 : seulement sur la même surface. Une fusée qui revient se poser sur l'herbe à quelques
                    // centaines de mètres reprenait le vieux nuage du pas de tir, et relançait les jets de sa tranchée.
                    if (s.SurfaceSet && s.Surface != g.Surface) continue;
                    if (anchorD < reviveD) { revive = s; reviveD = anchorD; }
                    continue;
                }
                float d = Vector3.Distance(s.WorldImpact, g.ImpactWorld);
                float lim = Mathf.Max(p.ClusterMatchMinM, 1.5f * Mathf.Max(g.EnvelopeRadiusM, s.ImpingementRadiusM));
                // Même foyer si l'impact reste dans sa zone (même éloigné de la dernière tache : fusée qui se déplace).
                if ((d < lim || anchorD < domain) && d < bestD) { best = s; bestD = d; }
            }
            if (best == null && revive != null) best = revive;

            bool created = false, revived = false;
            if (best == null)
            {
                best = Allocate();
                if (best == null) return;
                created = true;
                best.InUse = true;
                best.Id = _nextId++;
                best.Key = g.Key;
                best.IsDemo = g.IsDemo;
                best.Body = body;
                best.EnginesActive = true;
                best.TimeSinceIgnitionS = 0f;
                best.FrontM = g.EnvelopeRadiusM;
            }
            else if (!best.EnginesActive)
            {
                // Rallumage dans la fenêtre : même foyer, même Id.
                best.EnginesActive = true;
                best.TimeSinceCutoffS = 0f;
                revived = true;
            }
            best.MatchedThisTick = true;
            SetAnchor(best, ref g, body, created, revived);
            SetPhysics(best, ref g, body, p);
        }

        public void EndTick()
        {
            PhysicsParams p = GeConfig.Physics;
            for (int i = 0; i < MaxClusters; i++)
            {
                ClusterState s = _states[i];
                if (s.InUse && s.EnginesActive && !s.MatchedThisTick) Cutoff(s, p);
            }
        }

        private static void Cutoff(ClusterState s, PhysicsParams p)
        {
            s.EnginesActive = false;
            s.TimeSinceCutoffS = 0f;
            s.FrontSpeedAtCutoffMs = CloudFrontModel.FrontSpeed(s.FrontM, s.ImpingementRadiusM, s.WallJetVelocityMs, s.MaxCloudRadiusM, p);
            s.DissipationTimeS = CloudFrontModel.DissipationTime(s.FrontM, p) * (s.Surface == SurfaceKind.LaunchPad ? p.PadSteamDissipationFactor : 1f);
            s.IntensityAtCutoff = s.Intensity;
        }

        private ClusterState Allocate()
        {
            for (int i = 0; i < MaxClusters; i++) if (!_states[i].InUse) { _states[i].Reset(); return _states[i]; }
            // Plein : on recycle le foyer en dissipation le plus avancé.
            ClusterState victim = null;
            float worst = -1f;
            for (int i = 0; i < MaxClusters; i++)
            {
                ClusterState s = _states[i];
                if (s.EnginesActive) continue;
                float age = s.DissipationTimeS > 0f ? s.TimeSinceCutoffS / s.DissipationTimeS : 1f;
                if (age > worst) { worst = age; victim = s; }
            }
            if (victim == null)
            {
                if (!_warnedFull) { GeLog.Warn("Plus de " + MaxClusters + " foyers actifs simultanés : les nouveaux sont ignorés."); _warnedFull = true; }
                return null;
            }
            victim.Reset();
            return victim;
        }

        /// <summary>Sondages consécutifs (25 Hz) sur une autre surface avant de séparer le nuage (≈ 0,6 s).</summary>
        private const int SurfaceMismatchLimit = 15;

        /// <summary>Verticale du lieu (gravité), jamais la normale d'un collider incliné.</summary>
        private static Vector3 LocalUp(CelestialBody body, Vector3 point, Vector3 fallback)
        {
            if (body == null) return fallback.sqrMagnitude > 1e-6f ? fallback.normalized : Vector3.up;
            Vector3 up = point - (Vector3)body.position;
            return up.sqrMagnitude > 1e-6f ? up.normalized : Vector3.up;
        }

        private static void SetAnchor(ClusterState s, ref GroupData g, CelestialBody body, bool created, bool revived)
        {
            if (g.AnchorTransform != null)
            {
                bool changed = !s.AnchoredToDeck || s.AnchorTransform != g.AnchorTransform;
                s.AnchoredToDeck = true;
                s.AnchorTransform = g.AnchorTransform;
                s.AnchorLocalPoint = g.AnchorTransform.InverseTransformPoint(g.ImpactWorld);
                s.NormalLocal = g.AnchorTransform.InverseTransformDirection(g.NormalWorld);
                s.AxisLocal = g.AnchorTransform.InverseTransformDirection(g.AxisWorld);
                if (created || changed || revived) s.SAnchorLocalPoint = s.AnchorLocalPoint;
            }
            else
            {
                bool wasDeck = s.AnchoredToDeck;
                s.AnchoredToDeck = false;
                s.AnchorTransform = null;
                body.GetLatLonAlt(g.ImpactWorld, out s.Lat, out s.Lon, out s.Alt);
                s.NormalBody = SurfaceFrame.ToBody(body, g.NormalWorld);
                s.AxisBody = SurfaceFrame.ToBody(body, g.AxisWorld);
                // Rallumage : l'impact lissé repart du nouvel impact, pas de l'ancien (1.9.2 : resté sur le pas, il faisait
                // croire que la fusée était au-dessus des bouches du déflecteur pendant plusieurs secondes).
                if (created || wasDeck || revived) { s.SLat = s.Lat; s.SLon = s.Lon; s.SAlt = s.Alt; }
            }
            s.LeadVessel = g.LeadVessel;
            s.HasNozzleLocal = g.LeadVessel != null;
            if (s.HasNozzleLocal) s.NozzleLocal = g.LeadVessel.transform.InverseTransformPoint(g.NozzleWorld);
            s.SlantM = Vector3.Distance(g.NozzleWorld, g.ImpactWorld);
            s.StandoffM = g.StandoffM;
            s.ExitRadiusM = g.ExitRadiusM;
            if (!s.CloudAnchorSet)
            {
                s.CloudAnchorSet = true;
                if (g.AnchorTransform != null) s.CloudAnchorLocal = g.AnchorTransform.InverseTransformPoint(g.ImpactWorld);
                else body.GetLatLonAlt(g.ImpactWorld, out s.CALat, out s.CALon, out s.CAAlt);
                s.WorldCloudAnchor = g.ImpactWorld;
                // Repère du nuage : verticale du lieu, fixée (1.7). La normale mesurée bascule sur les pentes et les
                // structures du pas quand la fusée s'incline : tout le nuage basculait avec elle.
                s.WorldCloudUp = LocalUp(body, g.ImpactWorld, g.NormalWorld);
                if (g.AnchorTransform != null) s.CloudUpLocal = g.AnchorTransform.InverseTransformDirection(s.WorldCloudUp);
                s.WorldCloudNorth = CloudNorth(s, body, g.ImpactWorld, s.WorldCloudUp);
            }
            if (created || revived)
            {
                s.WorldImpact = s.LastImpactWorld = g.ImpactWorld;
                s.WorldNormal = s.LastNormalWorld = g.NormalWorld;
                s.WorldAxis = s.LastAxisWorld = g.AxisWorld;
                s.WorldNozzle = s.LastNozzleWorld = g.NozzleWorld;
            }
        }

        private void SetPhysics(ClusterState s, ref GroupData g, CelestialBody body, PhysicsParams p)
        {
            // Nature du nuage figée à sa naissance : pendant un changement de surface bref (bord du pas, rivage), le
            // nuage garde ses couleurs et son comportement ; au-delà de 0,6 s, ApplyGroup crée un nouveau foyer.
            if (!s.SurfaceSet) { s.Surface = g.Surface; s.SurfaceSet = true; s.SurfaceMismatchTicks = 0; }
            else if (g.Surface != s.Surface) s.SurfaceMismatchTicks++;
            else s.SurfaceMismatchTicks = 0;
            s.EngineCount = g.EngineCount;
            s.ThrustN = g.ThrustN;
            s.MassFlowKgS = g.MassFlowKgS;
            s.ExhaustVelocityMs = g.MassFlowKgS > 0f ? g.ThrustN / g.MassFlowKgS : 0f;
            s.ImpingementRadiusM = g.EnvelopeRadiusM;
            s.WallJetVelocityMs = g.WallJetVelocityMs;
            s.ImpingementPressurePa = PlumeModel.ImpingementPressure(g.ThrustN, g.CosAlpha, g.EnvelopeRadiusM, p);

            // Milieu au point d'impact.
            double alt = s.AnchorTransform != null ? FlightGlobals.getAltitudeAtPos((Vector3d)g.ImpactWorld, body) : s.Alt;
            double pKpa = body.atmosphere && !g.ForceVacuum ? body.GetPressure(Math.Max(alt, 0.0)) : 0.0;
            s.AmbientPressurePa = (float)(pKpa * 1000.0);
            s.AmbientDensityKgM3 = pKpa > 0.0 ? (float)body.GetDensity(pKpa, body.GetTemperature(Math.Max(alt, 0.0))) : 0f;
            double r = body.Radius + alt;
            s.GravityMs2 = g.ForcedGravityMs2 > 0f ? g.ForcedGravityMs2 : (float)(body.gravParameter / (r * r));
            s.Medium = s.AmbientPressurePa < p.VacuumPressureThresholdPa ? MediumKind.Vacuum : MediumKind.Atmosphere;

            // Paramètres de surface, corps, biome.
            SurfaceParams sp = GeConfig.Surface(s.Surface);
            BodyParams bp = GeConfig.Body(body.bodyName);
            float erod = sp.Erodibility, threshold = sp.ThresholdVelocityMs;
            Color a = sp.HasColors ? sp.DustA : bp.DustA;
            Color b = sp.HasColors ? sp.DustB : bp.DustB;
            float biomeSteam = -1f;
            if (s.Surface == SurfaceKind.Terrain || s.Surface == SurfaceKind.Unknown)
            {
                erod *= bp.ErodibilityScale;
                threshold *= bp.ThresholdScale;
                a = bp.DustA;
                b = bp.DustB;
                BiomeParams biome = ResolveBiome(s, body, bp);
                if (biome != null)
                {
                    if (biome.HasA) a = biome.DustA;
                    if (biome.HasB) b = biome.DustB;
                    if (biome.Erodibility >= 0f) erod = biome.Erodibility;
                    if (biome.ThresholdVelocityMs >= 0f) threshold = biome.ThresholdVelocityMs;
                    biomeSteam = biome.SteamFraction;
                }
            }
            s.DustA = a;
            s.DustB = b;
            s.Soot = Mathf.Clamp01(g.Soot);
            s.Erodibility = Mathf.Clamp01(erod);

            float steam = sp.SteamFraction;
            // Neige et glace (1.7) : le jet soulève un nuage blanc de neige et de vapeur, pas de la poussière.
            if (biomeSteam >= 0f) steam = Mathf.Max(steam, biomeSteam);
            s.Deluge = false;
            s.SiteKey = g.SiteKey;
            s.TrenchHeadingDeg = -1f;
            if (s.Surface == SurfaceKind.LaunchPad)
            {
                LaunchSiteParams site = GeConfig.FindLaunchSite(g.SiteKey);
                s.Deluge = site == null || site.Deluge;
                if (!s.Deluge) steam = Mathf.Min(steam, 0.3f);
                s.TrenchHeadingDeg = GeConfig.TrenchHeading(g.SiteKey, site);
            }
            s.SteamFraction = s.Medium == MediumKind.Vacuum ? 0f : Mathf.Clamp01(steam + g.SteamBonus);

            s.ActivationHeightM = PlumeModel.ActivationHeight(g.ThrustN, s.AmbientPressurePa, p);
            s.Activation = PlumeModel.ActivationFade(g.StandoffM, s.ActivationHeightM, p);
            // R_max ne décroît pas pendant la vie du foyer (une baisse de poussée ne rétrécit pas le nuage déjà formé)
            // et couvre au moins la tache d'impact. Dans le vide, il suit la portée visible de la nappe d'éjectas.
            float rmax = Mathf.Max(PlumeModel.MaxCloudRadius(g.ThrustN, p), 1.5f * g.EnvelopeRadiusM);
            if (s.Medium == MediumKind.Vacuum)
            {
                float range = VacuumEjectaModel.BallisticRange(VacuumEjectaModel.CharacteristicSpeed(g.WallJetVelocityMs, p),
                                                               VacuumEjectaModel.SheetAngleDeg(g.StandoffM, g.ExitRadiusM, p), s.GravityMs2);
                rmax = Mathf.Max(rmax, Mathf.Min(0.25f * range, 600f));
            }
            s.MaxCloudRadiusM = Mathf.Max(s.MaxCloudRadiusM, rmax);
            s.Lift = PlumeModel.DustLift(g.WallJetVelocityMs, threshold, s.Erodibility, p);
            s.SteamVisibility = PlumeModel.SteamVisibility(g.WallJetVelocityMs, s.SteamFraction, p);
            s.EjectaSpeedMs = VacuumEjectaModel.CharacteristicSpeed(g.WallJetVelocityMs, p);
            s.EjectaAngleDeg = VacuumEjectaModel.SheetAngleDeg(g.StandoffM, g.ExitRadiusM, p);
            s.FlameColor = g.FlameColor;
            s.FlameIntensityTarget = PlumeModel.FlameLightIntensity(g.ThrustN, g.FlameIntensity, p) * s.Activation;
        }

        private BiomeParams ResolveBiome(ClusterState s, CelestialBody body, BodyParams bp)
        {
            bool moved = Math.Abs(s.Lat - s.BiomeLat) > 0.0005 || Math.Abs(s.Lon - s.BiomeLon) > 0.0005;
            if (s.BiomeName == null || (moved && s.BiomeAgeS > 1f))
            {
                s.BiomeName = ScienceUtil.GetExperimentBiome(body, s.Lat, s.Lon) ?? string.Empty;
                s.BiomeLat = s.Lat;
                s.BiomeLon = s.Lon;
                s.BiomeAgeS = 0f;
                LogBiome(body.bodyName, s.BiomeName);
            }
            if (string.IsNullOrEmpty(s.BiomeName)) return null;
            return bp.Biomes.TryGetValue(s.BiomeName, out BiomeParams biome) ? biome : null;
        }

        private void LogBiome(string bodyName, string biome)
        {
            if (!_biomesSeen.TryGetValue(bodyName, out HashSet<string> set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _biomesSeen[bodyName] = set;
            }
            if (set.Add(biome))
            {
                bool configured = GeConfig.Body(bodyName).Biomes.ContainsKey(biome);
                GeLog.Info("Biome rencontré : " + bodyName + " / « " + biome + " »" + (configured ? " (palette configurée)" : " (pas de palette : couleurs du corps ; à ajouter dans Configs/Bodies.cfg)"));
            }
        }

        /// <summary>Intègre les foyers sur la frame (front, fondus, lissage de l'ancrage, positions monde).</summary>
        public void Advance(float dt)
        {
            PhysicsParams p = GeConfig.Physics;
            for (int i = 0; i < MaxClusters; i++)
            {
                ClusterState s = _states[i];
                if (!s.InUse) continue;
                s.TimeSinceIgnitionS += dt;
                s.BiomeAgeS += dt;
                if (s.EnginesActive)
                {
                    s.FrontM = CloudFrontModel.AdvanceActive(s.FrontM, s.ImpingementRadiusM, s.WallJetVelocityMs, s.MaxCloudRadiusM, dt, p);
                    // Le nuage déjà formé ne disparaît pas quand la source faiblit (fusée qui s'élève, gaz réduits) :
                    // montée lissée vers la source, descente au rythme de la dissipation.
                    float target = s.Activation * Mathf.Max(s.Lift, s.SteamVisibility) * CloudFrontModel.IgnitionFade(s.TimeSinceIgnitionS, p);
                    float tau = target >= s.Intensity ? p.IntensitySmoothingS : Mathf.Max(CloudFrontModel.DissipationTime(s.FrontM, p) / 3f, p.IntensitySmoothingS);
                    if (s.Medium == MediumKind.Vacuum) tau = p.IntensitySmoothingS; // dans le vide, rien ne flotte : la nappe suit la source
                    s.Intensity = GeMath.Approach(s.Intensity, target, tau, dt);
                    s.Source = GeMath.Approach(s.Source, target, p.IntensitySmoothingS, dt);
                    s.FlameIntensity = GeMath.Approach(s.FlameIntensity, s.FlameIntensityTarget, 0.1f, dt);
                }
                else
                {
                    s.TimeSinceCutoffS += dt;
                    s.Source = 0f;
                    if (s.Medium == MediumKind.Vacuum)
                    {
                        s.Intensity = s.IntensityAtCutoff * CloudFrontModel.VacuumCutoffFade(s.TimeSinceCutoffS, p);
                        if (s.TimeSinceCutoffS >= p.VacuumLingerS) { s.Reset(); continue; }
                    }
                    else
                    {
                        s.FrontM = CloudFrontModel.AdvanceAfterCutoff(s.FrontM, s.FrontSpeedAtCutoffMs, s.TimeSinceCutoffS, s.MaxCloudRadiusM, dt, p);
                        s.Intensity = s.IntensityAtCutoff * CloudFrontModel.CutoffFade(s.TimeSinceCutoffS, s.DissipationTimeS);
                        if (s.TimeSinceCutoffS >= s.DissipationTimeS) { s.Reset(); continue; }
                    }
                    s.FlameIntensity = GeMath.Approach(s.FlameIntensity, 0f, p.FlameLightFadeS, dt);
                    if (s.FlameIntensity < 1e-3f) s.FlameIntensity = 0f;
                }
                UpdateWorld(s, dt);
            }
        }

        private static void UpdateWorld(ClusterState s, float dt)
        {
            const float Tau = 0.08f; // lissage de l'ancrage entre deux sondages (25 Hz)
            float k = dt > 0f ? 1f - Mathf.Exp(-dt / Tau) : 0f;
            if (s.AnchoredToDeck && s.AnchorTransform == null && s.Body != null)
            {
                // Pièce du pont détruite ou déchargée : le foyer est figé au sol, là où il était.
                s.Body.GetLatLonAlt(s.LastImpactWorld, out s.Lat, out s.Lon, out s.Alt);
                s.SLat = s.Lat; s.SLon = s.Lon; s.SAlt = s.Alt;
                s.NormalBody = SurfaceFrame.ToBody(s.Body, s.LastNormalWorld);
                s.AxisBody = SurfaceFrame.ToBody(s.Body, s.LastAxisWorld);
                s.AnchoredToDeck = false;
            }
            if (s.AnchoredToDeck)
            {
                s.SAnchorLocalPoint = Vector3.Lerp(s.SAnchorLocalPoint, s.AnchorLocalPoint, k);
                s.WorldImpact = s.AnchorTransform.TransformPoint(s.SAnchorLocalPoint);
                s.WorldNormal = s.AnchorTransform.TransformDirection(s.NormalLocal).normalized;
                s.WorldAxis = s.AnchorTransform.TransformDirection(s.AxisLocal).normalized;
            }
            else if (s.Body != null)
            {
                double dLon = s.Lon - s.SLon;
                if (dLon > 180.0) dLon -= 360.0; else if (dLon < -180.0) dLon += 360.0;
                s.SLat += (s.Lat - s.SLat) * k;
                s.SLon += dLon * k;
                s.SAlt += (s.Alt - s.SAlt) * k;
                s.WorldImpact = SurfaceFrame.WorldPosition(s.Body, s.SLat, s.SLon, s.SAlt);
                s.WorldNormal = SurfaceFrame.FromBody(s.Body, s.NormalBody).normalized;
                s.WorldAxis = SurfaceFrame.FromBody(s.Body, s.AxisBody).normalized;
            }
            if (s.HasNozzleLocal && s.LeadVessel != null && s.LeadVessel.loaded)
                s.WorldNozzle = s.LeadVessel.transform.TransformPoint(s.NozzleLocal);
            else
                s.WorldNozzle = s.WorldImpact - s.WorldAxis * s.SlantM;
            if (s.CloudAnchorSet)
            {
                if (s.AnchoredToDeck && s.AnchorTransform != null)
                {
                    s.WorldCloudAnchor = s.AnchorTransform.TransformPoint(s.CloudAnchorLocal);
                    s.WorldCloudUp = s.AnchorTransform.TransformDirection(s.CloudUpLocal).normalized;
                    s.WorldCloudNorth = CloudNorth(s, s.Body, s.WorldCloudAnchor, s.WorldCloudUp);
                }
                else if (s.Body != null)
                {
                    if (s.CALat == 0.0 && s.CALon == 0.0 && s.CAAlt == 0.0) s.Body.GetLatLonAlt(s.WorldCloudAnchor, out s.CALat, out s.CALon, out s.CAAlt);
                    s.WorldCloudAnchor = SurfaceFrame.WorldPosition(s.Body, s.CALat, s.CALon, s.CAAlt);
                    s.WorldCloudUp = LocalUp(s.Body, s.WorldCloudAnchor, s.WorldNormal);
                    s.WorldCloudNorth = CloudNorth(s, s.Body, s.WorldCloudAnchor, s.WorldCloudUp);
                }
            }
            s.LastImpactWorld = s.WorldImpact;
            s.LastNormalWorld = s.WorldNormal;
            s.LastAxisWorld = s.WorldAxis;
            s.LastNozzleWorld = s.WorldNozzle;
        }

        /// <summary>Direction horizontale fixe au sol pour orienter la grille du nuage (nord local, ou axe avant du pont).</summary>
        private static Vector3 CloudNorth(ClusterState s, CelestialBody body, Vector3 point, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
            Vector3 north;
            if (s.AnchoredToDeck && s.AnchorTransform != null) north = s.AnchorTransform.forward;
            else if (body != null) SurfaceFrame.Basis(body, point, out Vector3 _, out north, out Vector3 _);
            else north = Vector3.forward;
            north = Vector3.ProjectOnPlane(north, n);
            if (north.sqrMagnitude < 1e-6f) north = Vector3.ProjectOnPlane(Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.forward, n);
            return north.normalized;
        }

        /// <summary>
        /// Remplit le tableau du contrat, trié du plus proche au plus lointain de la caméra.
        /// Les foyers hors budget ou de portée sortent progressivement du rendu.
        /// </summary>
        public int BuildOutput(ImpingementCluster[] output, Vector3 cameraPos, RendererSettings settings, float dt)
        {
            int n = 0;
            for (int i = 0; i < MaxClusters; i++)
            {
                ClusterState s = _states[i];
                if (!s.InUse) continue;
                s.CameraDistanceM = Vector3.Distance(cameraPos, s.WorldImpact);
                _order[n++] = i;
            }
            // Tri par insertion (n ≤ 32, sans allocation).
            for (int i = 1; i < n; i++)
            {
                int v = _order[i];
                float d = _states[v].CameraDistanceM;
                int j = i - 1;
                while (j >= 0 && _states[_order[j]].CameraDistanceM > d) { _order[j + 1] = _order[j]; j--; }
                _order[j + 1] = v;
            }

            int rank = 0, count = 0;
            float fadeInK = dt > 0f ? 1f - Mathf.Exp(-dt / 0.5f) : 0f;
            float fadeOutK = dt > 0f ? 1f - Mathf.Exp(-dt / 2.5f) : 0f;
            for (int oi = 0; oi < n; oi++)
            {
                ClusterState s = _states[_order[oi]];
                bool wanted = rank < settings.MaxRenderedClusters && s.CameraDistanceM <= settings.MaxRenderDistanceM + s.FrontM;
                if (wanted) rank++;
                float fadeStart = Mathf.Max(0f, settings.MaxRenderDistanceM - Mathf.Max(150f, settings.MaxRenderDistanceM * 0.15f));
                float distanceFade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fadeStart, settings.MaxRenderDistanceM + s.FrontM, s.CameraDistanceM));
                float target = wanted ? distanceFade : 0f;
                s.PriorityFade += (target - s.PriorityFade) * (target > s.PriorityFade ? fadeInK : fadeOutK);
                if (s.PriorityFade < 0.002f && !wanted) { s.PriorityFade = 0f; continue; }
                if (count >= output.Length) break;
                Fill(ref output[count++], s);
            }
            return count;
        }

        /// <summary>Pas de tir réels (sorties du déflecteur), fournis par le classifieur ; null dans les tests.</summary>
        public GroundBlastFx.Surface.LaunchPadRegistry Pads;

        private void Fill(ref ImpingementCluster c, ClusterState s)
        {
            bool on = s.EnginesActive;
            c.Id = s.Id;
            c.EnginesActive = on;
            c.TimeSinceIgnitionS = s.TimeSinceIgnitionS;
            c.TimeSinceCutoffS = on ? 0f : s.TimeSinceCutoffS;
            c.IsDemo = s.IsDemo;

            c.ImpactPointWorld = s.WorldImpact;
            c.SurfaceNormalWorld = s.WorldNormal;
            c.PlumeAxisWorld = s.WorldAxis;
            c.NozzleCenterWorld = s.WorldNozzle;
            c.StandoffM = s.HasNozzleLocal ? Vector3.Distance(s.WorldNozzle, s.WorldImpact) : s.SlantM;
            c.ImpingementRadiusM = s.ImpingementRadiusM;
            c.CloudFrontRadiusM = s.FrontM;
            c.MaxCloudRadiusM = s.MaxCloudRadiusM;
            c.AnchorTransform = s.AnchorTransform;

            c.Surface = s.Surface;
            c.Medium = s.Medium;
            c.EngineCount = s.EngineCount;
            c.TotalThrustN = on ? s.ThrustN : 0f;
            c.MassFlowKgS = on ? s.MassFlowKgS : 0f;
            c.ExhaustVelocityMs = s.ExhaustVelocityMs;
            c.ImpingementPressurePa = on ? s.ImpingementPressurePa : 0f;
            c.WallJetVelocityMs = on ? s.WallJetVelocityMs : 0f;
            c.AmbientPressurePa = s.AmbientPressurePa;
            c.AmbientDensityKgM3 = s.AmbientDensityKgM3;
            c.GravityMs2 = s.GravityMs2;
            c.WindWorldMs = s.Medium == MediumKind.Atmosphere && s.Body != null ? WindField.At(s.Body, s.WorldImpact) : Vector3.zero;

            c.Intensity01 = Mathf.Clamp01(s.Intensity * s.PriorityFade);
            c.Erodibility01 = s.Erodibility;
            c.SteamFraction01 = s.SteamFraction;
            c.DustAlbedoA = s.DustA;
            c.DustAlbedoB = s.DustB;
            c.TrenchDirectionWorld = s.Surface == SurfaceKind.LaunchPad && s.TrenchHeadingDeg >= 0f && s.Body != null
                ? SurfaceFrame.HeadingDirection(s.Body, s.WorldImpact, s.TrenchHeadingDeg)
                : Vector3.zero;
            c.FlameLightColor = s.FlameColor;
            c.FlameLightIntensity = s.FlameIntensity * s.PriorityFade;
            c.VacuumEjectaSpeedMs = s.EjectaSpeedMs;
            c.VacuumEjectaAngleDeg = s.EjectaAngleDeg;

            c.CameraDistanceM = s.CameraDistanceM;
            float scale = Mathf.Clamp(s.MaxCloudRadiusM / 100f, 0.5f, 4f);
            c.LodLevel = s.CameraDistanceM < 600f * scale ? 0 : (s.CameraDistanceM < 1800f * scale ? 1 : 2);

            c.CloudAnchorWorld = s.CloudAnchorSet ? s.WorldCloudAnchor : s.WorldImpact;
            c.CloudNorthWorld = s.WorldCloudNorth.sqrMagnitude > 0.5f ? s.WorldCloudNorth : Vector3.ProjectOnPlane(Vector3.forward, s.WorldNormal).normalized;
            c.CloudUpWorld = s.WorldCloudUp.sqrMagnitude > 0.5f ? s.WorldCloudUp : s.WorldNormal;
            // Air raréfié : dès ~30 kPa les grains fusent en nappe (Duna 6,7 kPa ≈ 0,85), le vide vaut 1.
            c.ThinAir01 = s.Medium == MediumKind.Vacuum ? 1f : 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2000f, 30000f, s.AmbientPressurePa));
            float end = on || s.DissipationTimeS <= 0f ? 1f : Mathf.Clamp01((s.DissipationTimeS - s.TimeSinceCutoffS) / (0.25f * s.DissipationTimeS));
            if (s.Medium == MediumKind.Vacuum) end = Mathf.Clamp01(s.Intensity * 4f);
            c.Visibility01 = Mathf.Clamp01(s.PriorityFade * end);
            c.DissipationTimeS = on ? CloudFrontModel.DissipationTime(s.FrontM, GeConfig.Physics) * (s.Surface == SurfaceKind.LaunchPad ? GeConfig.Physics.PadSteamDissipationFactor : 1f) : s.DissipationTimeS;
            c.Source01 = Mathf.Clamp01(s.Source * s.PriorityFade);

            // Sorties réelles du déflecteur : seulement si le nuage est né sur un pas de tir enregistré
            // (une démo « décollage » posée à côté du pas garde le cap configuré).
            c.DeflectorOutletCount = 0;
            c.DeflectorGround = Vector4.zero;
            c.DeflectorDir0World = c.DeflectorDir1World = Vector3.zero;
            if (s.Surface == SurfaceKind.LaunchPad && Pads != null && Pads.Count > 0)
            {
                // 1.0.1 : le pas trouvé est gardé pour tout le foyer, et le centre du nuage peut être un peu hors du rayon
                // du pas (plusieurs moteurs, fusée qui dérive au décollage). Sinon le nuage perdait les vraies bouches et
                // repassait sur le cap de secours : deux paires de jets dans deux directions.
                var pad = s.PadRef;
                if (pad == null || pad.Fx == null || pad.Deck == null)
                {
                    pad = Pads.Nearest(c.CloudAnchorWorld, 200f, out float d);
                    if (pad != null && d > pad.RadiusM + PadOutletMarginM) pad = null;
                    s.PadRef = pad;
                }
                if (pad != null)
                {
                    for (int i = 0; i < pad.OutletCount; i++)
                    {
                        if (pad.Outlets[i] == null) continue;
                        if (c.DeflectorOutletCount == 0)
                        {
                            c.DeflectorOutlet0World = pad.Outlets[i].position;
                            c.DeflectorGround.x = pad.GroundDropM[i]; c.DeflectorGround.y = pad.GroundEdgeM[i];
                            c.DeflectorDir0World = pad.OutletDir[i];
                        }
                        else
                        {
                            c.DeflectorOutlet1World = pad.Outlets[i].position;
                            c.DeflectorGround.z = pad.GroundDropM[i]; c.DeflectorGround.w = pad.GroundEdgeM[i];
                            c.DeflectorDir1World = pad.OutletDir[i];
                        }
                        c.DeflectorOutletCount++;
                    }
                }
            }
        }
    }
}
