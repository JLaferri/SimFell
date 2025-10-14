using System;
using System.Collections.Generic;
using System.Linq;

namespace SimFell.Engine.Base
{
    public class Stat
    {
        // Base Value
        public double BaseValue { get; set; }
        private readonly bool _hasDiminishingReturns;

        // Diminishing returns constants
        private static readonly double PointEffectiveness = 0.017; // 1.7% per point
        private static readonly double[] _breakPoints = { 0.0, 10.0, 15.0, 20.0, 25.0 };
        private static readonly double[] _breakPointMultipliers = { 1, 1, 0.95, 0.9, 0.85, 0.8 };
        private static readonly double[] _breakPointRatings;

        static Stat()
        {
            // Precompute break point ratings
            var ratingsPartial = new double[_breakPoints.Length];
            for (int i = 0; i < _breakPoints.Length; i++)
            {
                ratingsPartial[i] = (i > 0 ? _breakPoints[i] - _breakPoints[i - 1] : _breakPoints[i])
                                    / _breakPointMultipliers[i]
                                    / PointEffectiveness;
            }

            _breakPointRatings = new double[_breakPoints.Length];
            double sum = 0;
            for (int i = 0; i < ratingsPartial.Length; i++)
            {
                sum += ratingsPartial[i];
                _breakPointRatings[i] = sum;
            }
        }

        // Modifier storage
        private readonly HashSet<Modifier> _modifiers = new();
        private readonly HashSet<Modifier> _dynamicModifiers = new();

        // Protected access for derived classes
        protected IReadOnlyCollection<Modifier> DynamicModifiers => _dynamicModifiers;

        // Caching
        private CacheValues _staticCache = new CacheValues();
        private CacheValues _dynamicCache = new CacheValues();
        private bool _dynamicUseResultCache = false;
        private double _cachedResult = 0;
        private double _cachedBaseValue = double.NaN;
        private bool _cachedResultValid = false;

        // Diminishing returns cache
        private readonly Dictionary<int, double> _percentageCache = new();

        public event Action? OnModifierAdded;
        public event Action? OnModifierRemoved;
        public Action<Stat> OnInvalidate { get; set; } = _ => { };

        public Stat(double baseValue, bool hasDiminishingReturns = false)
        {
            BaseValue = baseValue;
            _hasDiminishingReturns = hasDiminishingReturns;
            InvalidateCache();
        }

        public void AddModifier(Modifier modifier)
        {
            var set = modifier is DynamicModifier ? _dynamicModifiers : _modifiers;
            if (set.Add(modifier))
            {
                InvalidateCache();
                if (modifier is DynamicModifier)
                    InvalidateDynamicCache();
                else
                    InvalidateStaticCache();

                OnModifierAdded?.Invoke();
            }
        }

        public void RemoveModifier(Modifier modifier)
        {
            var set = modifier is DynamicModifier ? _dynamicModifiers : _modifiers;
            if (set.Remove(modifier))
            {
                InvalidateCache();
                if (modifier is DynamicModifier)
                    InvalidateDynamicCache();
                else
                    InvalidateStaticCache();

                OnModifierRemoved?.Invoke();
            }
        }

        private void InvalidateDynamicCache() => _dynamicCache.Valid = false;
        private void InvalidateStaticCache() => _staticCache.Valid = false;

        public void InvalidateCache()
        {
            _cachedResultValid = false;
            _percentageCache.Clear();
            InvalidateDynamicCache();
            InvalidateStaticCache();
            OnInvalidate(this);
        }

        private void RecalculateModifierCache(ref CacheValues cache, IEnumerable<Modifier> modifiers)
        {
            if (cache.Valid) return;

            cache.FlatSum = 0;
            cache.AdditivePercentSum = 0;
            cache.MultiplicativePercent = 1;
            cache.Multiplicative = 1;
            cache.InverseMultiplicativePercent = 1;

            foreach (var mod in modifiers)
            {
                switch (mod.StatMod)
                {
                    case Modifier.StatModType.Flat: cache.FlatSum += mod.Value; break;
                    case Modifier.StatModType.AdditivePercent: cache.AdditivePercentSum += mod.Value; break;
                    case Modifier.StatModType.MultiplicativePercent:
                        cache.MultiplicativePercent *= 1 + mod.Value / 100; break;
                    case Modifier.StatModType.Multiplicative: cache.Multiplicative *= mod.Value; break;
                    case Modifier.StatModType.InverseMultiplicativePercent:
                        cache.InverseMultiplicativePercent /= 1 + mod.Value / 100; break;
                }
            }

            cache.Valid = true;
        }

