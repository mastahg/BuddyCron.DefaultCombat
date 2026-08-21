// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;
using DefaultCombat.Helpers;
using Reborn.Behaviors.Treesharp;
using Action = Reborn.Behaviors.Treesharp.Action;

namespace DefaultCombat.Behaviors
{
    /// <summary>Shared, combat-style-neutral interrupt policy. It reserves learned interrupts for
    /// encounter-critical casts and can cast directly on an off-target without changing the
    /// player's selected target.</summary>
    public static class Interrupts
    {
        private const float MinimumCastSeconds = 0.35f;
        private const float MinimumRemainingSeconds = 0.10f;

        private static readonly string[] s_interruptAbilities =
        {
            "Jolt",
            "Mind Snap",
            "Disruption",
            "Force Kick",
            "Quell",
            "Riot Strike",
            "Distraction",
            "Disabling Shot"
        };

        //NPC healing and repair casts are always the first interrupt priority.
        private static readonly string[] s_priorityHealingAbilities =
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
            "Medical Probe",
            "Progressive Scan",
            "Rapid Scan",
            "Repair Mode",
            "Salvation",
            "Supplication",
            "Surgical Probe",
            "Underworld Medicine"
        };

        // Exact encounter casts that are lethal, disabling, heavily buffing, or otherwise
        // important enough to justify pre-empting the damage rotation.
        private static readonly string[] s_priorityAbilities =
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

        // Healing/repair names vary across planets and instances. These deliberately narrow
        // fragments cover the high-value variants without treating every damage cast as urgent.
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

        private static readonly List<EncounterInterruptRule> s_encounterRules =
            new List<EncounterInterruptRule>();
        private static DateTime s_nextEncounterRuleRefreshUtc = DateTime.MinValue;
        private static DateTime s_encounterRuleWriteUtc = DateTime.MinValue;

        /// <summary>True when a unit is actively performing a cast on the interrupt allow-list.</summary>
        public static bool IsPriorityCast(HeroCharacter unit)
        {
            return PriorityRank(unit) > 0;
        }

        /// <summary>Runs before the normal cast wait and rotation. The learned class interrupt is
        /// fired directly at the most urgent important caster, including off-target enemies.</summary>
        public static Composite HandlePriorityCast =>
            new Decorator(
                ret => BoostedCombatHotkeys.InterruptsEnabled && HasCombatContext(),
                new Action(ret => TryInterruptPriorityCast()));

        private static RunStatus TryInterruptPriorityCast()
        {
            var candidates = Targeting.GetHeroCharacters()
                .Where(enemy => IsInterruptCandidate(enemy) && IsPriorityCast(enemy))
                .OrderByDescending(PriorityRank)
                .ThenBy(enemy => enemy.CastTimeRemaining)
                .ToList();

            if (candidates.Count == 0)
                return RunStatus.Failure;

            var interrupt = s_interruptAbilities.FirstOrDefault(ability => AbilityManager.HasAbility(ability));
            if (string.IsNullOrEmpty(interrupt))
                return RunStatus.Failure;

            // A mandatory stop is worth clipping our own damage cast. The next pulse performs the
            // interrupt after the cancellation has reached the client.
            if (Core.Player.IsCasting)
            {
                AbilityManager.StopCasting(ablCancelReasonEnum.Manual);
                return RunStatus.Success;
            }

            var target = candidates.FirstOrDefault(enemy => AbilityManager.CanCast(interrupt, enemy).Success);
            if (target == null)
                return RunStatus.Failure;

            var result = AbilityManager.Cast(interrupt, target);
            if (!result.Success)
                return RunStatus.Failure;

            var castName = target.CastingAbility != null ? target.CastingAbility.Name : "important cast";
            var offTarget = Core.Player.Target == null || Core.Player.Target.NodeId != target.NodeId
                ? " off-target"
                : string.Empty;
            Logger.Write(">> Interrupting" + offTarget + " <<   " + castName + " on " + target.Name);
            return RunStatus.Success;
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

            if (IsHealingOrRepairCast(castName))
                return 4;

            RefreshEncounterRules();
            if (s_encounterRules.Any(rule => rule.Matches(unit, castName)))
                return 3;

            if (s_priorityAbilities.Any(name =>
                    string.Equals(name, castName, StringComparison.OrdinalIgnoreCase)))
            {
                return 2;
            }

            return 0;
        }

