// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.Collections.Generic;
using System.Linq;
using BuddyCron;
using BuddyCron.Managers;
using BuddyCron.Objects;

namespace DefaultCombat.Helpers
{
    /// <summary>Rotation helper extensions: player-cast aura queries, party/companion enumeration,
    /// cleanse/crowd-control tables and per-class rest/buff tunables.</summary>
    public static class Extensions
    {
        /// <summary>Filter over a <see cref="HeroCharacter"/>.</summary>
        public delegate bool HeroCharacterPredicateDelegate(HeroCharacter torCharacter);

        /// <summary>Filter over a <see cref="HeroEffect"/>.</summary>
        public delegate bool TorEffectPredicateDelegate(HeroEffect torEffect);

        /// <summary>Filter over a <see cref="HeroPlayer"/>.</summary>
        public delegate bool HeroPlayerPredicateDelegate(HeroPlayer torPlayer);

        /// <summary>Debuffs worth cleansing, with the stack thresholds that trigger a cleanse.</summary>
        public static Debuff[] DebuffList =
        {
            new Debuff("Crushing Affliction (Force)", 0),
            new Debuff("Crushing Affliction (All)", 0),
            new Debuff("Corrosive Slime", 0),
            //=+=OPERATIONS=+=
            //Eternity Vault
            //Explosive Conflicts
            new Debuff("Mental Anguish", 0),
            new Debuff("Weakened", 0),
            //Gods from the Machine
            new Debuff("Armor Melt (Any)", 0),
            //Karagga's Palace
            new Debuff("Unstable Energy", 0),
            //Scum and Villainy
            //Temple of Scarifice
            //Terror from Beyond
            //The Dread Fortress
            new Debuff("Corrupted Nanites (Any)", 0),
            //The Dread Palace
            new Debuff("Affliction", 0),
            new Debuff("Death Mark", 0),
            //The Ravagers
            //=+=FLASHPOINTS=+=
            //Assault on Tython
            //Athiss
            //Battle of Rishi
            //Blood Hunt
            new Debuff("Bleeding (Physical)", 0),
            //Boarding Party
            //Cademinu
            //Collicoid War Games
            //Crisis on Umbara
            //Czerka Core Meltdown
            //Depths of Manaan
            //Directive 7
            //Hammer Station
			new Debuff("Laser", 6),
            //Kaon
            //Korriban Incursion
            //Kuat Drive Yards
            //Legacy of Rakata
            new Debuff("Hunting Trap (Any)", 0),
            //Lost Island
            //Maelstrom Prison
            new Debuff("Affliction (Force)", 0)
            //Mandalorian Raiders
            //Red Reaper
            //Taral V
            //The Battle of Illum
            //The Black Talon
            //The Esseles
            //The False Emperor
            //A Traitor Among the Chiss
            //The Foundry

        };

        /// <summary>Crowd-control debuffs — a target carrying one should not be attacked (it would break the CC).</summary>
        public static string[] DebuffNamesCrowdControl =
        {
            "Afraid (Mental)", // from Intimidating Roar / Awe
			"Blinded (Tech)", // from Flash Grenade / Flash Bang
			"Lifted (Force)", // from Whirlwind / Force Lift
			"Sliced", // from Slice Droid
			"Asleep (Mental)", // from Concussive Round & Mind Maze
			"Sleeping", // from Sleep Dart & Tranquilizer
			"Stunned" // from Debilitate / Dirty Kick
		};

        /// <summary>Shield-lockout debuffs — while present, the unit cannot receive another shield.</summary>
        public static string[] DebuffNamesShielded =
        {
            "Deionized", // Result of Sorcerer shielding
			"Force-imbalance" // Result of Sage shielding
		};

        private static readonly string[] s_buffNamesForCoverVariants = { "Crouch", "Cover" };

