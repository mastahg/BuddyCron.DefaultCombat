// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using BuddyCron;
using BuddyCron.Helpers;
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
        /// <summary>Living hostile units selected by or fighting the player, companion, or party.
        /// Rebuilt at most once per game frame; don't hold the list across frames.</summary>
        public static IReadOnlyList<HeroCharacter> Enemies => EnemyCache.Value.Units;
        /// <summary>Positions of <see cref="Enemies"/>, in the same order and with the same lifetime.</summary>
        public static IReadOnlyList<Vector3> EnemyPoints => EnemyCache.Value.Points;

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

        // One snapshot buffer reused every frame: the AoE gates of a single rotation pass call into
        // it many times, so positions, facing and per-enemy distances are read and computed once.
        private static readonly EnemySnapshot EnemyBuffer = new EnemySnapshot();
        private static readonly FrameCachedObject<EnemySnapshot> EnemyCache =
            new FrameCachedObject<EnemySnapshot>(RefreshEnemies);

        // Shared by the enemy scan and interrupt targeting, both of which run every pass.
        private static readonly List<HeroCharacter> NpcBuffer = new List<HeroCharacter>();
        private static readonly FrameCachedObject<List<HeroCharacter>> NpcCache =
            new FrameCachedObject<List<HeroCharacter>>(RefreshNpcs);

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

                    var enemies = EnemyCache.Value;
                    if (enemies.Target != null)
                    {
                        AoeDpsTarget = enemies.Target;
                        AoeDpsPoint = enemies.TargetPoint;
                        AoeDpsCount = CountNearTarget(enemies, Distance.MeleeAoE);
                        ShouldAoe = AoeDpsCount >= AoedpsCountNeeded;
                    }

                    AoePeanutButterCount = CountNearPlayer(enemies, Distance.MeleeAoE);
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

        /// <summary>All NPCs in the object manager as <see cref="HeroCharacter"/> (source list for the
        /// enemy scan and interrupt targeting). Rebuilt at most once per game frame; don't hold the
        /// list across frames.</summary>
        public static IReadOnlyList<HeroCharacter> GetHeroCharacters()
        {
            return NpcCache.Value;
        }

        private static List<HeroCharacter> RefreshNpcs()
        {
            NpcBuffer.Clear();
            foreach (var npc in HeroObjectManager.GetObjectsOfType<HeroNPC>())
                NpcBuffer.Add(npc);

            return NpcBuffer;
        }

        /// <summary>Refills the shared snapshot: player origin and facing, the living hostile target,
        /// and every engaged enemy with its distances to both and its heading relative to our facing.</summary>
        private static EnemySnapshot RefreshEnemies()
        {
            var snapshot = EnemyBuffer;
            snapshot.Units.Clear();
            snapshot.Points.Clear();
            snapshot.Geometry.Clear();
            snapshot.Target = null;
            snapshot.TargetPoint = Vector3.Zero;
            snapshot.Origin = Vector3.Zero;
            snapshot.Forward = Vector3.Zero;

            var me = Core.Player;
            if (me == null)
                return snapshot;

            snapshot.Origin = me.Location;
            snapshot.Forward = me.Forward;
            var target = me.Target;
            if (target != null && target.IsHostile && !target.IsDead)
            {
                snapshot.Target = target;
                snapshot.TargetPoint = target.Location;
            }

            foreach (var character in GetHeroCharacters())
            {
                if (!character.IsValidTarget() || !IsEngagedWithParty(character))
                    continue;

                var point = character.Location;
                snapshot.Units.Add(character);
                snapshot.Points.Add(point);
                snapshot.Geometry.Add(new EnemyGeometry
                {
                    PlayerDistanceSqr = point.DistanceSqr(snapshot.Origin),
                    TargetDistanceSqr = snapshot.Target != null ? point.DistanceSqr(snapshot.TargetPoint) : float.MaxValue,
                    FacingCos = FacingCos(point, snapshot.Origin, snapshot.Forward)
                });
            }

            return snapshot;
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

        /// <summary>True when enough tracked enemies stand within <paramref name="radius"/> of the
        /// player to justify an AoE centered on the player.</summary>
        public static bool ShouldPointBlankAoe(float radius)
        {
            return CountNearPlayer(EnemyCache.Value, radius) >= AoedpsCountNeeded;
        }

        /// <summary>True when enough tracked enemies stand within <paramref name="radius"/> of the
        /// current hostile target to justify an AoE centered on it (including a ground AoE placed
        /// on it); false without a hostile target.</summary>
        public static bool ShouldTargetAoe(float radius)
        {
            var enemies = EnemyCache.Value;
            return enemies.Target != null && CountNearTarget(enemies, radius) >= AoedpsCountNeeded;
        }

        /// <summary>True when enough tracked enemies stand inside a cone in front of the player to
        /// justify a frontal-cone AoE: within <paramref name="range"/> of the player and within
        /// half of <paramref name="arcDegrees"/> (the full cone width) of the player's facing.</summary>
        public static bool ShouldFrontalConeAoe(float range, float arcDegrees)
        {
            var enemies = EnemyCache.Value;
            if (enemies.Forward == Vector3.Zero)
                return false;

            var maxDistanceSqr = range * range;
            var minCos = HalfArcCos(arcDegrees);
            var inCone = 0;
            foreach (var enemy in enemies.Geometry)
            {
                if (enemy.PlayerDistanceSqr <= maxDistanceSqr && enemy.FacingCos >= minCos)
                    inCone++;
            }

            return inCone >= AoedpsCountNeeded;
        }

        /// <summary>True when <paramref name="unit"/> stands inside a cone in front of the player:
        /// within <paramref name="range"/> of the player and within half of
        /// <paramref name="arcDegrees"/> (the full cone width) of the player's facing. False for a
        /// null unit.</summary>
        public static bool InFrontalCone(HeroCharacter unit, float range, float arcDegrees)
        {
            if (unit == null)
                return false;

            var enemies = EnemyCache.Value;
            if (enemies.Forward == Vector3.Zero)
                return false;

            var point = unit.Location;
            return point.DistanceSqr(enemies.Origin) <= range * range &&
                   FacingCos(point, enemies.Origin, enemies.Forward) >= HalfArcCos(arcDegrees);
        }

        private static int CountNearPlayer(EnemySnapshot enemies, float radius)
        {
            var maxDistanceSqr = radius * radius;
            var count = 0;
            foreach (var enemy in enemies.Geometry)
            {
                if (enemy.PlayerDistanceSqr <= maxDistanceSqr)
                    count++;
            }

            return count;
        }

        private static int CountNearTarget(EnemySnapshot enemies, float radius)
        {
            var maxDistanceSqr = radius * radius;
            var count = 0;
            foreach (var enemy in enemies.Geometry)
            {
                if (enemy.TargetDistanceSqr <= maxDistanceSqr)
                    count++;
            }

            return count;
        }

        private static float HalfArcCos(float arcDegrees)
        {
            return MathF.Cos(arcDegrees * 0.5f * MathF.PI / 180f);
        }

        // Facing is yaw-only, so compare headings in the ground plane; a point on top of us has no
        // heading and is inside every cone.
        private static float FacingCos(Vector3 point, Vector3 origin, Vector3 forward)
        {
            var planar = new Vector3(point.X - origin.X, 0f, point.Z - origin.Z);
            var length = planar.Length();
            return length < 0.0001f ? 1f : Vector3.Dot(planar, forward) / length;
        }

        private static int PointsAroundPoint(Vector3 pt, IReadOnlyList<Vector3> points, float dist)
        {
            var maxDistance = dist * dist;
            var count = 0;
            for (var i = 0; i < points.Count; i++)
            {
                if (points[i].DistanceSqr(pt) <= maxDistance)
                    count++;
            }

            return count;
        }

        /// <summary>Per-frame view of the engaged enemies; <see cref="Points"/> and
        /// <see cref="Geometry"/> run parallel to <see cref="Units"/>.</summary>
        private sealed class EnemySnapshot
        {
            public readonly List<HeroCharacter> Units = new List<HeroCharacter>();
            public readonly List<Vector3> Points = new List<Vector3>();
            public readonly List<EnemyGeometry> Geometry = new List<EnemyGeometry>();
            /// <summary>Living hostile current target, or null.</summary>
            public HeroCharacter Target;
            public Vector3 TargetPoint;
            public Vector3 Origin;
            /// <summary>Player facing in the ground plane; <see cref="Vector3.Zero"/> when unreadable.</summary>
            public Vector3 Forward;
        }

        private struct EnemyGeometry
        {
            public float PlayerDistanceSqr;
            /// <summary><see cref="float.MaxValue"/> without a hostile target.</summary>
            public float TargetDistanceSqr;
            /// <summary>Cosine of the planar angle between our facing and the enemy (1 when on top of us).</summary>
            public float FacingCos;
        }
    }
}
