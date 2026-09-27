using System;

namespace GroundBlastFx.Model
{
    /// <summary>
    /// Vent de surface optionnel. Une vitesse et un cap de base sont tirés une fois
    /// par session, puis varient lentement (somme de sinus de périodes incommensurables : pas de boucle perceptible).
    /// Le Core convertit (vitesse, cap) en vecteur monde tangent à la surface.
    /// </summary>
    public sealed class WindModel
    {
        private readonly float _baseSpeed;
        private readonly float _baseHeadingRad;
        private readonly float _phaseA, _phaseB, _phaseC;

        public WindModel(int seed, float minSpeedMs, float maxSpeedMs)
        {
            var rng = new Random(seed);
            if (maxSpeedMs < minSpeedMs) maxSpeedMs = minSpeedMs;
            _baseSpeed = minSpeedMs + (float)rng.NextDouble() * (maxSpeedMs - minSpeedMs);
            _baseHeadingRad = (float)(rng.NextDouble() * 2.0 * Math.PI);
            _phaseA = (float)(rng.NextDouble() * 6.283);
            _phaseB = (float)(rng.NextDouble() * 6.283);
            _phaseC = (float)(rng.NextDouble() * 6.283);
            MinSpeedMs = minSpeedMs;
            MaxSpeedMs = maxSpeedMs;
        }

        public float MinSpeedMs { get; }
        public float MaxSpeedMs { get; }
        public float BaseSpeedMs => _baseSpeed;

        /// <summary>Vitesse (m/s) et cap (radians depuis le nord, sens horaire) à l'instant <paramref name="timeS"/>.</summary>
        public void Sample(double timeS, float periodS, out float speedMs, out float headingRad)
        {
            if (periodS < 1f) periodS = 1f;
            double w = 2.0 * Math.PI / periodS;
            float gust = (float)(0.55 * Math.Sin(w * timeS + _phaseA) + 0.30 * Math.Sin(w * 2.618 * timeS + _phaseB) + 0.15 * Math.Sin(w * 0.382 * timeS + _phaseC));
            speedMs = GeMath.Clamp(_baseSpeed * (1f + 0.25f * gust), MinSpeedMs, MaxSpeedMs);
            headingRad = _baseHeadingRad + 0.35f * (float)Math.Sin(w * 0.5 * timeS + _phaseB);
        }
    }
}
