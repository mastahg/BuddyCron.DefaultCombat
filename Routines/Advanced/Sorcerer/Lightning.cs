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


    public class Lightning : RotationBase
    {
        public override CharacterDiscipline Discipline => CharacterDiscipline.Lightning;

        public override string Name => "Sorcerer Lightning";

        public override Composite Buffs => new PrioritySelector(
            Spell.Buff("Mark of Power")
        );

        public override Composite Cooldowns
        {
            get
            {
                return new PrioritySelector(

                    Spell.Buff("Unbreakable Will", ret => Core.Player.IsStunned),


                    Spell.Buff("Force Barrier", ret => Core.Player.HealthPercent <= 15),
                    Spell.Buff("Unnatural Preservation", ret => Core.Player.HealthPercent <= 60),
                    Spell.HoT("Static Barrier", on => Core.Player, 100, ret => Core.Player.InCombat && !Core.Player.HasDebuff("Deionized")),


                    Spell.Buff("Consuming Darkness", ret => Core.Player.ForcePercent <= 25 && !Core.Player.HasDebuff("Weary")),


                    Spell.Cast("Polarity Shift",
                        ret => Core.Player.InCombat && Core.Player.Target != null && Core.Player.Target.StrongOrGreater() &&
                               (!Core.Player.Target.HasMyDebuff("Affliction") || Core.Player.HasBuff("Force Flash"))),
                    Spell.Cast("Recklessness",
                        ret => Core.Player.HasBuff("Force Flash") || !AbilityManager.HasAbility("Lightning Flash")),
                    Spell.Buff("Force Speed",
                        ret => Core.Player.HasBuff("Force Flash") || !AbilityManager.HasAbility("Lightning Flash")),
                    Spell.Buff("Unlimited Power", ret => CombatHotkeys.EnableRaidBuffs),


                    Spell.Buff("Unity", ret => Core.Player.Companion != null && Core.Player.HealthPercent <= 15)
                    );
            }
        }

        public override Composite SingleTarget
        {
            get
            {
                return new PrioritySelector(

                    CombatMovement.CloseDistance(Distance.Ranged),


                    RotationRuntime.HeroicMoment,


                    Spell.Cast("Jolt", ret => Core.Player.Target.IsCasting && CombatHotkeys.EnableInterrupts),


                    Spell.DoT("Affliction", "Affliction"),


                    Spell.Cast("Thundering Blast"),
                    Spell.Cast("Lightning Flash"),
                    Spell.Cast("Crushing Darkness", ret => Core.Player.HasBuff("Force Flash") || Core.Player.Level < 50),
                    Spell.Cast("Shock", ret => Core.Player.Target.HasMyDebuff("Crushed (Crushing Darkness)") || Core.Player.Level < 26),
                    Spell.Cast("Chain Lightning", ret => Core.Player.HasBuff("Lightning Storm")),


                    Spell.Cast("Halted Offensive", ret => Core.Player.HasBuff("Lightning Storm")),

                    Spell.Cast("Volt Rush", ret => Core.Player.IsMoving),


                    Spell.Cast("Lightning Bolt"),
                    Spell.Cast("Lightning Strike"),
                    Spell.Cast("Saber Strike", ret => Core.Player.ForcePercent <= 30)
                    );
            }
        }

        public override Composite AreaOfEffect
        {
            get
            {
                return new Decorator(ret => Targeting.ShouldAoe,
                    new PrioritySelector(
                        Spell.Cast("Chain Lightning", ret => Core.Player.HasBuff("Lightning Storm")),
                        Spell.Cast("Halted Offensive", ret => Core.Player.HasBuff("Lightning Storm")),
                        Spell.DoT("Affliction", "Affliction"),
                        Spell.Cast("Chain Lightning"),
                        Spell.Cast("Halted Offensive"),
                        Spell.CastOnGround("Force Storm")
                        ));
            }
        }
    }
}
