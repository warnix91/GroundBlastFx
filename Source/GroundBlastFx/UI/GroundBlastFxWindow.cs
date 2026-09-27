using System;
using GroundBlastFx.Config;
using KSP.UI.Screens;
using UnityEngine;

namespace GroundBlastFx.UI
{
    /// <summary>Réglages publics simples : densité et portée. Langue : celle du jeu. Les outils de développement restent hors de l'UI.</summary>
    public sealed class GroundBlastFxWindow
    {
        private const int WindowId = 0x47E0FF;
        private ApplicationLauncherButton _button;
        private Texture2D _icon;
        private bool _visible;
        private Rect _rect;

        public GroundBlastFxWindow(Core.GroundBlastFxAddon addon)
        {
            _rect = new Rect(GeSettings.WindowX, GeSettings.WindowY, 320f, 10f);
            GameEvents.onGUIApplicationLauncherReady.Add(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Add(OnLauncherUnready);
            if (ApplicationLauncher.Ready) AddButton();
        }

        private void AddButton()
        {
            if (_button != null || ApplicationLauncher.Instance == null) return;
            if (_icon == null) _icon = AppIcon.Create();
            _button = ApplicationLauncher.Instance.AddModApplication(
                () => _visible = true, () => _visible = false, null, null, null, null,
                ApplicationLauncher.AppScenes.FLIGHT, _icon);
        }

        private void OnLauncherUnready(GameScenes scene) { RemoveButton(); }

        private void RemoveButton()
        {
            if (_button != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(_button);
            _button = null;
        }

        public void Destroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(AddButton);
            GameEvents.onGUIApplicationLauncherUnreadifying.Remove(OnLauncherUnready);
            RemoveButton();
            if (_icon != null) UnityEngine.Object.Destroy(_icon);
            _icon = null;
            GeSettings.WindowX = _rect.x;
            GeSettings.WindowY = _rect.y;
            GeSettings.Save();
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_button == null) return;
            if (visible) _button.SetTrue(false); else _button.SetFalse(false);
        }

        public void OnGUI()
        {
            if (!_visible) return;
            GUI.skin = HighLogic.Skin;
            _rect = GUILayout.Window(WindowId, _rect, Draw, GeStrings.Get("windowTitle"), GUILayout.Width(320f));
        }

        private void Draw(int id)
        {
            bool changed = false;
            if (GUI.Button(new Rect(_rect.width - 24f, 4f, 20f, 18f), "×")) SetVisible(false);

            if (FlightGlobals.fetch != null && FlightGlobals.fetch.activeVessel != null
                && FlightGlobals.fetch.activeVessel.easingInToSurface)
                GUILayout.Label(GeStrings.Get("warnStockEasing"));

            GUILayout.Label(GeStrings.Get("labelIntensity") + " " + GeSettings.Renderer.GlobalIntensity.ToString("0.00"));
            float density = GUILayout.HorizontalSlider(GeSettings.Renderer.GlobalIntensity, 0.25f, 2f);
            density = Mathf.Round(density * 20f) / 20f;
            if (Math.Abs(density - GeSettings.Renderer.GlobalIntensity) > 1e-4f)
            { GeSettings.Renderer.GlobalIntensity = density; changed = true; }
            GUILayout.Label(GeStrings.Get("helpIntensity"));

            GUILayout.Space(6f);
            GUILayout.Label(GeStrings.Get("labelDistance") + " "
                + (GeSettings.Renderer.MaxRenderDistanceM / 1000f).ToString("0.0") + " km");
            float distance = GUILayout.HorizontalSlider(GeSettings.Renderer.MaxRenderDistanceM, 500f, 20000f);
            distance = Mathf.Round(distance / 250f) * 250f;
            if (Math.Abs(distance - GeSettings.Renderer.MaxRenderDistanceM) > 1f)
            { GeSettings.Renderer.MaxRenderDistanceM = distance; changed = true; }

            // La langue suit celle du jeu (réglages de KSP) : pas de choix ici (1.9.2, demande de l'auteur).

            if (changed) GeSettings.NotifyChanged();
            GUI.DragWindow();
        }
    }
}
