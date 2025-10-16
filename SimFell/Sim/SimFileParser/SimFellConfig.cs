using SimFell.Base;
using SimFell.Engine.Heroes;
using SimFell.Logging;
using SimFell.SimmyRewrite;

namespace SimFell.Sim.SimFileParser;

public class SimFellConfig
{
    public string ConfigName { get; set; }
    public string Hero { get; set; }

    // Stats
    public enum StatMode
    {
        Percent,
        Points,
        Gear
    }

    public StatMode SimStatMode { get; set; }
    public int Primary { get; set; }
    public int Crit { get; set; }
    public int Expertise { get; set; }
    public int Haste { get; set; }
    public int Spirit { get; set; }

    // Talents (raw string, or you could parse into an array later)
    public string Talents { get; set; }

    // Gear
    //TODO: Gear and how we handle/load it.

    // Simulation Info
    public int Duration { get; set; }
    public int RunCount { get; set; }
    public int Enemies { get; set; }

    public string RouteName { get; set; }
    public List<DungeonRoute> Route { get; set; } = new();

    // Example Dungeon Route.
    public struct DungeonRoute
    {
        public int enemies;
        public double duration;
        public bool ultimate;
    }

    public enum SimulationType
    {
        Average,
        Debug
    }

    public SimulationType SimType { get; set; }

    public enum SimulationMode
    {
        Time,
        Health
    }

    public SimulationMode SimMode { get; set; }

    // Actions
    public List<string> Actions { get; set; } = new List<string>();

    public static SimFellConfig LoadFromFile(string path)
    {
        var config = new SimFellConfig();

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue; // skip malformed lines

            var key = parts[0].Trim().ToLower();
            var value = parts[1].Trim();

            // Skip empty lines or comments
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                continue;

            //Configure Sim
            if (key == "name") config.ConfigName = value.Trim('"');
            if (key == "simulation_type")
                config.SimType = (SimulationType)Enum.Parse(typeof(SimulationType), value, true);
            if (key == "simulation_mode")
                config.SimMode = (SimulationMode)Enum.Parse(typeof(SimulationMode), value, true);
            if (key == "duration") config.Duration = int.Parse(parts[1]);
            if (key == "run_count") config.RunCount = int.Parse(parts[1]);
            if (key == "enemies") config.Enemies = int.Parse(parts[1]);

            //Configure Hero
            if (key == "hero") config.Hero = value;
            if (key == "stat_mode")
                config.SimStatMode = (StatMode)Enum.Parse(typeof(StatMode), value, true);
            if (key == "primary") config.Primary = int.Parse(parts[1]);
            if (key == "crit") config.Crit = int.Parse(parts[1]);
            if (key == "expertise") config.Expertise = int.Parse(parts[1]);
            if (key == "haste") config.Haste = int.Parse(parts[1]);
            if (key == "spirit") config.Spirit = int.Parse(parts[1]);
            if (key == "talents") config.Talents = value;
            if (key.StartsWith("action")) config.Actions.Add(line); //Add the entire Line. APLParser handles it.
            if (key.StartsWith("dungeon_route")) config.Route = ConfigureDungeonRoute(value);
        }

        if (config.Route.Count > 0)
        {
            config.Duration = 0;
            foreach (var route in config.Route)
            {
                if (route.enemies > 0) config.Duration += (int)route.duration;
            }
        }
        else
        {
            DungeonRoute newRoute = new();
            newRoute.enemies = config.Enemies;
            newRoute.duration = config.Duration;
            newRoute.ultimate = true;
            config.Route.Add(newRoute);
        }

        return config;
    }

    private static List<DungeonRoute> ConfigureDungeonRoute(string line)
    {
        List<DungeonRoute> route = new List<DungeonRoute>();
        string[] parts = line.Split(':');
        foreach (var part in parts)
        {
            string[] pull = part.Split(',');
            int enemyCount = 0;
            double duration = 0;
            bool ultimate = false;
            if (pull.Length >= 1)
            {
                enemyCount = int.Parse(pull[0]);
            }

            if (pull.Length >= 2)
            {
                duration = double.Parse(pull[1]);
            }

            if (pull.Length >= 3)
            {
                ultimate = true;
            }

            route.Add(new DungeonRoute() { duration = duration, ultimate = ultimate, enemies = enemyCount });
        }

        return route;
    }

    private static Unit LoadHero(string heroName)
    {
        var hero = heroName.ToLower();

        // Add Heroes here so they are able to be picked up in the config.
        if (hero == "rime") return new Rime();
        // if (hero == "tariq") return new Tariq();
        // if (hero == "ardeos") return new Ardeos();

        //Benchmark
        if (hero == "benchmark") return new Benchmark();

        // Returns if no other Hero is properly configured.
        ConsoleLogger.Log(SimulationLogLevel.Error, "Invalid Hero in SimFellConfig: " + heroName);
        return null;
    }

    private void ConfigureTalents(Unit unit)
    {
        if (string.IsNullOrWhiteSpace(Talents))
            return;

        var rows = Talents.Split('-');

        // Row index starts at 1
        for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            string row = rows[rowIndex];
            if (string.IsNullOrEmpty(row) || row == "0")
                continue;

            foreach (char colChar in row)
            {
                if (char.IsDigit(colChar) && colChar != '0')
                {
                    int col = colChar - '0';
                    unit.ActivateTalent(rowIndex + 1, col);
                }
            }
        }
    }

    public Unit GetHero()
    {
        Unit hero = LoadHero(Hero);
        ConfigureTalents(hero);
        if (SimStatMode != StatMode.Gear)
        {
            hero.SetPrimaryStats(Primary, Crit, Expertise, Haste, Spirit, SimStatMode == StatMode.Percent);
        }
        else
        {
            ConsoleLogger.Log(SimulationLogLevel.Error, "Gear Mode is not supported.");
        }

        hero.SimActions = APLParser.ParseAPL(Actions, hero);
        return hero;
    }
}