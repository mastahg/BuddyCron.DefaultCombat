// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Behaviors
{
    /// <summary>Shared PvE handling for important enemy casts, including off-target heals.</summary>
    public static class Interrupts
    {
        private const float MinimumCastSeconds = 0.35f;
        private const float MinimumRemainingSeconds = 0.10f;

        private static readonly Dictionary<CharacterDiscipline, string> s_interruptAbilities =
            new Dictionary<CharacterDiscipline, string>
            {
                { CharacterDiscipline.Darkness, "Jolt" },
                { CharacterDiscipline.Deception, "Jolt" },
                { CharacterDiscipline.Hatred, "Jolt" },
                { CharacterDiscipline.Corruption, "Jolt" },
                { CharacterDiscipline.Lightning, "Jolt" },
                { CharacterDiscipline.Madness, "Jolt" },

                { CharacterDiscipline.Balance, "Mind Snap" },
                { CharacterDiscipline.Seer, "Mind Snap" },
                { CharacterDiscipline.Telekinetics, "Mind Snap" },
                { CharacterDiscipline.Infiltration, "Mind Snap" },
                { CharacterDiscipline.KineticCombat, "Mind Snap" },
                { CharacterDiscipline.Serenity, "Mind Snap" },

                { CharacterDiscipline.Annihilation, "Disruption" },
                { CharacterDiscipline.Carnage, "Disruption" },
                { CharacterDiscipline.Fury, "Disruption" },
                { CharacterDiscipline.Immortal, "Disruption" },
                { CharacterDiscipline.Rage, "Disruption" },
                { CharacterDiscipline.Vengeance, "Disruption" },

                { CharacterDiscipline.Combat, "Force Kick" },
                { CharacterDiscipline.Concentration, "Force Kick" },
                { CharacterDiscipline.Watchman, "Force Kick" },
                { CharacterDiscipline.Defense, "Force Kick" },
                { CharacterDiscipline.Focus, "Force Kick" },
                { CharacterDiscipline.Vigilance, "Force Kick" },

                { CharacterDiscipline.AdvancedPrototype, "Quell" },
                { CharacterDiscipline.FirebugPyrotech, "Quell" },
                { CharacterDiscipline.ShieldTech, "Quell" },
                { CharacterDiscipline.Arsenal, "Quell" },
                { CharacterDiscipline.Bodyguard, "Quell" },
                { CharacterDiscipline.InnovativeOrdnance, "Quell" },

                { CharacterDiscipline.AssaultSpecialist, "Riot Strike" },
                { CharacterDiscipline.CombatMedic, "Riot Strike" },
                { CharacterDiscipline.Gunnery, "Riot Strike" },
                { CharacterDiscipline.Plasmatech, "Riot Strike" },
                { CharacterDiscipline.ShieldSpecialist, "Riot Strike" },
                { CharacterDiscipline.Tactics, "Riot Strike" },

                { CharacterDiscipline.Concealment, "Distraction" },
                { CharacterDiscipline.Lethality, "Distraction" },
                { CharacterDiscipline.Medicine, "Distraction" },
                { CharacterDiscipline.Engineering, "Distraction" },
                { CharacterDiscipline.Marksmanship, "Distraction" },
                { CharacterDiscipline.Virulence, "Distraction" },

                { CharacterDiscipline.DirtyFighting, "Disabling Shot" },
                { CharacterDiscipline.Saboteur, "Disabling Shot" },
                { CharacterDiscipline.Sharpshooter, "Disabling Shot" },
                { CharacterDiscipline.Ruffian, "Disabling Shot" },
                { CharacterDiscipline.Sawbones, "Disabling Shot" },
                { CharacterDiscipline.Scrapper, "Disabling Shot" }
            };

        private static readonly HashSet<string> s_priorityHealingAbilities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
            "Advanced Medical Probe",
            "Benevolence",
            "Configuring Repair Mode",
            "Dark Heal",
            "Dark Infusion",
            "Deliverance",
            "Diagnostic Scan",
            "Emergency Medpac",
            "Healing Trance",
            "Kolto Infusion",
            "Kolto Injection",
            "Kolto Pack",
            "Kolto Probe",
            "Kolto Scan",
            "Med Scan",
            "Medical Probe",
            "Progressive Scan",
            "Rapid Scan",
            "Repair Mode",
            "Salvation",
            "Supplication",
            "Surgical Probe",
            "Underworld Medicine"
        };

        private static readonly HashSet<string> s_priorityAbilities =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
            "Calibrating Weapons System",
            "Charged Blast",
            "Force Blast",
            "Force Explosion",
            "Incinerate Armor",
            "Plasma Arc",
            "Power of the Force",
            "Ravage",
            "Transliminal Coating",
            "Unlimited Power"
        };

        private static readonly string[] s_priorityNameFragments =
        {
            "Heal",
            "Kolto",
            "Medical",
            "Medicine",
            "Mending",
            "Recover",
            "Regenerat",
            "Rejuvenat",
            "Repair",
            "Renew",
            "Restor",
            "Resuscitat",
            "Reviv"
        };

        /// <summary>Interrupts the most urgent important cast before the normal rotation runs.</summary>
        public static Composite HandlePriorityCast =>
            new Decorator(
                ret => CombatHotkeys.EnableInterrupts && Core.Player != null && Core.Player.InCombat,
                new Action(ret => TryInterruptPriorityCast()));

        private static RunStatus TryInterruptPriorityCast()
        {
            var target = Targeting.GetHeroCharacters()
                .Where(IsInterruptCandidate)
                .OrderByDescending(PriorityRank)
                .ThenBy(enemy => enemy.CastTimeRemaining)
                .FirstOrDefault();
            if (target == null || PriorityRank(target) == 0)
                return RunStatus.Failure;

            if (!s_interruptAbilities.TryGetValue(Core.Player.CharacterDiscipline, out var interrupt) ||
                !AbilityManager.HasAbility(interrupt))
                return RunStatus.Failure;

            if (Core.Player.IsCasting)
            {
                AbilityManager.StopCasting(ablCancelReasonEnum.Manual);
                return RunStatus.Success;
            }

            var castName = target.CastingAbility.Name;
            if (!AbilityManager.CanCast(interrupt, target).Success ||
                !AbilityManager.Cast(interrupt, target).Success)
            {
                return RunStatus.Failure;
            }

            var offTarget = Core.Player.Target == null || Core.Player.Target.NodeId != target.NodeId
                ? " off-target"
                : string.Empty;
            Logger.Write(">> Interrupting" + offTarget + " <<   " + castName + " on " + target.Name);
            return RunStatus.Success;
        }

        private static bool IsInterruptCandidate(HeroCharacter enemy)
        {
            try
            {
                if (PriorityRank(enemy) == 0 || !enemy.IsValidTarget() || !enemy.IsTargetable ||
                    !enemy.InLineOfSight || enemy.DistanceSqr > Distance.RangedExt * Distance.RangedExt)
                {
                    return false;
                }

                var me = Core.Player;
                if (me.Target != null && enemy.NodeId == me.Target.NodeId)
                    return true;
                if (enemy.IsInCombatWith(me) || me.IsInCombatWith(enemy))
                    return true;
                if (me.Companion != null &&
                    (enemy.IsInCombatWith(me.Companion) || me.Companion.IsInCombatWith(enemy)))
                {
                    return true;
                }

                return me.PartyMembers(true).Any(member => member != null &&
                    (enemy.IsInCombatWith(member) || member.IsInCombatWith(enemy)));
            }
            catch
            {
                return false;
            }
        }

        private static int PriorityRank(HeroCharacter unit)
        {
            if (unit == null || !unit.IsCasting || unit.CastingAbility == null ||
                unit.CastTimeTotal < MinimumCastSeconds ||
                unit.CastTimeRemaining <= MinimumRemainingSeconds)
            {
                return 0;
            }

            var castName = unit.CastingAbility.Name;
            if (string.IsNullOrEmpty(castName))
                return 0;
            if (s_priorityHealingAbilities.Contains(castName) ||
                s_priorityNameFragments.Any(fragment =>
                    castName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return 2;
            }

            return s_priorityAbilities.Contains(castName) ? 1 : 0;
        }
    }
}
