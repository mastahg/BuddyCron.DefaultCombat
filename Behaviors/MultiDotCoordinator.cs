using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Behaviors
{
    public sealed class MultiDotProfile
    {
        public MultiDotProfile()
        {
            DebuffNames = new string[0];
            DebuffAbilitySpecIds = new ulong[0];
            MaxTargets = 1;
            ExpectedDurationSeconds = 15;
            RefreshWindowSeconds = 1.5;
            TargetSelectionDwellSeconds = 0.35;
            PostCastDwellSeconds = 0.85;
            TransactionTimeoutSeconds = 3;
        }

        public string Key { get; set; }

        public string AbilityName { get; set; }

        public IReadOnlyCollection<string> DebuffNames { get; set; }

        public IReadOnlyCollection<ulong> DebuffAbilitySpecIds { get; set; }

        public int MaxTargets { get; set; }

        public double ExpectedDurationSeconds { get; set; }

        public double RefreshWindowSeconds { get; set; }

        public double TargetSelectionDwellSeconds { get; set; }

        public double PostCastDwellSeconds { get; set; }

        public double TransactionTimeoutSeconds { get; set; }

        public Func<bool> Enabled { get; set; }

        public Func<IEnumerable<HeroCharacter>> CandidateProvider { get; set; }

        public Func<HeroCharacter, bool> CandidateFilter { get; set; }

        public Func<HeroCharacter, bool, double> MinimumTtdSeconds { get; set; }
    }

    public sealed class MultiDotCoordinator
    {
        private sealed class ApplicationLease
        {
            internal DateTime ExpiresUtc;
        }

        private readonly List<MultiDotProfile> _profiles;
        private readonly Dictionary<string, ApplicationLease> _leases =
            new Dictionary<string, ApplicationLease>();
        private readonly TargetHandoffCoordinator _handoff = new TargetHandoffCoordinator();

        public MultiDotCoordinator(params MultiDotProfile[] profiles)
        {
            _profiles = profiles == null
                ? new List<MultiDotProfile>()
                : profiles.Where(profile => profile != null).ToList();
        }

        public bool IsBusy => _handoff.IsBusy;

        public void Reset()
        {
            _leases.Clear();
            _handoff.Reset();
        }

        public RunStatus Continue()
        {
            return IsBusy ? _handoff.Continue() : RunStatus.Failure;
        }

        public RunStatus Tick()
        {
            if (IsBusy)
                return _handoff.Continue();

            PruneLeases();
            foreach (var profile in _profiles)
            {
                var result = TryApply(profile, true);
                if (result != RunStatus.Failure)
                    return result;
            }

            foreach (var profile in _profiles)
            {
                var result = TryApply(profile, false);
                if (result != RunStatus.Failure)
                    return result;
            }

            return RunStatus.Failure;
        }

        public bool IsMaintained(MultiDotProfile profile, HeroCharacter target)
        {
            if (profile == null || target == null)
                return false;

            PruneLeases();
            if (_leases.TryGetValue(LeaseKey(profile, target.NodeId), out var lease) &&
                lease.ExpiresUtc > DateTime.UtcNow)
            {
                return true;
            }

            var effect = FindOwnedEffect(profile, target);
            if (effect == null)
                return false;

            try
            {
                return effect.TimeLeft.TotalSeconds > profile.RefreshWindowSeconds;
            }
            catch
            {
                return false;
            }
        }

        private RunStatus TryApply(MultiDotProfile profile, bool selectedOnly)
        {
            if (!CanRun(profile))
                return RunStatus.Failure;

            var candidates = GetCandidates(profile, true);
            if (candidates.Count == 0)
                return RunStatus.Failure;

            int maximumTargets = Math.Max(1, profile.MaxTargets);
            int maintainedTargets = GetCandidates(profile, false).Count(target =>
                target != null && target.IsValid && !target.IsDead &&
                IsMaintained(profile, target));
            if (maintainedTargets >= maximumTargets)
                return RunStatus.Failure;

            var current = Core.Player != null ? Core.Player.Target : null;
            var eligible = candidates
                .Where(target => !IsMaintained(profile, target))
                .Where(target => IsWorthApplying(profile, target,
                    current != null && current.NodeId == target.NodeId))
                .ToList();
            if (selectedOnly)
            {
                eligible = current == null
                    ? new List<HeroCharacter>()
                    : eligible.Where(target => target.NodeId == current.NodeId).ToList();
            }
            if (eligible.Count == 0)
                return RunStatus.Failure;

            var target = current != null
                ? eligible.FirstOrDefault(candidate => candidate.NodeId == current.NodeId)
                : null;
            target = target ?? eligible
                .OrderByDescending(candidate => TimeToDie.Estimate(candidate).IsStable)
                .ThenByDescending(candidate => candidate.BossOrGreater())
                .ThenByDescending(candidate => candidate.StrongOrGreater())
                .ThenByDescending(candidate => candidate.HealthPercent)
                .ThenBy(candidate => candidate.DistanceSqr)
                .FirstOrDefault();
            if (target == null)
                return RunStatus.Failure;

            bool selected = current != null && current.NodeId == target.NodeId;
            if (!selected && RoutineManager.IsAnyDisallowed(CapabilityFlags.Targeting))
                return RunStatus.Failure;

            var canCast = AbilityManager.CanCast(profile.AbilityName, target);
            if (!canCast.Success)
                return RunStatus.Failure;

            Spell.StopMovementForCast(profile.AbilityName);
            var cast = AbilityManager.Cast(profile.AbilityName, target);
            if (cast.Success)
            {
                RecordApplication(profile, target);
                LogCast(profile, target, selected ? "selected" : "direct");
                return RunStatus.Success;
            }

            if (!selected && IsTargetOverrideFailure(cast.Item2))
            {
                BeginHandoff(profile, current, target);
                return RunStatus.Success;
            }

            Spell.LogCastFailure(profile.AbilityName, target, cast.ToString());
            return RunStatus.Failure;
        }

        private void BeginHandoff(MultiDotProfile profile, HeroCharacter original,
            HeroCharacter pending)
        {
            _handoff.Begin(new TargetHandoffRequest
            {
                LogPrefix = "MultiDot",
                ProfileKey = ProfileKey(profile),
                StartingTarget = original,
                RestoreTarget = original,
                PendingTarget = pending,
                TargetSelectionDwellSeconds = profile.TargetSelectionDwellSeconds,
                PostCastDwellSeconds = profile.PostCastDwellSeconds,
                TransactionTimeoutSeconds = profile.TransactionTimeoutSeconds,
                CanContinue = () => CanRun(profile),
                ResolveTarget = targetId => FindTarget(profile, targetId),
                IsPendingUsable = target => IsUsableCandidate(profile, target),
                IsRestorable = IsRestorable,
                TryCast = target => TrySwitchedCast(profile, target),
                OnCastTimeout = (target, detail) =>
                    Spell.LogCastFailure(profile.AbilityName, target, detail)
            });
        }

        private TargetHandoffCastAttempt TrySwitchedCast(MultiDotProfile profile,
            HeroCharacter target)
        {
            var canCast = AbilityManager.CanCast(profile.AbilityName, target);
            if (!canCast.Success)
                return new TargetHandoffCastAttempt { FailureDetail = canCast.ToString() };

            Spell.StopMovementForCast(profile.AbilityName);
            var cast = AbilityManager.Cast(profile.AbilityName, target);
            if (!cast.Success)
                return new TargetHandoffCastAttempt { FailureDetail = cast.ToString() };

            RecordApplication(profile, target);
            LogCast(profile, target, "switched");
            return new TargetHandoffCastAttempt { Accepted = true };
        }

        private static bool CanRun(MultiDotProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.AbilityName) ||
                !AbilityManager.HasAbility(profile.AbilityName))
            {
                return false;
            }

            try
            {
                return profile.Enabled == null || profile.Enabled();
            }
            catch
            {
                return false;
            }
        }

        private static bool IsWorthApplying(MultiDotProfile profile, HeroCharacter target,
            bool selected)
        {
            double minimumSeconds = 0;
            try
            {
                if (profile.MinimumTtdSeconds != null)
                    minimumSeconds = Math.Max(0, profile.MinimumTtdSeconds(target, selected));
            }
            catch
            {
                return false;
            }

            return minimumSeconds <= 0 ||
                   TimeToDie.WillLiveFor(target, minimumSeconds,
                       "MultiDot " + profile.AbilityName);
        }

        private List<HeroCharacter> GetCandidates(MultiDotProfile profile, bool applyFilter)
        {
            var candidates = new Dictionary<ulong, HeroCharacter>();
            try
            {
                var supplied = profile.CandidateProvider != null
                    ? profile.CandidateProvider()
                    : Targeting.Enemies;
                if (supplied != null)
                {
                    foreach (var candidate in supplied)
                    {
                        if (candidate != null)
                            candidates[candidate.NodeId] = candidate;
                    }
                }
            }
            catch
            {
            }

            var current = Core.Player != null ? Core.Player.Target : null;
            if (current != null)
                candidates[current.NodeId] = current;

            return candidates.Values
                .Where(candidate => !applyFilter || IsUsableCandidate(profile, candidate))
                .ToList();
        }

        private static bool IsUsableCandidate(MultiDotProfile profile, HeroCharacter target)
        {
            if (target == null || !target.IsValid || target.IsDead ||
                !target.IsEffectivePvEHostile() ||
                !target.IsTargetable || !target.InLineOfSight)
            {
                return false;
            }

            try
            {
                return profile.CandidateFilter == null || profile.CandidateFilter(target);
            }
            catch
            {
                return false;
            }
        }

        private HeroCharacter FindTarget(MultiDotProfile profile, ulong targetId)
        {
            if (targetId == 0)
                return null;

            return GetCandidates(profile, false)
                .FirstOrDefault(candidate => candidate.NodeId == targetId);
        }

        private static bool IsRestorable(HeroCharacter target)
        {
            return target != null && target.IsValid && !target.IsDead &&
                   target.IsEffectivePvEHostile() && target.IsTargetable;
        }

        private HeroEffect FindOwnedEffect(MultiDotProfile profile, HeroCharacter target)
        {
            try
            {
                ulong playerId = Core.Player.NodeId;
                return target.Debuffs.FirstOrDefault(effect =>
                    effect != null && effect.CasterGuid == playerId && MatchesEffect(profile, effect));
            }
            catch
            {
                return null;
            }
        }

        private static bool MatchesEffect(MultiDotProfile profile, HeroEffect effect)
        {
            if (profile.DebuffAbilitySpecIds != null && effect.AbilitySpecId != 0 &&
                profile.DebuffAbilitySpecIds.Contains(effect.AbilitySpecId))
            {
                return true;
            }

            string effectName = effect.Name ?? string.Empty;
            return profile.DebuffNames != null && profile.DebuffNames.Any(name =>
                !string.IsNullOrWhiteSpace(name) &&
                (string.Equals(effectName, name, StringComparison.OrdinalIgnoreCase) ||
                 effectName.StartsWith(name + " (", StringComparison.OrdinalIgnoreCase)));
        }

        private void RecordApplication(MultiDotProfile profile, HeroCharacter target)
        {
            double leaseSeconds = Math.Max(1.5,
                profile.ExpectedDurationSeconds - profile.RefreshWindowSeconds);
            _leases[LeaseKey(profile, target.NodeId)] = new ApplicationLease
            {
                ExpiresUtc = DateTime.UtcNow.AddSeconds(leaseSeconds)
            };
        }

        private void PruneLeases()
        {
            var now = DateTime.UtcNow;
            var expired = _leases
                .Where(pair => pair.Value == null || pair.Value.ExpiresUtc <= now)
                .Select(pair => pair.Key)
                .ToList();
            foreach (var key in expired)
                _leases.Remove(key);
        }

        private static bool IsTargetOverrideFailure(effResult result)
        {
            return result == effResult.TargetInvalid ||
                   result == effResult.TargetOverrideFailed ||
                   result == effResult.NoTarget ||
                   result == effResult.NotEnemy;
        }

        private void LogCast(MultiDotProfile profile, HeroCharacter target, string mode)
        {
            Logger.Write(string.Format(
                "[MultiDot] Cast accepted; profile={0} ability={1} target={2} id=0x{3:X} mode={4}",
                ProfileKey(profile), profile.AbilityName, SafeName(target), target.NodeId, mode));
        }

        private static string LeaseKey(MultiDotProfile profile, ulong targetId)
        {
            return ProfileKey(profile) + ":" + targetId;
        }

        private static string ProfileKey(MultiDotProfile profile)
        {
            return !string.IsNullOrWhiteSpace(profile.Key)
                ? profile.Key
                : profile.AbilityName ?? "unknown";
        }

        private static string SafeName(HeroCharacter target)
        {
            try
            {
                return target != null && !string.IsNullOrWhiteSpace(target.Name)
                    ? target.Name
                    : "unknown";
            }
            catch
            {
                return "unknown";
            }
        }

    }
}