        private void RecalculateModifierCache()
        {
            RecalculateModifierCache(ref _staticCache, _modifiers);
            RecalculateModifierCache(ref _dynamicCache, _dynamicModifiers);
        }

        public Stat SetDynamicUseResultCache()
        {
            _dynamicUseResultCache = true;
            return this;
        }

        public double GetValue(double? inBaseValue = null)
        {
            double baseVal = inBaseValue ?? BaseValue;

            if (_cachedResultValid && _cachedBaseValue == baseVal &&
                (_dynamicModifiers.Count == 0 || _dynamicUseResultCache))
                return _cachedResult;

            RecalculateModifierCache();

            double value = baseVal + _staticCache.FlatSum + _dynamicCache.FlatSum;
            if (_hasDiminishingReturns)
                value = GetStatAsPercentage((int)value);

            value += _staticCache.AdditivePercentSum + _dynamicCache.AdditivePercentSum;
            value *= _staticCache.MultiplicativePercent * _dynamicCache.MultiplicativePercent;
            value *= _staticCache.Multiplicative * _dynamicCache.Multiplicative;
            value *= _staticCache.InverseMultiplicativePercent * _dynamicCache.InverseMultiplicativePercent;

            _cachedResult = value;
            _cachedBaseValue = baseVal;
            _cachedResultValid = true;

            return value;
        }

        private double GetStatAsPercentage(int statPoints)
        {
            if (_percentageCache.TryGetValue(statPoints, out var cached))
                return cached;

            // Binary search for breakpoint interval
            int left = 0, right = _breakPointRatings.Length - 1;
            while (left < right)
            {
                int mid = (left + right) / 2;
                if (statPoints > _breakPointRatings[mid])
                    left = mid + 1;
                else
                    right = mid;
            }

            int i = left;
            double result;
            if (i == 0)
                result = statPoints * PointEffectiveness * _breakPointMultipliers[0];
            else if (statPoints <= _breakPointRatings[i])
            {
                result = _breakPoints[i - 1] +
                         (statPoints - _breakPointRatings[i - 1]) /
                         ((_breakPointRatings[i] - _breakPointRatings[i - 1]) /
                          (_breakPoints[i] - _breakPoints[i - 1]));
            }
            else
            {
                result = _breakPoints.Last() + (statPoints - _breakPointRatings.Last()) * PointEffectiveness *
                    _breakPointMultipliers.Last();
            }

            _percentageCache[statPoints] = result;
            return result;
        }

        public bool HasModifier(Modifier mod) => _modifiers.Contains(mod);
        public void ClearCache() => InvalidateCache();

        private class CacheValues
        {
            public double FlatSum;
            public double AdditivePercentSum;
            public double MultiplicativePercent = 1;
            public double Multiplicative = 1;
            public double InverseMultiplicativePercent = 1;
            public bool Valid = false;
        }
    }

    public class Modifier
    {
        public enum StatModType
        {
            Flat,
            AdditivePercent,
            MultiplicativePercent,
            Multiplicative,
            InverseMultiplicativePercent
        }

        public StatModType StatMod { get; }
        public virtual double Value { get; set; }

        public Modifier(StatModType statMod, double value)
        {
            StatMod = statMod;
            Value = value;
        }
    }

    public class DynamicModifier : Modifier
    {
        private readonly Func<double> _callback;
        public override double Value => _callback();

        public DynamicModifier(StatModType type, Func<double> callback) : base(type, 0)
        {
            _callback = callback;
        }
    }

    public class HealthStat : Stat
    {
        public double MaximumValue { get; set; }
        private bool _maxCacheValid = false;
        private double _cachedMax = 0;

        public HealthStat(double baseStat, bool diminishing = false) : base(baseStat, diminishing)
        {
            MaximumValue = baseStat;
            OnModifierAdded += () => _maxCacheValid = false;
            OnModifierRemoved += () => _maxCacheValid = false;
        }

        public double GetMaxValue()
        {
            if (_maxCacheValid) return _cachedMax;

            bool hasDynamic = DynamicModifiers.Count > 0;
            _cachedMax = GetValue(MaximumValue);

            if (!hasDynamic) _maxCacheValid = true;
            return _cachedMax;
        }
    }
}