using SimFell.Base;
using SimFell.Engine.Base;
using SimFell.Logging;
using SimFell.Sim;

namespace SimFell.Engine.Heroes;

public class Rime : Unit
{
    private const int MaxAnima = 9;
    private const int MaxWinterOrbs = 5;

    //Custom Rime Stats.
    private readonly Stat _spiritResetChance = new(0);

    private Spell _burstingIce;
    private Spell _coldSnap;
    private Spell _flightOfTheNavir;
    private Spell _freezingTorrent;
    private Spell _frostBolt;
    private Spell _frostSwallows; // Frost Swallow Procs.
    private Spell _glacialBlast;
    private Spell _iceBlitz;
    private Spell _iceComet;
    private Spell _wintersBlessing;
    private Spell _wrathOfWinter;

    // Talent Helpers
    private Aura _burstingIceAura;

    public Rime() : base("Rime")
    {
        ConfigureSpellBook();
        ConfigureTalents();
    }

    private int Anima { get; set; }
    private int WinterOrbs { get; set; }

    //Custom Rime Events.
    private Action<int> OnWinterOrbUpdate { get; set; }
    private Action<int> OnAnimaUpdate { get; set; }

    public void UpdateAnima(int animaDelta)
    {
        Anima += animaDelta;
        if (Anima >= MaxAnima)
        {
            Anima -= MaxAnima;
            UpdateWinterOrbs(1);
        }

        OnAnimaUpdate?.Invoke(animaDelta);
    }

    public void UpdateWinterOrbs(int winterOrbsDelta)
    {
        WinterOrbs += winterOrbsDelta;
        OnWinterOrbUpdate?.Invoke(winterOrbsDelta);

        if (winterOrbsDelta > 0)
        {
            for (int i = 0; i < 3; i++)
            {
                _frostSwallows.TriggerSpellEvent(this, PrimaryTarget);
            }
        }

        // TODO: Spirit Refund.

        if (WinterOrbs > MaxWinterOrbs)
            ConsoleLogger.Log(SimulationLogLevel.DamageEvents, "[bold red]Over Capped Winter Orbs[/]");
        WinterOrbs = Math.Clamp(WinterOrbs, 0, MaxWinterOrbs);
    }

