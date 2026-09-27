using System;
using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Model;
using UnityEngine;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Évalue chaque jet qui touche une surface (modèle §3.2) puis fusionne les taches proches en foyers (§3.3) :
    /// position = moyenne pondérée par la poussée ; rayon = enveloppe des taches ; poussée et débit = sommes.
    /// Les foyers sous le seuil d'activation (au-dessus de H_act) sont écartés.
    /// Tableaux réutilisés : aucune allocation en régime établi.
    /// </summary>
    public sealed class ImpactAggregator
    {
        private const int KindCount = 6;

        private readonly JetClusterer _clusterer = new JetClusterer();
        private int[] _jetIndex = new int[64];
        private JetEval[] _eval = new JetEval[64];
        private float[] _x = new float[64], _y = new float[64], _z = new float[64], _r = new float[64];
        private int[] _key = new int[64], _group = new int[64];
        private GroupData[] _groups = new GroupData[16];
        private float[] _kindWeight = new float[16 * KindCount];
        private float[] _leadWeight = new float[16];
        private float[] _deckWeight = new float[16];
        private bool[] _keep = new bool[16];

        public GroupData[] Groups => _groups;
        public int GroupCount { get; private set; }
        public int HitJetCount { get; private set; }
        public int RejectedGroups { get; private set; }

        public JetEval EvalOfJet(int jetIndex, out bool hit)
        {
            for (int k = 0; k < HitJetCount; k++)
            {
                if (_jetIndex[k] == jetIndex) { hit = true; return _eval[k]; }
            }
            hit = false;
            return default(JetEval);
        }

        public void Process(JetBuffer jets, JetHit[] hits, CelestialBody body)
        {
            PhysicsParams p = GeConfig.Physics;
            int n = jets.Count;
            EnsureJetCapacity(n);
            int m = 0;
            for (int i = 0; i < n; i++)
            {
                if (!hits[i].Hit) continue;
                ref JetSample j = ref jets.Items[i];
                ref JetHit h = ref hits[i];
                float pAmb = j.ForceVacuum ? 0f : j.AmbientPressureAtNozzlePa;
                var input = new JetInput
                {
                    ThrustN = j.ThrustN,
                    ExhaustVelocityMs = j.ExhaustVelocityMs,
                    ExitRadiusM = j.ExitRadiusM,
                    StandoffM = h.DistanceM,
                    CosAlpha = Mathf.Clamp01(Vector3.Dot(-j.AxisWorld, h.NormalWorld)),
                    AmbientPressurePa = pAmb,
                };
                PlumeModel.Evaluate(ref input, p, out JetImpingement res);
                _jetIndex[m] = i;
                _eval[m] = new JetEval
                {
                    ImpingementRadiusM = res.ImpingementRadiusM,
                    WallJetVelocityMs = res.WallJetVelocityMs,
                    CosAlpha = input.CosAlpha,
                    MassFlowKgS = res.MassFlowKgS,
                };
                _x[m] = h.PointWorld.x;
                _y[m] = h.PointWorld.y;
                _z[m] = h.PointWorld.z;
                _r[m] = res.ImpingementRadiusM;
                _key[m] = j.Key;
                m++;
            }
            HitJetCount = m;

            int groups = _clusterer.Cluster(m, _x, _y, _z, _r, _key, p.ClusterMergeFactor, _group);
            EnsureGroupCapacity(groups);
            for (int g = 0; g < groups; g++)
            {
                _groups[g] = default(GroupData);
                _leadWeight[g] = -1f;
                _deckWeight[g] = -1f;
                for (int k = 0; k < KindCount; k++) _kindWeight[g * KindCount + k] = 0f;
            }

            // Sommes pondérées par la poussée.
            for (int k = 0; k < m; k++)
            {
                int i = _jetIndex[k];
                int g = _group[k];
                ref JetSample j = ref jets.Items[i];
                ref JetHit h = ref hits[i];
                ref GroupData d = ref _groups[g];
                float w = j.ThrustN;
                d.Key = j.Key;
                d.IsDemo |= j.IsDemo;
                d.EngineCount++;
                d.ThrustN += w;
                d.MassFlowKgS += _eval[k].MassFlowKgS;
                d.ImpactWorld += h.PointWorld * w;
                d.NormalWorld += h.NormalWorld * w;
                d.AxisWorld += j.AxisWorld * w;
                d.NozzleWorld += j.NozzleWorld * w;
                d.StandoffM += h.DistanceM * w;
                d.ExitRadiusM += j.ExitRadiusM * w;
                d.WallJetVelocityMs += _eval[k].WallJetVelocityMs * w;
                d.CosAlpha += _eval[k].CosAlpha * w;
                d.FlameColor += j.FlameColor * w;
                d.FlameIntensity += j.FlameIntensity * w;
                d.SteamBonus += j.SteamBonus * w;
                d.Soot += j.Soot * w;
                d.ForceVacuum |= j.ForceVacuum;
                if (j.ForcedGravityMs2 > 0f) d.ForcedGravityMs2 = j.ForcedGravityMs2;
                if (d.SiteKey == null && h.SiteKey != null) d.SiteKey = h.SiteKey;
                int kind = (int)h.Surface;
                if (kind >= 0 && kind < KindCount) _kindWeight[g * KindCount + kind] += w;
                if (w > _leadWeight[g]) { _leadWeight[g] = w; d.LeadVessel = j.Vessel; }
                if (h.Surface == SurfaceKind.VesselDeck && h.AnchorTransform != null && w > _deckWeight[g]) { _deckWeight[g] = w; d.AnchorTransform = h.AnchorTransform; }
            }

            int kept = 0;
            RejectedGroups = 0;
            for (int g = 0; g < groups; g++)
            {
                ref GroupData d = ref _groups[g];
                float inv = d.ThrustN > 0f ? 1f / d.ThrustN : 0f;
                d.WeightSum = d.ThrustN;
                d.ImpactWorld *= inv;
                d.NormalWorld = d.NormalWorld.normalized;
                d.AxisWorld = d.AxisWorld.normalized;
                d.NozzleWorld *= inv;
                d.StandoffM *= inv;
                d.ExitRadiusM *= inv;
                d.WallJetVelocityMs *= inv;
                d.CosAlpha *= inv;
                d.FlameColor *= inv;
                d.FlameColor.a = 1f;
                d.FlameIntensity *= inv;
                d.SteamBonus *= inv;
                d.Soot *= inv;

                // Surface : celle qui reçoit la plus grande part de la poussée.
                float bestW = -1f;
                for (int k = 0; k < KindCount; k++)
                {
                    float kw = _kindWeight[g * KindCount + k];
                    if (kw > bestW) { bestW = kw; d.Surface = (SurfaceKind)k; }
                }
                d.SurfaceWeightBest = bestW;
                if (d.Surface != SurfaceKind.VesselDeck) d.AnchorTransform = null;

                // Activation : H_act de la poussée totale du foyer, à la pression au point d'impact.
                double alt = FlightGlobals.getAltitudeAtPos((Vector3d)d.ImpactWorld, body);
                float pAmb = d.ForceVacuum || !body.atmosphere ? 0f : (float)(body.GetPressure(Math.Max(alt, 0.0)) * 1000.0);
                // Souffle du foyer entier, selon sa poussée totale (et non la moyenne des souffles de chaque tuyère).
                float veGroup = d.MassFlowKgS > 0f ? d.ThrustN / d.MassFlowKgS : 0f;
                d.WallJetVelocityMs = PlumeModel.GroupWallJetVelocity(d.ThrustN * d.CosAlpha, veGroup, d.StandoffM, pAmb, d.WallJetVelocityMs, p);
                float hAct = PlumeModel.ActivationHeight(d.ThrustN, pAmb, p);
                _keep[g] = PlumeModel.ActivationFade(d.StandoffM, hAct, p) > 0.001f;
                if (!_keep[g]) RejectedGroups++;
            }

            // Enveloppe des taches (2e passe, centre connu).
            for (int k = 0; k < m; k++)
            {
                int g = _group[k];
                ref GroupData d = ref _groups[g];
                float dist = Vector3.Distance(hits[_jetIndex[k]].PointWorld, d.ImpactWorld) + _eval[k].ImpingementRadiusM;
                if (dist > d.EnvelopeRadiusM) d.EnvelopeRadiusM = dist;
            }

            // Compactage des foyers retenus.
            for (int g = 0; g < groups; g++)
            {
                if (!_keep[g]) continue;
                if (kept != g) _groups[kept] = _groups[g];
                kept++;
            }
            GroupCount = kept;
        }

        private void EnsureJetCapacity(int n)
        {
            if (_jetIndex.Length >= n) return;
            int c = _jetIndex.Length;
            while (c < n) c *= 2;
            Array.Resize(ref _jetIndex, c);
            Array.Resize(ref _eval, c);
            Array.Resize(ref _x, c);
            Array.Resize(ref _y, c);
            Array.Resize(ref _z, c);
            Array.Resize(ref _r, c);
            Array.Resize(ref _key, c);
            Array.Resize(ref _group, c);
        }

        private void EnsureGroupCapacity(int n)
        {
            if (_groups.Length >= n) return;
            int c = _groups.Length;
            while (c < n) c *= 2;
            Array.Resize(ref _groups, c);
            Array.Resize(ref _kindWeight, c * KindCount);
            Array.Resize(ref _leadWeight, c);
            Array.Resize(ref _deckWeight, c);
            Array.Resize(ref _keep, c);
        }
    }
}
