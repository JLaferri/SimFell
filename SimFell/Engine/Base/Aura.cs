using System.Diagnostics;
using SimFell.Base;
using SimSharp;
using Process = SimSharp.Process;

namespace SimFell.Engine.Base;

public class Aura
{
    public Aura(string id, string name, double duration, double tickInterval, int maxStatcks = 1)
    {
        ID = id.Replace("-", "_");
        Name = name;
        Duration = duration;
        MaxStacks = maxStatcks;
        CurrentStacks = maxStatcks;
        TickInterval = new Stat(tickInterval);
        _hastedTickRate = true;
    }

    // Configuration.
    public string ID { get; set; }
    public string Name { get; set; }
    public double Duration { get; set; }
    public DateTime AuraExpires { get; set; }
    public Stat TickInterval { get; set; }
    public int MaxStacks { get; set; }
    public int CurrentStacks { get; set; }

    // Events
    public Action<Unit, Unit, Aura>? OnTick;
    public Action<Unit, Unit, Aura, double>? OnPartialTick;
    public Action<Unit, Unit, Aura>? OnApply;
    public Action<Unit, Unit, Aura>? OnRemove;
    public Action<Unit, Unit, Aura>? OnIncreaseStack;
    public Action<Unit, Unit, Aura>? OnDecreaseStack;

    // Tick Rate Helpers
    private bool _hastedTickRate;
    private Process _tickProcess;
    private Process _removeProcess;
    private DateTime _lastTick;
    private DateTime _scheduledTick;

    public Aura WithOnApply(Action<Unit, Unit, Aura> onApply)
    {
        OnApply = onApply;
        return this;
    }

    public Aura WithOnRemove(Action<Unit, Unit, Aura> onRemove)
    {
        OnRemove = onRemove;
        return this;
    }

    public Aura WithOnTick(Action<Unit, Unit, Aura> onTick)
    {
        OnTick = onTick;
        return this;
    }

    public Aura WithOnPartialTick(Action<Unit, Unit, Aura, double> onPartialTick)
    {
        OnPartialTick = onPartialTick;
        return this;
    }

    public Aura WithOnIncreaseStack(Action<Unit, Unit, Aura> onIncreaseStack)
    {
        OnIncreaseStack = onIncreaseStack;
        return this;
    }

    public Aura WithOnDecreaseStack(Action<Unit, Unit, Aura> onDecreaseStack)
    {
        OnDecreaseStack = onDecreaseStack;
        return this;
    }

    public Aura WithoutHastedTickRate()
    {
        _hastedTickRate = false;
        return this;
    }

    public void Apply(Unit caster, Unit target)
    {
        OnApply?.Invoke(caster, target, this);
        AuraExpires = caster.Simulator.Now + TimeSpan.FromSeconds(Duration);
        if (TickInterval.GetValue() > 0)
            _tickProcess = caster.Simulator.Env.Process(TickProcess(caster.Simulator.Env, caster, target));
        _removeProcess = caster.Simulator.Env.Process(RemoveProcess(caster.Simulator.Env, caster, target));
    }

    private IEnumerable<Event> TickProcess(Simulation env, Unit caster, Unit target)
    {
        void OnHasteChanged() => _tickProcess?.Interrupt();
        if (_hastedTickRate)
        {
            caster.HasteStat.OnModifierAdded += OnHasteChanged;
            caster.HasteStat.OnModifierRemoved += OnHasteChanged;
        }

        _lastTick = env.Now;
        _scheduledTick = _lastTick;

        while (_removeProcess.IsAlive)
        {
            double tickDuration = TickInterval.GetValue();
            tickDuration = _hastedTickRate ? caster.GetHastedValue(tickDuration) : tickDuration;
            _scheduledTick = _lastTick + TimeSpan.FromSeconds(tickDuration);

            yield return env.Timeout(_scheduledTick - env.Now);

            // If a "Fault" happened - Aka, anything that effects the Tick Rate.
            if (_tickProcess.HandleFault())
            {
                continue;
            }

            _lastTick = env.Now;
            OnTick?.Invoke(caster, target, this);
        }

        if (_hastedTickRate)
        {
            caster.HasteStat.OnModifierAdded -= OnHasteChanged;
            caster.HasteStat.OnModifierRemoved -= OnHasteChanged;
        }
    }

    private IEnumerable<Event> RemoveProcess(Simulation env, Unit caster, Unit target)
    {
        double timeLeft = (AuraExpires - env.Now).TotalSeconds;
        yield return env.Timeout(TimeSpan.FromSeconds(timeLeft), 1);
        _tickProcess?.Interrupt();

        // Handle Partial Tick.
        double partialTickFraction = 0;
        if (TickInterval.GetValue() > 0)
        {
            double expectedElapse = (_scheduledTick - _lastTick).TotalSeconds;
            double actualElapse = (env.Now - _lastTick).TotalSeconds;
            partialTickFraction = actualElapse / expectedElapse;
        }

        Remove(caster, target, partialTickFraction);
    }

    public void Remove(Unit caster, Unit target, double partialTickFraction)
    {
        if (partialTickFraction > 0.01)
        {
            OnPartialTick?.Invoke(caster, target, this, partialTickFraction);
        }

        OnRemove?.Invoke(caster, target, this);
    }
}