        private static readonly IEnumerable<ClassTunables> s_tunablesByClass = new List<ClassTunables>
        {
			// Republic
			new ClassTunables
            {
                Class = CharacterClass.Knight,
                RejuvenateAbilityName = "Introspection",
                SelfBuffName = "Force Might",
                IsRejuvenationNeeded = torPlayer => torPlayer.HealthPercent < 70,
                IsRejuvenationComplete = torPlayer => torPlayer.HealthPercent >= 95,
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.Consular,
                RejuvenateAbilityName = "Meditation",
                SelfBuffName = "Force Valor",
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete =
                    torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat >= 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.Smuggler,
                RejuvenateAbilityName = "Recuperate",
                SelfBuffName = "Lucky Shots",
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete =
                    torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat >= 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.Trooper,
                RejuvenateAbilityName = "Recharge and Reload",
                SelfBuffName = "Fortification",
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete = torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat > 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },

			// Empire
			new ClassTunables
            {
                Class = CharacterClass.Warrior,
                RejuvenateAbilityName = "Channel Hatred",
                SelfBuffName = "Unnatural Might",
                IsRejuvenationNeeded = torPlayer => torPlayer.HealthPercent < 70,
                IsRejuvenationComplete = torPlayer => torPlayer.HealthPercent >= 95,
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.Inquisitor,
                RejuvenateAbilityName = "Seethe",
                SelfBuffName = "Mark of Power",
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete =
                    torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat >= 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.Agent,
                RejuvenateAbilityName = "Recuperate",
                SelfBuffName = "Coordination",
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete =
                    torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat >= 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },
            new ClassTunables
            {
                Class = CharacterClass.BountyHunter,
                RejuvenateAbilityName = "Recharge and Reload",
                SelfBuffName = "Hunter's Boon",
                // The cached resource counts DOWN on the current build (100 = no heat,
                // 0 = overheated; live-verified idle = 100/100) — same remaining-resource
                // convention as every other class, so no inversion here.
                IsRejuvenationNeeded = torPlayer => (torPlayer.HealthPercent < 70) || (torPlayer.ResourceStat < 70),
                IsRejuvenationComplete = torPlayer => (torPlayer.HealthPercent >= 95) && (torPlayer.ResourceStat >= 95),
                NormalizedResource = torPlayer => torPlayer.ResourceStat
            },

			// Boundary condition --
			new ClassTunables
            {
                Class = CharacterClass.Unknown,
                RejuvenateAbilityName = "UNDEFINED",
                SelfBuffName = "UNDEFINED",
                IsRejuvenationNeeded = torPlayer => false,
                IsRejuvenationComplete = torPlayer => true,
                NormalizedResource = torPlayer => 0.0f
            }
        };

        // Derived data structure for quick lookup --
        private static readonly Dictionary<CharacterClass, ClassTunables> s_tunablesMap = s_tunablesByClass.ToDictionary(x => x.Class, x => x);

        /// <summary>True when <paramref name="u"/> has a buff named <paramref name="aura"/> that was
        /// cast by the player.</summary>
        public static bool HasMyBuff(this HeroCharacter u, string aura)
        {
            var pNode = Core.Player.NodeId;
            return u.Buffs.Any(a => a.Name == aura && a.CasterGuid == pNode);
        }
        /// <summary>True when <paramref name="u"/> has a debuff named <paramref name="aura"/> that
        /// was cast by the player.</summary>
        public static bool HasMyDebuff(this HeroCharacter u, string aura)
        {
            var pNode = Core.Player.NodeId;
            return u.Debuffs.Any(a => a.Name == aura && a.CasterGuid == pNode);
        }

        /// <summary>True when <paramref name="u"/> has a player-cast buff named
        /// <paramref name="aura"/>; also returns the matching effect (null when absent).</summary>
        public static bool HasMyBuff(this HeroCharacter u, string aura, out HeroEffect? effect)
        {
            effect = null;
            var pNode = Core.Player.NodeId;
            effect =  u.Buffs.FirstOrDefault(a => a.Name == aura && a.CasterGuid == pNode);
            return effect != null;
        }


