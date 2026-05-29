using System;
using System.Collections.Generic;

namespace UGS.UnitTask
{
    public sealed class UnitTaskScheduler : IUnitTaskScheduler, IUnitTaskDebugSnapshotSource, IDisposable
    {
        public int Id { get; }
        public string Name { get; }
        public int ChainCount => _chains.Count;
        public UnitTaskSchedulerConfig Config { get; }

        private readonly List<IUnitTaskChain> _chains;
        private readonly UnitTaskDebugTraceBuffer _traceBuffer;
        private readonly UnitTaskContextProxy _proxyContext;
        private float _lastTime;
        private bool _disposed;

        public UnitTaskScheduler(int id, string name, UnitTaskSchedulerConfig? config = null)
        {
            Id = id;
            Name = name;
            Config = config ?? UnitTaskSchedulerConfig.Default;
            _chains = new List<IUnitTaskChain>(16);
            _traceBuffer = Config.EnableDebugTrace ? new UnitTaskDebugTraceBuffer(Config.DebugTraceCapacity) : null;
            _proxyContext = _traceBuffer != null ? new UnitTaskContextProxy(_traceBuffer) : null;
            _lastTime = 0f;

            if (Config.EnableDebugTrace)
            {
                UnitTaskDebugRegistry.Register(this);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            UnitTaskDebugRegistry.Unregister(this);
        }

        public void AddChain(IUnitTaskChain chain)
        {
            if (chain == null) throw new ArgumentNullException(nameof(chain));

            var insertIndex = _chains.Count;
            for (var i = 0; i < _chains.Count; i++)
            {
                if (_chains[i].Priority < chain.Priority)
                {
                    insertIndex = i;
                    break;
                }
            }

            _chains.Insert(insertIndex, chain);
        }

        public bool RemoveChain(int chainId)
        {
            for (var i = 0; i < _chains.Count; i++)
            {
                if (_chains[i].ChainId == chainId)
                {
                    _chains.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        public void Tick(IUnitTaskContext context, float deltaTime)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (_disposed)
            {
                return;
            }

            if (_chains.Count == 0)
            {
                return;
            }

            _lastTime = context.Time;

            var effectiveContext = context;
            if (_proxyContext != null)
            {
                _proxyContext.SetInner(context);
                effectiveContext = _proxyContext;
            }

            var initialCount = _chains.Count;
            var maxTicks = Config.MaxChainsPerTick <= 0 ? initialCount : Math.Min(Config.MaxChainsPerTick, initialCount);
            for (int i = 0, ticked = 0; ticked < maxTicks && i < _chains.Count; ticked++)
            {
                var chain = _chains[i];
                chain.Tick(effectiveContext, deltaTime);

                if (i < _chains.Count && ReferenceEquals(_chains[i], chain))
                {
                    i++;
                }
            }

            if (Config.AutoRemoveFinishedChains)
            {
                for (var i = _chains.Count - 1; i >= 0; i--)
                {
                    var status = _chains[i].Status;
                    if (status == UnitTaskChainStatus.Completed || status == UnitTaskChainStatus.Cancelled)
                    {
                        _chains.RemoveAt(i);
                    }
                }
            }
        }

        public UnitTaskSchedulerSnapshot Capture()
        {
            var chains = new List<UnitTaskChainSnapshot>(_chains.Count);

            for (var i = 0; i < _chains.Count; i++)
            {
                var chain = _chains[i];
                var debugView = chain as IUnitTaskChainDebugView;

                var tasks = new List<UnitTaskSnapshot>(debugView != null ? debugView.Tasks.Count : 1);
                var currentIndex = debugView != null ? debugView.CurrentIndex : -1;

                if (debugView != null)
                {
                    for (var t = 0; t < debugView.Tasks.Count; t++)
                    {
                        var task = debugView.Tasks[t];
                        var label = debugView.TaskLabels != null && t < debugView.TaskLabels.Count ? debugView.TaskLabels[t] : null;
                        tasks.Add(new UnitTaskSnapshot(label, task.Priority, task.Status, task.BoundUnitId, task.LastReason, task.GetType()));
                    }
                }
                else
                {
                    var current = chain.Current;
                    if (current != null)
                    {
                        tasks.Add(new UnitTaskSnapshot(null, current.Priority, current.Status, current.BoundUnitId, current.LastReason, current.GetType()));
                    }
                }

                chains.Add(new UnitTaskChainSnapshot(
                    chainId: chain.ChainId,
                    name: chain.Name,
                    priority: chain.Priority,
                    status: chain.Status,
                    loopMode: chain.LoopMode,
                    loopDelaySeconds: chain.LoopDelaySeconds,
                    failedPolicy: chain.FailedPolicy,
                    currentIndex: currentIndex,
                    tasks: tasks));
            }

            var decisions = _traceBuffer != null ? _traceBuffer.ToArray() : Array.Empty<UnitTaskDecisionRecord>();
            return new UnitTaskSchedulerSnapshot(Id, Name, _lastTime, chains, decisions);
        }

        private sealed class UnitTaskContextProxy : IUnitTaskContext, IUnitTaskDebugTraceSink, IUnitTaskContextServices
        {
            private IUnitTaskContext _inner;
            private readonly UnitTaskDebugTraceBuffer _buffer;

            public UnitTaskContextProxy(UnitTaskDebugTraceBuffer buffer)
            {
                _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            }

            public void SetInner(IUnitTaskContext inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public bool TryGetUnit(int unitId, out IUnit unit) => _inner.TryGetUnit(unitId, out unit);
            public IReadOnlyList<int> GetCandidateUnitIds(UnitTaskCandidateQuery query) => _inner.GetCandidateUnitIds(query);
            public bool IsUnitEligible(int unitId, IUnitTask task) => _inner.IsUnitEligible(unitId, task);
            public IRandom Random => _inner.Random;
            public float Time => _inner.Time;

            public bool TryGetService<T>(out T service) where T : class
            {
                service = _inner as T;
                return service != null;
            }

            public void Record(in UnitTaskDecisionRecord record)
            {
                _buffer.Record(in record);
            }
        }
    }
}
