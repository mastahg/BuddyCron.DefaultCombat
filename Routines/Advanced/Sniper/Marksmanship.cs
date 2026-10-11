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

namespace DefaultCombat.Routines
{
    /// <summary>
    ///     7.x Sniper Marksmanship (ranged burst DPS) rotation, fought from cover: Followthrough
    ///     and Penetrating Blasts on cooldown, Ambush on Zeroing Shots stacks.
    /// </summary>
    public class Marksmanship : RotationBase
    {
        // Orbital Strike and Suppressive Fire: ground AoE, 8 m radius, 30 m max range.
        private const float GroundAoeRadius = 0.8f;
        private const float GroundAoeRange = 3.0f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Marksmanship;

        public override string Name => "Sniper Marksmanship";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Coordination")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    //Survival
                    Spell.Buff("Escape", ret => Core.Player.IsStunned),
                    Spell.Buff("Shield Probe", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Evasion", ret => Core.Player.HealthPercent <= 40),
                    Spell.Buff("Entrench", ret => Core.Player.IsInCover() && Core.Player.HealthPercent <= 60),
                    //Ability tree choice (lvl 68) -- silently skipped when not chosen
                    Spell.Buff("Ballistic Shield", ret => Core.Player.HealthPercent <= 50),
                    //Resets defensive cooldowns when things go bad
                    Spell.Buff("Meticulous Preparation", ret => Core.Player.HealthPercent <= 25),

                    //Energy
                    Spell.Cast("Adrenaline Probe", ret => Core.Player.EnergyPercent <= 45),
                    Spell.Cast("Sniper Volley", ret => Core.Player.EnergyPercent <= 65),

                    //Offensive
                    Spell.Buff("Entrench", ret => Core.Player.Target.StrongOrGreater() && Core.Player.IsInCover()),
                    Spell.Cast("Laze Target"),
                    //Ability tree choice (lvl 43) -- silently skipped when not chosen
                    Spell.Cast("Target Acquired", ret => Core.Player.Target.StrongOrGreater()),

                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    //Movement
                    CombatMovement.CloseDistance(Distance.Ranged),


                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Cover -- Snipe/Ambush/Followthrough/Penetrating Blasts all require it, and it is our energy regen
                    Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Low Energy -- keep at/above the 45-50% high regen band. Followthrough is cheap and
                    //its proc window is short, so it is the one thing we still fire while regenerating.
                    new Decorator(ret => Core.Player.EnergyPercent < 50,
                        new PrioritySelector(
                            Spell.Cast("Followthrough"),
                            Spell.Cast("Rifle Shot")
                            )),

                    //Consume any existing Followthrough window, then use Ambush before the channel.
                    //The selector returns to Followthrough after either qualifying attack enables it.
                    Spell.Cast("Followthrough"),
                    //Zeroing Shots is a Marksmanship passive; low level chars simply hard cast Ambush.
                    Spell.Cast("Ambush", ret => Core.Player.BuffCount("Zeroing Shots") >= 2 || Core.Player.Level < 30),
                    Spell.Cast("Penetrating Blasts"),
                    Spell.Cast("Takedown", ret => Core.Player.Target.HealthPercent <= 30),
                    //Filler dot -- only worth the energy on things that live long enough
                    Spell.DoT("Corrosive Dart", "Corrosive Dart", 0,
                        ret => Core.Player.Target.StrongOrGreater() && Core.Player.EnergyPercent > 60),
                    Spell.Cast("Snipe"),

                    //Never stall
                    Spell.Cast("Rifle Shot")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),
                        //Ability tree choice (lvl 68) for Marksmanship -- skipped when not chosen
                        //Orbital Strike is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Orbital Strike", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 60 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Fragmentation Grenade only splashes 5 m around the target.
                        Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent > 50 && Targeting.ShouldTargetAoe(0.5f)),
                        //Suppressive Fire is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Suppressive Fire", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 30 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
