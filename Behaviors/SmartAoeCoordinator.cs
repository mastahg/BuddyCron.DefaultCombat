using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;

namespace DefaultCombat.Behaviors
{
    public sealed class SmartAoeProfile
    {
        public SmartAoeProfile()
        {
            Radius = Distance.MeleeAoE;
            MaxTargets = 8;
            TargetSelectionDwellSeconds = 0.35;
            PostCastDwellSeconds = 0.85;
            TransactionTimeoutSeconds = 3;
        }

        public string Key { get; set; }

        public string AbilityName { get; set; }

        public float Radius { get; set; }

        public int MaxTargets { get; set; }

        public double TargetSelectionDwellSeconds { get; set; }

        public double PostCastDwellSeconds { get; set; }

        public double TransactionTimeoutSeconds { get; set; }

        public Func<bool> Enabled { get; set; }

        public Func<IEnumerable<HeroCharacter>> CandidateProvider { get; set; }

        public Func<HeroCharacter, bool> CandidateFilter { get; set; }

        public Func<HeroCharacter, bool> SplashHazard { get; set; }

        public Func<HeroCharacter, double> ImpactValue { get; set; }
    }

    public sealed class SmartAoeCoordinator
    {
        private sealed class TargetScore
        {
            internal HeroCharacter Target;
            internal int HitCount;
            internal double Impact;
            internal bool Selected;
        }

        private readonly SmartAoeProfile _profile;
        private readonly TargetHandoffCoordinator _handoff = new TargetHandoffCoordinator();

        public SmartAoeCoordinator(SmartAoeProfile profile)
        {
            _profile = profile;
        }

        public bool IsBusy => _handoff.IsBusy;

        public void Reset()
        {
            _handoff.Reset();
        }

        public RunStatus Continue()
        {
            return IsBusy ? _handoff.Continue() : RunStatus.Failure;
        }

        public RunStatus Tick(int minimumTargets)
        {
            return Tick(minimumTargets, null);
        }

        public RunStatus Tick(int minimumTargets, HeroCharacter preferredTarget)
        {
            if (IsBusy)
                return _handoff.Continue();

            if (!CanRun())
                return RunStatus.Failure;

            var targetScore = SelectTarget(Math.Max(1, minimumTargets), preferredTarget);
            if (targetScore == null || targetScore.Target == null)
                return RunStatus.Failure;

            var current = Core.Player != null ? Core.Player.Target : null;
            bool selected = current != null && current.NodeId == targetScore.Target.NodeId;
            if (!selected && RoutineManager.IsAnyDisallowed(CapabilityFlags.Targeting))
                return RunStatus.Failure;

            Spell.StopMovementForCast(_profile.AbilityName);
            var cast = AbilityManager.Cast(_profile.AbilityName, targetScore.Target);
            if (cast.Success)
            {
                LogCast(targetScore.Target, targetScore.HitCount, selected ? "selected" : "direct");
                return RunStatus.Success;
            }

            if (!selected && IsTargetOverrideFailure(cast.Item2))
            {
                var restoreTarget = IsRestorable(preferredTarget) ? preferredTarget : current;
                BeginHandoff(current, restoreTarget, targetScore.Target, targetScore.HitCount);
                return RunStatus.Success;
            }

            Spell.LogCastFailure(_profile.AbilityName, targetScore.Target, cast.ToString());
            return RunStatus.Failure;
        }

        private TargetScore SelectTarget(int minimumTargets, HeroCharacter preferredTarget)
        {
            var allCandidates = GetCandidates(false);
            var candidates = allCandidates.Where(IsUsableCandidate).ToList();
            if (candidates.Count == 0)
                return null;

            var current = preferredTarget ?? (Core.Player != null ? Core.Player.Target : null);
            var scores = new List<TargetScore>();
            foreach (var candidate in candidates)
            {
                if (allCandidates.Any(enemy =>
                        WithinRadius(enemy, candidate.Location, _profile.Radius) &&
                        IsSplashHazard(enemy)))
                {
                    continue;
                }

                var canCast = AbilityManager.CanCast(_profile.AbilityName, candidate);
                if (!canCast.Success)
                    continue;

                var hits = candidates
                    .Where(enemy => WithinRadius(enemy, candidate.Location, _profile.Radius))
                    .OrderByDescending(ImpactFor)
                    .Take(Math.Max(1, _profile.MaxTargets))
                    .ToList();
                if (hits.Count < minimumTargets)
                    continue;

                scores.Add(new TargetScore
                {
                    Target = candidate,
                    HitCount = hits.Count,
                    Impact = hits.Sum(ImpactFor),
                    Selected = current != null && current.NodeId == candidate.NodeId
                });
            }

            return scores
                .OrderByDescending(score => score.HitCount)
                .ThenByDescending(score => score.Impact)
                .ThenByDescending(score => score.Selected)
                .ThenByDescending(score => score.Target.BossOrGreater())
                .ThenByDescending(score => score.Target.StrongOrGreater())
                .ThenBy(score => score.Target.DistanceSqr)
                .FirstOrDefault();
        }

