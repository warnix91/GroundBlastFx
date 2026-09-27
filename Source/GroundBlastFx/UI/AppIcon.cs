using UnityEngine;

namespace GroundBlastFx.UI
{
    /// <summary>
    /// Icône du bouton AppLauncher, dessinée par le code (38 × 38) : aucune texture à livrer.
    /// Un jet orange qui descend vers un nuage de poussière beige posé sur le sol (dessin des versions 1.0 à 1.8, que
    /// l'auteur préfère ; 1.9.1 : bords lissés par suréchantillonnage 3 × 3).
    /// </summary>
    public static class AppIcon
    {
        private static readonly Color Dust = new Color(0.92f, 0.84f, 0.70f, 1f);
        private static readonly Color DustDark = new Color(0.70f, 0.62f, 0.50f, 1f);
        private static readonly Color Flame = new Color(1f, 0.62f, 0.25f, 1f);
        private static readonly Color Ground = new Color(0.85f, 0.85f, 0.85f, 1f);

        public static Texture2D Create()
        {
            const int size = 38, samples = 3;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { name = "GroundBlastFxIcon", filterMode = FilterMode.Bilinear };
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    Color sum = Color.clear;
                    for (int sy = 0; sy < samples; sy++)
                        for (int sx = 0; sx < samples; sx++)
                            sum += Sample(x - 0.5f + (sx + 0.5f) / samples, y - 0.5f + (sy + 0.5f) / samples);
                    tex.SetPixel(x, y, sum / (samples * samples));
                }
            }
            tex.Apply(false, true);
            return tex;
        }

        private static Color Sample(float x, float y)
        {
            Color c = Color.clear;
            // Sol
            if (y >= 4.5f && y <= 6.5f && x >= 2.5f && x <= 34.5f) c = Ground;
            // Nuage : trois lobes
            float l = Mathf.Max(Blob(x, y, 11f, 11f, 6.5f), Mathf.Max(Blob(x, y, 19f, 13f, 7.5f), Blob(x, y, 27f, 11f, 6.5f)));
            if (l > 0f && y >= 6.5f) c = Color.Lerp(DustDark, Dust, Mathf.Clamp01((y - 7f) / 10f));
            // Jet : triangle qui s'élargit vers le bas
            float half = Mathf.Lerp(1.2f, 3.5f, Mathf.Clamp01((33f - y) / 14f));
            if (y >= 18.5f && y <= 33.5f && Mathf.Abs(x - 19f) <= half + 0.5f) c = Flame;
            // Tuyère
            if (y >= 32.5f && y <= 35.5f && Mathf.Abs(x - 19f) <= 3f) c = Ground;
            return c;
        }

        private static float Blob(float x, float y, float cx, float cy, float r)
        {
            float dx = x - cx, dy = y - cy;
            return r * r - (dx * dx + dy * dy);
        }
    }
}