        /// <summary>True when <paramref name="u"/> has a player-cast debuff named
        /// <paramref name="aura"/>; also returns the matching effect (null when absent).</summary>
        public static bool HasMyDebuff(this HeroCharacter u, string aura, out HeroEffect? effect)
        {
            effect = null;
            var pNode = Core.Player.NodeId;
            effect = u.Debuffs.FirstOrDefault(a => a.Name == aura && a.CasterGuid == pNode);
            return effect != null;
        }



        /// <summary>Stack count of the player-cast buff <paramref name="buffName"/> on
        /// <paramref name="p"/>, or 0 when absent.</summary>
        public static int BuffCount(this HeroCharacter p, string buffName)
        {
            return !p.HasMyBuff(buffName, out var effect) ? 0: effect.Stacks;
        }

        /// <summary>Stack count of the player-cast debuff <paramref name="buffName"/> on
        /// <paramref name="p"/>, or 0 when absent.</summary>
        public static int DebuffCount(this HeroCharacter p, string buffName)
        {
            return !p.HasMyDebuff(buffName, out var effect) ? 0 : effect.Stacks;
        }

        /// <summary>Seconds remaining on the player-cast buff <paramref name="buffName"/>, or 0 when absent.</summary>
        public static double BuffTimeLeft(this HeroCharacter p, string buffName)
        {
            return !p.HasMyBuff(buffName, out var effect) ? 0 : effect.TimeLeft.TotalSeconds;
        }

        /// <summary>Seconds remaining on the player-cast debuff <paramref name="buffName"/>, or 0 when absent.</summary>
        public static double DebuffTimeLeft(this HeroCharacter p, string buffName)
        {
            return !p.HasMyDebuff(buffName, out var effect) ? 0 : effect.TimeLeft.TotalSeconds;
        }

        /// <summary>True when <paramref name="p"/> carries any debuff from <see cref="DebuffList"/>
        /// at or above its stack threshold (threshold 0 = any stacks).</summary>
        public static bool NeedsCleanse(this HeroCharacter p)
        {
            var debuffs = p.Debuffs
                .Where(effect => effect != null && !string.IsNullOrEmpty(effect.Name))
                .ToList();

            foreach (var d in DebuffList)
            {
                if (debuffs.Any(effect =>
                    string.Equals(effect.Name, d.Name, StringComparison.Ordinal) &&
                    (d.Stacks == 0 || effect.Stacks >= d.Stacks)))
                {
                    return true;
                }
            }

            return false;
        }


        /// <summary>The player's party members (optionally including their summoned companions)
        /// that pass <paramref name="memberQualifier"/>.</summary>
        public static IEnumerable<HeroCharacter> PartyMembers(this HeroPlayer torPlayer, bool includeCompanions = true,
            HeroCharacterPredicateDelegate memberQualifier = null)
        {
            memberQualifier = memberQualifier ?? (c => true); // resolve playerQualifier to something sane

            IEnumerable<HeroCharacter> partyMembers = torPlayer.PartyPlayers();

            if (includeCompanions)
            {
                partyMembers = partyMembers.Concat(torPlayer.PartyCompanions());
            }

            return partyMembers.Where(c => memberQualifier(c));
        }

        /// <summary>True when the character's party role is a tank role (melee or ranged).</summary>
        public static bool IsPartyRoleTank(this HeroCharacter torCharacter)
        {
            var role = torCharacter.PartyRole();
            return (role == Global.PartyRole.MeleeTank) || (role == Global.PartyRole.RangedTank);
        }

