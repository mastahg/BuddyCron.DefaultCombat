// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Reborn.Utilities.Math;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Behaviors
{
    /// <summary>Per-pulse scan that computes the shared heal/dispel/tank targets and AoE decisions
    /// consumed by the rotations.</summary>
    public static class Targeting
    {
        private const int AoedpsCountNeeded = 3;
        private const int AoeHealCountNeeded = 3;

        //Settings for making target queries
        private const int MaxHealth = Health.Max;
        private const float HealingDistance = Distance.Ranged;
        private const float AoeHealDist = Distance.HealAoe;
        private const int AoeHealHp = Health.High;
        //Collections
        /// <summary>Group members considered for healing this scan.</summary>
        public static List<HeroCharacter> HealCandidates;
        /// <summary>Group members identified as tanks.</summary>
        public static List<HeroCharacter> Tanks;
        /// <summary>Positions of the heal candidates, for AoE-heal placement.</summary>
        public static List<Vector3> HealCandidatePoints;
        /// <summary>Hostile units in range this scan.</summary>
        public static List<HeroCharacter> Enemies = new List<HeroCharacter>();
        /// <summary>Positions of the enemies, for AoE placement.</summary>
        public static List<Vector3> EnemyPoints = new List<Vector3>();

        //Static Points and People
        /// <summary>Explicit tank name the user configured; empty for auto-detection.</summary>
        public static string TankName = "";
        /// <summary>Working values used while resolving <see cref="Tank"/> from <see cref="TankName"/>.</summary>
        public static string TankNameStart;
        /// <inheritdoc cref="TankNameStart"/>
        public static string TankNameCheck;
        /// <summary>The resolved tank, if any.</summary>
        public static HeroCharacter Tank;
        /// <summary>Best single-target heal recipient this scan, including the local player.</summary>
        public static HeroCharacter HealTarget;
        /// <summary>Best AoE-heal anchor this scan.</summary>
        public static HeroCharacter AoeHealTarget;
        /// <summary>Best AoE-damage anchor this scan.</summary>
        public static HeroCharacter AoeDpsTarget;
        /// <summary>Group member carrying a cleansable debuff, if any.</summary>
        public static HeroCharacter DispelTarget;

        /// <summary>Ground point for targeted AoE damage.</summary>
        public static Vector3 AoeDpsPoint = Vector3.Zero;

        //Counts
        /// <summary>Injured heal candidates clustered around <see cref="AoeHealTarget"/>.</summary>
        public static int AoeHealCount;
        /// <summary>Enemies clustered around <see cref="AoeDpsTarget"/>.</summary>
        public static int AoeDpsCount;
        /// <summary>Enemies inside point-blank AoE range of the player.</summary>
        public static int AoePeanutButterCount;
        /// <summary>True when enough hurt allies cluster to justify AoE healing.</summary>
        public static bool ShouldAoeHeal;
        /// <summary>True when enough enemies cluster to justify targeted AoE.</summary>
        public static bool ShouldAoe;
        /// <summary>True when enough enemies surround the player to justify point-blank AoE.</summary>
        public static bool ShouldPbaoe;
        /// <summary>Ground point for targeted AoE heals.</summary>
        public static Vector3 AoeHealPoint = Vector3.Zero;


        //Caching shit
        /// <summary>Scans since the object cache was rebuilt (starts expired).</summary>
        public static int cacheCount = 75;
        /// <summary>Scans a cached object list stays valid for.</summary>
        public static int maxCacheCount = 2;
        /// <summary>Cached characters used between full rescans.</summary>
        public static List<HeroCharacter> Objects;
        /// <summary>Working list for the in-progress rescan.</summary>
        public static List<HeroCharacter> objects;

        //Determine if we should use the tank's target.
        private static bool UseTankTarget => Core.Player.Target == null && Tank != null && Tank.NodeId != Core.Player.NodeId && Tank.InCombat &&
                                             Tank.Target != null;

        /// <summary>Composite that refreshes the heal/dispel/tank targets and AoE counts each pulse;
        /// always fails so the enclosing selector continues.</summary>
        public static Composite ScanTargets
        {
            get
            {
                return new Action(delegate
                {
                    cacheCount++;

                    AoeHealCount = 0;
                    AoeDpsCount = 0;
                    AoePeanutButterCount = 0;
                    Tank = null;
                    HealTarget = null;
                    AoeHealTarget = null;
                    AoeDpsTarget = null;
                    DispelTarget = null;
                    AoeHealPoint = Vector3.Zero;
                    AoeDpsPoint = Vector3.Zero;
                    ShouldAoeHeal = false;
                    ShouldAoe = false;
                    ShouldPbaoe = false;

                    HealCandidates = new List<HeroCharacter>();
                    HealCandidatePoints = new List<Vector3>();
                    Enemies = new List<HeroCharacter>();
                    EnemyPoints = new List<Vector3>();
                    Tanks = new List<HeroCharacter>();

                    if (cacheCount >= maxCacheCount || Objects == null)
                        updateObjects();

                    if (RotationRuntime.IsHealer)
                    {
                        foreach (var character in Objects)
                        {
                            if (!string.IsNullOrEmpty(TankName) && character.Name == TankName)
                                Tank = character;

                            if (Tank == null && Core.Player.FocusTargetIsActive && character.NodeId == Core.Player.FocusTargetId)
                                Tank = character;

                            if (character.IsPartyRoleTank())
                                Tanks.Add(character);

                            if (character.HealthPercent <= MaxHealth)
                            {
                                if (HealTarget == null || character.HealthPercent < HealTarget.HealthPercent)
                                    HealTarget = character;

                                HealCandidates.Add(character);
                                if (character.HealthPercent <= AoeHealHp)
                                    HealCandidatePoints.Add(character.Location);
                            }

                            if (character.NeedsCleanse() &&
                                (DispelTarget == null || character.HealthPercent < DispelTarget.HealthPercent))
                            {
                                DispelTarget = character;
                            }
                        }

                        Tank = Tank ?? Tanks.FirstOrDefault();
                        if (Tank == null && Core.Player.Companion != null &&
                            Objects.Any(character => character.NodeId == Core.Player.Companion.NodeId))
                        {
                            Tank = Core.Player.Companion;
                        }
                        Tank = Tank ?? Core.Player;

                        if (HealCandidatePoints.Count >= AoeHealCountNeeded)
                        {
                            AoeHealTarget = AoeHealLocation(AoeHealDist);
                            if (AoeHealTarget != null)
                            {
                                AoeHealCount = PointsAroundPoint(AoeHealTarget.Location, HealCandidatePoints, AoeHealDist);
                                AoeHealPoint = AoeHealLocation(AoeHealTarget);
                                ShouldAoeHeal = AoeHealCount >= AoeHealCountNeeded;
                            }
                        }
                    }

                    foreach (var character in GetHeroCharacters())
                    {
                        if (!character.IsValidTarget() || !IsEngagedWithParty(character))
                            continue;

                        Enemies.Add(character);
                        EnemyPoints.Add(character.Location);
                    }

                    if (Core.Player.Target != null && Core.Player.Target.IsHostile && !Core.Player.Target.IsDead)
                    {
                        AoeDpsTarget = Core.Player.Target;
                        AoeDpsPoint = Core.Player.Target.Location;
                        AoeDpsCount = PointsAroundPoint(Core.Player.Target.Location, EnemyPoints, Distance.MeleeAoE);
                        ShouldAoe = AoeDpsCount >= AoedpsCountNeeded;
                    }

                    AoePeanutButterCount = PointsAroundPoint(Core.Player.Location, EnemyPoints, Distance.MeleeAoE);
                    ShouldPbaoe = AoePeanutButterCount >= AoedpsCountNeeded;
                    return RunStatus.Failure;
                });
            }
        }

        /// <summary>Assigns the current friendly target as the tank (by name), or clears the
        /// assignment when re-invoked on the same character or with no friendly target.</summary>
        public static void SetTank()
        {
            var target = Core.Player.Target;
            if (target != null && target.IsFriendly && !TankName.Equals(target.Name))
            {
                TankName = target.Name;
                Logger.Write("Tank set to : " + TankName);
            }
            else
            {
                TankName = "";
                Logger.Write("Cleared Tank");
            }

            Tank = null;
        }

        /// <summary>True when an enemy is selected or fighting the player, companion, or party.</summary>
        internal static bool IsEngagedWithParty(HeroCharacter enemy)
        {
            try
            {
                var me = Core.Player;
                if (enemy == null || me == null)
                    return false;

                if (me.Target != null && enemy.NodeId == me.Target.NodeId)
                    return true;
                if (me.IsInCombatWith(enemy))
                    return true;

                var companion = me.Companion;
                if (companion != null && companion.IsInCombatWith(enemy))
                    return true;

                return me.PartyMembers(true).Any(member => member != null &&
                    member.IsInCombatWith(enemy));
            }
            catch
            {
                return false;
            }
        }

        private static void updateObjects()
        {
            Objects = new List<HeroCharacter>();
            if (RotationRuntime.IsHealer)
            {
                foreach (var character in Core.Player.PartyMembers(true))
                    AddHealCandidate(character);

                AddHealCandidate(Core.Player);
                AddHealCandidate(Core.Player.Companion);

                var selectedTarget = Core.Player.Target;
                if (selectedTarget != null && selectedTarget.IsFriendly)
                    AddHealCandidate(selectedTarget);
            }

            cacheCount = 0;
        }

        /// <summary>Adds a living, nearby, visible unit once to the cached healing roster.</summary>
        private static void AddHealCandidate(HeroCharacter character)
        {
            if (character == null || character.IsDead || !character.InLineOfSight ||
                character.DistanceSqr >= HealingDistance * HealingDistance ||
                Objects.Any(existing => existing.NodeId == character.NodeId))
            {
                return;
            }

            Objects.Add(character);
        }

        /// <summary>Snapshot of all NPCs in the object manager as <see cref="HeroCharacter"/>
        /// (source list for the enemy scan).</summary>
        public static List<HeroCharacter> GetHeroCharacters()
        {
            //List<HeroCharacter> objects = HeroObjectManager.GetObjectsOfType<HeroCharacter>().ToList();

            /*
            if (Core.Player.Companion != null)
                objects.Add(Core.Player.Companion);
            */
            var npcs = HeroObjectManager.GetObjectsOfType<HeroNPC>();
            var objects = npcs.Cast<HeroCharacter>().ToList();

            return objects;
        }

        private static Vector3 AoeHealLocation(HeroCharacter p)
        {
            return p != null ? p.Location : Vector3.Zero;
        }

        /// <summary>Heal candidate with the most other candidates within <paramref name="dist"/>
        /// (tank or self as the baseline), or null when too few cluster to justify an AoE heal.</summary>
        private static HeroCharacter AoeHealLocation(float dist)
        {
            var injuredCandidates = HealCandidates
                .Where(character => character.HealthPercent <= AoeHealHp)
                .ToList();
            HeroCharacter pt = Tank != null && Tank.HealthPercent <= AoeHealHp
                ? Tank
                : injuredCandidates.FirstOrDefault();
            if (pt == null)
                return null;

            var currentPtCount = PointsAroundPoint(pt.Location, HealCandidatePoints, dist);
            foreach (var candidate in injuredCandidates)
            {
                var candidateCount = PointsAroundPoint(candidate.Location, HealCandidatePoints, dist);
                if (candidateCount > currentPtCount)
                {
                    pt = candidate;
                    currentPtCount = candidateCount;
                }
            }

            return currentPtCount >= AoeHealCountNeeded ? pt : null;
        }

        /// <summary>True when at least <paramref name="minMobs"/> tracked enemies are within
        /// <paramref name="distance"/> of <paramref name="center"/>.</summary>
        public static bool CheckDpsAoe(int minMobs, float distance, Vector3 center)
        {
            return PointsAroundPoint(center, EnemyPoints, distance) >= minMobs;
        }

        private static int PointsAroundPoint(Vector3 pt, List<Vector3> l, float dist)
        {
            var maxDistance = dist * dist;
            return l.Count(p => p.DistanceSqr(pt) <= maxDistance);
        }
    }
}
