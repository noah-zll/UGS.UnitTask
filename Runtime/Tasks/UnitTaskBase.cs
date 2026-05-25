using System;
using System.Collections.Generic;

namespace UGS.UnitTask
{
    [Serializable]
    public abstract class UnitTaskBase : IUnitTask
    {
        public int Priority { get; }
        public UnitTaskStatus Status { get; private set; }
        public int? BoundUnitId { get; private set; }
        public UnitTaskReasonCode LastReason { get; protected set; }

        protected UnitTaskBase(int priority, int? boundUnitId = null)
        {
            Priority = priority;
            BoundUnitId = boundUnitId;
            Status = UnitTaskStatus.Pending;
            LastReason = UnitTaskReasonCode.None;
        }

        public bool TryBindExecutor(IUnitTaskContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (Status == UnitTaskStatus.Succeeded ||
                Status == UnitTaskStatus.Failed ||
                Status == UnitTaskStatus.Cancelled ||
                Status == UnitTaskStatus.Skipped)
            {
                return false;
            }

            if (BoundUnitId.HasValue)
            {
                if (!context.TryGetUnit(BoundUnitId.Value, out _))
                {
                    LastReason = UnitTaskReasonCode.UnitNotFound;
                    return false;
                }

                if (!context.IsUnitEligible(BoundUnitId.Value, this))
                {
                    LastReason = UnitTaskReasonCode.UnitIneligible;
                    return false;
                }

                if (Status == UnitTaskStatus.Pending)
                {
                    Status = UnitTaskStatus.Ready;
                }

                LastReason = UnitTaskReasonCode.None;
                return true;
            }

            if (!TrySelectExecutor(context, out var selectedUnitId, out var reasonCode))
            {
                LastReason = reasonCode;
                return false;
            }

            BoundUnitId = selectedUnitId;

            if (!context.TryGetUnit(BoundUnitId.Value, out _))
            {
                BoundUnitId = null;
                LastReason = UnitTaskReasonCode.UnitNotFound;
                return false;
            }

            if (!context.IsUnitEligible(BoundUnitId.Value, this))
            {
                BoundUnitId = null;
                LastReason = UnitTaskReasonCode.UnitIneligible;
                return false;
            }

            Status = UnitTaskStatus.Ready;
            LastReason = UnitTaskReasonCode.None;
            return true;
        }

        public void Tick(IUnitTaskContext context, float deltaTime)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (Status == UnitTaskStatus.Succeeded ||
                Status == UnitTaskStatus.Failed ||
                Status == UnitTaskStatus.Cancelled ||
                Status == UnitTaskStatus.Skipped)
            {
                return;
            }

            if (Status == UnitTaskStatus.Pending)
            {
                if (!TryBindExecutor(context))
                {
                    return;
                }
            }

            if (Status == UnitTaskStatus.Ready)
            {
                if (!CanEnterRunning(context))
                {
                    return;
                }

                Status = UnitTaskStatus.Running;
            }

            if (Status == UnitTaskStatus.Running)
            {
                if (!CanRunning(context))
                {
                    return;
                }

                OnTick(context, deltaTime);
            }
        }

        protected virtual bool CanEnterRunning(IUnitTaskContext context)
        {
            return true;
        }

        protected virtual bool CanRunning(IUnitTaskContext context)
        {
            return true;
        }

        public void Cancel(IUnitTaskContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            if (Status == UnitTaskStatus.Succeeded ||
                Status == UnitTaskStatus.Failed ||
                Status == UnitTaskStatus.Cancelled ||
                Status == UnitTaskStatus.Skipped)
            {
                return;
            }

            OnCancel(context);
            Status = UnitTaskStatus.Cancelled;
        }

        public void Reset(IUnitTaskContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            OnReset(context);
            Status = UnitTaskStatus.Pending;
            LastReason = UnitTaskReasonCode.None;
        }

        protected void SetSucceeded()
        {
            Status = UnitTaskStatus.Succeeded;
            LastReason = UnitTaskReasonCode.None;
        }

        protected void SetFailed(UnitTaskReasonCode reasonCode = UnitTaskReasonCode.TaskExecutionFailed)
        {
            Status = UnitTaskStatus.Failed;
            LastReason = reasonCode;
        }

        protected void SetSkipped(UnitTaskReasonCode reasonCode)
        {
            Status = UnitTaskStatus.Skipped;
            LastReason = reasonCode;
        }

        protected void ClearExecutor()
        {
            BoundUnitId = null;
        }

        protected virtual bool TrySelectExecutor(IUnitTaskContext context, out int unitId, out UnitTaskReasonCode reasonCode)
        {
            unitId = default;
            reasonCode = UnitTaskReasonCode.NoCandidates;
            return false;
        }

        protected abstract void OnTick(IUnitTaskContext context, float deltaTime);

        protected virtual void OnCancel(IUnitTaskContext context)
        {
        }

        protected virtual void OnReset(IUnitTaskContext context)
        {
            ClearExecutor();
        }

        internal static bool TryPickRandomEligible(
            IUnitTaskContext context,
            IReadOnlyList<int> candidates,
            IUnitTask task,
            out int unitId,
            out UnitTaskReasonCode reasonCode)
        {
            unitId = default;

            if (candidates == null || candidates.Count == 0)
            {
                reasonCode = UnitTaskReasonCode.NoCandidates;
                return false;
            }

            var eligibleCount = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                var id = candidates[i];
                if (context.IsUnitEligible(id, task))
                {
                    eligibleCount++;
                }
            }

            if (eligibleCount == 0)
            {
                reasonCode = UnitTaskReasonCode.AllIneligible;
                return false;
            }

            var targetIndexInEligible = context.Random.NextInt(0, eligibleCount);
            var currentEligibleIndex = 0;
            for (var i = 0; i < candidates.Count; i++)
            {
                var id = candidates[i];
                if (!context.IsUnitEligible(id, task))
                {
                    continue;
                }

                if (currentEligibleIndex == targetIndexInEligible)
                {
                    unitId = id;
                    reasonCode = UnitTaskReasonCode.None;
                    return true;
                }

                currentEligibleIndex++;
            }

            reasonCode = UnitTaskReasonCode.SelectionError;
            return false;
        }
    }
}
