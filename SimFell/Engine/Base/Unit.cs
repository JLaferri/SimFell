using SimFell.Engine.Base;
using SimFell.Engine.Base.Interfaces;
using SimFell.Logging;
using SimFell.Sim;
using SimSharp;

namespace SimFell.Base;

public class Unit
{
    public Unit(string name, bool hasInfiniteHP = false)
    {
        Name = name;
        Stamina = new Stat(999999);
        Health = new HealthStat(Stamina.GetValue());
        SpiritCharge = 100;
        _hasInfiniteHP = hasInfiniteHP;

        //Add base 5% Crit.
        CritcalStrikeStat.AddModifier(new Modifier(Modifier.StatModType.AdditivePercent, 5));

        //Baseline GCD
        GlobalCooldown = TimeSpan.FromSeconds(1.5);
        _isOnGCD = false;
    }

    // Configuration
    public string Name { get; }
    public Simulator Simulator { get; set; }
    public HealthStat Health { get; set; }
    public double SpiritCharge { get; set; }
    private readonly bool _hasInfiniteHP;
    public Unit PrimaryTarget { get; set; }
    public List<Unit> Targets { get; set; }

    public List<SimAction> SimActions { get; set; } = new();
    public List<Spell> SpellBook { get; set; } = new();
    public List<Talent> Talents { get; set; } = new();

    // GCD
    public static TimeSpan GlobalCooldown;

    // Base Stats
    public Stat MainStat = new(1000);
    public Stat Stamina = new(0);
    public Stat CritcalStrikeStat = new(0, true);
    public Stat ExpertiseStat = new(0, true);
    public Stat HasteStat = new(0, true);
    public Stat SpiritStat = new(0, true);

    // Other Stats
    public Stat CriticalStrikePowerStat = new Stat(0);

    // Other Modifiers
    public Stat DamageBuffs { get; set; } = new Stat(0);

    // Casting
    public bool IsCasting { get; private set; }

    // Auras
    public List<Aura> Buffs { get; set; } = [];
    public List<Aura> Debuffs { get; set; } = [];

    // Spirit of Heroism
    static Modifier _spiritOfHeroismModifier = new Modifier(Modifier.StatModType.AdditivePercent, 30);

    protected Aura SpiritOfHeroism = new Aura("spirit-of-heroism", "Spirit of Heroism", 20, 0)
        .WithOnApply((_, target, _) => target.HasteStat.AddModifier(_spiritOfHeroismModifier))
        .WithOnRemove((_, target, _) => target.HasteStat.RemoveModifier(_spiritOfHeroismModifier));

    //Events
    public Action<Unit, Spell, Unit>? OnCastDone { get; set; }
    public Action<Unit, Unit, double, IDamageSource, bool>? OnDamageDealt { get; set; }
    public Action<Unit, double, IDamageSource, bool>? OnDamageReceived { get; set; }
    public Action<Unit, double, IDamageSource>? OnCrit { get; set; }

    public Process CastProcess { get; set; }

    // Privates
    private bool _isOnGCD;

    public IEnumerable<Event> DoAction()
    {
        if (PrimaryTarget != null)
        {
            foreach (var action in SimActions)
            {
                // If not casting, and not on GCD
                if (!IsCasting && !IsOnGCD())
                {
                    if (action.CanExecute(this))
                    {
                        action.Spell.Cast(this, PrimaryTarget);
                        yield break;
                    }
                }
            }
        }

        yield return Simulator.Env.Timeout(TimeSpan.FromSeconds(1));
        if (!IsCasting && !IsOnGCD()) Simulator.Env.Process(DoAction(), 999);
    }

    public virtual void SetPrimaryStats(int mainStat, int criticalStrikeStat, int expertiseStat, int hasteStat,
        int spiritStat, bool isPercentile = false)
    {
        MainStat.BaseValue = mainStat;
        if (!isPercentile)
        {
            CritcalStrikeStat.BaseValue = criticalStrikeStat;
            ExpertiseStat.BaseValue = expertiseStat;
            HasteStat.BaseValue = hasteStat;
            SpiritStat.BaseValue = spiritStat;
        }
        else
        {
            CritcalStrikeStat.AddModifier(new Modifier(Modifier.StatModType.AdditivePercent, criticalStrikeStat - 5));
            ExpertiseStat.AddModifier(new Modifier(Modifier.StatModType.AdditivePercent, expertiseStat));
            HasteStat.AddModifier(new Modifier(Modifier.StatModType.AdditivePercent, hasteStat));
            SpiritStat.AddModifier(new Modifier(Modifier.StatModType.AdditivePercent, spiritStat));
        }
    }

