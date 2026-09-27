using GroundBlastFx.Config;
using GroundBlastFx.Contracts;
using GroundBlastFx.Core;
using UnityEngine;

namespace GroundBlastFx.UI
{
    /// <summary>
    /// Overlay de debug indépendant du renderer (tâche CL-1.7) : rayons de sondage, taches r_i, fronts R_front,
    /// étiquettes des foyers, flèche de tranchée. Dessiné en espace écran dans OnGUI (GL + IMGUI), sans toucher
    /// à la caméra de vol ni au pipeline de rendu (décision D-CL-008).
    /// Les chaînes des étiquettes sont reconstruites 4 fois par seconde seulement.
    /// </summary>
    public sealed class DebugOverlay
    {
        private readonly GroundBlastFxAddon _addon;
        private Material _lineMat;
        private bool _matFailed;
        private readonly string[] _labels = new string[ClusterTracker.MaxClusters];
        private readonly int[] _labelIds = new int[ClusterTracker.MaxClusters];
        private float _labelTimer;
        private GUIStyle _labelStyle;
        private readonly Vector3[] _demoPos = new Vector3[4];
        private readonly string[] _demoNames = new string[4];

        public TrenchTool Trench { get; }

        private static readonly Color RayHit = new Color(0.3f, 1f, 0.3f, 0.9f);
        private static readonly Color RayMiss = new Color(1f, 0.3f, 0.3f, 0.6f);
        private static readonly Color RingColor = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color FrontColor = new Color(1f, 0.55f, 0.15f, 1f);
        private static readonly Color MaxColor = new Color(1f, 0.55f, 0.15f, 0.35f);
        private static readonly Color ScorchColor = new Color(0.6f, 0.6f, 0.6f, 0.8f);
        private static readonly Color TrenchColor = new Color(0.2f, 0.8f, 1f, 1f);

        public DebugOverlay(GroundBlastFxAddon addon)
        {
            _addon = addon;
            Trench = new TrenchTool(addon);
        }

        public void Destroy()
        {
            if (_lineMat != null) Object.Destroy(_lineMat);
            _lineMat = null;
        }

        public void OnGUI()
        {
            bool overlay = GeSettings.ShowOverlay;
            if (!overlay && !Trench.Active) return;
            if (Event.current.type != EventType.Repaint) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null) return;
            Trench.Refresh();

            if (EnsureMaterial())
            {
                GL.PushMatrix();
                _lineMat.SetPass(0);
                GL.LoadPixelMatrix();
                GL.Begin(GL.LINES);
                if (overlay) DrawRaysAndClusters(cam);
                if (Trench.Active) DrawTrench(cam);
                GL.End();
                GL.PopMatrix();
            }
            if (overlay) DrawLabels(cam);
        }

        private bool EnsureMaterial()
        {
            if (_lineMat != null) return true;
            if (_matFailed) return false;
            Shader s = Shader.Find("Hidden/Internal-Colored");
            if (s == null)
            {
                _matFailed = true;
                GeLog.Warn("Overlay : shader Hidden/Internal-Colored introuvable, seules les étiquettes seront affichées.");
                return false;
            }
            _lineMat = new Material(s) { hideFlags = HideFlags.HideAndDontSave };
            _lineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _lineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _lineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _lineMat.SetInt("_ZWrite", 0);
            _lineMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return true;
        }

        private void DrawRaysAndClusters(Camera cam)
        {
            JetBuffer jets = _addon.Jets;
            JetHit[] hits = _addon.Hits;
            int n = Mathf.Min(jets.Count, hits.Length);
            for (int i = 0; i < n; i++)
            {
                ref JetSample j = ref jets.Items[i];
                if (hits[i].Hit)
                {
                    Line(cam, j.NozzleWorld, hits[i].PointWorld, RayHit);
                    JetEval e = _addon.Aggregator.EvalOfJet(i, out bool found);
                    if (found && e.ImpingementRadiusM > 0f)
                        Circle(cam, hits[i].PointWorld, hits[i].NormalWorld, e.ImpingementRadiusM, RingColor, 24);
                }
                else
                {
                    Line(cam, j.NozzleWorld, j.NozzleWorld + j.AxisWorld * Mathf.Min(j.ProbeLengthM, 200f), RayMiss);
                }
            }

            ImpingementCluster[] cl = _addon.Clusters;
            for (int i = 0; i < _addon.ClusterCount; i++)
            {
                ref ImpingementCluster c = ref cl[i];
                Circle(cam, c.ImpactPointWorld, c.SurfaceNormalWorld, c.ImpingementRadiusM, RingColor, 48);
                Circle(cam, c.ImpactPointWorld, c.SurfaceNormalWorld, c.CloudFrontRadiusM, FrontColor, 64);
                Circle(cam, c.ImpactPointWorld, c.SurfaceNormalWorld, c.MaxCloudRadiusM, MaxColor, 64);
                Line(cam, c.NozzleCenterWorld, c.ImpactPointWorld, FrontColor);
                if (c.TrenchDirectionWorld != Vector3.zero)
                    Arrow(cam, c.ImpactPointWorld, c.TrenchDirectionWorld, c.SurfaceNormalWorld, Mathf.Max(c.ImpingementRadiusM * 3f, 20f), TrenchColor);
                if (c.WindWorldMs.sqrMagnitude > 0.01f)
                    Line(cam, c.ImpactPointWorld + c.SurfaceNormalWorld * 2f, c.ImpactPointWorld + c.SurfaceNormalWorld * 2f + c.WindWorldMs * 3f, Color.cyan);
            }

            ScorchMark[] marks = _addon.Marks;
            for (int i = 0; i < _addon.MarkCount; i++)
                Circle(cam, marks[i].CenterWorld, marks[i].NormalWorld, marks[i].RadiusM, ScorchColor, 32);
        }

