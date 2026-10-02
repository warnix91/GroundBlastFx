using System;
using System.IO;
using GroundBlastFx.Contracts;
using GroundBlastFx.Core;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using QualityLevel = GroundBlastFx.Contracts.QualityLevel;

namespace GroundBlastFx.Rendering
{
    /// <summary>
    /// Implémentation en jeu d'IGroundBlastFxRenderer : couche KSP autour de RenderCore (commun avec le harnais Unity).
    /// - Deux CommandBuffers sur FlightCamera.fetch.mainCamera : après les opaques pour la poussière (remis en dernier à
    ///   chaque frame, après la diffusion de Scatterer), après les transparents pour les embruns.
    /// - Soleil : Sun.Instance.sunLight (la vraie lumière solaire de KSP), nuit et rougissement calculés au foyer.
    /// - Lumières de flamme : une Light ponctuelle par foyer, sans ombres.
    /// </summary>
    public sealed class VolumetricRenderer : IGroundBlastFxRenderer
    {
        /// <summary>
        /// Relevé du sol pour la carte des hauteurs du nuage (1.7) : décor, terrain et pas de tir (mêmes calques que la
        /// sonde, sans les pièces des vaisseaux), et surface de la mer (l'océan n'a pas de collider). Un lot de rayons
        /// (RaycastCommand) à la naissance d'un nuage.
        /// </summary>
        private sealed class KspGround : RenderCore.IGroundSampler
        {
            private const int Mask = (1 << 15) | (1 << 28) | (1 << 30);

            public void Cast(Vector3[] origins, int count, Vector3 down, float maxDist, float[] outDist)
            {
                CelestialBody body = FlightGlobals.currentMainBody;
                var cmds = new NativeArray<RaycastCommand>(count, Allocator.TempJob);
                var res = new NativeArray<RaycastHit>(count, Allocator.TempJob);
                try
                {
                    for (int i = 0; i < count; i++) cmds[i] = new RaycastCommand(origins[i], down, maxDist, Mask, 1);
                    RaycastCommand.ScheduleBatch(cmds, res, 32).Complete();
                    for (int i = 0; i < count; i++)
                    {
                        float d = res[i].collider != null ? res[i].distance : -1f;
                        if (body != null && body.ocean
                            && GroundBlastFx.Surface.SurfaceProbe.RaySeaLevel(body, origins[i], down, d >= 0f ? d : maxDist, out float t, out Vector3 _))
                            d = t;
                        outDist[i] = d;
                    }
                }
                finally
                {
                    cmds.Dispose();
                    res.Dispose();
                }
            }
        }

        private RendererSettings _settings;
        private AssetBundle _bundle;
        private RenderCore _core;
        private readonly Light[] _lights = new Light[RenderCore.MaxClusters];
        private Camera _camera;
        private CommandBuffer _commands;
        private CommandBuffer _waterCommands;
        private int _width, _height, _divisor = 2;
        private string _status = "non initialisé";
        private bool _loggedOtherBuffers;
        private int _bufferChecks;   // relevé des autres CommandBuffers limité aux premières secondes (allocation)

        public bool IsAvailable => _core != null && _core.IsAvailable;
        public string BackendName => _core != null ? _core.BackendName : "Null";