    private void ConfigureSpellBook()
    {
        //TODO: Figure out what the target cap, soft caps, and damage spreads actually are.

        _burstingIceAura = new Aura("bursting-ice", "Bursting Ice", 5, 0.5, 1)
            .WithOnTick((_, _, _) =>
            {
                DealAOEDamage(0.55, 0.1, 5, 19, _burstingIce);
                UpdateAnima(1);
            })
            .WithOnPartialTick((_, _, _, partialTickFraction) =>
            {
                DealAOEDamage(0.55 * partialTickFraction, 0.1, 5, 19, _burstingIce);
                UpdateAnima(1);
            });
        _burstingIce = new Spell("bursting-ice", "Bursting Ice", 10, 2)
            .WithSpellEvent((caster, spell, _) => { PrimaryTarget.ApplyDebuff(caster, _burstingIceAura); });

        _coldSnap = new Spell("cold-snap", "Cold Snap", 12, 0)
            .WithCharges(2)
            .WithHastedCooldown()
            .WithSpellEvent((_, spell, _) =>
            {
                DealDamage(PrimaryTarget, 3.04, 0.1, spell);
                UpdateWinterOrbs(1);
            });

        _frostBolt = new Spell("frost-bolt", "Frost Bolt", 0, 1.5)
            .WithSpellEvent((_, spell, _) =>
            {
                DealDamage(PrimaryTarget, 2.34, 0.1, spell);
                UpdateAnima(1);
            });

        Action<Unit, Unit, double, Spell, bool> flightOfTheNavirDamageEvent = (_, _, _, spellSource, _) =>
        {
            int swallowTriggers = spellSource == _coldSnap ? 5
                : spellSource == _freezingTorrent ? 1
                : 0;

            for (int i = 0; i < swallowTriggers; i++) _frostSwallows.TriggerSpellEvent(this, PrimaryTarget);
        };

        _flightOfTheNavir = new Spell("flight-of-the-navir", "Flight Of The Navir", 60, 0)
            .WithSpellEvent((caster, spell, _) =>
            {
                var aura = new Aura("flight-of-the-navir", "Flight Of The Navir", 20, 0)
                    .WithOnApply((_, _, _) => { OnDamageDealt += flightOfTheNavirDamageEvent; })
                    .WithOnRemove((_, _, _) => { OnDamageDealt -= flightOfTheNavirDamageEvent; });
                caster.ApplyBuff(caster, aura);
            });

        _freezingTorrent = new ChanneledSpell("freezing-torrent", "Freezing Torrent", 2, 0.4, 15)
            .WithOnTick(((_, spell, _) =>
            {
                DealDamage(PrimaryTarget, 1.42, 0.1, spell);
                UpdateAnima(1);
            }))
            .WithOnPartialTick(((_, spell, _, partialTickFraction) =>
            {
                DealDamage(PrimaryTarget, 1.42 * partialTickFraction, 0.1, spell);
                UpdateAnima(1);
            }));

        _frostSwallows = new Spell("frost-swallows", "Frost Swallows", 0, 0)
            .WithSpellEvent((_, spell, _) => { DealDamage(PrimaryTarget, 0.68, 0.1, spell); });

        _glacialBlast = new Spell("glacial-blast", "Glacial Blast", 0, 2)
            .WithCanCast((_, spell) => WinterOrbs >= spell.ResourceCostModifiers.GetValue(2))
            .WithCastingCost((_, spell) => UpdateWinterOrbs((int)spell.ResourceCostModifiers.GetValue(2)))
            .WithSpellEvent((_, spell, _) => { DealDamage(PrimaryTarget, 9.9, 0.1, spell); });

        _iceBlitz = new Spell("ice-blitz", "Ice Blitz", 120, 0)
            .WithCanCastWhileCasting()
            .WithCanCastWhileGCD()
            .WithoutGCD()
            .WithSpellEvent((caster, _, _) =>
            {
                Modifier iceBlitzDamageModifier = new Modifier(Modifier.StatModType.MultiplicativePercent, 20);
                caster.ApplyBuff(caster,
                    new Aura("ice-blitz", "Ice Blitz", 20, 0)
                        .WithOnApply((_, target, _) => target.DamageBuffs.AddModifier(iceBlitzDamageModifier))
                        .WithOnRemove((_, target, _) => target.DamageBuffs.RemoveModifier(iceBlitzDamageModifier))
                );
            });

        _iceComet = new Spell("ice-comet", "Ice Comet", 0, 0)
            .WithCanCast((_, _) => WinterOrbs >= 2)
            .WithCastingCost((_, _) => UpdateWinterOrbs(-2))
            .WithSpellEvent((_, spell, _) => { DealAOEDamage(4.51, 0.1, 5, 19, spell); });

        _wintersBlessing = new Spell("winters-blessing", "Winter's Blessing", 60, 0)
            .WithCanCastWhileGCD()
            .WithoutGCD()
            .WithSpellEvent((caster, _, _) =>
            {
                Modifier iceBlitzDamageModifier = new Modifier(Modifier.StatModType.MultiplicativePercent, 20);
                caster.ApplyBuff(caster,
                    new Aura("ice-blitz", "Ice Blitz", 20, 0)
                        .WithOnApply((_, target, _) => target.DamageBuffs.AddModifier(iceBlitzDamageModifier))
                        .WithOnRemove((_, target, _) => target.DamageBuffs.RemoveModifier(iceBlitzDamageModifier))
                );
            });

        _wrathOfWinter = new Spell("wrath-of-winter", "Wrath of Winter", 0, 1.5)
            .WithCanCast((_, _) => SpiritCharge >= 100)
            .WithCastingCost((_, _) => SpiritCharge = 0)
            .WithSpellEvent((caster, _, _) =>
            {
                UpdateWinterOrbs(1); // Triggers once on cast finished.

                // Apply Spirit of Heroism.
                caster.ApplyBuff(caster, SpiritOfHeroism);

                Modifier wrathOfWinterDamageModifier = new Modifier(Modifier.StatModType.MultiplicativePercent, 20);
                Modifier glacialBlastCastModifier = new Modifier(Modifier.StatModType.Multiplicative, 0);
                caster.ApplyBuff(caster,
                    new Aura("wrath-of-winter", "Wrath of Winter", 20, 4)
                        .WithOnTick((_, _, _) => UpdateWinterOrbs(1))
                        .WithOnApply((_, target, _) =>
                        {
                            target.DamageBuffs.AddModifier(wrathOfWinterDamageModifier);
                            _glacialBlast.CastTime.AddModifier(glacialBlastCastModifier);
                        })
                        .WithOnRemove((_, target, _) =>
                        {
                            target.DamageBuffs.RemoveModifier(wrathOfWinterDamageModifier);
                            _glacialBlast.CastTime.RemoveModifier(glacialBlastCastModifier);
                        })
                );
            });

        SpellBook.Add(_burstingIce);
        SpellBook.Add(_coldSnap);
        SpellBook.Add(_flightOfTheNavir);
        SpellBook.Add(_freezingTorrent);
        SpellBook.Add(_frostBolt);
        SpellBook.Add(_glacialBlast);
        SpellBook.Add(_iceBlitz);
        SpellBook.Add(_iceComet);
        SpellBook.Add(_wintersBlessing);
        SpellBook.Add(_wrathOfWinter);
    }