        private static bool IsHealingOrRepairCast(string castName)
        {
            return s_priorityHealingAbilities.Any(name =>
                       string.Equals(name, castName, StringComparison.OrdinalIgnoreCase)) ||
                   s_priorityNameFragments.Any(fragment =>
                       castName.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool HasCombatContext()
        {
            var me = Core.Player;
            if (me == null)
                return false;

            if (me.InCombat || (me.Companion != null && !me.Companion.IsDead && me.Companion.InCombat))
                return true;

            return me.GroupId != 0 && HeroObjectManager.Players.Any(player =>
                player.GroupId == me.GroupId && player.InCombat);
        }

        private static bool IsInterruptCandidate(HeroCharacter enemy)
        {
            try
            {
                if (enemy == null || enemy.IsDead || !enemy.IsEffectivePvEHostile() ||
                    !enemy.IsTargetable || !enemy.InLineOfSight ||
                    enemy.DistanceSqr > Distance.RangedExt * Distance.RangedExt)
                {
                    return false;
                }

                var me = Core.Player;
                if (me == null)
                    return false;

                if ((me.Target != null && enemy.NodeId == me.Target.NodeId) ||
                    enemy.IsInCombatWith(me) || me.AttackerIds.Contains(enemy.NodeId))
                {
                    return true;
                }

                if (me.Companion != null && enemy.IsInCombatWith(me.Companion))
                    return true;

                return me.GroupId != 0 && HeroObjectManager.Players.Any(player =>
                    player.GroupId == me.GroupId && enemy.IsInCombatWith(player));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Reads Encounter Assist's hot-reloadable rules, if installed, so any validated
        /// Cast/Interrupt entry automatically becomes part of combat-assist interrupt targeting.</summary>
        private static void RefreshEncounterRules()
        {
            if (DateTime.UtcNow < s_nextEncounterRuleRefreshUtc)
                return;

            s_nextEncounterRuleRefreshUtc = DateTime.UtcNow.AddSeconds(2);
            var path = ResolveEncounterRulesPath();
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                var writeUtc = File.GetLastWriteTimeUtc(path);
                if (writeUtc == s_encounterRuleWriteUtc)
                    return;

                var loaded = new List<EncounterInterruptRule>();
                using (var document = JsonDocument.Parse(
                           File.ReadAllText(path),
                           new JsonDocumentOptions
                           {
                               CommentHandling = JsonCommentHandling.Skip,
                               AllowTrailingCommas = true
                           }))
                {
                    JsonElement rules;
                    if (!document.RootElement.TryGetProperty("rules", out rules) ||
                        rules.ValueKind != JsonValueKind.Array)
                    {
                        return;
                    }

                    foreach (var item in rules.EnumerateArray())
                    {
                        var trigger = ReadString(item, "trigger");
                        if (!ReadBool(item, "enabled", true) ||
                            (!string.IsNullOrEmpty(trigger) &&
                             !string.Equals(trigger, "Cast", StringComparison.OrdinalIgnoreCase)) ||
                            !ReadEquals(item, "action", "Interrupt"))
                        {
                            continue;
                        }

                        loaded.Add(new EncounterInterruptRule
                        {
                            AbilitySpecId = ReadUInt64(item, "abilitySpecId"),
                            AreaId = ReadUInt64(item, "areaId"),
                            AbilityName = ReadString(item, "abilityName"),
                            AbilityContains = ReadString(item, "abilityContains"),
                            SourceName = ReadString(item, "sourceName"),
                            SourceContains = ReadString(item, "sourceContains")
                        });
                    }
                }

                s_encounterRules.Clear();
                s_encounterRules.AddRange(loaded.Where(rule => rule.HasAbilitySelector));
                s_encounterRuleWriteUtc = writeUtc;
            }
            catch
            {
                // Encounter Assist keeps the previous valid rule set on malformed live edits;
                // the combat routine follows the same fail-soft behavior.
            }
        }

        private static string ResolveEncounterRulesPath()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "Plugins", "Rebornbuddy3DOverlay", "EncounterRules.json"),
                Path.Combine(Environment.CurrentDirectory, "Plugins", "Rebornbuddy3DOverlay", "EncounterRules.json"),
                Path.Combine(Environment.CurrentDirectory, "Rebornbuddy3DOverlay", "EncounterRules.json")
            };
            return candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        }

        private static string ReadString(JsonElement item, string property)
        {
            JsonElement value;
            return item.TryGetProperty(property, out value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static bool ReadEquals(JsonElement item, string property, string expected)
        {
            return string.Equals(ReadString(item, property), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool ReadBool(JsonElement item, string property, bool defaultValue)
        {
            JsonElement value;
            return item.TryGetProperty(property, out value) &&
                   (value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : defaultValue;
        }

        private static ulong ReadUInt64(JsonElement item, string property)
        {
            JsonElement value;
            if (!item.TryGetProperty(property, out value))
                return 0;

            ulong number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out number))
                return number;

            if (value.ValueKind != JsonValueKind.String)
                return 0;

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? (ulong.TryParse(text.Substring(2), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out number) ? number : 0)
                : (ulong.TryParse(text, out number) ? number : 0);
        }

        private sealed class EncounterInterruptRule
        {
            internal ulong AbilitySpecId;
            internal ulong AreaId;
            internal string AbilityName = string.Empty;
            internal string AbilityContains = string.Empty;
            internal string SourceName = string.Empty;
            internal string SourceContains = string.Empty;

            internal bool HasAbilitySelector =>
                AbilitySpecId != 0 || !string.IsNullOrEmpty(AbilityName) ||
                !string.IsNullOrEmpty(AbilityContains);

            internal bool Matches(HeroCharacter source, string castName)
            {
                var sourceName = source.Name ?? string.Empty;
                if (AreaId != 0 && source.AreaId != AreaId)
                    return false;
                if (!string.IsNullOrEmpty(SourceName) &&
                    !string.Equals(sourceName, SourceName, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!string.IsNullOrEmpty(SourceContains) &&
                    sourceName.IndexOf(SourceContains, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;

                return (AbilitySpecId != 0 && source.CastingAbilitySpecId == AbilitySpecId) ||
                       (!string.IsNullOrEmpty(AbilityName) &&
                        string.Equals(castName, AbilityName, StringComparison.OrdinalIgnoreCase)) ||
                       (!string.IsNullOrEmpty(AbilityContains) &&
                        castName.IndexOf(AbilityContains, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
    }
}