        public void Initialize(RendererSettings settings)
        {
            _settings = settings;
            RenderCore.Log = s => GeLog.Info(s);
            string path = Path.Combine(Path.Combine(GePaths.ModRoot, "Shaders"), "GroundBlastFx.unity3d");
            if (!File.Exists(path)) { _status = "bundle absent : " + path; GeLog.Warn(_status); return; }
            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null) { _status = "bundle refusé par Unity"; GeLog.Warn(_status); return; }
            _core = new RenderCore(_bundle);
            RenderCore.Ground = new KspGround();
            if (!_core.IsAvailable) { _status = _core.Status; GeLog.Warn("Renderer volumétrique indisponible : " + _status); return; }
            _core.Configure(settings);
            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject("GroundBlastFx lumière de flamme " + i) { hideFlags = HideFlags.DontSave };
                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.enabled = false;
                _lights[i] = light;
            }
            _status = _core.Status;
            GeLog.Info("Renderer volumétrique : " + _status);
            Camera.onPreRender += OnCameraPreRender;
        }

        /// <summary>
        /// 1.0.1 : la composition passe en dernier parmi les CommandBuffers « avant les transparents ». Scatterer (versions
        /// publiques) ajoute à chaque frame, au même moment, sa diffusion atmosphérique : elle réécrit le décor à partir
        /// d'une copie de l'écran prise avant nos nuages, qui disparaissaient alors partout sauf sur l'eau (passe placée
        /// après les transparents) et sur les corps sans atmosphère. Scatterer ajoute son buffer pendant le tri des objets
        /// (OnWillRenderObject) ; onPreRender vient ensuite, on remet donc le nôtre en fin de liste à chaque frame.
        /// </summary>
        private void OnCameraPreRender(Camera cam)
        {
            if (cam == null || cam != _camera || _commands == null) return;
            if (!_loggedOtherBuffers && _bufferChecks++ < 600)
            {
                CommandBuffer[] list = cam.GetCommandBuffers(RenderCore.Event);
                if (list.Length > 1)
                {
                    _loggedOtherBuffers = true;
                    var names = new System.Text.StringBuilder();
                    foreach (CommandBuffer b in list)
                        if (b.name != _commands.name) names.Append(names.Length > 0 ? ", " : "").Append(b.name);
                    GeLog.Info("Autres CommandBuffers avant les transparents : " + names + " → composition GroundBlastFx placée après eux.");
                }
            }
            cam.RemoveCommandBuffer(RenderCore.Event, _commands);
            cam.AddCommandBuffer(RenderCore.Event, _commands);
            // Orientation réelle de la caméra au moment du rendu (elle peut encore bouger après notre LateUpdate).
            _core?.UpdateCamera(cam);
        }

        public void ApplySettings(RendererSettings settings)
        {
            _settings = settings;
            _core?.Configure(settings);
            _width = 0; // la passe réduite est recréée à la prochaine frame
        }

        public void Submit(ImpingementCluster[] clusters, int clusterCount, ScorchMark[] marks, int markCount)
        {
            if (!IsAvailable) return;
            Camera cam = FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : null;
            if (cam == null || !cam.isActiveAndEnabled) { Detach(); DisableLights(); return; }
            if (cam != _camera) { Detach(); _camera = cam; }
            cam.depthTextureMode |= DepthTextureMode.Depth;
            int div = _settings.Quality == QualityLevel.Low ? 4 : (_settings.Quality == QualityLevel.Ultra && cam.pixelHeight <= 1080 ? 1 : 2);
            int width = Math.Max(1, cam.pixelWidth), height = Math.Max(1, cam.pixelHeight);
            if (_commands == null || width != _width || height != _height || div != _divisor)
                Attach(width, height, div);

            RenderEnvironment env = BuildEnvironment(clusters, clusterCount);
            int rendered = _core.Prepare(cam, clusters, clusterCount, marks, markCount, _settings, ref env);
            for (int i = 0; i < _lights.Length; i++)
            {
                Light light = _lights[i];
                if (light == null) continue;
                FlameLightParams p = _core.Lights[i];
                bool on = rendered > 0 && p.Enabled;
                if (light.enabled != on) light.enabled = on;
                if (!on) continue;
                light.transform.position = p.Position;
                light.color = p.Color;
                light.intensity = p.Intensity;
                light.range = p.Range;
            }
            _core.Record(_commands, width, height, div, 1);
            _core.Record(_waterCommands, width, height, div, 2);
        }

        /// <summary>Soleil de KSP, nuit, ciel et brume selon l'atmosphère au foyer le plus proche de la caméra.</summary>
        private static RenderEnvironment BuildEnvironment(ImpingementCluster[] clusters, int count)
        {
            Vector3 sunDir = Vector3.up;
            Color sunRaw = new Color(1f, 0.97f, 0.92f);
            Light sunLight = Sun.Instance != null ? Sun.Instance.sunLight : null;
            if (sunLight != null)
            {
                sunDir = -sunLight.transform.forward;
                sunRaw = sunLight.color * Mathf.Max(sunLight.intensity, 0f);
            }
            else if (RenderSettings.sun != null)
            {
                sunDir = -RenderSettings.sun.transform.forward;
                sunRaw = RenderSettings.sun.color * RenderSettings.sun.intensity;
            }
            Vector3 up = Vector3.up;
            float atm = 1f;
            Color ground = new Color(0.5f, 0.47f, 0.42f);
            if (count > 0)
            {
                up = clusters[0].SurfaceNormalWorld;
                atm = clusters[0].Medium == MediumKind.Vacuum ? 0f : Mathf.Clamp01(clusters[0].AmbientPressurePa / 101325f);
                ground = clusters[0].DustAlbedoA;
            }
            else if (FlightGlobals.ActiveVessel != null)
            {
                up = FlightGlobals.ActiveVessel.upAxis;
                atm = Mathf.Clamp01((float)(FlightGlobals.ActiveVessel.staticPressurekPa / 101.325));
            }
            RenderEnvironment env = RenderEnvironment.FromSun(sunDir, sunRaw, up, atm, ground, Time.time, Time.deltaTime);
            // Lumière ambiante réelle de KSP (réglage « luminosité ambiante » du joueur) : plancher de l'éclairage du nuage,
            // pour qu'il reste aussi lisible que le décor la nuit.
            Color amb = RenderSettings.ambientLight * 0.8f;
            if (atm > 0.001f)
                env.AmbientSky = new Color(Mathf.Max(env.AmbientSky.r, amb.r), Mathf.Max(env.AmbientSky.g, amb.g), Mathf.Max(env.AmbientSky.b, amb.b));
            return env;
        }

        private void Attach(int width, int height, int divisor)
        {
            Camera cam = _camera;
            Detach();
            _camera = cam;
            _width = width; _height = height; _divisor = divisor;
            _commands = new CommandBuffer { name = "GroundBlastFx sol + composition" };
            _waterCommands = new CommandBuffer { name = "GroundBlastFx eau + composition" };
            _camera.AddCommandBuffer(RenderCore.Event, _commands);
            _camera.AddCommandBuffer(CameraEvent.AfterForwardAlpha, _waterCommands);
            _loggedOtherBuffers = false;
            _bufferChecks = 0;
        }

        private void Detach()
        {
            if (_camera != null && _commands != null) _camera.RemoveCommandBuffer(RenderCore.Event, _commands);
            if (_camera != null && _waterCommands != null) _camera.RemoveCommandBuffer(CameraEvent.AfterForwardAlpha, _waterCommands);
            if (_commands != null) { _commands.Release(); _commands = null; }
            if (_waterCommands != null) { _waterCommands.Release(); _waterCommands = null; }
            _camera = null;
        }

        private void DisableLights()
        {
            for (int i = 0; i < _lights.Length; i++) if (_lights[i] != null) _lights[i].enabled = false;
        }

        public string GetDebugInfo()
        {
            return _core == null ? _status : _core.DebugInfo(_width, _height, _divisor);
        }

        public void Shutdown()
        {
            Camera.onPreRender -= OnCameraPreRender;
            Detach();
            DisableLights();
            for (int i = 0; i < _lights.Length; i++)
                if (_lights[i] != null) { UnityEngine.Object.Destroy(_lights[i].gameObject); _lights[i] = null; }
            _core?.Dispose();
            _core = null;
            if (_bundle != null) { _bundle.Unload(true); _bundle = null; }
        }
    }
}
