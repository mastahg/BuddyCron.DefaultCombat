// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using System.Linq;
using BuddyCron.Behaviors;
using BuddyCron.Helpers;
using BuddyCron.Managers;
using BuddyCron.Navigation;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using Reborn.Utilities;
using Reborn.Behaviors.Treesharp;
using DefaultCombat.Helpers;

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     7.x Sorcerer Lightning (ranged burst DPS) rotation, built around the Affliction
    ///     auto-crit setup for Thundering Blast and the Lightning Storm / Force Flash procs.
    /// </summary>
    internal sealed class LightningPvE : RotationEngineBase
    {
        private const float OverloadRange = 1.5f;
        private const double AfflictionMinimumTtd = 8.0;
        private const double AfflictionSetupMinimumTtd = 5.0;
        private const int MaximumAfflictionTargets = 2;
        private const double CrushingDarknessMinimumTtd = 10.0;
        private const double CrushingDarknessStandardMinimumTtd = 14.0;
        private const double ThunderingBlastMinimumTtd = 2.5;
        private const double LightningFlashMinimumTtd = 2.0;
        private const double BurstSetupMinimumTtd = 5.0;
        private const double BurstChargesMinimumTtd = 6.0;
        private const double PolarityShiftMinimumTtd = 12.0;
        private const double UnlimitedPowerMinimumTtd = 20.0;
        private const double ShockExecuteTtd = 2.0;
        private const float ShockExecuteHealthPercent = 20.0f;
        private const double ForceStormMinimumRemainingTtd = 1.5;
        private const float StaticBarrierEmergencyHealthPercent = 75.0f;
        private const float StaticBarrierReflectHealthPercent = 90.0f;
        private const float ShockMinimumForcePercent = 35.0f;

        private static System.DateTime _lastAoeStateLogUtc = System.DateTime.MinValue;
        private static string _lastCapabilitySignature = string.Empty;
        private static ulong _burstSetupTargetId;

        private static readonly MultiDotProfile s_afflictionProfile = new MultiDotProfile
        {
            Key = "Lightning.Affliction",
            AbilityName = "Affliction",
            DebuffNames = new[] { "Affliction" },
            DebuffAbilitySpecIds = new[] { 0xE000892C91A4F3EAUL },
            MaxTargets = MaximumAfflictionTargets,
            ExpectedDurationSeconds = 18,
            RefreshWindowSeconds = 1.5,
            TargetSelectionDwellSeconds = 0.4,
            PostCastDwellSeconds = 0.9,
            TransactionTimeoutSeconds = 3,
            Enabled = () => HasAffliction,
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableAfflictionTarget,
            MinimumTtdSeconds = (target, selected) =>
                selected && HasThunderingBlast
                    ? AfflictionSetupMinimumTtd
                    : AfflictionMinimumTtd
        };

        private static readonly MultiDotCoordinator s_afflictionCoordinator =
            new MultiDotCoordinator(s_afflictionProfile);

        private static readonly MultiDotProfile s_crushingDarknessProfile = new MultiDotProfile
        {
            Key = "Lightning.CrushingDarkness",
            AbilityName = "Crushing Darkness",
            DebuffNames = new[] { "Crushing Darkness", "Crushed (Crushing Darkness)" },
            MaxTargets = 1,
            ExpectedDurationSeconds = 6,
            RefreshWindowSeconds = 0.5,
            Enabled = () => HasCrushingDarkness,
            CandidateProvider = () => Targeting.Enemies,
            CandidateFilter = IsUsableAfflictionTarget
        };

        private static readonly MultiDotCoordinator s_crushingDarknessCoordinator =
            new MultiDotCoordinator(s_crushingDarknessProfile);

        private static readonly SmartAoeProfile s_chainLightningProfile = new SmartAoeProfile
        {
            Key = "Lightning.ChainLightning",
            AbilityName = "Chain Lightning",
            Radius = Distance.MeleeAoE,
            MaxTargets = 8,
            TargetSelectionDwellSeconds = 0.4,
            PostCastDwellSeconds = 0.9,
            TransactionTimeoutSeconds = 3,
            Enabled = () => HasChainLightning,
            CandidateProvider = Targeting.GetHeroCharacters,
            CandidateFilter = IsUsableChainLightningTarget,
            SplashHazard = enemy => enemy != null && enemy.IsEffectivePvEHostile() && !enemy.IsDead &&
                                    enemy.IsEngagedWithPlayer() && enemy.IsCrowdControlled()
        };

        private static readonly SmartAoeCoordinator s_chainLightningCoordinator =
            new SmartAoeCoordinator(s_chainLightningProfile);

        public LightningPvE()
        {
            s_afflictionCoordinator.Reset();
            s_crushingDarknessCoordinator.Reset();
            s_chainLightningCoordinator.Reset();
        }

        private static HeroCharacter CurrentTarget => Core.Player.Target;

        private static bool IsSoloContent => Core.Player.GroupId == 0;

        private static bool HasAffliction => AbilityManager.HasAbility("Affliction");

        private static bool HasLightningFlash => AbilityManager.HasAbility("Lightning Flash");

        private static bool HasLightningBolt => AbilityManager.HasAbility("Lightning Bolt");

        private static bool HasLightningStrike => AbilityManager.HasAbility("Lightning Strike");

        private static bool HasForceLightning => AbilityManager.HasAbility("Force Lightning");

        private static bool HasForceStorm => AbilityManager.HasAbility("Force Storm");

        private static bool HasChainLightning => AbilityManager.HasAbility("Chain Lightning");

        private static bool HasHaltedOffensive => AbilityManager.HasAbility("Halted Offensive");

        private static bool HasCrushingDarkness => AbilityManager.HasAbility("Crushing Darkness");

        private static bool HasVoltRush => AbilityManager.HasAbility("Volt Rush");

        private static bool HasThunderingBlast => AbilityManager.HasAbility("Thundering Blast");

        private static bool HasLightningBarrier => AbilityManager.HasAbility("Lightning Barrier");

        private static bool HasLightningStormConsumer =>
            HasChainLightning || HasHaltedOffensive;

        private static bool HasBossImmunity(HeroCharacter enemy) =>
            enemy != null &&
            (enemy.HasBuff("Boss Immunity") || enemy.HasDebuff("Boss Immunity"));

        //Boss1 is the gold Elite tier and is normally a valid knockback/stun target. Boss2+
        //maps to Champion and higher tiers, which we reserve from automatic crowd control.
        private static bool IsCrowdControlProtected(HeroCharacter enemy) =>
            enemy == null ||
            enemy.Toughness == cbtToughnessEnum.boss_2 ||
            enemy.Toughness == cbtToughnessEnum.boss_3 ||
            enemy.Toughness == cbtToughnessEnum.boss_4 ||
            enemy.Toughness == cbtToughnessEnum.boss_raid ||
            enemy.Toughness == cbtToughnessEnum.player ||
            HasBossImmunity(enemy);

        private static bool IsBossTarget =>
            CurrentTarget != null && CurrentTarget.IsEngagedWithPlayer() &&
            (CurrentTarget.BossOrGreater() || HasBossImmunity(CurrentTarget));

        private static bool HasMeaningfulAggro =>
            Core.Player.Attackers.Any(enemy => enemy != null && !enemy.IsDead);

        private static bool HasDangerousPlayerAggro =>
            Core.Player.Attackers.Count(enemy => enemy != null && !enemy.IsDead) >= 2 ||
            Core.Player.Attackers.Any(enemy =>
                enemy != null && !enemy.IsDead && enemy.StrongOrGreater());

        private static bool HasPlayerAggro(HeroCharacter enemy) =>
            enemy != null &&
            Core.Player.Attackers.Any(attacker =>
                attacker != null && !attacker.IsDead && attacker.NodeId == enemy.NodeId);

        private static bool ImmediateOverloadTarget =>
            Targeting.Enemies.Any(enemy =>
                enemy.Distance <= OverloadRange &&
                HasPlayerAggro(enemy) &&
                enemy.StrongOrGreater() &&
                !IsCrowdControlProtected(enemy));

        private static bool MultipleNormalMeleeEnemies =>
            Targeting.Enemies.Count(enemy =>
                enemy.Distance <= OverloadRange &&
                HasPlayerAggro(enemy) &&
                !enemy.StrongOrGreater()) >= RoutineSettings.Instance.NormalOverloadEnemyCount;

        private static bool ShouldUseOverload =>
            IsSoloContent && (ImmediateOverloadTarget || MultipleNormalMeleeEnemies);

        private static bool HasDurableTarget =>
            CurrentTarget != null && CurrentTarget.IsEngagedWithPlayer() &&
            CurrentTarget.StrongOrGreater();

        private static bool CurrentTargetLivesFor(double seconds) =>
            CurrentTarget != null && TimeToDie.WillLiveFor(CurrentTarget, seconds);

        private static bool PackLivesFor(double seconds) =>
            Core.Player.IsPlayerOrCompanionInCombat() &&
            (CurrentTargetLivesFor(seconds) || TimeToDie.PackWillLiveFor(Targeting.Enemies, seconds));

        private static bool IsCurrentTargetCrushed =>
            CurrentTarget != null &&
            s_crushingDarknessCoordinator.IsMaintained(
                s_crushingDarknessProfile, CurrentTarget);

        private static bool CurrentTargetNearDeath
        {
            get
            {
                if (CurrentTarget == null)
                    return false;
                if (CurrentTarget.HealthPercent <= ShockExecuteHealthPercent)
                    return true;

                var estimate = TimeToDie.Estimate(CurrentTarget);
                return estimate.IsStable && estimate.Seconds <= ShockExecuteTtd;
            }
        }

        private static int ForceStormLivingTargetCount =>
            TimeToDie.CountClusterTargetsLivingFor(
                Targeting.Enemies,
                Targeting.AoeDpsPoint,
                Distance.MeleeAoE,
                ForceStormMinimumRemainingTtd);

        private static bool ForceStormHasEnoughLivingTargets =>
            ForceStormLivingTargetCount >= RoutineSettings.Instance.AoeEnemyCount;

        private static bool AutomaticAoeReady =>
            Core.Player.InCombat &&
            BoostedCombatHotkeys.AoeAllowed &&
            Targeting.AoeDpsTarget != null &&
            Targeting.AoeDpsCount >= RoutineSettings.Instance.AoeEnemyCount;

        private static bool HasMyAffliction(HeroCharacter enemy) =>
            s_afflictionCoordinator.IsMaintained(s_afflictionProfile, enemy);

        private static bool IsUsableAfflictionTarget(HeroCharacter enemy) =>
            enemy != null && enemy.IsEngagedWithPlayer() &&
            enemy.IsEffectivePvEHostile() && !enemy.IsDead;

        private static bool IsUsableChainLightningTarget(HeroCharacter enemy) =>
            enemy != null && enemy.IsEngagedWithPlayer() && enemy.IsEffectivePvEHostile() &&
            enemy.IsTargetable && enemy.InLineOfSight && !enemy.IsDead &&
            !enemy.IsStunned && !enemy.IsCrowdControlled();

        private static RunStatus CastSmartChainLightning(bool allowed, int minimumTargets)
        {
            return allowed && HasChainLightning
                ? s_chainLightningCoordinator.Tick(minimumTargets)
                : RunStatus.Failure;
        }

        private static bool ShouldUsePolarityShift =>
            PackLivesFor(PolarityShiftMinimumTtd) &&
            (Targeting.ShouldAoe ||
             CurrentTarget == null ||
             !HasAffliction ||
             !HasMyAffliction(CurrentTarget) ||
             (HasThunderingBlast && AbilityManager.CanCast("Thundering Blast", CurrentTarget).Success));

        private static bool HasBurstSetupProc =>
            Core.Player.HasBuff("Force Flash");

        private static bool HasSingleTargetRecklessnessSpend
        {
            get
            {
                if (CurrentTarget == null)
                    return false;
                if (HasLightningFlash && !HasBurstSetupProc &&
                    AbilityManager.CanCast("Lightning Flash", CurrentTarget).Success)
                {
                    return false;
                }

                if (HasThunderingBlast)
                {
                    return (!HasAffliction || HasMyAffliction(CurrentTarget)) &&
                           AbilityManager.CanCast("Thundering Blast", CurrentTarget).Success;
                }

                bool crushingDarknessReady =
                    HasCrushingDarkness &&
                    !IsCurrentTargetCrushed &&
                    AbilityManager.CanCast("Crushing Darkness", CurrentTarget).Success;
                if (crushingDarknessReady)
                    return false;

                return HasForceLightning &&
                       AbilityManager.CanCast("Force Lightning", CurrentTarget).Success;
            }
        }

        private static bool ShouldUseBurstCharges =>
            PackLivesFor(BurstChargesMinimumTtd) &&
            ((AutomaticAoeReady && ForceStormHasEnoughLivingTargets) ||
             HasSingleTargetRecklessnessSpend);

        private static bool ShouldPrimeBurstWindow =>
            CurrentTargetLivesFor(BurstSetupMinimumTtd) &&
            HasLightningFlash &&
            CurrentTarget.NodeId != _burstSetupTargetId &&
            (!HasAffliction || HasMyAffliction(CurrentTarget));

        private static bool ShouldUseCrushingDarkness =>
            CurrentTarget != null &&
            TimeToDie.HasUsefulCastsRemaining(CurrentTarget, 2,
                CrushingDarknessMinimumTtd,
                CrushingDarknessStandardMinimumTtd, "Crushing Darkness") &&
            (HasBurstSetupProc || !HasLightningFlash);

        private static RunStatus TickCrushingDarkness()
        {
            return ShouldUseCrushingDarkness
                ? s_crushingDarknessCoordinator.Tick()
                : RunStatus.Failure;
        }

        private static bool ShouldUseStaticBarrier =>
            Core.Player.InCombat &&
            !Core.Player.HasDebuff("Deionized") &&
            (Core.Player.HealthPercent <= StaticBarrierEmergencyHealthPercent ||
             (HasLightningBarrier &&
              HasMeaningfulAggro &&
              Core.Player.HealthPercent <= StaticBarrierReflectHealthPercent));

        private static bool ShouldUseShock =>
            CurrentTarget != null &&
            !Core.Player.HasBuff("Recklessness") &&
            (CurrentTargetNearDeath ||
             (!Core.Player.HasBuff("Polarity Shift") &&
              (IsCurrentTargetCrushed ||
               Core.Player.ForcePercent >= ShockMinimumForcePercent)));

        private static bool ShouldUseForceLightningEarly =>
            CurrentTarget != null &&
            (CurrentTargetNearDeath || Core.Player.HasBuff("Recklessness"));

        private static void RecordBurstSetup()
        {
            if (CurrentTarget != null)
                _burstSetupTargetId = CurrentTarget.NodeId;
        }

        private static RunStatus LogAoeState()
        {
            if ((System.DateTime.UtcNow - _lastAoeStateLogUtc).TotalSeconds >= 3)
            {
                _lastAoeStateLogUtc = System.DateTime.UtcNow;
                Logger.Write("[AOE] Lightning 2026-08-21.4 cluster={0}/{1}, mode={2}, enabled={3}, allowed={4}, forceStormKnown={5}, point={6}",
                    Targeting.AoeDpsCount,
                    RoutineSettings.Instance.AoeEnemyCount,
                    RoutineSettings.Instance.AreaDamage,
                    BoostedCombatHotkeys.EnableAoe,
                    BoostedCombatHotkeys.AoeAllowed,
                    HasForceStorm,
                    Targeting.AoeDpsPoint);
            }

            return RunStatus.Failure;
        }

        private static RunStatus LogAdaptiveCapabilities()
        {
            string signature =
                "Affliction=" + HasAffliction +
                ", ThunderingBlast=" + HasThunderingBlast +
                ", LightningFlash=" + HasLightningFlash +
                ", CrushingDarkness=" + HasCrushingDarkness +
                ", ChainLightning=" + HasChainLightning +
                ", HaltedOffensive=" + HasHaltedOffensive +
                ", ForceStorm=" + HasForceStorm +
                ", LightningBolt=" + HasLightningBolt +
                ", LightningStrike=" + HasLightningStrike +
                ", ForceLightning=" + HasForceLightning +
                ", VoltRush=" + HasVoltRush +
                ", LightningBarrier=" + HasLightningBarrier;

            if (signature != _lastCapabilitySignature)
            {
                _lastCapabilitySignature = signature;
                Logger.Write("[Lightning] Adaptive abilities: {0}", signature);
            }

            return RunStatus.Failure;
        }

        private static Composite AutomaticAoe =>
            new Decorator(ret => Core.Player.InCombat &&
                                 Targeting.AoeDpsTarget != null &&
                                 Targeting.AoeDpsCount >= RoutineSettings.Instance.AoeEnemyCount,
                new PrioritySelector(
                    new Action(ret => LogAoeState()),
                    new Decorator(ret => BoostedCombatHotkeys.AoeAllowed,
                        new PrioritySelector(
                            Spell.CastOnGround("Force Storm", ret => Targeting.AoeDpsPoint,
                                ret => Core.Player.HasBuff("Recklessness") &&
                                       ForceStormHasEnoughLivingTargets),
                            new Action(ret => CastSmartChainLightning(
                                Core.Player.HasBuff("Lightning Storm"),
                                RoutineSettings.Instance.AoeEnemyCount)),
                            new Action(ret => CastSmartChainLightning(
                                true, RoutineSettings.Instance.AoeEnemyCount)),
                            Spell.CastOnGround("Force Storm", ret => Targeting.AoeDpsPoint,
                                ret => ForceStormHasEnoughLivingTargets)
                        ))
                ));

        public override CharacterDiscipline Discipline => CharacterDiscipline.Lightning;

        public override string Name => "Sorcerer Lightning";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Mark of Power"),
            Spell.HoT("Static Barrier", on => Core.Player, 100,
                ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.Defensives) &&
                       !Core.Player.InCombat)
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    //Emergency solo spacing. Never scatter a tank's Flashpoint pull. Cast on the
                    //player so all nearby enemies are considered instead of only the current target.
                    Spell.Cast("Overload", on => Core.Player,
                        ret => Core.Player.InCombat &&
                               RoutineSettings.IsAutomatic(RoutineSettings.Instance.Knockbacks) &&
                               ShouldUseOverload),

                    //If Overload is unavailable while solo, lock down a strong melee attacker.
                    Spell.Cast("Electrocute",
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.CrowdControl) &&
                               Core.Player.InCombat && IsSoloContent && CurrentTarget != null &&
                               CurrentTarget.Distance <= Distance.Melee &&
                               HasPlayerAggro(CurrentTarget) &&
                               CurrentTarget.StrongOrGreater() &&
                               !IsCrowdControlProtected(CurrentTarget)),

                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.Defensives),
                        new PrioritySelector(
                            Spell.Buff("Unbreakable Will", ret => Core.Player.IsStunned),
                            Spell.Buff("Force Barrier",
                                ret => Core.Player.InCombat &&
                                       Core.Player.HealthPercent <= RoutineSettings.Instance.ForceBarrierHealthPercent),
                            Spell.Cast("Cloud Mind", on => Core.Player,
                                ret => Core.Player.InCombat && HasMeaningfulAggro &&
                                       ((Core.Player.GroupId != 0 && HasDangerousPlayerAggro) ||
                                        (Core.Player.HealthPercent <= RoutineSettings.Instance.CloudMindHealthPercent &&
                                         (HasDurableTarget || Targeting.Enemies.Count >= 2)))),
                            Spell.HoT("Static Barrier", on => Core.Player, 100,
                                ret => ShouldUseStaticBarrier))),

                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.SelfHealing),
                        new PrioritySelector(
                            Spell.Buff("Unnatural Preservation",
                                ret => Core.Player.InCombat &&
                                       Core.Player.HealthPercent <= RoutineSettings.Instance.UnnaturalPreservationHealthPercent),
                            Spell.Cast("Dark Heal", on => Core.Player,
                                ret => Core.Player.InCombat &&
                                       Core.Player.HealthPercent <=
                                           System.Math.Min(RoutineSettings.Instance.DarkHealHealthPercent, 30) &&
                                       !Core.Player.IsMoving))),

                    Spell.Cast("Expunge", on => Core.Player,
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.Cleanse) &&
                               Core.Player.NeedsCleanse()),

                    //Force management (Consuming Darkness applies Weary without Force Surge, so only use it starved)
                    Spell.Buff("Consuming Darkness",
                        ret => Core.Player.InCombat &&
                               Core.Player.ForcePercent <= 20 &&
                               !Core.Player.HasDebuff("Weary") &&
                               CurrentTargetLivesFor(4.0)),

                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.OffensiveCooldowns),
                        new PrioritySelector(
                            Spell.Cast("Polarity Shift", ret => ShouldUsePolarityShift),
                            Spell.Cast("Recklessness", ret => ShouldUseBurstCharges))),

                    Spell.Buff("Unlimited Power",
                        ret => BoostedCombatHotkeys.RaidBuffsAllowed && Core.Player.InCombat && IsBossTarget &&
                               PackLivesFor(UnlimitedPowerMinimumTtd)),

                    //Companion
                    Spell.Buff("Unity",
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.CompanionCooldowns) &&
                               Core.Player.InCombat &&
                               Core.Player.Companion != null &&
                               Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new Decorator(
                    ret => Core.Player.IsPlayerOrCompanionInCombat() &&
                           (s_afflictionCoordinator.IsBusy ||
                            s_crushingDarknessCoordinator.IsBusy ||
                            s_chainLightningCoordinator.IsBusy ||
                             (CurrentTarget != null && CurrentTarget.IsEffectivePvEHostile() &&
                              CurrentTarget.IsTargetable && !CurrentTarget.IsDead)),
                    new PrioritySelector(
                    new Action(ret => LogAdaptiveCapabilities()),
                    new Decorator(
                        ret => s_afflictionCoordinator.IsBusy,
                        new Action(ret => s_afflictionCoordinator.Continue())),
                    new Decorator(
                        ret => s_crushingDarknessCoordinator.IsBusy,
                        new Action(ret => s_crushingDarknessCoordinator.Continue())),
                    new Decorator(
                        ret => s_chainLightningCoordinator.IsBusy,
                        new Action(ret => s_chainLightningCoordinator.Continue())),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.HeroicMoment),
                        RotationRuntime.HeroicMoment),

                    //Reserve Jolt for verified high-priority casts. IsCasting alone also becomes
                    //true during some instant activations, which caused the old off-cooldown waste.
                    Spell.Cast("Jolt", ret => BoostedCombatHotkeys.InterruptsAllowed),
                    Spell.Cast("Electrocute",
                        ret => BoostedCombatHotkeys.InterruptsAllowed &&
                               RoutineSettings.IsAutomatic(RoutineSettings.Instance.CrowdControl) &&
                               !IsCrowdControlProtected(CurrentTarget)),

                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.CoreRotation),
                        new Action(ret => s_afflictionCoordinator.Tick())),

                    AutomaticAoe,

                    new Decorator(
                        ret => RoutineSettings.IsAutomatic(RoutineSettings.Instance.CoreRotation),
                        new PrioritySelector(

                    //On the first sustained window for a target, Lightning Flash establishes Force
                    //Flash/Stormwatch before Thundering Blast. The action records only a successful
                    //cast; if the ability is not trained, the normal leveling path continues.
                    new Sequence(
                        Spell.Cast("Lightning Flash", ret => ShouldPrimeBurstWindow),
                        new Action(ret =>
                        {
                            RecordBurstSetup();
                            return RunStatus.Success;
                        })),

                    //Sustained priority. Thundering Blast is only used without Affliction before
                    //Affliction is learned; otherwise the auto-crit setup is mandatory.
                    Spell.Cast("Thundering Blast",
                        ret => CurrentTargetLivesFor(ThunderingBlastMinimumTtd) &&
                               (!HasAffliction || HasMyAffliction(CurrentTarget))),
                    Spell.Cast("Lightning Flash",
                        ret => CurrentTargetLivesFor(LightningFlashMinimumTtd)),
                    new Action(ret => TickCrushingDarkness()),

                    new Action(ret => CastSmartChainLightning(
                        Core.Player.HasBuff("Recklessness") &&
                        Core.Player.HasBuff("Lightning Storm"), 1)),
                    Spell.Cast("Halted Offensive",
                        ret => Core.Player.HasBuff("Recklessness") &&
                               Core.Player.HasBuff("Lightning Storm") &&
                               HasLightningStormConsumer),

                    Spell.Cast("Shock", ret => ShouldUseShock),

                    new Action(ret => CastSmartChainLightning(
                        Core.Player.HasBuff("Lightning Storm"), 1)),
                    Spell.Cast("Halted Offensive",
                        ret => Core.Player.HasBuff("Lightning Storm") && HasLightningStormConsumer),

                    Spell.Cast("Lightning Bolt", ret => Core.Player.HasBuff("Polarity Shift")),
                    Spell.Cast("Lightning Strike", ret => Core.Player.HasBuff("Polarity Shift")),

                    Spell.Cast("Force Lightning", ret => ShouldUseForceLightningEarly),

                    Spell.Cast("Volt Rush", ret => Core.Player.IsMoving),

                    Spell.Cast("Lightning Bolt"),
                    Spell.Cast("Lightning Strike"),
                    Spell.Cast("Force Lightning"),
                    Spell.Cast("Saber Strike", ret => Core.Player.ForcePercent <= 30)
                        ))
                    ));
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return AutomaticAoe;
            }
        }
    }
}