    public void ActivateTalent(string gridPos)
    {
        var talent = Talents.FirstOrDefault(talent => talent.GridPos == gridPos);
        if (talent != null)
        {
            talent.Activate(this);
            ConsoleLogger.Log(SimulationLogLevel.Setup, $"Activated talent '{talent.Name}'");
        }
    }

    public void ApplyBuff(Unit caster, Aura aura)
    {
        void RemoveHandler(Unit removeCaster, Unit removeTarget, Aura removeAura)
        {
            removeAura.OnRemove -= RemoveHandler;
            Buffs.Remove(removeAura);
            ConsoleLogger.Log(
                SimulationLogLevel.BuffEvents,
                $"[bold blue]{Name}[/] loses buff: [bold yellow]{removeAura.Name}[/]"
            );
        }

        aura.OnRemove += RemoveHandler;
        Buffs.Add(aura);
        aura.Apply(caster, this);

        ConsoleLogger.Log(
            SimulationLogLevel.BuffEvents,
            $"[bold blue]{Name}[/] gains buff: [bold yellow]{aura.Name}[/]"
        );
    }

    public bool HasBuff(Aura aura)
    {
        return Buffs.Contains(aura);
    }

    public Aura GetBuff(Aura aura)
    {
        return GetBuff(aura.ID);
    }

    public Aura GetBuff(string id)
    {
        id = id.Replace("-", "_");
        return Buffs.FirstOrDefault(x => x.ID == id);
    }

    public void RemoveBuff(Unit caster, Aura aura)
    {
        aura.Remove(caster, caster, 0);
    }

    public void ApplyDebuff(Unit caster, Aura aura)
    {
        void RemoveHandler(Unit removeCaster, Unit removeTarget, Aura removeAura)
        {
            removeAura.OnRemove -= RemoveHandler;
            Debuffs.Remove(removeAura);
            ConsoleLogger.Log(
                SimulationLogLevel.BuffEvents,
                $"[bold blue]{Name}[/] loses debuff: [bold yellow]{removeAura.Name}[/]"
            );
        }

        aura.OnRemove += RemoveHandler;

        Debuffs.Add(aura);
        aura.Apply(caster, this);

        ConsoleLogger.Log(
            SimulationLogLevel.BuffEvents,
            $"[bold blue]{Name}[/] gains debuff: [bold yellow]{aura.Name}[/]"
        );
    }

    public Aura GetDebuff(Aura aura)
    {
        return GetDebuff(aura.ID);
    }

    public Aura GetDebuff(string id)
    {
        id = id.Replace("-", "_");
        return Debuffs.FirstOrDefault(x => x.ID == id);
    }

    public void StartCasting(Process castProcess)
    {
        CastProcess = castProcess;
        IsCasting = true;
    }

    public void FinishCasting()
    {
        IsCasting = false;
        if (!_isOnGCD) Simulator.Env.Process(DoAction(), 999);
    }

    public bool IsOnGCD()
    {
        return _isOnGCD;
    }

    public void TriggerGCD()
    {
        _isOnGCD = true;
        Simulator.Env.Process(GCDProcess(GlobalCooldown.TotalSeconds));
    }

    private IEnumerable<Event> GCDProcess(double duration)
    {
        duration = GetHastedValue(duration);
        yield return Simulator.Env.Timeout(TimeSpan.FromSeconds(duration));
        _isOnGCD = false;
        if (!IsCasting) Simulator.Env.Process(DoAction(), 999);
    }

