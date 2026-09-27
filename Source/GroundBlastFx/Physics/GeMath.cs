using System;

namespace GroundBlastFx.Model
{
    // Le dossier s'appelle Physics/ mais l'espace de noms est GroundBlastFx.Model : un espace de noms
    // GroundBlastFx.Physics masquerait la classe UnityEngine.Physics dans tout le code GroundBlastFx.*.
    // Ce dossier n'utilise jamais UnityEngine : il est compilé tel quel par les tests unitaires (.NET 10).

    /// <summary>Fonctions scalaires sans allocation.</summary>
    public static class GeMath
    {
        public const float G0 = 9.80665f;
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float SeaLevelPressurePa = 101325f;

        public static float Clamp01(float x) { return x < 0f ? 0f : (x > 1f ? 1f : x); }

        public static float Clamp(float x, float min, float max) { return x < min ? min : (x > max ? max : x); }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * Clamp01(t); }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            if (edge0 == edge1) return x < edge0 ? 0f : 1f;
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Rapproche <paramref name="current"/> de <paramref name="target"/> avec une constante de temps (filtre exponentiel).</summary>
        public static float Approach(float current, float target, float timeConstantS, float dt)
        {
            if (timeConstantS <= 0f || dt <= 0f) return dt > 0f ? target : current;
            float k = 1f - (float)Math.Exp(-dt / timeConstantS);
            return current + (target - current) * k;
        }

        public static float Max(float a, float b) { return a > b ? a : b; }

        public static float Min(float a, float b) { return a < b ? a : b; }
    }
}
