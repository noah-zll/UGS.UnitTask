using System.Collections.Generic;

namespace UGS.UnitTask
{
    public sealed class RandomEligibleSelector : IUnitTaskExecutorSelector
    {
        public static readonly RandomEligibleSelector Instance = new RandomEligibleSelector();

        private RandomEligibleSelector()
        {
        }

        public bool TrySelect(
            IUnitTaskContext context,
            IReadOnlyList<int> candidates,
            IUnitTask task,
            out int unitId,
            out UnitTaskReasonCode reasonCode)
        {
            return UnitTaskBase.TryPickRandomEligible(context, candidates, task, out unitId, out reasonCode);
        }
    }
}