    private void ConfigureTalents()
    {
        var _chillingFinesse = new Talent("chilling-finesse", "Chilling Finesse", "1.1")
            .WithOnActivate((_) =>
            {
                // Freezing Torrent Ticks reduce Bursting Ice Cooldown.
                (_freezingTorrent as ChanneledSpell).OnTick += (_, _, _) => _burstingIce.UpdateCooldown(this, 0.3);
                // Cold Snaps reduce Freezing Torrent Cooldowns.
                _coldSnap.OnCast += (_, _, _) => _freezingTorrent.UpdateCooldown(this, 1.5);
            });

        Modifier wintersEmbraceDamageBuff = new Modifier(Modifier.StatModType.MultiplicativePercent, 20);
        Modifier wintersEmbraceNegateBursting = new Modifier(Modifier.StatModType.InverseMultiplicativePercent, 20);

        var wintersEmbraceBuff = new Aura("winters_embrace", "Winter's Embrace", 9999, 0)
            .WithOnApply((_, _, _) =>
            {
                _burstingIce.DamageModifiers.AddModifier(wintersEmbraceNegateBursting);
                DamageBuffs.AddModifier(wintersEmbraceDamageBuff);
            })
            .WithOnRemove((_, _, _) =>
            {
                _burstingIce.DamageModifiers.RemoveModifier(wintersEmbraceNegateBursting);
                DamageBuffs.RemoveModifier(wintersEmbraceDamageBuff);
            });

        var _wintersEmbrace = new Talent("winters-embrace", "Winter's Embrace", "1.2")
            .WithOnActivate((_) =>
            {
                _burstingIceAura.OnApply += (_, _, _) => { ApplyBuff(this, wintersEmbraceBuff); };
                _burstingIceAura.OnRemove += (_, _, _) => { RemoveBuff(this, wintersEmbraceBuff); };
            });

        var _glacialAssault = new Talent(
                id: "glacial-assault",
                name: "Glacial Assault",
                gridPos: "1.3")
            .WithOnActivate(unit =>
                {
                    int glacialAssaultStacks = 0;
                    int glacialAssaultMaxStacks = 4;
                    Modifier instantCastMod =
                        new Modifier(Modifier.StatModType.Multiplicative,
                            0); //Multiplies cast time by 0 for instance cast.
                    Modifier damageMod =
                        new Modifier(Modifier.StatModType.MultiplicativePercent, 40); //Multiplies damage by 40$.
                    Modifier resourceCostMod =
                        new Modifier(Modifier.StatModType.Multiplicative,
                            0); //Multiplies resource by 0 for instance cast.

                    Aura glacialAssaultAura = new Aura(
                            id: "glacial-assault",
                            name: "Glacial Assault",
                            duration: 99999,
                            tickInterval: 0
                        ).WithOnApply((_, _, _) =>
                        {
                            _glacialBlast.CastTime.AddModifier(instantCastMod);
                            _glacialBlast.DamageModifiers.AddModifier(damageMod);
                            _glacialBlast.ResourceCostModifiers.AddModifier(resourceCostMod);
                        })
                        .WithOnRemove((_, _, _) =>
                        {
                            _glacialBlast.CastTime.RemoveModifier(instantCastMod);
                            _glacialBlast.DamageModifiers.RemoveModifier(damageMod);
                            _glacialBlast.ResourceCostModifiers.RemoveModifier(resourceCostMod);
                            glacialAssaultStacks = 0;
                        });

                    unit.OnDamageDealt += (caster, target, damage, spell, crit) =>
                    {
                        if (spell == _coldSnap)
                        {
                            glacialAssaultStacks++;
                            if (glacialAssaultStacks == glacialAssaultMaxStacks)
                            {
                                caster.ApplyBuff(caster, glacialAssaultAura);
                            }
                        }

                        if (spell == _glacialBlast && caster.Buffs.Contains(glacialAssaultAura))
                        {
                            caster.RemoveBuff(caster, glacialAssaultAura);
                        }
                    };
                }
            );

        Talents.Add(_chillingFinesse);
        Talents.Add(_wintersEmbrace);
        Talents.Add(_glacialAssault);

        var _avalanche = new Talent(
                id: "avalanche",
                name: "Avalanche",
                gridPos: "3.2")
            .WithOnActivate(unit =>
            {
                _iceComet.OnCast += (unit1, spell, unit2) =>
                {
                    double rollChance = SimRandom.NextDouble();
                    if (rollChance < 0.07)
                    {
                        DealAOEDamage(4.51, 0.1, 5, 19, spell);
                        DealAOEDamage(4.51, 0.1, 5, 19, spell);
                    }
                    else if (rollChance < 0.15)
                    {
                        DealAOEDamage(4.51, 0.1, 5, 19, spell);
                    }
                };
            });

        Talents.Add(_avalanche);
    }
}