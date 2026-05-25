using System;

namespace UGS.UnitTask
{
    [Serializable]
    public abstract class CandidateUnitTaskBase : UnitTaskBase, IUnitTaskCandidateProvider
    {
        public UnitTaskCandidateQuery CandidateQuery { get; }

        private readonly IUnitTaskExecutorSelector _selector;

        protected CandidateUnitTaskBase(
            UnitTaskCandidateQuery candidateQuery,
            int priority,
            IUnitTaskExecutorSelector selector = null)
            : base(priority, boundUnitId: null)
        {
            CandidateQuery = candidateQuery;
            _selector = selector ?? RandomEligibleSelector.Instance;
        }

        protected override bool TrySelectExecutor(IUnitTaskContext context, out int unitId, out UnitTaskReasonCode reasonCode)
        {
            var candidates = context.GetCandidateUnitIds(CandidateQuery);
            return _selector.TrySelect(context, candidates, this, out unitId, out reasonCode);
        }
    }
}

