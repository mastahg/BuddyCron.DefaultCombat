// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using BuddyCron;
using BuddyCron.Behaviors;
using BuddyCron.Helpers;
using BuddyCron.Managers;
using BuddyCron.Navigation;
using BuddyCron.Objects;
using DefaultCombat.Behaviors;
using Reborn.Utilities;
using Reborn.Behaviors.Treesharp;
using DefaultCombat.Helpers;
using Targeting = DefaultCombat.Behaviors.Targeting;

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     Operative Lethality (DoT melee dps) rotation: stealth Lethal Strike opener, keeps the
    ///     Corrosive DoTs at full uptime and spends Tactical Advantage on Corrosive Assault.
    /// </summary>
    public class Lethality : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Lethality;

        public override string Name => "Operative Lethality";

        /// <summary>
        ///     While stealthed with Lethal Strike available we hold everything else so the opener
        ///     is always the stealth Lethal Strike (grants Tactical Advantage + Augmented Toxins).
        ///     If Lethal Strike is on cooldown we drop the hold rather than stall out of stealth.
        /// </summary>
        private bool HoldForOpener => Core.Player.IsStealthed && Core.Player.Target != null &&
                                             AbilityManager.CanCast("Lethal Strike", Core.Player.Target).Success;

        public override Composite Buffs
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Coordination"),
					Spell.Buff("Stealth", ret => !Rest.KeepResting() && !RotationRuntime.MovementDisabled && !Core.Player.IsMounted)
                    );
            }
        }

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Escape", ret => Core.Player.IsStunned),

                    //Raid buff - costs 1 Tactical Advantage
                    Spell.Buff("Tactical Superiority", ret => CombatHotkeys.EnableRaidBuffs && Core.Player.HasBuff("Tactical Advantage")),

                    //Energy / Tactical Advantage economy
                    Spell.Cast("Adrenaline Probe", ret => Core.Player.EnergyPercent <= 45),
                    Spell.Cast("Stim Boost", ret => Core.Player.InCombat && Core.Player.BuffCount("Tactical Advantage") < 2),

                    //Ability tree choice - resets cooldowns, save it for real fights
                    Spell.Buff("Tactical Overdrive",
                        ret => Core.Player.InCombat && Core.Player.Target != null && Core.Player.Target.StrongOrGreater()),

                    //Defensives
                    Spell.Buff("Shield Probe", ret => Core.Player.HealthPercent <= 80),
                    Spell.Buff("Evasion", ret => Core.Player.HealthPercent <= 50),

                    //Off-heals (Operatives keep these in every spec)
                    Spell.Cast("Kolto Infusion", on => Core.Player, ret => Core.Player.HealthPercent <= 40 && Core.Player.HasBuff("Tactical Advantage")),
                    Spell.HoT("Kolto Probe", on => Core.Player, 60),

                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Holotraverse", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance > .4f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Energy floor - free filler while Adrenaline Probe is down
                    Spell.Cast("Rifle Shot",
                        ret => Core.Player.EnergyPercent < 35 && !AbilityManager.CanCast("Adrenaline Probe", Core.Player).Success),

                    //Guarantee the stealth opener; the normal on-cooldown use sits after the DoTs.
                    Spell.Cast("Lethal Strike", ret => Core.Player.IsStealthed),

                    //DoT upkeep - 100% uptime is the whole spec. Kept above Toxic Blast / Corrosive
                    //Assault so those never need a debuff-name gate that could stall the priority.
                    Spell.Cast("Corrosive Dart",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Corrosive Dart") || Core.Player.Target.DebuffTimeLeft("Corrosive Dart") <= 3)),
                    Spell.Cast("Corrosive Grenade",
                        ret => !HoldForOpener &&
                               (!Core.Player.Target.HasMyDebuff("Corrosive Grenade") || Core.Player.Target.DebuffTimeLeft("Corrosive Grenade") <= 3)),

                    //Open the poison amplification window, use Lethal Strike on cooldown, then pair
                    //Toxic Haze and Corrosive Assault with that window.
                    Spell.Cast("Toxic Blast", ret => !HoldForOpener),
                    Spell.Cast("Lethal Strike", ret => !HoldForOpener),
                    Spell.Cast("Toxic Haze", on => Core.Player,
                        ret => !HoldForOpener && Core.Player.HasBuff("Tactical Advantage")),
                    Spell.Cast("Corrosive Assault", ret => !HoldForOpener && Core.Player.HasBuff("Tactical Advantage")),

                    //Build Tactical Advantage
                    Spell.Cast("Shiv", ret => !HoldForOpener && Core.Player.BuffCount("Tactical Advantage") < 2),

                    //Cheap filler
                    Spell.Cast("Overload Shot", ret => !HoldForOpener && Core.Player.EnergyPercent >= 85 && !Core.Player.HasBuff("Tactical Advantage")),

                    //Never stall
                    Spell.Cast("Rifle Shot", ret => !HoldForOpener)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe && !HoldForOpener,
                    new PrioritySelector(
                        Spell.DoT("Corrosive Grenade", "Corrosive Grenade"),
                        Spell.Cast("Toxic Haze", on => Core.Player,
                            ret => Core.Player.HasBuff("Tactical Advantage")),
                        Spell.Cast("Noxious Knives"),
                        Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent >= 60)
                        ));
            }
        }
    }
}
