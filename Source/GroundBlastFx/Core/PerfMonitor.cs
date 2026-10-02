using System;
using System.Diagnostics;

namespace GroundBlastFx.Core
{
    /// <summary>
    /// Mesure du coût CPU du Core sur le thread principal et des allocations managées.
    /// - Temps : Stopwatch autour de Update + LateUpdate (hors renderer, mesuré à part), moyenne glissante et maximum.
    /// - Allocations : GC.GetTotalMemory(false) avant/après chaque section ; une frame où un GC a eu lieu est ignorée.
    ///   Avec le GC Boehm de Mono la mesure est grossière (granularité des blocs), mais une allocation régulière
    ///   apparaît sur la moyenne ; « 0 o/frame » en régime établi est le critère.
    /// </summary>
    public sealed class PerfMonitor
    {
        private readonly Stopwatch _sw = new Stopwatch();
        private long _memBefore;
        private int _gcBefore;
        private double _frameTicks;
        private long _frameAlloc;

        private const int Window = 240;
        private readonly double[] _msHistory = new double[Window];
        private readonly long[] _allocHistory = new long[Window];
        private int _index;
        private int _filled;

        public double AverageMs { get; private set; }
        public double MaxMs { get; private set; }
        public double AverageAllocBytes { get; private set; }
        public long FramesWithAlloc { get; private set; }
        public long FramesMeasured { get; private set; }
        public double RendererAverageMs { get; private set; }

        private double _rendererEma;

        public void Begin()
        {
            _gcBefore = GC.CollectionCount(0);
            _memBefore = GC.GetTotalMemory(false);
            _sw.Reset();
            _sw.Start();
        }

        public void End()
        {
            _sw.Stop();
            _frameTicks += _sw.Elapsed.TotalMilliseconds;
            if (GC.CollectionCount(0) == _gcBefore)
            {
                long delta = GC.GetTotalMemory(false) - _memBefore;
                if (delta > 0) _frameAlloc += delta;
            }
        }

        public void RendererSample(double ms)
        {
            _rendererEma = _rendererEma <= 0 ? ms : _rendererEma + (ms - _rendererEma) * 0.05;
            RendererAverageMs = _rendererEma;
        }

        /// <summary>À appeler une fois par frame, après la dernière section mesurée.</summary>
        public void EndFrame()
        {
            _msHistory[_index] = _frameTicks;
            _allocHistory[_index] = _frameAlloc;
            _index = (_index + 1) % Window;
            if (_filled < Window) _filled++;
            FramesMeasured++;
            if (_frameAlloc > 0) FramesWithAlloc++;
            double sum = 0, max = 0;
            long asum = 0;
            for (int i = 0; i < _filled; i++)
            {
                sum += _msHistory[i];
                if (_msHistory[i] > max) max = _msHistory[i];
                asum += _allocHistory[i];
            }
            AverageMs = sum / _filled;
            MaxMs = max;
            AverageAllocBytes = (double)asum / _filled;
            _frameTicks = 0;
            _frameAlloc = 0;
        }

        public void ResetCounters()
        {
            FramesWithAlloc = 0;
            FramesMeasured = 0;
        }
    }
}
