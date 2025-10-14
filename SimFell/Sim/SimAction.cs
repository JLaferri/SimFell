using SimFell.Base;
using SimFell.Engine.Base;

namespace SimFell.Sim;

public class SimAction
{
    public Spell Spell { get; set; }
    public string Raw { get; set; }
    public Func<Unit, bool> ConditionCheck { get; set; }

    public bool CanExecute(Unit unit)
    {
        // A: Check if spell can be cast (defaulting to true for now)
        if (!Spell.IsReady(unit))
            return false;

        // B: Check the condition
        return ConditionCheck(unit);
    }
}