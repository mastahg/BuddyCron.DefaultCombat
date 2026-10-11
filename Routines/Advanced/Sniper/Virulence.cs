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
    ///     7.x Sniper Virulence (ranged DoT DPS) rotation, fought from cover: keeps both
    ///     Corrosive DoTs up and channels Cull while they tick.
    /// </summary>
    public class Virulence : RotationBase
    {
        // Orbital Strike and Suppressive Fire: ground AoE, 8 m radius, 30 m max range.
        private const float GroundAoeRadius = 0.8f;
        private const float GroundAoeRange = 3.0f;
        // Corrosive Grenade and Fragmentation Grenade splash 5 m around the target.
        private const float GrenadeSplashRadius = 0.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Virulence;

        public override string Name => "Sniper Virulence";

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

                    //Offensive -- Laze Target is the autocrit cooldown; every Sniper discipline keeps it
                    Spell.Cast("Laze Target", ret => Core.Player.Target.StrongOrGreater()),
                    //Ability tree choice (lvl 47) -- silently skipped when not chosen
                    Spell.Cast("Target Acquired", ret => Core.Player.Target.StrongOrGreater()),

                    Spell.Buff("Entrench", ret => Core.Player.Target.StrongOrGreater() && Core.Player.IsInCover()),
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

                    //Cover -- Lethal Shot/Snipe/Series of Shots require it and it is our energy regen
                    Spell.Buff("Crouch", ret => !Core.Player.IsInCover() && !Core.Player.IsMoving),

                    //Interrupt
                    Spell.Cast("Distraction", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Rotation -- dots first, they are the whole spec
                    Spell.DoT("Corrosive Dart", "Corrosive Dart"),
                    Spell.DoT("Corrosive Grenade", "Corrosive Grenade"),
                    //Weakening Blast makes every dot tick hit harder -- pair it with Cull
                    Spell.Cast("Weakening Blast",
                        ret => Core.Player.Target.HasMyDebuff("Corrosive Dart") || Core.Player.Target.HasMyDebuff("Corrosive Grenade")),
                    //Cull only pays off while the dots are actually ticking. Corrosive Grenade is a later
                    //pickup, so do not let its absence lock Cull out on a low level character.
                    Spell.Cast("Cull",
                        ret => Core.Player.Target.DebuffTimeLeft("Corrosive Dart") > 3
                               && (Core.Player.Target.DebuffTimeLeft("Corrosive Grenade") > 3 || Core.Player.Level < 40)),
                    Spell.Cast("Takedown", ret => Core.Player.Target.HealthPercent <= 30 || Core.Player.HasBuff("Lethal Takedown")),

                    //Low Energy -- Series of Shots is energy positive, use it before falling to Rifle Shot
                    Spell.Cast("Series of Shots", ret => Core.Player.EnergyPercent < 65),
                    Spell.Cast("Rifle Shot", ret => Core.Player.EnergyPercent < 50),

                    //Fillers -- Lethal Shot replaces Snipe once it is trained
                    Spell.Cast("Lethal Shot"),
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
                        //Corrosive Grenade spreads Corrosive Dart, so land the dart first
                        Spell.DoT("Corrosive Dart", "Corrosive Dart"),
                        //Corrosive Grenade only reaches 5 m around the target.
                        Spell.DoT("Corrosive Grenade", "Corrosive Grenade", 0, ret => Targeting.ShouldTargetAoe(GrenadeSplashRadius)),
                        //Ability tree choice (lvl 68) for Virulence -- skipped when not chosen
                        //Orbital Strike is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Orbital Strike", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 60 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius)),
                        //Fragmentation Grenade only splashes 5 m around the target.
                        Spell.Cast("Fragmentation Grenade", ret => Core.Player.EnergyPercent > 50 && Targeting.ShouldTargetAoe(GrenadeSplashRadius)),
                        //Suppressive Fire is an 8 m ground circle at the target, cast from up to 30 m.
                        Spell.CastOnGround("Suppressive Fire", ret => Core.Player.IsInCover() && Core.Player.EnergyPercent > 30 && Core.Player.Target.Distance <= GroundAoeRange && Targeting.ShouldTargetAoe(GroundAoeRadius))
                        ));
            }
        }
    }
}