        private void BeginHandoff(HeroCharacter starting, HeroCharacter restore,
            HeroCharacter pending, int hitCount)
        {
            _handoff.Begin(new TargetHandoffRequest
            {
                LogPrefix = "SmartAoE",
                ProfileKey = ProfileKey(),
                StartingTarget = starting,
                RestoreTarget = restore,
                PendingTarget = pending,
                TargetSelectionDwellSeconds = _profile.TargetSelectionDwellSeconds,
                PostCastDwellSeconds = _profile.PostCastDwellSeconds,
                TransactionTimeoutSeconds = _profile.TransactionTimeoutSeconds,
                CanContinue = CanRun,
                ResolveTarget = FindTarget,
                IsPendingUsable = IsUsableCandidate,
                IsRestorable = IsRestorable,
                TryCast = target => TrySwitchedCast(target, hitCount),
                OnCastTimeout = (target, detail) =>
                    Spell.LogCastFailure(_profile.AbilityName, target, detail)
            });
        }

        private TargetHandoffCastAttempt TrySwitchedCast(HeroCharacter target, int hitCount)
        {
            var canCast = AbilityManager.CanCast(_profile.AbilityName, target);
            if (!canCast.Success)
                return new TargetHandoffCastAttempt { FailureDetail = canCast.ToString() };

            Spell.StopMovementForCast(_profile.AbilityName);
            var cast = AbilityManager.Cast(_profile.AbilityName, target);
            if (!cast.Success)
                return new TargetHandoffCastAttempt { FailureDetail = cast.ToString() };

            LogCast(target, hitCount, "switched");
            return new TargetHandoffCastAttempt { Accepted = true };
        }

        private bool CanRun()
        {
            if (_profile == null || string.IsNullOrWhiteSpace(_profile.AbilityName) ||
                !AbilityManager.HasAbility(_profile.AbilityName))
            {
                return false;
            }

            try
            {
                return _profile.Enabled == null || _profile.Enabled();
            }
            catch
            {
                return false;
            }
        }

        private List<HeroCharacter> GetCandidates(bool applyFilter)
        {
            var candidates = new Dictionary<ulong, HeroCharacter>();
            try
            {
                var supplied = _profile.CandidateProvider != null
                    ? _profile.CandidateProvider()
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
                .Where(candidate => !applyFilter || IsUsableCandidate(candidate))
                .ToList();
        }

        private bool IsUsableCandidate(HeroCharacter target)
        {
            if (target == null || !target.IsValid || target.IsDead ||
                !target.IsEffectivePvEHostile() ||
                !target.IsTargetable || !target.InLineOfSight)
            {
                return false;
            }

            try
            {
                return _profile.CandidateFilter == null || _profile.CandidateFilter(target);
            }
            catch
            {
                return false;
            }
        }

        private HeroCharacter FindTarget(ulong targetId)
        {
            if (targetId == 0)
                return null;

            return GetCandidates(false)
                .FirstOrDefault(candidate => candidate.NodeId == targetId);
        }

        private double ImpactFor(HeroCharacter target)
        {
            try
            {
                if (_profile.ImpactValue != null)
                    return Math.Max(0, _profile.ImpactValue(target));
            }
            catch
            {
                return 0;
            }

            if (target == null)
                return 0;

            return 1 + (target.BossOrGreater() ? 0.1 : target.StrongOrGreater() ? 0.05 : 0);
        }

        private bool IsSplashHazard(HeroCharacter target)
        {
            try
            {
                return _profile.SplashHazard != null && _profile.SplashHazard(target);
            }
            catch
            {
                return true;
            }
        }

        private static bool WithinRadius(HeroCharacter target, Vector3 center, float radius)
        {
            try
            {
                return target != null &&
                       Vector3.DistanceSquared(target.Location, center) <= radius * radius;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRestorable(HeroCharacter target)
        {
            return target != null && target.IsValid && !target.IsDead && target.IsTargetable;
        }

        private static bool IsTargetOverrideFailure(effResult result)
        {
            return result == effResult.TargetInvalid ||
                   result == effResult.TargetOverrideFailed ||
                   result == effResult.NoTarget ||
                   result == effResult.NotEnemy;
        }

        private void LogCast(HeroCharacter target, int hitCount, string mode)
        {
            Logger.Write(string.Format(
                "[SmartAoE] Cast accepted; profile={0} ability={1} target={2} id=0x{3:X} hits={4} mode={5}",
                ProfileKey(), _profile.AbilityName, SafeName(target), target.NodeId, hitCount, mode));
        }

        private string ProfileKey()
        {
            return !string.IsNullOrWhiteSpace(_profile.Key)
                ? _profile.Key
                : _profile.AbilityName ?? "unknown";
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