        private void DrawTrench(Camera cam)
        {
            if (Trench.Body == null) return;
            Vector3 c = Trench.CenterWorld;
            SurfaceFrame.Basis(Trench.Body, c, out Vector3 up, out Vector3 _, out Vector3 _);
            Vector3 p = c + up * 1.5f;
            Circle(cam, p, up, 8f, TrenchColor, 32);
            if (Trench.HeadingDeg < 0f)
            {
                for (int k = 0; k < 8; k++)
                    Arrow(cam, p, SurfaceFrame.HeadingDirection(Trench.Body, c, k * 45f), up, 25f, TrenchColor);
            }
            else
            {
                Arrow(cam, p, SurfaceFrame.HeadingDirection(Trench.Body, c, Trench.HeadingDeg), up, 60f, TrenchColor);
            }
        }

        private void DrawLabels(Camera cam)
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false };
                _labelStyle.normal.textColor = Color.white;
            }
            _labelTimer -= Time.unscaledDeltaTime;
            bool refresh = _labelTimer <= 0f;
            if (refresh) _labelTimer = 0.25f;

            ImpingementCluster[] cl = _addon.Clusters;
            for (int i = 0; i < _addon.ClusterCount && i < _labels.Length; i++)
            {
                ref ImpingementCluster c = ref cl[i];
                if (refresh || _labelIds[i] != c.Id || _labels[i] == null)
                {
                    _labelIds[i] = c.Id;
                    _labels[i] = "#" + c.Id + (c.IsDemo ? " DÉMO " : " ") + c.Surface + "/" + c.Medium
                                 + (c.EnginesActive ? "" : " coupé " + c.TimeSinceCutoffS.ToString("0") + " s")
                                 + "\nI=" + c.Intensity01.ToString("0.00") + "  h=" + c.StandoffM.ToString("0.0") + " m  r_i=" + c.ImpingementRadiusM.ToString("0.0")
                                 + " m  R=" + c.CloudFrontRadiusM.ToString("0") + "/" + c.MaxCloudRadiusM.ToString("0") + " m"
                                 + "\nF=" + (c.TotalThrustN / 1000f).ToString("0") + " kN ×" + c.EngineCount + "  u_i=" + c.WallJetVelocityMs.ToString("0") + " m/s  vapeur=" + c.SteamFraction01.ToString("0.00");
                }
                Vector3 sp = cam.WorldToScreenPoint(c.ImpactPointWorld + c.SurfaceNormalWorld * 3f);
                if (sp.z <= 0f) continue;
                GUI.Label(new Rect(sp.x + 6f, Screen.height - sp.y - 6f, 420f, 60f), _labels[i], _labelStyle);
            }

            if (GeSettings.ShowDemoLabels)
            {
                int n = _addon.Demo.GetLabels(_demoPos, _demoNames);
                for (int i = 0; i < n; i++)
                {
                    Vector3 sp = cam.WorldToScreenPoint(_demoPos[i]);
                    if (sp.z <= 0f) continue;
                    GUI.Label(new Rect(sp.x - 30f, Screen.height - sp.y + 10f, 200f, 24f), "DÉMO " + _demoNames[i], _labelStyle);
                }
            }
        }

        // --- Primitives en espace écran ---

        private static void Line(Camera cam, Vector3 a, Vector3 b, Color color)
        {
            Vector3 sa = cam.WorldToScreenPoint(a);
            Vector3 sb = cam.WorldToScreenPoint(b);
            if (sa.z <= 0f || sb.z <= 0f) return;
            GL.Color(color);
            GL.Vertex3(sa.x, sa.y, 0f);
            GL.Vertex3(sb.x, sb.y, 0f);
        }

        private static void Circle(Camera cam, Vector3 center, Vector3 normal, float radius, Color color, int segments)
        {
            if (radius <= 0f || normal.sqrMagnitude < 1e-6f) return;
            Vector3 t1 = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 t2 = Vector3.Cross(normal, t1);
            Vector3 prev = center + t1 * radius;
            for (int k = 1; k <= segments; k++)
            {
                float a = 2f * Mathf.PI * k / segments;
                Vector3 next = center + (t1 * Mathf.Cos(a) + t2 * Mathf.Sin(a)) * radius;
                Line(cam, prev, next, color);
                prev = next;
            }
        }

        private static void Arrow(Camera cam, Vector3 from, Vector3 dir, Vector3 up, float length, Color color)
        {
            Vector3 tip = from + dir * length;
            Line(cam, from, tip, color);
            Vector3 side = Vector3.Cross(up, dir).normalized;
            float h = Mathf.Max(length * 0.15f, 2f);
            Line(cam, tip, tip - dir * h + side * h * 0.6f, color);
            Line(cam, tip, tip - dir * h - side * h * 0.6f, color);
        }
    }
}
