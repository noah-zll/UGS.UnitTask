using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace UGS.UnitTask
{
    public interface IUnit
    {
        int Id { get; }
    }

    public interface IRandom
    {
        int NextInt(int minInclusive, int maxExclusive);
    }

    public enum UnitTaskStatus
    {
        [Description("待处理")]
        Pending,
        [Description("待执行")]
        Ready,
        [Description("执行中")]
        Running,
        [Description("已完成")]
        Succeeded,
        [Description("已失败")]
        Failed,
        [Description("已取消")]
        Cancelled,
        [Description("已跳过")]
        Skipped
    }

    public enum UnitTaskChainStatus
    {
        Active,
        Paused,
        Completed,
        Cancelled
    }

    public enum ChainLoopMode
    {
        OneShot,
        Loop,
        LoopWithDelay
    }

    public enum UnitTaskChainFailedPolicy
    {
        AbortChain,
        SkipTaskAndContinue,
        RetryTask
    }

    public enum UnitTaskReasonCode
    {
        [Description("无")]
        None,
        [Description("无候选单位")]
        NoCandidates,
        [Description("所有单位都不符合条件")]
        AllIneligible,
        [Description("单位不存在")]
        UnitNotFound,
        [Description("单位不符合条件")]
        UnitIneligible,
        [Description("预算限制")]
        BudgetLimited,
        [Description("选择错误")]
        SelectionError,
        [Description("任务前置条件失败")]
        TaskPreconditionFailed,
        [Description("任务执行失败")]
        TaskExecutionFailed
    }

    [Serializable]
    public readonly struct UnitTaskCandidateQuery : IEquatable<UnitTaskCandidateQuery>
    {
        public readonly int Kind;
        public readonly int Param0;
        public readonly int Param1;
        public readonly int Param2;

        public UnitTaskCandidateQuery(int kind, int param0 = 0, int param1 = 0, int param2 = 0)
        {
            Kind = kind;
            Param0 = param0;
            Param1 = param1;
            Param2 = param2;
        }

        public bool Equals(UnitTaskCandidateQuery other)
        {
            return Kind == other.Kind && Param0 == other.Param0 && Param1 == other.Param1 && Param2 == other.Param2;
        }

        public override bool Equals(object obj)
        {
            return obj is UnitTaskCandidateQuery other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Kind;
                hashCode = (hashCode * 397) ^ Param0;
                hashCode = (hashCode * 397) ^ Param1;
                hashCode = (hashCode * 397) ^ Param2;
                return hashCode;
            }
        }

        public static bool operator ==(UnitTaskCandidateQuery left, UnitTaskCandidateQuery right) => left.Equals(right);
        public static bool operator !=(UnitTaskCandidateQuery left, UnitTaskCandidateQuery right) => !left.Equals(right);
    }

    public interface IUnitTaskContext
    {
        bool TryGetUnit(int unitId, out IUnit unit);
        IReadOnlyList<int> GetCandidateUnitIds(UnitTaskCandidateQuery query);
        bool IsUnitEligible(int unitId, IUnitTask task);
        IRandom Random { get; }
        float Time { get; }
    }

    public interface IUnitTaskContextServices
    {
        bool TryGetService<T>(out T service) where T : class;
    }

    public interface IUnitTask
    {
        int Priority { get; }
        UnitTaskStatus Status { get; }
        int? BoundUnitId { get; }
        UnitTaskReasonCode LastReason { get; }

        bool TryBindExecutor(IUnitTaskContext context);
        void Tick(IUnitTaskContext context, float deltaTime);
        void Cancel(IUnitTaskContext context);
        void Reset(IUnitTaskContext context);
    }

    public interface IUnitTaskCandidateProvider
    {
        UnitTaskCandidateQuery CandidateQuery { get; }
    }

    public interface IUnitTaskExecutorSelector
    {
        bool TrySelect(
            IUnitTaskContext context,
            IReadOnlyList<int> candidates,
            IUnitTask task,
            out int unitId,
            out UnitTaskReasonCode reasonCode);
    }

    public interface IUnitTaskBranchCondition
    {
        bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain, int fromTaskIndex, IUnitTask task);
    }

    public interface IUnitTaskChainRunCondition
    {
        bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain);
    }

    public readonly struct UnitTaskBranchCase
    {
        public readonly IUnitTaskBranchCondition Condition;
        public readonly int ToTaskIndex;
        public readonly bool EndChain;

        public UnitTaskBranchCase(IUnitTaskBranchCondition condition, int toTaskIndex)
        {
            Condition = condition;
            ToTaskIndex = toTaskIndex;
            EndChain = false;
        }

        public UnitTaskBranchCase(IUnitTaskBranchCondition condition)
        {
            Condition = condition;
            ToTaskIndex = -1;
            EndChain = true;
        }
    }

    public readonly struct UnitTaskBranchCaseByLabel
    {
        public readonly IUnitTaskBranchCondition Condition;
        public readonly string ToTaskLabel;
        public readonly bool EndChain;

        public UnitTaskBranchCaseByLabel(IUnitTaskBranchCondition condition, string toTaskLabel)
        {
            Condition = condition;
            ToTaskLabel = toTaskLabel;
            EndChain = false;
        }

        public UnitTaskBranchCaseByLabel(IUnitTaskBranchCondition condition)
        {
            Condition = condition;
            ToTaskLabel = null;
            EndChain = true;
        }
    }

    public interface IUnitTaskChain
    {
        int ChainId { get; }
        string Name { get; }
        int Priority { get; }
        ChainLoopMode LoopMode { get; }
        float LoopDelaySeconds { get; }
        float TickIntervalSeconds { get; }
        UnitTaskChainFailedPolicy FailedPolicy { get; }
        IUnitTaskChainRunCondition RunCondition { get; }

        UnitTaskChainStatus Status { get; }
        IUnitTask Current { get; }
        int Count { get; }

        event Action<IUnitTaskChain> Started;
        event Action<IUnitTaskChain, UnitTaskChainStatus> Ended;
        event Action<IUnitTaskChain> Reseted;

        void Enqueue(IUnitTask task);
        void Enqueue(IUnitTask task, string label);
        void SetTaskLabel(int taskIndex, string label);
        bool TryGetTaskIndex(string label, out int taskIndex);
        void AddBranch(int fromTaskIndex, IUnitTaskBranchCondition condition, int toTaskIndex);
        void AddEndBranch(int fromTaskIndex, IUnitTaskBranchCondition condition);
        void AddBranch(string fromTaskLabel, IUnitTaskBranchCondition condition, string toTaskLabel);
        void AddEndBranch(string fromTaskLabel, IUnitTaskBranchCondition condition);
        void AddBranches(int fromTaskIndex, params UnitTaskBranchCase[] branches);
        void AddBranches(string fromTaskLabel, params UnitTaskBranchCaseByLabel[] branches);
        void SetTickInterval(float tickIntervalSeconds);
        void SetRunCondition(IUnitTaskChainRunCondition condition);
        void Tick(IUnitTaskContext context, float deltaTime);
        void Cancel(IUnitTaskContext context);
        void Pause();
        void Resume();
    }

    public readonly struct UnitTaskSchedulerConfig
    {
        public readonly int MaxChainsPerTick;
        public readonly bool AutoRemoveFinishedChains;
        public readonly bool EnableDebugTrace;
        public readonly int DebugTraceCapacity;

        public UnitTaskSchedulerConfig(
            int maxChainsPerTick,
            bool autoRemoveFinishedChains,
            bool enableDebugTrace,
            int debugTraceCapacity)
        {
            MaxChainsPerTick = maxChainsPerTick;
            AutoRemoveFinishedChains = autoRemoveFinishedChains;
            EnableDebugTrace = enableDebugTrace;
            DebugTraceCapacity = debugTraceCapacity <= 0 ? 0 : debugTraceCapacity;
        }

        public static UnitTaskSchedulerConfig Default => new UnitTaskSchedulerConfig(
            maxChainsPerTick: 64,
            autoRemoveFinishedChains: true,
            enableDebugTrace: false,
            debugTraceCapacity: 256);
    }

    public interface IUnitTaskScheduler
    {
        int ChainCount { get; }
        UnitTaskSchedulerConfig Config { get; }

        void AddChain(IUnitTaskChain chain);
        bool RemoveChain(int chainId);
        void Tick(IUnitTaskContext context, float deltaTime);
    }
}

