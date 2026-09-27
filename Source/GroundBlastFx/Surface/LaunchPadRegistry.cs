using System;
using System.Collections.Generic;
using System.Reflection;
using GroundBlastFx.Core;
using UnityEngine;

namespace GroundBlastFx.Surface
{
    /// <summary>
    /// Pas de tir réels de la scène (composant stock <c>LaunchPadFX</c>, posé sur le collider « PadFXReceiver »).
    /// Deux usages (D-CL-018) :
    /// - distinguer le pas lui-même du grand collider de terrain qui l'entoure au KSC
    ///   (« model_launchpad_ground_collider ») : hors du rayon du pas, c'est du terrain, donc de la poussière ;
    /// - donner les sorties RÉELLES du déflecteur de flammes : KSP y place ses propres émetteurs de fumée
    ///   (champ protégé <c>ps</c> de LaunchPadFX), que GroundBlastFx masque et remplace.
    /// Les pas sont enregistrés quand le classifieur rencontre leur collider (aucune recherche dans la scène à chaque
    /// image, aucune allocation en régime établi).
    /// </summary>
    public sealed class LaunchPadRegistry
    {
        public const int MaxOutlets = 2;

        public sealed class Pad
        {
            public LaunchPadFX Fx;
            public Collider Deck;
            public Transform StaticRoot;
            public Vector3 ZoneCenter;
            public float RadiusM;
            public readonly Transform[] Outlets = new Transform[MaxOutlets];
            public int OutletCount;
            /// <summary>Dénivelé (m) entre chaque bouche et le terrain environnant, et distance où il est à moitié franchi.</summary>
            public readonly float[] GroundDropM = new float[MaxOutlets];
            public readonly float[] GroundEdgeM = new float[MaxOutlets];
            /// <summary>Sens de sortie (horizontal) : émission de la fumée stock si elle est rasante, sinon depuis le centre du pas.</summary>
            public readonly Vector3[] OutletDir = new Vector3[MaxOutlets];
        }

        // Décor (15 Local Scenery) + terrain (28 TerrainColliders) : pas les pièces, pas les déclencheurs.
        private const int GroundMask = (1 << 15) | (1 << 28);
        private const int ProfileSteps = 18;
        private const float ProfileStepM = 5f;
        private static readonly float[] Heights = new float[ProfileSteps];

        /// <summary>
        /// Profil du sol devant une bouche : rayons verticaux tous les 5 m sur 90 m dans la direction d'émission.
        /// Le pas du KSC est surélevé : sans ce profil, la vapeur resterait au niveau de la table et le nuage
        /// « flotterait » au-dessus du terrain avec un dessous plat.
        /// </summary>
        private static void MeasureGround(Pad pad, int i, Vector3 center, Vector3 up)
        {
            Transform o = pad.Outlets[i];
            Vector3 dir = Vector3.ProjectOnPlane(o.position - center, up);
            if (dir.sqrMagnitude < 1e-4f) dir = Vector3.ProjectOnPlane(o.forward, up);
            dir.Normalize();
            int n = 0;
            for (int k = 0; k < ProfileSteps; k++)
            {
                Vector3 top = o.position + dir * ((k + 1) * ProfileStepM) + up * 60f;
                Heights[k] = float.NaN;
                if (Physics.Raycast(top, -up, out RaycastHit hit, 200f, GroundMask, QueryTriggerInteraction.Ignore))
                { Heights[k] = Vector3.Dot(hit.point - o.position, up); n++; }
            }
            pad.GroundDropM[i] = 0f;
            pad.GroundEdgeM[i] = 20f;
            if (n < 4) return;
            // Niveau du terrain environnant : moyenne des 4 derniers points valides (loin de la structure du pas).
            float far = 0f; int nf = 0;
            for (int k = ProfileSteps - 1; k >= 0 && nf < 4; k--) if (!float.IsNaN(Heights[k])) { far += Heights[k]; nf++; }
            float drop = Mathf.Clamp(-far / nf, 0f, 30f);
            if (drop < 0.5f) return;
            pad.GroundDropM[i] = drop;
            for (int k = 0; k < ProfileSteps; k++)
                if (!float.IsNaN(Heights[k]) && Heights[k] < -0.5f * drop) { pad.GroundEdgeM[i] = (k + 1) * ProfileStepM; break; }
        }