        /// <summary>Summoned companions of the player's party members that pass
        /// <paramref name="companionQualifier"/>.</summary>
        public static IEnumerable<HeroCharacter> PartyCompanions(this HeroPlayer torPlayer, HeroCharacterPredicateDelegate companionQualifier = null)
        {
            // Extension methods guarantee the 'this' argument is never null, so no need to check a contract here

            companionQualifier = companionQualifier ?? (ctx => true); // resolve playerQualifier to something sane

            return torPlayer.PartyPlayers(p => p.IsCompanionInUse() && companionQualifier(p.Companion)).Select(p => p.Companion);
        }

        /// <summary>Infers the character's combat role from their active combat-style discipline.</summary>
        public static Global.PartyRole PartyRole(this HeroCharacter torCharacter)
        {
            switch (torCharacter.CharacterDiscipline)
            {
                case CharacterDiscipline.Darkness:
                case CharacterDiscipline.KineticCombat:
                case CharacterDiscipline.Immortal:
                case CharacterDiscipline.Defense:
                case CharacterDiscipline.ShieldTech:
                case CharacterDiscipline.ShieldSpecialist:
                    return Global.PartyRole.MeleeTank;
                case CharacterDiscipline.CombatMedic:
                case CharacterDiscipline.Bodyguard:
                case CharacterDiscipline.Medicine:
                case CharacterDiscipline.Seer:
                case CharacterDiscipline.Sawbones:
                case CharacterDiscipline.Corruption:
                    return Global.PartyRole.Healer;
                case CharacterDiscipline.Deception:
                case CharacterDiscipline.Hatred:
                case CharacterDiscipline.Infiltration:
                case CharacterDiscipline.Serenity:
                case CharacterDiscipline.Rage:
                case CharacterDiscipline.Vengeance:
                case CharacterDiscipline.Focus:
                case CharacterDiscipline.Vigilance:
                case CharacterDiscipline.Annihilation:
                case CharacterDiscipline.Carnage:
                case CharacterDiscipline.Fury:
                case CharacterDiscipline.Watchman:
                case CharacterDiscipline.Combat:
                case CharacterDiscipline.Concentration:
                case CharacterDiscipline.AdvancedPrototype:
                case CharacterDiscipline.FirebugPyrotech:
                case CharacterDiscipline.Tactics:
                case CharacterDiscipline.Plasmatech:
                case CharacterDiscipline.Concealment:
                case CharacterDiscipline.Lethality:
                case CharacterDiscipline.Scrapper:
                case CharacterDiscipline.Ruffian:
                    return Global.PartyRole.MeleeDPS;
                default:
                    return Global.PartyRole.RangedDPS;
            }
        }

        /// <summary>True when the player has companions unlocked and one is currently summoned.</summary>
        public static bool IsCompanionInUse(this HeroPlayer torPlayer)
        {
            // Extension methods guarantee the 'this' argument is never null, so no need to check a contract here
            return (torPlayer.CompanionsUnlocked.Count > 0) && (torPlayer.Companion != null);
        }

        /// <summary>Players sharing the player's group that pass <paramref name="playerQualifier"/>;
        /// a solo player yields a list containing only themself.</summary>
        public static IEnumerable<HeroPlayer> PartyPlayers(this HeroPlayer torPlayer, HeroPlayerPredicateDelegate playerQualifier = null)
        {
            playerQualifier = playerQualifier ?? (ctx => true); // resolve playerQualifier to something sane

            var playerGroupId = torPlayer.GroupId;

            // If we're solo, only have the torPlayer on the list...
            // We can't build this list using the 'normal' query, as all solo players have the common GroupId of zero.
            // We don't want a list of 'solo' players (what the normal query would do), we want a list with only the solo torPlayer on it.
            if (playerGroupId == 0)
            {
                return HeroObjectManager.GetObjectsOfType<HeroPlayer>().Where(p => (p == Core.Player) && playerQualifier(p));
            }

            // NB: IsInParty() is implemented in terms of PartyPlayers().  Be careful not to implement this method in terms of
            // IsInParty(); otherwise, infinite recursive descent will occur.
            return HeroObjectManager.GetObjectsOfType<HeroPlayer>().Where(p => playerQualifier(p) && (p.GroupId == playerGroupId));
        }

