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
    ///     Juggernaut Rage (burst melee dps) rotation: Raging Burst on cooldown with the
    ///     Shockwave / Dominate procs, Furious Strike right behind it.
    /// </summary>
    public class Rage : RotationBase
    {
        // Smash is a self-cast 5.1 m sphere around us (abl.sith_warrior.smash).
        private const float SmashRadius = 0.5f;

        public override CharacterDiscipline Discipline => CharacterDiscipline.Rage;

        public override string Name => "Juggernaut Rage";

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
                    Spell.Buff("Saber Reflect", ret => Core.Player.HealthPercent <= 90),
                    Spell.Buff("Saber Ward", ret => Core.Player.HealthPercent <= 70),
                    Spell.Buff("Enraged Defense", ret => Core.Player.HealthPercent < 70),
                    Spell.Buff("Endure Pain", ret => Core.Player.HealthPercent <= 30),

                    //Enrage is an offensive cooldown for Rage: 6 Rage up front, +1/sec, and it grants
                    //Shockwave (next Smash/Raging Burst is free and hits 15% harder).
                    Spell.Cast("Enrage", ret => !Core.Player.HasBuff("Shockwave") || Core.Player.ActionPoints <= 4),
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

                    //Build Dominate and Shockwave before spending the Raging Burst window. If neither
                    //proc provider is learned yet, use the low-level burst without guessing a level.
                    Spell.Cast("Obliterate", ret => !Core.Player.HasBuff("Dominate")),
                    Spell.Cast("Force Crush", ret => !Core.Player.HasBuff("Shockwave")),
                    Spell.Cast("Raging Burst",
                        ret => Core.Player.HasBuff("Shockwave") || Core.Player.HasBuff("Dominate") ||
                               (!AbilityManager.HasAbility("Obliterate") &&
                                !AbilityManager.HasAbility("Force Crush"))),
                    Spell.Cast("Furious Strike", ret => Core.Player.ActionPoints >= 5 || Core.Player.HasBuff("Fuming Rage")),

                    //Execute
                    Spell.Cast("Vicious Throw", ret => Core.Player.Target.HealthPercent <= 30),

                    //Smash is the low-level stand-in before Raging Burst is trained, and is still worth
                    //pressing when a pack is stacked around us. It is a 5 m sphere around us, not the target.
                    Spell.Cast("Smash",
                        ret => Core.Player.Target.Distance <= SmashRadius &&
                               (!AbilityManager.HasAbility("Raging Burst") || Targeting.ShouldPointBlankAoe(SmashRadius))),

                    Spell.Cast("Force Scream"),
                    Spell.Cast("Ravage"),

                    //Fillers
                    Spell.Cast("Retaliation"),
                    Spell.Cast("Vicious Slash", ret => Core.Player.ActionPoints >= 9),
                    Spell.Cast("Sundering Assault", ret => Core.Player.ActionPoints <= 7),
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
                        //Smash is a 5 m sphere around us, not the target.
                        Spell.Cast("Smash", ret => Targeting.ShouldPointBlankAoe(SmashRadius)),
                        Spell.Cast("Obliterate"),
                        Spell.Cast("Force Crush"),
                        Spell.Cast("Furious Strike", ret => Core.Player.ActionPoints >= 5 || Core.Player.HasBuff("Fuming Rage")),
                        //Sweeping Slash is a 5 m, 179-degree forward cone.
                        Spell.Cast("Sweeping Slash", ret => Core.Player.ActionPoints >= 6 && Targeting.ShouldFrontalConeAoe(0.5f, 179f)),
                        Spell.Cast("Saber Throw")
                        ));
            }
        }
    }
}
