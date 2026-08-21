using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Objects;

namespace DefaultCombat.Routines
{
    internal sealed class PvPResolveTracker
    {
        private sealed class ResolveState
        {
            internal double Points;
            internal DateTime LastUpdateUtc = DateTime.UtcNow;
            internal bool Controlled;
            internal bool WhiteBarred;
            internal readonly HashSet<string> SeenEffects = new HashSet<string>();
        }

        private readonly Dictionary<ulong, ResolveState> _states =
            new Dictionary<ulong, ResolveState>();

        internal const double MaximumAutomaticHardStunResolve = 150;
        internal const double MaximumAutomaticKnockbackResolve = 750;

        private const double ResolveWhiteBar = 1000;
        private const double ResolveMaximum = 1500;
        private const double WhiteBarDecayPerSecond = 100;
        private const double PartialResolveDecayPerSecond = 25;

        internal void Reset()
        {
            _states.Clear();
        }

        internal void Track(HeroCharacter unit)
        {
            if (unit == null)
                return;

            ResolveState state;
            if (!_states.TryGetValue(unit.NodeId, out state))
            {
                state = new ResolveState();
                _states[unit.NodeId] = state;
            }

            var now = DateTime.UtcNow;
            var elapsed = Math.Max(0, (now - state.LastUpdateUtc).TotalSeconds);
            if (!state.Controlled && state.Points > 0)
            {
                var decay = state.WhiteBarred
                    ? WhiteBarDecayPerSecond
                    : PartialResolveDecayPerSecond;
                state.Points = Math.Max(0, state.Points - decay * elapsed);
                if (state.Points <= 0)
                    state.WhiteBarred = false;
            }

            var controls = unit.Debuffs
                .Where(effect => LightningPvPEffects.ControlResolveRate(effect) > 0)
                .ToList();

            foreach (var effect in controls)
            {
                var key = effect.AbilitySpecId.ToString("X16") + ":" +
                          effect.EffectNumber + ":" + effect.StartTime.Ticks;
                if (!state.SeenEffects.Add(key))
                    continue;

                var duration = effect.Duration.TotalSeconds;
                if (duration <= 0 || duration > 12)
                    duration = Math.Max(0.5, effect.TimeLeft.TotalSeconds);

                var newResolve = duration * LightningPvPEffects.ControlResolveRate(effect);
                var overlapResolve = controls
                    .Where(other => !ReferenceEquals(other, effect))
                    .Select(other => Math.Max(0, other.TimeLeft.TotalSeconds) *
                                     LightningPvPEffects.ControlResolveRate(other))
                    .DefaultIfEmpty(0)
                    .Max();
                var addition = Math.Max(0, newResolve - overlapResolve);
                state.Points = Math.Min(ResolveMaximum, state.Points + addition);
                if (state.Points >= ResolveWhiteBar)
                    state.WhiteBarred = true;

                Logger.Write(
                    "[PvPResolve] unit={0}, effect={1}, spec=0x{2:X16}, add={3:0}, estimate={4:0}",
                    unit.Name,
                    effect.Name,
                    effect.AbilitySpecId,
                    addition,
                    state.Points);
            }

            state.Controlled = controls.Count > 0;
            state.LastUpdateUtc = now;
        }

        internal double Estimate(HeroCharacter unit)
        {
            if (unit == null)
                return 0;

            ResolveState state;
            return _states.TryGetValue(unit.NodeId, out state) ? state.Points : 0;
        }

        internal bool IsWhiteBarred(HeroCharacter unit)
        {
            if (unit == null)
                return false;

            ResolveState state;
            return _states.TryGetValue(unit.NodeId, out state) && state.WhiteBarred;
        }
    }
}
