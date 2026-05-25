using System;

namespace UGS.UnitTask
{
    [Serializable]
    public sealed class WaitSecondsTask : UnitTaskBase
    {
        public float DurationSeconds { get; }

        private float _elapsed;

        public WaitSecondsTask(float durationSeconds, int priority = 0, int? boundUnitId = null)
            : base(priority, boundUnitId)
        {
            DurationSeconds = durationSeconds < 0f ? 0f : durationSeconds;
            _elapsed = 0f;
        }

        protected override void OnTick(IUnitTaskContext context, float deltaTime)
        {
            if (DurationSeconds <= 0f)
            {
                SetSucceeded();
                return;
            }

            _elapsed += deltaTime;
            if (_elapsed >= DurationSeconds)
            {
                SetSucceeded();
            }
        }

        protected override void OnReset(IUnitTaskContext context)
        {
            base.OnReset(context);
            _elapsed = 0f;
        }
    }
}

