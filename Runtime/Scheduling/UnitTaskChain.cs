using System;
using System.Collections.Generic;

namespace UGS.UnitTask
{
    [Serializable]
    public sealed class UnitTaskChain : IUnitTaskChain, IUnitTaskChainDebugView
    {
        private readonly struct BranchRule
        {
            public readonly IUnitTaskBranchCondition Condition;
            public readonly int ToTaskIndex;
            public readonly bool EndChain;

            public BranchRule(IUnitTaskBranchCondition condition, int toTaskIndex, bool endChain)
            {
                Condition = condition;
                ToTaskIndex = toTaskIndex;
                EndChain = endChain;
            }
        }

        public int ChainId { get; }
        public string Name { get; }
        public int Priority { get; private set; }
        public ChainLoopMode LoopMode { get; private set; }
        public float LoopDelaySeconds { get; private set; }
        public float TickIntervalSeconds { get; private set; }
        public UnitTaskChainFailedPolicy FailedPolicy { get; }
        public IUnitTaskChainRunCondition RunCondition { get; private set; }
        public int MaxRetriesPerTask { get; }
        public float RetryDelaySeconds { get; }
        public UnitTaskChainStatus Status { get; private set; }

        public event Action<IUnitTaskChain> Started;
        public event Action<IUnitTaskChain, UnitTaskChainStatus> Ended;

        public IUnitTask Current
        {
            get
            {
                if (_currentIndex < 0 || _currentIndex >= _tasks.Count)
                {
                    return null;
                }

                return _tasks[_currentIndex];
            }
        }

        public int Count => _tasks.Count;
        public int CurrentIndex => _currentIndex;
        public IReadOnlyList<IUnitTask> Tasks => _tasks;
        public IReadOnlyList<string> TaskLabels => _taskLabels;

        private readonly List<IUnitTask> _tasks;
        private readonly List<string> _taskLabels;
        private readonly List<int> _retryCounts;
        private readonly List<List<BranchRule>> _branches;
        private readonly List<BranchRule> _entryBranches;
        private readonly Dictionary<string, int> _labelToIndex;
        private int _currentIndex;

        private bool _isWaitingForLoopDelay;
        private float _loopDelayUntilTime;

        private bool _isWaitingForRetryDelay;
        private float _retryUntilTime;

        private float _nextTickTime;
        private float _lastTickTime;

        private bool _hasStarted;
        private bool _hasEnded;
        private bool _entryResolved;

        public UnitTaskChain(
            int chainId,
            string name = null,
            int priority = 0,
            ChainLoopMode loopMode = ChainLoopMode.OneShot,
            float loopDelaySeconds = 0f,
            UnitTaskChainFailedPolicy failedPolicy = UnitTaskChainFailedPolicy.AbortChain,
            int maxRetriesPerTask = 0,
            float retryDelaySeconds = 0f)
        {
            ChainId = chainId;
            Name = string.IsNullOrEmpty(name) ? $"Chain#{chainId}" : name;
            Priority = priority;
            LoopMode = loopMode;
            LoopDelaySeconds = loopDelaySeconds < 0f ? 0f : loopDelaySeconds;
            TickIntervalSeconds = 0f;
            FailedPolicy = failedPolicy;
            RunCondition = null;
            MaxRetriesPerTask = maxRetriesPerTask < 0 ? 0 : maxRetriesPerTask;
            RetryDelaySeconds = retryDelaySeconds < 0f ? 0f : retryDelaySeconds;
            Status = UnitTaskChainStatus.Active;

            _tasks = new List<IUnitTask>(8);
            _taskLabels = new List<string>(8);
            _retryCounts = new List<int>(8);
            _branches = new List<List<BranchRule>>(8);
            _entryBranches = new List<BranchRule>(2);
            _labelToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            _currentIndex = 0;
            _isWaitingForLoopDelay = false;
            _loopDelayUntilTime = 0f;
            _isWaitingForRetryDelay = false;
            _retryUntilTime = 0f;
            _nextTickTime = 0f;
            _lastTickTime = 0f;
            _hasStarted = false;
            _hasEnded = false;
            _entryResolved = false;
        }

        public void Enqueue(IUnitTask task)
        {
            Enqueue(task, label: null);
        }