        private static readonly FieldInfo PsField =
            typeof(LaunchPadFX).GetField("ps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly List<Pad> _pads = new List<Pad>();

        public int Count => _pads.Count;

        public void Clear() { _pads.Clear(); }

        /// <summary>Appelé par le classifieur à la première rencontre d'un collider de pas de tir.</summary>
        public void Register(Collider deck)
        {
            if (deck == null) return;
            // Stock : composant sur l'objet du collider tagué (comme ModuleSurfaceFX) ; pas moddés : parfois sur un parent.
            LaunchPadFX fx = deck.GetComponent<LaunchPadFX>();
            if (fx == null) fx = deck.GetComponentInParent<LaunchPadFX>(); // pas d'opérateur ?? sur les objets Unity
            if (fx == null) return;
            for (int i = 0; i < _pads.Count; i++) if (_pads[i].Fx == fx) return;
            var pad = new Pad { Fx = fx, Deck = deck };
            Bounds b = deck.bounds;
            pad.RadiusM = Mathf.Clamp(Mathf.Max(b.extents.x, b.extents.z) + 12f, 25f, 90f);
            Vector3 center = b.center;
            pad.ZoneCenter = center;

            // Émetteurs de fumée stock : placés par les artistes de KSP aux bouches du déflecteur.
            try
            {
                var systems = PsField != null ? PsField.GetValue(fx) as ParticleSystem[] : null;
                if (systems != null)
                {
                    // On garde les deux émetteurs les plus éloignés l'un de l'autre (deux sorties opposées) ou le seul.
                    for (int i = 0; i < systems.Length && pad.OutletCount < MaxOutlets; i++)
                    {
                        if (systems[i] == null) continue;
                        Transform t = systems[i].transform;
                        float horizontal = Vector3.ProjectOnPlane(t.position - center, deck.transform.up).magnitude;
                        if (horizontal >= 3f && horizontal <= 150f) AddOutlet(pad, t);
                    }
                }
                // Kerbal Konstructs déclare les bouches dans STATIC/MODULE PadSmoke ; le champ ps de LaunchPadFX
                // peut rester vide. Chercher les transforms nommés dans le modèle exact, pas une direction estimée.
                AddConfiguredOutlets(pad);
                CelestialBody body = FlightGlobals.currentMainBody;
                Vector3 up = body != null ? ((Vector3)(pad.ZoneCenter - (Vector3)body.position)).normalized : deck.transform.up;
                for (int i = 0; i < pad.OutletCount; i++)
                {
                    Transform o = pad.Outlets[i];
                    Vector3 radial = Vector3.ProjectOnPlane(o.position - pad.ZoneCenter, up).normalized;
                    Vector3 fwd = Vector3.ProjectOnPlane(o.forward, up);
                    bool grazing = fwd.sqrMagnitude > 0.5f && Vector3.Dot(fwd.normalized, radial) > 0.2f;
                    pad.OutletDir[i] = grazing ? fwd.normalized : radial;
                    MeasureGround(pad, i, pad.ZoneCenter, up);
                }
                LogPad(pad, systems ?? new ParticleSystem[0], pad.ZoneCenter);
            }
            catch (Exception e)
            {
                GeLog.ExceptionOnce("LaunchPadRegistry.Register", e);
            }
            _pads.Add(pad);
        }

        private static void AddOutlet(Pad pad, Transform t)
        {
            if (t == null || pad.OutletCount >= MaxOutlets) return;
            for (int i = 0; i < pad.OutletCount; i++)
                if (Vector3.Distance(pad.Outlets[i].position, t.position) < 3f) return;
            pad.Outlets[pad.OutletCount++] = t;
        }

        private static Transform FindNamed(Transform root, string name)
        {
            if (root.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return root;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
                if (all[i].name.Equals(name, StringComparison.OrdinalIgnoreCase)) return all[i];
            return null;
        }

        private static bool MatchesStatic(Transform t, string staticName)
        {
            string n = t.name;
            return n.Equals(staticName, StringComparison.OrdinalIgnoreCase) ||
                n.IndexOf("_" + staticName + "_", StringComparison.OrdinalIgnoreCase) >= 0 ||
                n.EndsWith("_" + staticName, StringComparison.OrdinalIgnoreCase);
        }

        private static void AddConfiguredOutlets(Pad pad)
        {
            GameDatabase db = GameDatabase.Instance;
            if (db == null) return;
            ConfigNode[] statics = db.GetConfigNodes("STATIC");
            for (int c = 0; c < statics.Length; c++)
            {
                ConfigNode cfg = statics[c];
                string staticName = cfg.GetValue("name");
                if (string.IsNullOrEmpty(staticName)) continue;
                Transform root = pad.Deck.transform;
                while (root != null && !MatchesStatic(root, staticName)) root = root.parent;
                if (root == null) continue;
                ConfigNode[] modules = cfg.GetNodes("MODULE");
                bool hasPadSmoke = false;
                for (int m = 0; m < modules.Length; m++)
                    if (string.Equals(modules[m].GetValue("name"), "PadSmoke", StringComparison.OrdinalIgnoreCase)) hasPadSmoke = true;
                if (!hasPadSmoke) continue;
                pad.StaticRoot = root;
                pad.OutletCount = 0; // les bouches explicites du static priment sur un éventuel ps générique
                string launchTransform = cfg.GetValue("DefaultLaunchPadTransform");
                Transform spawn = !string.IsNullOrEmpty(launchTransform) ? FindNamed(root, launchTransform) : null;
                if (spawn != null) pad.ZoneCenter = spawn.position;
                float width = 0f, length = 0f;
                float.TryParse(cfg.GetValue("DefaultLaunchSiteWidth"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out width);
                float.TryParse(cfg.GetValue("DefaultLaunchSiteLength"), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out length);
                if (width > 0f || length > 0f)
                    pad.RadiusM = Mathf.Clamp(0.5f * Mathf.Max(width, length) + 12f, 25f, 150f);
                // Un static peut avoir plusieurs récepteurs indépendants. Chaque impact ne doit alimenter que
                // la bouche du récepteur touché, sinon la vapeur apparaît à une autre sortie du pas.
                for (int m = 0; m < modules.Length; m++)
                {
                    ConfigNode module = modules[m];
                    if (!string.Equals(module.GetValue("name"), "PadSmoke", StringComparison.OrdinalIgnoreCase)) continue;
                    string receiver = module.GetValue("smokeReceiverName");
                    if (!string.IsNullOrEmpty(receiver) && !MatchesReceiver(pad.Deck.transform, root, receiver)) continue;
                    string names = module.GetValue("smokeEmittersNames");
                    if (string.IsNullOrEmpty(names)) continue;
                    string[] parts = names.Split(',');
                    for (int i = 0; i < parts.Length; i++) AddOutlet(pad, FindNamed(root, parts[i].Trim()));
                }
                GeLog.Info("Pas moddé « " + staticName + " » : " + pad.OutletCount + " bouche(s) déclarée(s), zone " + pad.RadiusM.ToString("0") + " m.");
                return;
            }
        }

        private static bool MatchesReceiver(Transform deck, Transform root, string receiver)
        {
            for (Transform t = deck; t != null && t != root; t = t.parent)
                if (t.name.Equals(receiver, StringComparison.OrdinalIgnoreCase)) return true;
            return root != null && root.name.Equals(receiver, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Pas de tir enregistré le plus proche de <paramref name="point"/>, s'il est à moins de <paramref name="range"/>.</summary>
        public Pad Nearest(Vector3 point, float range, out float distance)
        {
            distance = float.MaxValue;
            Pad best = null;
            float bestDeckDistance = float.MaxValue;
            for (int i = _pads.Count - 1; i >= 0; i--)
            {
                Pad p = _pads[i];
                if (p.Fx == null || p.Deck == null) { _pads.RemoveAt(i); continue; } // décor déchargé
                Vector3 up = FlightGlobals.currentMainBody != null
                    ? (p.ZoneCenter - (Vector3)FlightGlobals.currentMainBody.position).normalized : p.Deck.transform.up;
                float d = Vector3.ProjectOnPlane(point - p.ZoneCenter, up).magnitude;
                float deckDistance = (p.Deck.bounds.ClosestPoint(point) - point).sqrMagnitude;
                // Plusieurs récepteurs d'un même pad ont le même centre de zone. Départager avec le collider
                // réellement touché, tout en gardant la distance au site pour le test de zone.
                if (d < distance - 0.5f || (Mathf.Abs(d - distance) <= 0.5f && deckDistance < bestDeckDistance))
                { distance = d; bestDeckDistance = deckDistance; best = p; }
            }
            return distance <= range ? best : null;
        }

        /// <summary>
        /// Vrai si le point est sur un pas de tir connu. Faux s'il est sur le terrain qui entoure un pas connu
        /// (moins de 2 km) ; <paramref name="known"/> vaut faux loin de tout pas enregistré (décision laissée au classifieur).
        /// </summary>
        public bool IsOnPad(Vector3 point, out bool known)
        {
            Pad p = Nearest(point, 2000f, out float d);
            known = p != null;
            return known && d <= p.RadiusM;
        }

        public bool IsAbovePad(Vector3 point, float maxHeightM)
        {
            Pad p = Nearest(point, 2000f, out float d);
            if (p == null || d > p.RadiusM) return false;
            Vector3 up = FlightGlobals.currentMainBody != null
                ? (p.ZoneCenter - (Vector3)FlightGlobals.currentMainBody.position).normalized : p.Deck.transform.up;
            float height = Vector3.Dot(point - p.ZoneCenter, up);
            return height >= -35f && height <= maxHeightM;
        }

        private static void LogPad(Pad pad, ParticleSystem[] systems, Vector3 center)
        {
            var sb = new System.Text.StringBuilder(256);
            sb.Append("Pas de tir « ").Append(Path(pad.Deck.transform)).Append(" » : rayon ").Append(pad.RadiusM.ToString("0"))
              .Append(" m, ").Append(systems.Length).Append(" émetteur(s) de fumée stock");
            Vector3 up = pad.Deck.transform.up;
            Vector3 north = Vector3.ProjectOnPlane(FlightGlobals.currentMainBody != null
                ? (Vector3)FlightGlobals.currentMainBody.transform.up : Vector3.forward, up).normalized;
            for (int i = 0; i < systems.Length; i++)
            {
                if (systems[i] == null) continue;
                Transform t = systems[i].transform;
                Vector3 d = Vector3.ProjectOnPlane(t.position - center, up);
                sb.Append(" ; [").Append(i).Append("] ").Append(d.magnitude.ToString("0.0")).Append(" m au cap ")
                  .Append(Heading(north, d, up).ToString("0")).Append("°, hauteur ")
                  .Append(Vector3.Dot(t.position - center, up).ToString("0.0")).Append(" m, émission vers ")
                  .Append(Heading(north, Vector3.ProjectOnPlane(t.forward, up), up).ToString("0")).Append("° (pente ")
                  .Append((Mathf.Asin(Mathf.Clamp(Vector3.Dot(t.forward, up), -1f, 1f)) * Mathf.Rad2Deg).ToString("0")).Append("°)");
            }
            sb.Append(" → ").Append(pad.OutletCount).Append(" sortie(s) de déflecteur retenue(s)");
            for (int i = 0; i < pad.OutletCount; i++)
                sb.Append(i == 0 ? " ; sol devant la bouche " : ", ").Append(i).Append(" : ")
                  .Append(pad.GroundDropM[i].ToString("0.0")).Append(" m plus bas à ").Append(pad.GroundEdgeM[i].ToString("0")).Append(" m");
            sb.Append('.');
            GeLog.Info(sb.ToString());
        }

        private static float Heading(Vector3 north, Vector3 d, Vector3 up)
        {
            if (north.sqrMagnitude < 0.5f || d.sqrMagnitude < 1e-6f) return 0f;
            float h = Vector3.SignedAngle(north, d, up);
            return h < 0f ? h + 360f : h;
        }

        private static string Path(Transform t)
        {
            string s = t.name;
            for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; s = t.name + "/" + s; }
            return s;
        }
    }
}
