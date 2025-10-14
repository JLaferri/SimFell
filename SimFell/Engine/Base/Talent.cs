using SimFell.Base;

namespace SimFell.Engine.Base;

public class Talent : CharacterEffect<Talent>
{
    public string ID { get; }
    public string Name { get; }
    public string GridPos { get; }

    public Talent(string id, string name, string gridPos)
    {
        ID = id.Replace("-", "_");
        Name = name;
        GridPos = gridPos;
    }

    [Obsolete]
    public Talent(string id, string name, string gridPos, Action<Unit>? onActivate = null,
        Action<Unit>? onDeactivate = null)
    {
        //ConsoleLogger.Log(SimulationLogLevel.Error, "Talent Deprecated. Use other constructor.");
        ID = id;
        Name = name;
        GridPos = gridPos;
        OnActivate = onActivate;
        OnDeactivate = onDeactivate;
    }
}