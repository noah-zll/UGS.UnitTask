using System;

namespace UGS.UnitTask
{
    public sealed class AlwaysTrueChainRunCondition : IUnitTaskChainRunCondition
    {
        public static readonly AlwaysTrueChainRunCondition Instance = new AlwaysTrueChainRunCondition();

        private AlwaysTrueChainRunCondition()
        {
        }

        public bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain)
        {
            return true;
        }
    }

    public sealed class FuncChainRunCondition : IUnitTaskChainRunCondition
    {
        private readonly Func<IUnitTaskContext, IUnitTaskChain, bool> _func;

        public FuncChainRunCondition(Func<IUnitTaskContext, IUnitTaskChain, bool> func)
        {
            _func = func ?? throw new ArgumentNullException(nameof(func));
        }

        public bool Evaluate(IUnitTaskContext context, IUnitTaskChain chain)
        {
            return _func(context, chain);
        }
    }
}