    public double TakeDamage(double amount, bool isCritical, IDamageSource? spellSource = null)
    {
        var totalDamage = (int)amount;

        // Log damage event with coloring for critical hits
        if (ConsoleLogger.Enabled)
        {
            var sourceName = spellSource != null
                ? spellSource.Name
                : "Unknown";
            var message = $"[bold blue]{sourceName}[/]"
                          + $" hits [bold yellow]{Name}[/]"
                          + $" for [bold magenta]{totalDamage}[/] "
                          + $"{(isCritical ? " (Critical Strike)" : "")}";
            ConsoleLogger.Log(SimulationLogLevel.DamageEvents, message, isCritical ? "💥" : null);
        }

        OnDamageReceived?.Invoke(this, totalDamage, spellSource, isCritical);

        if (!_hasInfiniteHP) Health.BaseValue -= totalDamage;
        if (Health.GetValue() < 0) Health.BaseValue = 0;

        return totalDamage;
    }

    public double DealDamage(Unit target, double damagePercent, double damageSpread, IDamageSource spellSource,
        bool includeCriticalStrike = true, bool includeExpertise = true, bool isFlatDamage = false)
    {
        // TODO: Handle Damage Spread. 
        var (damage, isCritical) =
            GetDamage(target, damagePercent, spellSource, includeCriticalStrike, includeExpertise, isFlatDamage);

        var totalDamageTaken = target.TakeDamage(damage, isCritical, spellSource);
        if (isCritical) OnCrit?.Invoke(this, totalDamageTaken, spellSource); //On Crit events called.
        OnDamageDealt?.Invoke(this, target, totalDamageTaken, spellSource, isCritical); //Called when damage is dealt.

        return damage;
    }

    public void DealAOEDamage(double damagePercent, double damageSpread, double softCap, double targetCap,
        IDamageSource spellSource,
        bool includePrimaryTarget = true, bool includeCriticalStrike = true, bool includeExpertise = true,
        bool isFlatDamage = false)
    {
        //Gets the list of targets, skips the first one if includePrimaryTarget is False.
        var affectedTargets = includePrimaryTarget
            ? Targets
            : Targets.Where(t => t != PrimaryTarget).ToList();
        int targetCount = affectedTargets.Count;

        // Calculate damage per target
        double damagePerTarget =
            damagePercent * (targetCount > softCap ? Math.Sqrt(softCap / targetCount) : 1.0);

        // Deal damage to each affected target
        foreach (var target in affectedTargets.Take(targetCount))
        {
            DealDamage(target, damagePerTarget, damageSpread, spellSource, includeCriticalStrike, includeExpertise,
                isFlatDamage);
        }
    }

    public (double damage, bool isCritical) GetDamage(Unit target, double damagePercent,
        IDamageSource? spellSource = null,
        bool includeCriticalStrike = true, bool includeExpertise = true, bool isFlatDamage = false)
    {
        var critPercent = CritcalStrikeStat.GetValue();
        critPercent = includeCriticalStrike ? critPercent : 0;

        if (spellSource != null && spellSource.GetType() == typeof(Spell))
        {
            damagePercent = ((Spell)spellSource).DamageModifiers.GetValue(damagePercent);
            critPercent = ((Spell)spellSource).CritModifiers.GetValue(critPercent);
        }

        Modifier grievousCritsModifier = new Modifier(Modifier.StatModType.AdditivePercent, 0);
        if (critPercent > 100.0)
        {
            grievousCritsModifier.Value = critPercent - 100.0;
            CriticalStrikePowerStat.AddModifier(grievousCritsModifier);
        }

        //Converts the DamagePercent into a Damage Value.
        var damage = damagePercent * MainStat.GetValue(); // Adds the Damage as Main Stat.
        if (isFlatDamage) damage = damagePercent;
        if (includeExpertise)
            damage *= 1 + (ExpertiseStat.GetValue() / 100f); // Modifies the damage based on expertise.
        damage = DamageBuffs.GetValue(damage);

        var isCritical = SimRandom.Roll(critPercent);
        isCritical = SimRandom.CanCrit ? isCritical : false;
        //Handle general On Crit.
        damage *= isCritical ? 2 : 1; //Doubles the damage if there is a Critical Hit.
        //Handle Crit Power.
        if (isCritical) damage = CriticalStrikePowerStat.GetValue(damage);
        CriticalStrikePowerStat.RemoveModifier(grievousCritsModifier);

        //Any additional mods on the target.
        // TODO: This.
        // damage = target.GetDamageTakenWithDebuffs(damage);

        return (damage, isCritical);
    }

    public double GetHastedValue(double value)
    {
        return value / (1 + (HasteStat.GetValue() / 100));
    }
}