using System;

namespace UGS.UnitTask
{
    public sealed class AlwaysTrueBranchCondition : IUnitTaskBranchCondition
    {
        public static readonly AlwaysTrueBranchCondition Instance = new AlwaysTrueBranchCondition();

        private AlwaysTrueBranchCondition()
        {
        }

        public bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain, int fromTaskIndex, IUnitTask task)
        {
            return true;
        }
    }

    public sealed class AlwaysFalseBranchCondition : IUnitTaskBranchCondition
    {
        public static readonly AlwaysFalseBranchCondition Instance = new AlwaysFalseBranchCondition();

        private AlwaysFalseBranchCondition()
        {
        }

        public bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain, int fromTaskIndex, IUnitTask task)
        {
            return false;
        }
    }

    public sealed class FuncBranchCondition : IUnitTaskBranchCondition
    {
        private readonly Func<IUnitTaskContext, IUnitTaskChain, int, IUnitTask, bool> _func;

        public FuncBranchCondition(Func<IUnitTaskContext, IUnitTaskChain, int, IUnitTask, bool> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain, int fromTaskIndex, IUnitTask task)
        {
            return _func(context, chain, fromTaskIndex, task);
        }
    }
}

