using System.Collections.Concurrent;
using SimFell.Base;
using SimFell.Engine.Base;
using SimFell.Engine.Base.Interfaces;
using SimFell.Sim.SimFileParser;
using SimSharp;

namespace SimFell.Sim;

public class Simulator
{
    public Simulation Env { get; set; }
    public DateTime Now => Env.Now;

    private double _duration;
    private readonly TimeSpan _tick = TimeSpan.FromMilliseconds(100);
    private readonly Unit _caster;
    private readonly List<Unit> _targets = new();

    private ConcurrentDictionary<string, SpellStats> _spellStats = new ConcurrentDictionary<string, SpellStats>();
    private ConcurrentDictionary<string, float> _resourcesGenerated = new ConcurrentDictionary<string, float>();

    public Simulator(Unit caster, List<Unit> targets)
    {
        _caster = caster;
        _caster.Simulator = this;
        _caster.OnCastDone += OnCastDone;
        Env = new Simulation();
    }

    private void ConfigureRoute(SimFellConfig.DungeonRoute route)
    {
        //Unsub from old Stuff.
        foreach (var enemy in _targets)
        {
            enemy.OnDamageReceived -= OnDamageReceived;
        }

        if (route.enemies > 0) _duration += route.duration;
        _targets.Clear();

        for (int i = 0; i < route.enemies; i++)
        {
            Unit target = new Unit("Goblin #" + (i + 1), true);
            target.Simulator = this;
            target.OnDamageReceived += OnDamageReceived;
            _targets.Add(target);
        }

        _caster.Targets = _targets;
        _caster.SpiritCharge = route.ultimate ? 100 : 0;
        if (_targets.Count > 0)
            _caster.PrimaryTarget = _targets[0];
    }

    public void Run(List<SimFellConfig.DungeonRoute> dungeonRoutes)
    {
        // Compute simulation end as absolute DateTime
        Env.Process(_caster.DoAction());
        var endTime = Env.Now;
        double currentDuration = 0;
        foreach (var dungeonRoute in dungeonRoutes)
        {
            ConfigureRoute(dungeonRoute);
            endTime = Env.Now + TimeSpan.FromSeconds(dungeonRoute.duration);
            Env.Run(endTime);
            currentDuration += dungeonRoute.duration;
            if (_caster.CastProcess != null && _caster.CastProcess.IsAlive)
            {
                _caster.CastProcess.Interrupt();
                _caster.FinishCasting();
            }
        }
    }

    // Event Handlers for Reporting.
    private void OnDamageReceived(Unit unit, double damageReceived, IDamageSource? spellSource, bool isCritical)
    {
        string spellName = spellSource?.Name ?? "Unknown";
        _spellStats.AddOrUpdate(
            spellName,
            _ => new SpellStats
            {
                SpellName = spellName,
                TotalDamage = damageReceived,
                Ticks = 1,
                LargestHit = damageReceived,
                SmallestHit = damageReceived,
                CritCount = isCritical ? 1 : 0,
            },
            (key, existingStats) =>
            {
                existingStats.TotalDamage += damageReceived;
                existingStats.Ticks++;
                if (damageReceived > existingStats.LargestHit)
                {
                    existingStats.LargestHit = damageReceived;
                }

                if (damageReceived < existingStats.SmallestHit || existingStats.SmallestHit == 0)
                {
                    existingStats.SmallestHit = damageReceived;
                }

                if (isCritical)
                {
                    existingStats.CritCount++;
                }

                return existingStats;
            });
    }

    private void OnCastDone(Unit caster, Spell spellSource, Unit target)
    {
        string spellName = spellSource?.Name ?? "Unknown";
        _spellStats.AddOrUpdate(
            spellName,
            _ => new SpellStats
            {
                SpellName = spellName,
                TotalDamage = 0,
                Ticks = 0,
                LargestHit = 0,
                SmallestHit = 0,
                CritCount = 0,
                Casts = 1
            },
            (key, existingStats) =>
            {
                existingStats.Casts++;
                return existingStats;
            });
    }

    public double GetDPS()
    {
        double totalDamage = _spellStats.Values.Sum(s => s.TotalDamage);
        return totalDamage / _duration;
    }

    public ConcurrentDictionary<string, SpellStats> GetSpellStats()
    {
        return _spellStats;
    }
}