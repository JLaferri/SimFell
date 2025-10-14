using SimFell.Base;
using SimFell.Engine.Base;
using SimFell.Logging;

namespace SimFell.Engine.Heroes;

public class Benchmark : Unit
{
    private int targetEvents = 1600000000;
    private double castTime = 0;
    private int simDuration = 600;
    private int runCount = 10000;

    private Spell _benchmark;
    private Spell _benchmarkNoDmg;
    private Spell _benchmarkAOEDot;

    public Benchmark() : base("Benchmark")
    {
        //castTime = simDuration / (targetEvents / runCount);
        castTime = 0.02623;
        ConfigureSpellBook();
    }

    private void ConfigureSpellBook()
    {
        _benchmark = new Spell("benchmark", "Bench Mark", 0, castTime)
            .WithoutGCD()
            .WithSpellEvent((_, _, _) => DealDamage(PrimaryTarget, 1.0, 0.1, _benchmark));

        _benchmarkNoDmg = new Spell("benchmark-nodmg", "Bench Mark: No Damage", 0, castTime)
            .WithoutGCD();

        _benchmarkAOEDot = new Spell("benchmark-aoedot", "Bench Mark: AOE Damage", 10, 2)
            .WithoutGCD()
            .WithSpellEvent((_, _, _) =>
            {
                foreach (var tar in Targets)
                {
                    tar.ApplyDebuff(this, new Aura("benchmark-aoedot", "Bench Mark: AOE Damage", 10, 0.1)
                        .WithOnTick((_, target, _) => DealDamage(target, 1, 0.1, _benchmarkAOEDot)));
                }
            });


        SpellBook.Add(_benchmark);
        SpellBook.Add(_benchmarkNoDmg);
        SpellBook.Add(_benchmarkAOEDot);
    }
}