        public void Enqueue(IUnitTask task, string label)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));

            _tasks.Add(task);
            _retryCounts.Add(0);
            _branches.Add(null);
            _taskLabels.Add(null);

            if (!string.IsNullOrEmpty(label))
            {
                SetTaskLabel(_tasks.Count - 1, label);
            }
        }

        public void SetTaskLabel(int taskIndex, string label)
        {
            if (taskIndex < 0 || taskIndex >= _tasks.Count) throw new ArgumentOutOfRangeException(nameof(taskIndex));

            if (string.IsNullOrEmpty(label))
            {
                var old = _taskLabels[taskIndex];
                if (!string.IsNullOrEmpty(old))
                {
                    _labelToIndex.Remove(old);
                }

                _taskLabels[taskIndex] = null;
                return;
            }

            if (_labelToIndex.TryGetValue(label, out var existing) && existing != taskIndex)
            {
                throw new ArgumentException($"Duplicate task label '{label}'.", nameof(label));
            }

            var previous = _taskLabels[taskIndex];
            if (!string.IsNullOrEmpty(previous) && !string.Equals(previous, label, StringComparison.Ordinal))
            {
                _labelToIndex.Remove(previous);
            }

            _taskLabels[taskIndex] = label;
            _labelToIndex[label] = taskIndex;
        }

        public bool TryGetTaskIndex(string label, out int taskIndex)
        {
            if (string.IsNullOrEmpty(label))
            {
                taskIndex = -1;
                return false;
            }

            return _labelToIndex.TryGetValue(label, out taskIndex);
        }

        public void AddBranch(int fromTaskIndex, IUnitTaskBranchCondition condition, int toTaskIndex)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (toTaskIndex < 0 || toTaskIndex >= _tasks.Count) throw new ArgumentOutOfRangeException(nameof(toTaskIndex));
            if (toTaskIndex == fromTaskIndex) throw new ArgumentException("toTaskIndex must not equal fromTaskIndex.", nameof(toTaskIndex));

            if (fromTaskIndex == -1)
            {
                _entryBranches.Add(new BranchRule(condition, toTaskIndex, endChain: false));
                _entryResolved = false;
                return;
            }

            if (fromTaskIndex < 0 || fromTaskIndex >= _tasks.Count) throw new ArgumentOutOfRangeException(nameof(fromTaskIndex));

            var list = _branches[fromTaskIndex];
            if (list == null)
            {
                list = new List<BranchRule>(2);
                _branches[fromTaskIndex] = list;
            }

            list.Add(new BranchRule(condition, toTaskIndex, endChain: false));
        }

        public void AddEndBranch(int fromTaskIndex, IUnitTaskBranchCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            if (fromTaskIndex == -1)
            {
                _entryBranches.Add(new BranchRule(condition, toTaskIndex: -1, endChain: true));
                _entryResolved = false;
                return;
            }

            if (fromTaskIndex < 0 || fromTaskIndex >= _tasks.Count) throw new ArgumentOutOfRangeException(nameof(fromTaskIndex));

            var list = _branches[fromTaskIndex];
            if (list == null)
            {
                list = new List<BranchRule>(2);
                _branches[fromTaskIndex] = list;
            }

            list.Add(new BranchRule(condition, toTaskIndex: -1, endChain: true));
        }

        public void AddBranch(string fromTaskLabel, IUnitTaskBranchCondition condition, string toTaskLabel)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            var fromIndex = -1;
            if (!string.IsNullOrEmpty(fromTaskLabel) && !TryGetTaskIndex(fromTaskLabel, out fromIndex))
            {
                throw new ArgumentException($"Unknown fromTaskLabel '{fromTaskLabel}'.", nameof(fromTaskLabel));
            }

            if (!TryGetTaskIndex(toTaskLabel, out var toIndex))
            {
                throw new ArgumentException($"Unknown toTaskLabel '{toTaskLabel}'.", nameof(toTaskLabel));
            }

            AddBranch(fromIndex, condition, toIndex);
        }

        public void AddEndBranch(string fromTaskLabel, IUnitTaskBranchCondition condition)
        {
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            var fromIndex = -1;
            if (!string.IsNullOrEmpty(fromTaskLabel) && !TryGetTaskIndex(fromTaskLabel, out fromIndex))
            {
                throw new ArgumentException($"Unknown fromTaskLabel '{fromTaskLabel}'.", nameof(fromTaskLabel));
            }

            AddEndBranch(fromIndex, condition);
        }

        public void AddBranches(int fromTaskIndex, params UnitTaskBranchCase[] branches)
        {
            if (branches == null) throw new ArgumentNullException(nameof(branches));

            for (var i = 0; i < branches.Length; i++)
            {
                var b = branches[i];
                if (b.Condition == null) throw new ArgumentNullException(nameof(branches), "Branch condition is null.");

                if (b.EndChain)
                {
                    AddEndBranch(fromTaskIndex, b.Condition);
                }
                else
                {
                    AddBranch(fromTaskIndex, b.Condition, b.ToTaskIndex);
                }
            }
        }

        public void AddBranches(string fromTaskLabel, params UnitTaskBranchCaseByLabel[] branches)
        {
            if (branches == null) throw new ArgumentNullException(nameof(branches));

            for (var i = 0; i < branches.Length; i++)
            {
                var b = branches[i];
                if (b.Condition == null) throw new ArgumentNullException(nameof(branches), "Branch condition is null.");

                if (b.EndChain)
                {
                    AddEndBranch(fromTaskLabel, b.Condition);
                }
                else
                {
                    AddBranch(fromTaskLabel, b.Condition, b.ToTaskLabel);
                }
            }
        }

        public void SetPriority(int priority)
        {
            Priority = priority;
        }

        public void SetLoopMode(ChainLoopMode loopMode)
        {
            LoopMode = loopMode;
        }

        public void SetLoopDelaySeconds(float loopDelaySeconds)
        {
            LoopDelaySeconds = loopDelaySeconds < 0f ? 0f : loopDelaySeconds;
        }

        public void SetTickInterval(float tickIntervalSeconds)
        {
            TickIntervalSeconds = tickIntervalSeconds < 0f ? 0f : tickIntervalSeconds;
            _nextTickTime = 0f;
            _lastTickTime = 0f;
        }

        public void SetRunCondition(IUnitTaskChainRunCondition condition)
        {
            RunCondition = condition;
        }

        public void Tick(IUnitTaskContext context, float deltaTime)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
            {
                return;
            }

            if (Status == UnitTaskChainStatus.Paused)
            {
                return;
            }

            if (RunCondition != null && !RunCondition.Evaluate(context, this))
            {
                return;
            }

            if (_tasks.Count == 0)
            {
                Complete(context, taskIndex: -1, task: null);
                return;
            }

            var canAdvance = true;
            if (TickIntervalSeconds > 0f)
            {
                if (_nextTickTime > 0f && context.Time < _nextTickTime)
                {
                    canAdvance = false;
                }
                else
                {
                    _nextTickTime = context.Time + TickIntervalSeconds;
                }
            }

            if (_isWaitingForLoopDelay)
            {
                if (context.Time < _loopDelayUntilTime)
                {
                    return;
                }

                _isWaitingForLoopDelay = false;
                _loopDelayUntilTime = 0f;
            }

            if (_isWaitingForRetryDelay)
            {
                if (context.Time < _retryUntilTime)
                {
                    return;
                }

                _isWaitingForRetryDelay = false;
                _retryUntilTime = 0f;
            }

            var current = Current;
            if (!canAdvance && (current == null || current.Status != UnitTaskStatus.Running))
            {
                return;
            }

            var trace = context as IUnitTaskDebugTraceSink;
            TryStart(context, trace);
            ApplyEntryBranches(context);
            if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
            {
                return;
            }

            while (true)
            {
                if (_currentIndex >= _tasks.Count)
                {
                    if (!canAdvance)
                    {
                        return;
                    }
                    if (LoopMode == ChainLoopMode.OneShot)
                    {
                        Complete(context, taskIndex: _tasks.Count - 1, task: null);
                        return;
                    }

                    RestartFromBeginning(context);
                    if (_isWaitingForLoopDelay)
                    {
                        return;
                    }
                    return;
                }

                var task = _tasks[_currentIndex];
                if (!canAdvance && task.Status != UnitTaskStatus.Running)
                {
                    return;
                }
                if (task.Status == UnitTaskStatus.Succeeded || task.Status == UnitTaskStatus.Skipped)
                {
                    _currentIndex = ResolveNextIndex(context, _currentIndex, task);
                    if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
                    {
                        return;
                    }
                    continue;
                }
                if (task.Status == UnitTaskStatus.Failed)
                {
                    Complete(context, taskIndex: _currentIndex, task: task);
                    return;
                }
                if (task.Status == UnitTaskStatus.Cancelled)
                {
                    CancelInternal(context, taskIndex: _currentIndex, task: task, boundUnitId: task.BoundUnitId);
                    return;
                }
                var beforeStatus = task.Status;
                var beforeBoundUnitId = task.BoundUnitId;
                var beforeReason = task.LastReason;
                task.Tick(context, deltaTime);
                var afterStatus = task.Status;
                var afterBoundUnitId = task.BoundUnitId;
                var afterReason = task.LastReason;

                if (trace != null)
                {
                    if (!beforeBoundUnitId.HasValue && afterBoundUnitId.HasValue)
                    {
                        trace.Record(new UnitTaskDecisionRecord(
                            time: context.Time,
                            kind: UnitTaskDecisionKind.ExecutorBound,
                            chainId: ChainId,
                            taskIndex: _currentIndex,
                            boundUnitId: afterBoundUnitId,
                            taskStatus: afterStatus,
                            reason: UnitTaskReasonCode.None,
                            taskType: task.GetType()));
                    }
                    else if (beforeStatus == UnitTaskStatus.Pending && afterStatus == UnitTaskStatus.Pending && beforeReason != afterReason)
                    {
                        trace.Record(new UnitTaskDecisionRecord(
                            time: context.Time,
                            kind: UnitTaskDecisionKind.ExecutorBindFailed,
                            chainId: ChainId,
                            taskIndex: _currentIndex,
                            boundUnitId: afterBoundUnitId,
                            taskStatus: afterStatus,
                            reason: afterReason,
                            taskType: task.GetType()));
                    }
                }

                if (task.Status == UnitTaskStatus.Succeeded || task.Status == UnitTaskStatus.Skipped)
                {
                    if (trace != null)
                    {
                        trace.Record(new UnitTaskDecisionRecord(
                            time: context.Time,
                            kind: task.Status == UnitTaskStatus.Succeeded ? UnitTaskDecisionKind.TaskSucceeded : UnitTaskDecisionKind.TaskSkipped,
                            chainId: ChainId,
                            taskIndex: _currentIndex,
                            boundUnitId: afterBoundUnitId,
                            taskStatus: task.Status,
                            reason: task.LastReason,
                            taskType: task.GetType()));
                    }
                    _currentIndex = ResolveNextIndex(context, _currentIndex, task);
                    if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
                    {
                        return;
                    }
                    continue;
                }

                if (task.Status == UnitTaskStatus.Failed)
                {
                    if (trace != null)
                    {
                        trace.Record(new UnitTaskDecisionRecord(
                            time: context.Time,
                            kind: UnitTaskDecisionKind.TaskFailed,
                            chainId: ChainId,
                            taskIndex: _currentIndex,
                            boundUnitId: afterBoundUnitId,
                            taskStatus: task.Status,
                            reason: task.LastReason,
                            taskType: task.GetType()));
                    }

                    if (FailedPolicy == UnitTaskChainFailedPolicy.RetryTask && MaxRetriesPerTask > 0)
                    {
                        var currentRetry = _retryCounts[_currentIndex];
                        if (currentRetry < MaxRetriesPerTask)
                        {
                            _retryCounts[_currentIndex] = currentRetry + 1;

                            if (RetryDelaySeconds > 0f)
                            {
                                _isWaitingForRetryDelay = true;
                                _retryUntilTime = context.Time + RetryDelaySeconds;
                            }

                            task.Reset(context);

                            if (trace != null)
                            {
                                trace.Record(new UnitTaskDecisionRecord(
                                    time: context.Time,
                                    kind: UnitTaskDecisionKind.TaskRetryScheduled,
                                    chainId: ChainId,
                                    taskIndex: _currentIndex,
                                    boundUnitId: task.BoundUnitId,
                                    taskStatus: task.Status,
                                    reason: UnitTaskReasonCode.None,
                                    taskType: task.GetType()));
                            }

                            return;
                        }
                    }

                    if (FailedPolicy == UnitTaskChainFailedPolicy.SkipTaskAndContinue)
                    {
                        _currentIndex = ResolveNextIndex(context, _currentIndex, task);
                        if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
                        {
                            return;
                        }
                        continue;
                    }

                    Complete(context, taskIndex: _currentIndex, task: task);
                    return;
                }

                if (task.Status == UnitTaskStatus.Cancelled)
                {
                    CancelInternal(context, taskIndex: _currentIndex, task: task, boundUnitId: afterBoundUnitId);
                    return;
                }

                return;
            }
        }

        public void Cancel(IUnitTaskContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (Status == UnitTaskChainStatus.Completed || Status == UnitTaskChainStatus.Cancelled)
            {
                return;
            }

            var trace = context as IUnitTaskDebugTraceSink;
            var current = Current;
            if (current != null)
            {
                current.Cancel(context);
            }

            CancelInternal(context, taskIndex: _currentIndex, task: current, boundUnitId: current != null ? current.BoundUnitId : null);
        }

        public void Pause()
        {
            if (Status == UnitTaskChainStatus.Active)
            {
                Status = UnitTaskChainStatus.Paused;
            }
        }

        public void Resume()
        {
            if (Status == UnitTaskChainStatus.Paused)
            {
                Status = UnitTaskChainStatus.Active;
            }
        }

        public void Reset(IUnitTaskContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            for (var i = 0; i < _tasks.Count; i++)
            {
                _tasks[i].Reset(context);
                _retryCounts[i] = 0;
            }

            Status = UnitTaskChainStatus.Active;
            _currentIndex = 0;
            _isWaitingForLoopDelay = false;
            _loopDelayUntilTime = 0f;
            _isWaitingForRetryDelay = false;
            _retryUntilTime = 0f;
            _nextTickTime = 0f;
            _lastTickTime = 0f;
            _hasStarted = false;
            _hasEnded = false;
            _entryResolved = false;
        }

        private void RestartFromBeginning(IUnitTaskContext context)
        {
            var trace = context as IUnitTaskDebugTraceSink;

            if (LoopMode == ChainLoopMode.LoopWithDelay && LoopDelaySeconds > 0f)
            {
                _isWaitingForLoopDelay = true;
                _loopDelayUntilTime = context.Time + LoopDelaySeconds;
            }

            for (var i = 0; i < _tasks.Count; i++)
            {
                _tasks[i].Reset(context);
                _retryCounts[i] = 0;
            }

            _currentIndex = 0;
            _isWaitingForRetryDelay = false;
            _retryUntilTime = 0f;
            _entryResolved = false;
            _lastTickTime = 0f;

            if (trace != null)
            {
                trace.Record(new UnitTaskDecisionRecord(
                    time: context.Time,
                    kind: UnitTaskDecisionKind.ChainLoopRestarted,
                    chainId: ChainId,
                    taskIndex: 0,
                    boundUnitId: null,
                    taskStatus: UnitTaskStatus.Pending,
                    reason: UnitTaskReasonCode.None,
                    taskType: _tasks.Count > 0 ? _tasks[0].GetType() : null));
            }
        }

        private int ResolveNextIndex(IUnitTaskContext context, int fromTaskIndex, IUnitTask task)
        {
            var branchRules = _branches[fromTaskIndex];
            if (branchRules != null)
            {
                for (var i = 0; i < branchRules.Count; i++)
                {
                    var rule = branchRules[i];
                    if (rule.Condition != null && rule.Condition.Evaluate(context, this, fromTaskIndex, task))
                    {
                        if (rule.EndChain)
                        {
                            Complete(context, taskIndex: fromTaskIndex, task: task);
                            return _tasks.Count;
                        }

                        return JumpTo(context, fromTaskIndex, rule.ToTaskIndex);
                    }
                }
            }

            return fromTaskIndex + 1;
        }

        private void ApplyEntryBranches(IUnitTaskContext context)
        {
            if (_entryResolved)
            {
                return;
            }

            _entryResolved = true;

            if (_entryBranches.Count == 0)
            {
                return;
            }

            for (var i = 0; i < _entryBranches.Count; i++)
            {
                var rule = _entryBranches[i];
                if (rule.Condition != null && rule.Condition.Evaluate(context, this, fromTaskIndex: -1, task: null))
                {
                    if (rule.EndChain)
                    {
                        Complete(context, taskIndex: -1, task: null);
                        _currentIndex = _tasks.Count;
                        return;
                    }

                    _currentIndex = rule.ToTaskIndex;
                    return;
                }
            }
        }

        private int JumpTo(IUnitTaskContext context, int fromTaskIndex, int toTaskIndex)
        {
            if (toTaskIndex < 0 || toTaskIndex >= _tasks.Count)
            {
                return fromTaskIndex + 1;
            }

            if (toTaskIndex == fromTaskIndex)
            {
                return fromTaskIndex + 1;
            }

            if (toTaskIndex < fromTaskIndex)
            {
                ResetRange(context, toTaskIndex, fromTaskIndex);
                _isWaitingForRetryDelay = false;
                _retryUntilTime = 0f;
            }

            return toTaskIndex;
        }

        private void ResetRange(IUnitTaskContext context, int startInclusive, int endInclusive)
        {
            if (startInclusive < 0) startInclusive = 0;
            if (endInclusive >= _tasks.Count) endInclusive = _tasks.Count - 1;
            if (startInclusive > endInclusive) return;

            for (var i = startInclusive; i <= endInclusive; i++)
            {
                _tasks[i].Reset(context);
                _retryCounts[i] = 0;
            }
        }

        private void TryStart(IUnitTaskContext context, IUnitTaskDebugTraceSink trace)
        {
            if (_hasStarted)
            {
                return;
            }

            _hasStarted = true;
            Started?.Invoke(this);

            if (trace != null)
            {
                trace.Record(new UnitTaskDecisionRecord(
                    time: context.Time,
                    kind: UnitTaskDecisionKind.ChainStarted,
                    chainId: ChainId,
                    taskIndex: _currentIndex,
                    boundUnitId: null,
                    taskStatus: UnitTaskStatus.Pending,
                    reason: UnitTaskReasonCode.None,
                    taskType: null));
            }
        }

        private void Complete(IUnitTaskContext context, int taskIndex, IUnitTask task)
        {
            if (_hasEnded)
            {
                return;
            }

            _hasEnded = true;
            Status = UnitTaskChainStatus.Completed;

            var trace = context as IUnitTaskDebugTraceSink;
            if (trace != null)
            {
                trace.Record(new UnitTaskDecisionRecord(
                    time: context.Time,
                    kind: UnitTaskDecisionKind.ChainCompleted,
                    chainId: ChainId,
                    taskIndex: taskIndex,
                    boundUnitId: null,
                    taskStatus: task != null ? task.Status : UnitTaskStatus.Succeeded,
                    reason: task != null ? task.LastReason : UnitTaskReasonCode.None,
                    taskType: task != null ? task.GetType() : null));
            }

            Ended?.Invoke(this, Status);
        }

        private void CancelInternal(IUnitTaskContext context, int taskIndex, IUnitTask task, int? boundUnitId)
        {
            if (_hasEnded)
            {
                return;
            }

            _hasEnded = true;
            Status = UnitTaskChainStatus.Cancelled;

            var trace = context as IUnitTaskDebugTraceSink;
            if (trace != null && task != null && task.Status == UnitTaskStatus.Cancelled)
            {
                trace.Record(new UnitTaskDecisionRecord(
                    time: context.Time,
                    kind: UnitTaskDecisionKind.TaskCancelled,
                    chainId: ChainId,
                    taskIndex: taskIndex,
                    boundUnitId: boundUnitId,
                    taskStatus: task.Status,
                    reason: task.LastReason,
                    taskType: task.GetType()));
            }

            if (trace != null)
            {
                trace.Record(new UnitTaskDecisionRecord(
                    time: context.Time,
                    kind: UnitTaskDecisionKind.ChainCancelled,
                    chainId: ChainId,
                    taskIndex: taskIndex,
                    boundUnitId: null,
                    taskStatus: task != null ? task.Status : UnitTaskStatus.Cancelled,
                    reason: task != null ? task.LastReason : UnitTaskReasonCode.None,
                    taskType: task != null ? task.GetType() : null));
            }

            Ended?.Invoke(this, Status);
        }
    }
}

