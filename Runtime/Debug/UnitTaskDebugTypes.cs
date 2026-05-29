using System;
using System.Collections.Generic;

namespace UGS.UnitTask
{
    public enum UnitTaskDecisionKind
    {
        ChainStarted,
        ExecutorBound,
        ExecutorBindFailed,
        TaskSucceeded,
        TaskFailed,
        TaskRetryScheduled,
        TaskSkipped,
        TaskCancelled,
        ChainCompleted,
        ChainCancelled,
        ChainLoopRestarted
    }

    public readonly struct UnitTaskDecisionRecord
    {
        public readonly float Time;
        public readonly UnitTaskDecisionKind Kind;
        public readonly int ChainId;
        public readonly string ChainName;
        public readonly int TaskIndex;
        public readonly int? BoundUnitId;
        public readonly UnitTaskStatus TaskStatus;
        public readonly UnitTaskReasonCode Reason;
        public readonly Type TaskType;

        public UnitTaskDecisionRecord(
            float time,
            UnitTaskDecisionKind kind,
            int chainId,
            string chainName,
            int taskIndex,
            int? boundUnitId,
            UnitTaskStatus taskStatus,
            UnitTaskReasonCode reason,
            Type taskType)
        {
            Time = time;
            Kind = kind;
            ChainId = chainId;
            ChainName = chainName;
            TaskIndex = taskIndex;
            BoundUnitId = boundUnitId;
            TaskStatus = taskStatus;
            Reason = reason;
            TaskType = taskType;
        }
    }

    public interface IUnitTaskDebugTraceSink
    {
        void Record(in UnitTaskDecisionRecord record);
    }

    public interface IUnitTaskChainDebugView
    {
        int CurrentIndex { get; }
        IReadOnlyList<IUnitTask> Tasks { get; }
        IReadOnlyList<string> TaskLabels { get; }
    }

    public sealed class UnitTaskSchedulerSnapshot
    {
        public int Id { get; }
        public string Name { get; }
        public float Time { get; }
        public IReadOnlyList<UnitTaskChainSnapshot> Chains { get; }
        public IReadOnlyList<UnitTaskDecisionRecord> RecentDecisions { get; }

        public UnitTaskSchedulerSnapshot(
            int id,
            string name,
            float time,
            IReadOnlyList<UnitTaskChainSnapshot> chains,
            IReadOnlyList<UnitTaskDecisionRecord> recentDecisions)
        {
            Id = id;
            Name = name;
            Time = time;
            Chains = chains ?? Array.Empty<UnitTaskChainSnapshot>();
            RecentDecisions = recentDecisions ?? Array.Empty<UnitTaskDecisionRecord>();
        }
    }

    public sealed class UnitTaskChainSnapshot
    {
        public int ChainId { get; }
        public string Name { get; }
        public int Priority { get; }
        public UnitTaskChainStatus Status { get; }
        public ChainLoopMode LoopMode { get; }
        public float LoopDelaySeconds { get; }
        public UnitTaskChainFailedPolicy FailedPolicy { get; }
        public int CurrentIndex { get; }
        public IReadOnlyList<UnitTaskSnapshot> Tasks { get; }

        public UnitTaskChainSnapshot(
            int chainId,
            string name,
            int priority,
            UnitTaskChainStatus status,
            ChainLoopMode loopMode,
            float loopDelaySeconds,
            UnitTaskChainFailedPolicy failedPolicy,
            int currentIndex,
            IReadOnlyList<UnitTaskSnapshot> tasks)
        {
            ChainId = chainId;
            Name = name;
            Priority = priority;
            Status = status;
            LoopMode = loopMode;
            LoopDelaySeconds = loopDelaySeconds;
            FailedPolicy = failedPolicy;
            CurrentIndex = currentIndex;
            Tasks = tasks ?? Array.Empty<UnitTaskSnapshot>();
        }
    }

    public readonly struct UnitTaskSnapshot
    {
        public readonly string Label;
        public readonly int Priority;
        public readonly UnitTaskStatus Status;
        public readonly int? BoundUnitId;
        public readonly UnitTaskReasonCode LastReason;
        public readonly Type TaskType;

        public UnitTaskSnapshot(string label, int priority, UnitTaskStatus status, int? boundUnitId, UnitTaskReasonCode lastReason, Type taskType)
        {
            Label = label;
            Priority = priority;
            Status = status;
            BoundUnitId = boundUnitId;
            LastReason = lastReason;
            TaskType = taskType;
        }
    }

    public interface IUnitTaskDebugSnapshotSource
    {
        UnitTaskSchedulerSnapshot Capture();
    }
}
