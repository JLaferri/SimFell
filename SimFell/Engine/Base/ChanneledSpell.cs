using SimFell.Base;
using SimSharp;

namespace SimFell.Engine.Base;

public class ChanneledSpell : Spell
{
    public ChanneledSpell(string id, string name, double channelDuration, double tickInterval,
        double cooldown)
        : base(id, name, cooldown, 0)
    {
        ChannelDuration = new Stat(channelDuration);
        TickInterval = new Stat(tickInterval);
        _hastedTickRate = true;
        _hastedChannelDuration = false;
    }

    public Stat ChannelDuration { get; } // total seconds
    public Stat TickInterval { get; } // seconds per tick
    public Action<Unit, Spell, Unit>? OnTick;
    public Action<Unit, Spell, Unit, double>? OnPartialTick;
    private bool _hastedTickRate = true;
    private bool _hastedChannelDuration;

    public ChanneledSpell WithOnTick(Action<Unit, Spell, Unit> onTick)
    {
        OnTick = onTick;
        return this;
    }

    public ChanneledSpell WithOnPartialTick(Action<Unit, Spell, Unit, double> onPartialTick)
    {
        OnPartialTick = onPartialTick;
        return this;
    }


    protected override IEnumerable<Event> CastProcess(Unit caster, Unit target)
    {
        // Channeled Spells Cooldowns start at the start of the channel.
        Charges--;
        ScheduleCooldown(caster);

        // Channeled spells always have a tick at the very start.
        OnTick?.Invoke(caster, this, target);

        // Channeled Spells are snapshotted.
        double channelDuration = ChannelDuration.GetValue();
        channelDuration = _hastedChannelDuration ? caster.GetHastedValue(channelDuration) : channelDuration;
        DateTime channelEnd = caster.Simulator.Now + TimeSpan.FromSeconds(channelDuration);

        double tickDuration = TickInterval.GetValue();
        tickDuration = _hastedTickRate ? caster.GetHastedValue(tickDuration) : tickDuration;

        DateTime scheduledTick = caster.Simulator.Now + TimeSpan.FromSeconds(tickDuration);
        DateTime lastTick = caster.Simulator.Now;

        while (caster.Simulator.Now < channelEnd)
        {
            // Wait until either the next tick or channel end, whichever comes first
            TimeSpan waitTime = scheduledTick <= channelEnd
                ? scheduledTick - caster.Simulator.Now
                : channelEnd - caster.Simulator.Now;

            yield return caster.Simulator.Env.Timeout(waitTime);

            if (_castProcess.HandleFault())
            {
                break;
            }

            if (scheduledTick <= channelEnd)
            {
                OnTick?.Invoke(caster, this, target);
                lastTick = caster.Simulator.Now;
                scheduledTick = caster.Simulator.Now + TimeSpan.FromSeconds(tickDuration);
            }
        }

        double expectedElapse = (scheduledTick - lastTick).TotalSeconds;
        double actualElapse = (caster.Simulator.Now - lastTick).TotalSeconds;
        double partialTickFraction = actualElapse / expectedElapse;

        if (partialTickFraction > 0.01)
        {
            OnPartialTick?.Invoke(caster, this, target, partialTickFraction);
        }

        CastComplete(caster, target, false);
    }
}