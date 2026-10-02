using System;
using System.Collections.Generic;
using System.Text;
using GroundBlastFx.Contracts;
using GroundBlastFx.Core;
using GroundBlastFx.Model;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace GroundBlastFx.Surface
{
    /// <summary>
    /// Sondage de la surface sous chaque jet :
    /// - 1 rayon central + une couronne de 6 à 8 rayons (selon la qualité) inclinés du demi-angle du jet ;
    /// - rayons lancés en lot via RaycastCommand (jobs), repli sur Physics.Raycast en cas de problème ;
    /// - décor (Local Scenery, TerrainColliders) : jamais les pièces, donc jamais le vaisseau lui-même ;
    /// - pont d'un autre vaisseau (barge) : rayon central supplémentaire sur le calque des pièces, en excluant le vaisseau du jet ;
    /// - eau : l'océan n'a pas de collider, intersection rayon/sphère au niveau de la mer quand body.ocean est vrai ;
    ///   l'impact le plus proche (terrain, pont ou eau) l'emporte.
    /// Schedule() est appelé dans Update, Complete() dans LateUpdate : les jobs tournent pendant le reste de la frame.
    /// Aucune allocation managée en régime établi (tableaux natifs persistants, agrandis seulement si besoin).
    /// </summary>
    public sealed class SurfaceProbe : IDisposable
    {
        private const int MaxRing = 8;

        private NativeArray<RaycastCommand> _commands;
        private NativeArray<RaycastHit> _results;
        private int _capacity;
        private JobHandle _handle;
        private bool _pending;
        private bool _loggedPadSurroundings;
        private int _jetCount;
        private int _raysPerJet;
        private bool _useJobs = true;
        private readonly RaycastHit[] _deckHits = new RaycastHit[16];
        private int _sceneryMask;
        private int _partMask;
        private float[] _ringCos = new float[MaxRing];
        private float[] _ringSin = new float[MaxRing];
        private int _ringCountBuilt = -1;

        public int LastRayCount { get; private set; }
        public bool UsingJobs => _useJobs;
        public int SceneryMask => _sceneryMask;

        public void Init()
        {
            int scenery = (1 << 15) | (1 << 30); // masque de ModuleSurfaceFX stock (Local Scenery + 30)
            int local = LayerMask.NameToLayer("Local Scenery");
            int terrain = LayerMask.NameToLayer("TerrainColliders");
            if (local >= 0) scenery |= 1 << local;
            if (terrain >= 0) scenery |= 1 << terrain;
            _sceneryMask = scenery;
            int parts = 1 << 0;
            int partsLayer = LayerMask.NameToLayer("Parts");
            if (partsLayer >= 0) parts |= 1 << partsLayer;
            _partMask = parts;

            var sb = new StringBuilder("Calques Unity : ");
            for (int i = 0; i < 32; i++)
            {
                string n = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(n)) continue;
                sb.Append(i).Append('=').Append(n).Append(i < 31 ? ", " : "");
            }
            GeLog.FileOnly(sb.ToString());
            GeLog.Info("Sondage : masque décor 0x" + _sceneryMask.ToString("X8") + ", masque pièces 0x" + _partMask.ToString("X8"));
        }

        public void Schedule(JetBuffer jets, int ringRays)
        {
            if (_pending) Complete(null, null, null, null); // ne devrait pas arriver : sécurité
            _jetCount = jets.Count;
            ringRays = Mathf.Clamp(ringRays, 0, MaxRing);
            _raysPerJet = 1 + ringRays;
            BuildRing(ringRays);
            int needed = _jetCount * _raysPerJet;
            LastRayCount = needed;
            if (needed == 0) return;
            EnsureCapacity(needed);

            for (int i = 0; i < _jetCount; i++)
            {
                ref JetSample j = ref jets.Items[i];
                int b = i * _raysPerJet;
                Vector3 axis = j.AxisWorld;
                float len = j.ProbeLengthM;
                _commands[b] = new RaycastCommand(j.NozzleWorld, axis, len, _sceneryMask, 1);
                if (ringRays == 0) continue;
                // Couronne : rayons inclinés du demi-angle du jet (borné à 30° pour rester sous le vaisseau).
                float theta = Mathf.Min(PlumeModel.HalfAngleRad(j.AmbientPressureAtNozzlePa, Config.GeConfig.Physics), 30f * Mathf.Deg2Rad);
                float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
                Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 v = Vector3.Cross(axis, u);
                for (int k = 0; k < ringRays; k++)
                {
                    Vector3 dir = axis * ct + (u * _ringCos[k] + v * _ringSin[k]) * st;
                    _commands[b + 1 + k] = new RaycastCommand(j.NozzleWorld, dir, len / ct, _sceneryMask, 1);
                }
            }
            for (int i = needed; i < _capacity; i++) _commands[i] = new RaycastCommand(Vector3.zero, Vector3.down, 0f, 0, 1);

            if (_useJobs)
            {
                try
                {
                    _handle = RaycastCommand.ScheduleBatch(_commands, _results, 8);
                    _pending = true;
                    return;
                }
                catch (Exception e)
                {
                    _useJobs = false;
                    GeLog.Warn("RaycastCommand indisponible, repli sur Physics.Raycast : " + e.Message);
                }
            }
            RunSynchronously(needed);
        }

        private void RunSynchronously(int count)
        {
            for (int i = 0; i < count; i++)
            {
                RaycastCommand c = _commands[i];
                RaycastHit hit;
                if (!Physics.Raycast(c.from, c.direction, out hit, c.distance, c.layerMask, QueryTriggerInteraction.Ignore)) hit = default(RaycastHit);
                _results[i] = hit;
            }
        }

        /// <summary>Termine les jobs et remplit <paramref name="hits"/> (même indice que les jets).</summary>
        public void Complete(JetBuffer jets, JetHit[] hits, SurfaceClassifier classifier, CelestialBody body)
        {
            if (_pending)
            {
                _handle.Complete();
                _pending = false;
            }
            if (jets == null || hits == null) return;
            int n = Mathf.Min(_jetCount, jets.Count);
            bool otherVessels = FlightGlobals.VesselsLoaded.Count > 1;
            for (int i = 0; i < n; i++)
            {
                ref JetSample j = ref jets.Items[i];
                JetHit h = default(JetHit);
                int b = i * _raysPerJet;
                RaycastHit center = _results[b];
                float best = float.MaxValue;
                if (center.collider != null)
                {
                    best = center.distance;
                    h.Hit = true;
                    h.PointWorld = center.point;
                    h.DistanceM = center.distance;
                    h.Collider = center.collider;
                    h.Surface = classifier.ClassifyScenery(center.collider, out h.SiteKey);
                }

                // Normale moyenne : rayon central + couronne (lisse les facettes du terrain). Chaque normale est tournée
                // vers le haut local : un collider aux faces inversées (maillage de terrain d'un autre mod) donnerait
                // sinon une normale vers le bas, et le nuage serait simulé tête en bas, sous le sol (invisible).
                Vector3 upHere = body != null && center.collider != null ? ((Vector3)(center.point - (Vector3)body.position)).normalized : -j.AxisWorld;
                Vector3 nsum = center.collider != null ? FaceUp(center.normal, upHere, center.collider) : Vector3.zero;
                int ringHits = 0;
                for (int k = 1; k < _raysPerJet; k++)
                {
                    RaycastHit r = _results[b + k];
                    if (r.collider == null) continue;
                    ringHits++;
                    nsum += FaceUp(r.normal, upHere, r.collider);
                }
                h.RingHits = ringHits;
                if (h.Hit) h.NormalWorld = nsum.sqrMagnitude > 1e-6f ? nsum.normalized : upHere;

                // Pont d'un autre vaisseau (barge) : le plus proche des pièces qui n'appartiennent pas au vaisseau du jet.
                if (otherVessels && !j.IsDemo)
                {
                    float maxDist = Mathf.Min(j.ProbeLengthM, best);
                    int count = Physics.RaycastNonAlloc(j.NozzleWorld, j.AxisWorld, _deckHits, maxDist, _partMask, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < count; k++)
                    {
                        RaycastHit r = _deckHits[k];
                        if (r.collider == null || r.distance >= best) continue;
                        Part p = FlightGlobals.GetPartUpwardsCached(r.collider.gameObject);
                        if (p == null || p.vessel == null || p.vessel == j.Vessel) continue;
                        best = r.distance;
                        h.Hit = true;
                        h.PointWorld = r.point;
                        h.NormalWorld = r.normal;
                        h.DistanceM = r.distance;
                        h.Collider = r.collider;
                        h.Surface = SurfaceKind.VesselDeck;
                        h.AnchorTransform = p.transform;
                        h.SiteKey = null;
                    }
                }

                // Eau : intersection rayon / sphère du niveau de la mer.
                if (body != null && body.ocean)
                {
                    if (RaySeaLevel(body, j.NozzleWorld, j.AxisWorld, Mathf.Min(j.ProbeLengthM, best), out float t, out Vector3 up))
                    {
                        h.Hit = true;
                        h.PointWorld = j.NozzleWorld + j.AxisWorld * t;
                        h.NormalWorld = up;
                        h.DistanceM = t;
                        h.Collider = null;
                        h.Surface = SurfaceKind.Water;
                        h.AnchorTransform = null;
                        h.SiteKey = null;
                    }
                }

                if (h.Hit)
                {
                    if (j.ForcedSurface != SurfaceKind.Unknown)
                    {
                        h.Surface = j.ForcedSurface;
                        if (h.SiteKey == null) h.SiteKey = j.SiteKey;
                    }
                    else
                    {
                        if (j.PadLatched && (h.Surface == SurfaceKind.Terrain || h.Surface == SurfaceKind.Structure || h.Surface == SurfaceKind.LaunchPad || h.Surface == SurfaceKind.Unknown))
                        {
                            h.Surface = SurfaceKind.LaunchPad;
                            if (h.SiteKey == null) h.SiteKey = j.SiteKey;
                        }
                        // Abords d'un pas de tir connu (au KSC, tout le terrain autour du pas est un seul collider
                        // « launchpad ground ») : hors du rayon du pas, c'est du terrain → poussière, pas de déluge.
                        if (h.Surface == SurfaceKind.LaunchPad && classifier.Pads.Count > 0)
                        {
                            bool onPad = classifier.Pads.IsOnPad(h.PointWorld, out bool known);
                            if (known && !onPad)
                            {
                                h.Surface = SurfaceKind.Terrain;
                                h.SiteKey = null;
                                if (!_loggedPadSurroundings)
                                {
                                    _loggedPadSurroundings = true;
                                    GeLog.Info("Surface : abords d'un pas de tir (hors de son rayon) → Terrain (poussière).");
                                }
                            }
                        }
                    }
                }
                hits[i] = h;
            }
        }

        private bool _loggedFlippedNormal;

        /// <summary>Normale tournée vers le haut local ; journalise une fois un collider à faces inversées.</summary>
        private Vector3 FaceUp(Vector3 n, Vector3 up, Collider c)
        {
            if (Vector3.Dot(n, up) >= 0f) return n;
            if (!_loggedFlippedNormal)
            {
                _loggedFlippedNormal = true;
                GeLog.Info("Surface : normale orientée vers le bas corrigée (collider « " + (c != null ? c.name : "?") + " », faces inversées).");
            }
            return -n;
        }

        /// <summary>Intersection du rayon avec la sphère de rayon body.Radius (calculs en double : grandes distances).</summary>
        public static bool RaySeaLevel(CelestialBody body, Vector3 origin, Vector3 dir, float maxDist, out float t, out Vector3 normal)
        {
            t = 0f;
            normal = Vector3.up;
            Vector3d c = body.position;
            double ox = origin.x - c.x, oy = origin.y - c.y, oz = origin.z - c.z;
            double r = body.Radius;
            double cc = ox * ox + oy * oy + oz * oz - r * r;
            if (cc <= 0.0) return false; // tuyère sous le niveau de la mer
            double bb = ox * dir.x + oy * dir.y + oz * dir.z;
            double disc = bb * bb - cc;
            if (disc < 0.0) return false;
            double hitT = -bb - Math.Sqrt(disc);
            if (hitT < 0.0 || hitT > maxDist) return false;
            t = (float)hitT;
            double px = ox + dir.x * hitT, py = oy + dir.y * hitT, pz = oz + dir.z * hitT;
            double len = Math.Sqrt(px * px + py * py + pz * pz);
            normal = new Vector3((float)(px / len), (float)(py / len), (float)(pz / len));
            return true;
        }

        private void BuildRing(int ringRays)
        {
            if (ringRays == _ringCountBuilt) return;
            _ringCountBuilt = ringRays;
            for (int k = 0; k < ringRays; k++)
            {
                double a = 2.0 * Math.PI * k / ringRays;
                _ringCos[k] = (float)Math.Cos(a);
                _ringSin[k] = (float)Math.Sin(a);
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (_capacity >= needed && _commands.IsCreated) return;
            int cap = Mathf.Max(64, _capacity);
            while (cap < needed) cap *= 2;
            DisposeArrays();
            _commands = new NativeArray<RaycastCommand>(cap, Allocator.Persistent);
            _results = new NativeArray<RaycastHit>(cap, Allocator.Persistent);
            _capacity = cap;
        }

        private void DisposeArrays()
        {
            if (_pending) { _handle.Complete(); _pending = false; }
            if (_commands.IsCreated) _commands.Dispose();
            if (_results.IsCreated) _results.Dispose();
            _capacity = 0;
        }

        public void Dispose()
        {
            DisposeArrays();
        }
    }
}
