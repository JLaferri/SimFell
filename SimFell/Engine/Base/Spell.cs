using SimFell.Base;
using SimFell.Engine.Base.Interfaces;
using SimFell.Logging;
using SimSharp;

namespace SimFell.Engine.Base;

public class Spell : IDamageSource
{
    public Spell(string id, string name, double cooldown, double castTime)
    {
        ID = id.Replace("-", "_");
        Name = name;
        Cooldown = new Stat(cooldown);
        CastTime = new Stat(castTime);
        TravelTime = new Stat(0);
        OffCooldown = DateTime.MinValue;
        Charges = 1;
        MaxCharges = 1;
        TriggersGCD = true;
        _hastedCastTime = true;
        _hastedCooldown = false;
    }

    // Configuration
    public string ID { get; set; }
    public string Name { get; set; }
    public Stat CastTime { get; } // in seconds
    public Stat Cooldown { get; } // in seconds
    public Stat TravelTime { get; set; } // in seconds
    public bool CanCastWhileCasting { get; set; }
    public bool CanCastWhileGCD { get; set; }
    public bool TriggersGCD { get; set; }
    public int Charges { get; protected set; }
    public int MaxCharges { get; set; }

    // Stat Mods.
    public Stat DamageModifiers { get; set; } = new Stat(0);
    public Stat CritModifiers { get; set; } = new Stat(0);
    public Stat ResourceCostModifiers { get; set; } = new Stat(0);

    // Additional
    public DateTime OffCooldown = DateTime.MinValue; // DateTime is correct for Simulation.Now
    public Action<Unit, Spell, Unit>? OnCast;

    // Privates
    private bool _cooldownProcessStarted = false;
    private bool _hastedCooldown = false;
    private bool _hastedCastTime = true;
    private Action<Unit, Spell, Unit> _spellEvent;
    private Action<Unit, Spell> _castingCostEvent;
    private Func<Unit, Spell, bool> _canCastFunc;

    protected Process _castProcess;
    protected Process _cooldownProcess;
    private DateTime _cooldownProcessStartTime;

    public bool IsReady(Unit caster)
    {
        return Charges > 0 && (_canCastFunc?.Invoke(caster, this) ?? true);
    }

    public virtual void Cast(Unit caster, Unit target)
    {
        // Trigger GCD
        if (TriggersGCD) caster.TriggerGCD();
        ConsoleLogger.Log(
            SimulationLogLevel.CastEvents,
            $"Casting [bold blue]{Name}[/]"
        );
        if (CastTime.GetValue() == 0)
        {
            Charges--;
            ScheduleCooldown(caster);
        }

        _castProcess = caster.Simulator.Env.Process(CastProcess(caster, target));
        caster.StartCasting(_castProcess);
    }

    protected virtual IEnumerable<Event> CastProcess(Unit caster, Unit target)
    {
        // Cast times are snapshotted.
        double castTime = CastTime.GetValue();
        castTime = _hastedCastTime ? caster.GetHastedValue(castTime) : castTime;
        if (castTime > 0)
            yield return caster.Simulator.Env.Timeout(TimeSpan.FromSeconds(castTime));
        CastComplete(caster, target, true);

        if (TravelTime.GetValue() > 0)
            yield return caster.Simulator.Env.Timeout(TimeSpan.FromSeconds(TravelTime.GetValue()));
        TriggerSpellEvent(caster, target);
    }

    protected void CastComplete(Unit caster, Unit target, bool scheduleCoolDown)
    {
        // Spell cooldown
        if (scheduleCoolDown)
        {
            Charges--;
            ScheduleCooldown(caster);
        }

        // Trigger On Casting Done Events.
        OnCast?.Invoke(caster, this, target);
        _castingCostEvent?.Invoke(caster, this);
        caster.OnCastDone?.Invoke(caster, this, target);
        caster.FinishCasting();

        ConsoleLogger.Log(
            SimulationLogLevel.CastEvents,
            $"Finished Casting [bold blue]{Name}[/]");
    }

    public void TriggerSpellEvent(Unit caster, Unit target)
    {
        _spellEvent?.Invoke(caster, this, target);
    }

    protected void ScheduleCooldown(Unit caster)
    {
        if (_cooldownProcessStarted) return;
        _cooldownProcessStartTime = caster.Simulator.Now;

        UpdateOffCooldown(caster);
        _cooldownProcess = caster.Simulator.Env.Process(CooldownProcess(caster));
    }

    private void UpdateOffCooldown(Unit caster)
    {
        double cooldownDuration = Cooldown.GetValue();
        cooldownDuration = _hastedCooldown ? caster.GetHastedValue(cooldownDuration) : cooldownDuration;
        OffCooldown = _cooldownProcessStartTime + TimeSpan.FromSeconds(cooldownDuration);
    }

    private IEnumerable<Event> CooldownProcess(Unit caster)
    {
        _cooldownProcessStarted = true;

        void OnHasteChanged()
        {
            if (_cooldownProcess != null && _cooldownProcess.IsAlive) _cooldownProcess.Interrupt();
        }

        if (_hastedCooldown)
        {
            caster.HasteStat.OnModifierAdded += OnHasteChanged;
            caster.HasteStat.OnModifierRemoved += OnHasteChanged;
        }

        while (caster.Simulator.Env.Now < OffCooldown)
        {
            UpdateOffCooldown(caster);
            TimeSpan remaining = OffCooldown - caster.Simulator.Env.Now;
            if (remaining > TimeSpan.Zero) yield return caster.Simulator.Env.Timeout(remaining);

            if (_cooldownProcess.HandleFault()) continue; // Called whenever a CDR event happens.
        }

        _cooldownProcessStarted = false;
        if (Charges < MaxCharges) Charges++;
        if (Charges < MaxCharges) ScheduleCooldown(caster);
    }

    public void UpdateCooldown(Unit caster, double deltaTime)
    {
        if (OffCooldown > caster.Simulator.Env.Now)
        {
            _cooldownProcessStartTime -= TimeSpan.FromSeconds(deltaTime);
            if (_cooldownProcess != null) _cooldownProcess.Interrupt();
        }
    }

    public Spell WithSpellEvent(Action<Unit, Spell, Unit> spellEvent)
    {
        _spellEvent = spellEvent;
        return this;
    }

    public Spell WithCanCastWhileCasting()
    {
        CanCastWhileCasting = true;
        return this;
    }

    public Spell WithCanCastWhileGCD()
    {
        CanCastWhileGCD = true;
        return this;
    }

    public Spell WithoutGCD()
    {
        TriggersGCD = false;
        return this;
    }

    public Spell WithCharges(int charges)
    {
        Charges = charges;
        MaxCharges = charges;
        return this;
    }

    public Spell WithoutHastedCastTime()
    {
        _hastedCastTime = false;
        return this;
    }

    public Spell WithHastedCooldown()
    {
        _hastedCooldown = true;
        return this;
    }

    public Spell WithTravelTime(double travelTime)
    {
        TravelTime = new Stat(travelTime);
        return this;
    }

    public Spell WithCanCast(Func<Unit, Spell, bool> canCast)
    {
        _canCastFunc = canCast;
        return this;
    }

    public Spell WithCastingCost(Action<Unit, Spell> costingCost)
    {
        _castingCostEvent = costingCost;
        return this;
    }
}