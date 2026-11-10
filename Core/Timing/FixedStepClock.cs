using System;

namespace V12.Core.Timing
{
    /// <summary>
    /// Deterministic fixed-step accumulator. The host feeds real frame delta
    /// time into <see cref="Advance"/>; the clock calls the simulation callback
    /// with a constant <see cref="FixedDeltaTime"/> until the accumulator drains.
    /// </summary>
    public sealed class FixedStepClock
    {
        public const float DefaultFixedDeltaTime = 1f / 60f;

        private double _accumulator;
        private double _realTime;
        private double _simulationTime;

        /// <summary>Simulation step size. Defaults to 60 Hz.</summary>
        public float FixedDeltaTime { get; set; } = DefaultFixedDeltaTime;

        /// <summary>Upper bound on catch-up steps per host frame. Prevents a
        /// long stall from turning into an unbounded spiral.</summary>
        public int MaxStepsPerFrame { get; set; } = 8;

        /// <summary>Largest real-time delta accepted in one host frame. Anything
        /// larger is treated as a stall and clamped before accumulation.</summary>
        public float MaxFrameDeltaTime { get; set; } = 0.25f;

        /// <summary>Scales real time before accumulation. 1 is normal speed.</summary>
        public float TimeScale { get; set; } = 1f;

        /// <summary>When true, real time still advances but no simulation steps run.</summary>
        public bool IsPaused { get; set; }

        /// <summary>Number of fixed steps executed since the last reset.</summary>
        public long Tick { get; private set; }

        /// <summary>Total simulated time, advanced only by fixed steps.</summary>
        public double SimulationTime => _simulationTime;

        /// <summary>Total real time fed into the clock, including paused time.</summary>
        public double RealTime => _realTime;

        /// <summary>Fraction of a fixed step left over for render interpolation.</summary>
        public float Alpha
        {
            get
            {
                if (FixedDeltaTime <= 0f) return 0f;
                return Math.Clamp((float)(_accumulator / FixedDeltaTime), 0f, 1f);
            }
        }

        /// <summary>Steps executed in the most recent <see cref="Advance"/> call.</summary>
        public int LastStepCount { get; private set; }

        public FixedStepClock() { }

        public FixedStepClock(float fixedDeltaTime)
        {
            if (fixedDeltaTime <= 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime));
            FixedDeltaTime = fixedDeltaTime;
        }

        /// <summary>
        /// Feed real frame delta time and run zero or more fixed steps.
        /// Returns the number of steps that ran.
        /// </summary>
        public int Advance(float realDeltaTime, Action<float> fixedStep)
        {
            if (realDeltaTime < 0f) realDeltaTime = 0f;
            _realTime += realDeltaTime;

            if (IsPaused || realDeltaTime <= 0f || FixedDeltaTime <= 0f)
            {
                LastStepCount = 0;
                return 0;
            }

            double frameDelta = Math.Min(realDeltaTime, MaxFrameDeltaTime) * Math.Max(0f, TimeScale);
            _accumulator += frameDelta;

            // A float step size promoted to double can leave a remainder a few
            // ulps below the step. Treat that as a full step instead of silently
            // losing one tick every few frames.
            const double StepEpsilon = 1e-8d;

            int steps = 0;
            while (_accumulator + StepEpsilon >= FixedDeltaTime && steps < MaxStepsPerFrame)
            {
                _accumulator -= FixedDeltaTime;
                if (_accumulator < 0d) _accumulator = 0d;
                _simulationTime += FixedDeltaTime;
                Tick++;
                fixedStep?.Invoke(FixedDeltaTime);
                steps++;
            }

            // Drop an unserviceable backlog instead of stretching the next frame.
            if (steps >= MaxStepsPerFrame && _accumulator + StepEpsilon >= FixedDeltaTime)
                _accumulator = 0d;

            LastStepCount = steps;
            return steps;
        }

        /// <summary>Run exactly one fixed step, bypassing the accumulator.</summary>
        public void StepOnce(Action<float> fixedStep)
        {
            if (FixedDeltaTime <= 0f) return;
            _simulationTime += FixedDeltaTime;
            Tick++;
            fixedStep?.Invoke(FixedDeltaTime);
            LastStepCount = 1;
        }

        /// <summary>Reset tick counters, accumulated time, and interpolation alpha.</summary>
        public void Reset()
        {
            _accumulator = 0d;
            _realTime = 0d;
            _simulationTime = 0d;
            Tick = 0;
            LastStepCount = 0;
        }
    }
}