        /// <summary>True when the character has any debuff from <see cref="DebuffNamesCrowdControl"/>.</summary>
        public static bool IsCrowdControlled(this HeroCharacter torCharacter)
        {
            return torCharacter.Debuffs.FirstOrDefault(d => DebuffNamesCrowdControl.Contains(d.Name)) != null;
        }


        public static bool IsPlayerOrCompanionInCombat(this HeroPlayer player)
        {
            try
            {
                return player != null &&
                       (player.InCombat ||
                        (player.Companion != null && !player.Companion.IsDead && player.Companion.InCombat));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsEngagedWithPlayer(this HeroCharacter c)
        {
            try
            {
                var player = Core.Player;
                if (c == null || player == null)
                    return false;

                var companion = player.Companion;
                return c.InCombat ||
                       c.IsInCombatWith(player) ||
                       player.IsInCombatWith(c) ||
                       (companion != null && !companion.IsDead &&
                        (c.IsInCombatWith(companion) || companion.IsInCombatWith(c)));
            }
            catch
            {
                return false;
            }
        }

        public static bool IsEffectivePvEHostile(this HeroCharacter c)
        {
            try
            {
                if (c == null || c is HeroPlayer)
                    return false;

                if (c.IsHostile)
                    return true;

                var player = Core.Player;
                if (player == null || !c.IsEngagedWithPlayer())
                    return false;

                if (player.AttackerIds.Contains(c.NodeId))
                    return true;

                var companion = player.Companion;
                return companion != null && !companion.IsDead &&
                       companion.AttackerIds.Contains(c.NodeId);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True for a hostile, living, engaged character that is neither stunned nor
        /// crowd-controlled; false on null or any read failure.</summary>
        public static bool IsValidTarget(this HeroCharacter c)
        {
            try
            {
                return c != null && c.IsEngagedWithPlayer() && c.IsEffectivePvEHostile() &&
                       !c.IsDead && !c.IsStunned && !c.IsCrowdControlled();
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when the unit's toughness is boss tier or higher (null-safe).</summary>
        public static bool BossOrGreater(this HeroCharacter unit)
        {
            if (unit != null) if (unit.Toughness >= cbtToughnessEnum.boss_1) return true;
            return false;
        }

        /// <summary>True when the unit's toughness is strong tier or higher (null-safe).</summary>
        public static bool StrongOrGreater(this HeroCharacter unit)
        {
            if (unit != null) if (unit.Toughness >= cbtToughnessEnum.strong) return true;
            return false;
        }

        /// <summary>True when the player's active discipline is a healing spec.</summary>
        public static bool IsHealer(this HeroPlayer p)
        {
            switch (p.CharacterDiscipline)
            {
                case CharacterDiscipline.CombatMedic:
                case CharacterDiscipline.Bodyguard:
                case CharacterDiscipline.Medicine:
                case CharacterDiscipline.Seer:
                case CharacterDiscipline.Sawbones:
                case CharacterDiscipline.Corruption:
                    return true;
            }
            return false;
        }

        /// <summary>True when the character has a cover/crouch buff.</summary>
        public static bool IsInCover(this HeroCharacter torCharacter)
        {
            return torCharacter.Buffs.Any(b => s_buffNamesForCoverVariants.Contains(b.Name));
        }

        /// <summary>Approximates whether the player is behind <paramref name="target"/> by comparing
        /// the player's heading with the target's.</summary>
        public static bool IsBehind(this HeroCharacter torCharacter, HeroCharacter target)
        {
            return Math.Abs(Core.Player!.Heading - target.Heading) <= 150; // && CurrentTarget.IsInRange(0.35f)
        }

        // With 7.x combat styles the origin story (CharacterClass) and the combat style
        // (AdvancedClass) can differ — an Agent origin playing Powertech rests with the
        // bounty hunter's "Recharge and Reload", not the agent's "Recuperate". Abilities,
        // buffs and resource all follow the style, so the tunables key must too.
        private static CharacterClass TunablesClass(HeroPlayer torPlayer)
        {
            switch (torPlayer.AdvancedClass)
            {
                case AdvancedClass.Juggernaut:
                case AdvancedClass.Marauder:
                    return CharacterClass.Warrior;
                case AdvancedClass.Guardian:
                case AdvancedClass.Sentinel:
                    return CharacterClass.Knight;
                case AdvancedClass.Sorcerer:
                case AdvancedClass.Assassin:
                    return CharacterClass.Inquisitor;
                case AdvancedClass.Sage:
                case AdvancedClass.Shadow:
                    return CharacterClass.Consular;
                case AdvancedClass.Mercenary:
                case AdvancedClass.Powertech:
                    return CharacterClass.BountyHunter;
                case AdvancedClass.Operative:
                case AdvancedClass.Sniper:
                    return CharacterClass.Agent;
                case AdvancedClass.Scoundrel:
                case AdvancedClass.Gunslinger:
                    return CharacterClass.Smuggler;
                case AdvancedClass.Commando:
                case AdvancedClass.Vanguard:
                    return CharacterClass.Trooper;
                default:
                    return torPlayer.CharacterClass;
            }
        }

        /// <summary>The player's remaining class resource on a 0-100 scale (100 = full/rested),
        /// keyed by the combat style.</summary>
        public static float ResourcePercent(this HeroPlayer torPlayer)
        {
            return s_tunablesMap[TunablesClass(torPlayer)].NormalizedResource(torPlayer);
        }

        /// <summary>Name of the combat style's out-of-combat rest/recuperate channel.</summary>
        public static string RejuvenateAbilityName(this HeroPlayer torPlayer)
        {
            return s_tunablesMap[TunablesClass(torPlayer)].RejuvenateAbilityName;
        }

        /// <summary>Name of the combat style's long-duration self buff (e.g. Force Might).</summary>
        public static string SelfBuffName(this HeroPlayer torPlayer)
        {
            return s_tunablesMap[TunablesClass(torPlayer)].SelfBuffName;
        }

        private delegate bool PredicateDelegate(HeroPlayer torPlayer);

        private delegate float NormalizedResourceDelegate(HeroPlayer torPlayer);

        private class ClassTunables
        {
            /// <summary>
            ///     The <c>CharacterClass</c> to which this set of <c>ClassTunables</c> applies.
            /// </summary>
            public CharacterClass Class = CharacterClass.Unknown;

            /// <summary>True once resting has restored enough resource to stop.</summary>
            public PredicateDelegate IsRejuvenationComplete = torPlayer => true;
            /// <summary>True when the class should rest to restore resource.</summary>
            public PredicateDelegate IsRejuvenationNeeded = torPlayer => false;
            /// <summary>The class resource mapped to a 0-1 scale.</summary>
            public NormalizedResourceDelegate NormalizedResource = torPlayer => 0.0f;
            /// <summary>Ability cast to restore resource while resting.</summary>
            public string RejuvenateAbilityName = "UNDEFINED";
            /// <summary>The class's persistent self-buff.</summary>
            public string SelfBuffName = "UNDEFINED";
        }
    }

    /// <summary>A cleansable debuff name plus the stack count that warrants a cleanse (0 = any).</summary>
    public class Debuff
    {
        /// <summary>Display name of the debuff.</summary>
        public string Name;
        /// <summary>Stack count that warrants a cleanse; 0 = any.</summary>
        public int Stacks;

        /// <summary>Creates an entry for debuff <paramref name="name"/> with cleanse threshold
        /// <paramref name="stacks"/>.</summary>
        public Debuff(string name, int stacks)
        {
            Name = name;
            Stacks = stacks;
        }
    }
}
