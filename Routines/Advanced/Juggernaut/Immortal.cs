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
    ///     Juggernaut Immortal (tank) rotation: Crushing Blow and Force Scream lead, with
    ///     Aegis Assault building Rage and keeping its defensive buff up.
    /// </summary>
    public class Immortal : RotationBase
    {
        // Smash is a self-cast 5.1 m sphere around us (abl.sith_warrior.smash).
        private const float SmashRadius = 0.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Immortal;

        public override string Name => "Juggernaut Immortal";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Unnatural Might")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(
                    Spell.Buff("Unleash", ret => Core.Player.IsStunned),
					Spell.Buff("Furious Power", ret => Core.Player.Target.BossOrGreater()),

                    //Defensives, strongest last. Saber Reflect is an ability-tree choice (may be
                    //Mad Dash / Intimidating Roar instead) - it simply gets skipped if untrained.
                    Spell.Buff("Saber Reflect", ret => Core.Player.HealthPercent <= 80),
                    Spell.Buff("Enraged Defense", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Invincible", ret => Core.Player.HealthPercent <= 60),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 45),
                    Spell.Buff("Endure Pain", ret => Core.Player.HealthPercent <= 25),
                    Spell.Cast("Enrage", ret => Core.Player.ActionPoints <= 6),
                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(
                    Spell.Cast("Force Charge", ret => CombatHotkeys.EnableCharge && Core.Player.Target.EdgeDistance >= 1f),
                    Spell.Cast("Saber Throw", ret => !RotationRuntime.MovementDisabled && Core.Player.Target.EdgeDistance > .4f && Core.Player.Target.EdgeDistance <= 3f),

                    //Movement
                    CombatMovement.CloseDistance(Distance.Melee),

                    //Legacy Heroic Moment Abilities --will only be active when user initiates Heroic Moment--
                    RotationRuntime.HeroicMoment,

                    //Interrupts
                    Spell.Cast("Disruption", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),

                    //Rotation - Crushing Blow and Force Scream are the hard hitters (Force Scream also
                    //grants the Sonic Barrier absorb shield). Aegis Assault is the Rage builder and
                    //keeps the damage-reduction/absorb buff up, which also makes Crushing Blow cleave.
                    Spell.Cast("Crushing Blow"),
                    Spell.Cast("Force Scream"),
                    Spell.Cast("Aegis Assault", ret => Core.Player.ActionPoints <= 8 || !Core.Player.HasBuff("Aegis Assault")),
                    Spell.Cast("Ravage"),
                    Spell.Cast("Backhand", ret => !Core.Player.Target.IsStunned),
                    Spell.Cast("Vicious Throw", ret => Core.Player.Target.HealthPercent <= 30),
                    //Smash is a 5 m sphere around us; only worth a GCD when the pack is inside it.
                    Spell.Cast("Smash", ret => Targeting.ShouldPointBlankAoe(SmashRadius)),

                    //Fillers
                    Spell.Cast("Retaliation"),
                    Spell.Cast("Vicious Slash", ret => Core.Player.ActionPoints >= 9),
                    Spell.Cast("Assault")
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldPbaoe,
                    new PrioritySelector(
                        //Aegis Assault enables Crushing Blow's cleave; establish the buff first.
                        Spell.Cast("Aegis Assault", ret => !Core.Player.HasBuff("Aegis Assault")),
                        //With Aegis Assault up, Crushing Blow cleaves a 5 m sphere around the target.
                        Spell.Cast("Crushing Blow", ret => Targeting.ShouldTargetAoe(0.5f)),
                        //Smash is a 5 m sphere around us, not the target.
                        Spell.Cast("Smash", ret => Targeting.ShouldPointBlankAoe(SmashRadius)),
                        Spell.Cast("Force Scream"),
                        Spell.Cast("Aegis Assault", ret => Core.Player.ActionPoints <= 8),
                        Spell.Cast("Retaliation"),
                        //Sweeping Slash is a 5 m, 179-degree forward cone.
                        Spell.Cast("Sweeping Slash", ret => Core.Player.ActionPoints >= 6 && Targeting.ShouldFrontalConeAoe(0.5f, 179f))
                        ));
            }
        }
    }
}
