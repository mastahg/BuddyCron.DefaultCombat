// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.ComponentModel;
using System.IO;
using BuddyCron.Settings;
using Reborn.Utilities.Settings;

namespace DefaultCombat.Helpers
{
    /// <summary>Determines how the routine treats an action category.</summary>
    public enum RoutineActionMode
    {
        Auto,
        Manual,
        Disabled
    }

    /// <summary>Built-in combat-assist presets. Custom preserves user-edited values.</summary>
    public enum RoutineProfile
    {
        Leveling,
        Dungeon,
        Raid,
        PvP,
        Custom
    }

    /// <summary>Per-character, JSON-backed settings shared by every Default Combat rotation.</summary>
    public sealed class RoutineSettings : JsonSettings
    {
        private static RoutineSettings s_instance;

        public static RoutineSettings Instance => s_instance ?? (s_instance = new RoutineSettings());

        private RoutineSettings()
            : base(Path.Combine(CharacterSettings.CharacterSettingsDirectory, "DefaultCombatRoutineSettings.json"))
        {
        }

        private RoutineProfile _profile;
        private RoutineActionMode _coreRotation;
        private RoutineActionMode _areaDamage;
        private RoutineActionMode _interrupts;
        private RoutineActionMode _knockbacks;
        private RoutineActionMode _crowdControl;
        private RoutineActionMode _defensives;
        private RoutineActionMode _selfHealing;
        private RoutineActionMode _cleanse;
        private RoutineActionMode _offensiveCooldowns;
        private RoutineActionMode _raidBuffs;
        private RoutineActionMode _companionCooldowns;
        private RoutineActionMode _heroicMoment;
        private RoutineActionMode _medpac;
        private RoutineActionMode _movement;
        private int _normalOverloadEnemyCount;
        private int _aoeEnemyCount;
        private int _forceBarrierHealthPercent;
        private int _unnaturalPreservationHealthPercent;
        private int _darkHealHealthPercent;
        private int _cloudMindHealthPercent;
        private int _medpacHealthPercent;

        [DefaultValue(RoutineProfile.Leveling)]
        public RoutineProfile Profile
        {
            get => _profile;
            set => SetField(ref _profile, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode CoreRotation
        {
            get => _coreRotation;
            set => SetField(ref _coreRotation, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode AreaDamage
        {
            get => _areaDamage;
            set => SetField(ref _areaDamage, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Interrupts
        {
            get => _interrupts;
            set => SetField(ref _interrupts, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Knockbacks
        {
            get => _knockbacks;
            set => SetField(ref _knockbacks, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode CrowdControl
        {
            get => _crowdControl;
            set => SetField(ref _crowdControl, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Defensives
        {
            get => _defensives;
            set => SetField(ref _defensives, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode SelfHealing
        {
            get => _selfHealing;
            set => SetField(ref _selfHealing, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Cleanse
        {
            get => _cleanse;
            set => SetField(ref _cleanse, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode OffensiveCooldowns
        {
            get => _offensiveCooldowns;
            set => SetField(ref _offensiveCooldowns, value);
        }

        [DefaultValue(RoutineActionMode.Manual)]
        public RoutineActionMode RaidBuffs
        {
            get => _raidBuffs;
            set => SetField(ref _raidBuffs, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode CompanionCooldowns
        {
            get => _companionCooldowns;
            set => SetField(ref _companionCooldowns, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode HeroicMoment
        {
            get => _heroicMoment;
            set => SetField(ref _heroicMoment, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Medpac
        {
            get => _medpac;
            set => SetField(ref _medpac, value);
        }

        [DefaultValue(RoutineActionMode.Auto)]
        public RoutineActionMode Movement
        {
            get => _movement;
            set => SetField(ref _movement, value);
        }

        [DefaultValue(2)]
        public int NormalOverloadEnemyCount
        {
            get => _normalOverloadEnemyCount;
            set => SetField(ref _normalOverloadEnemyCount, Clamp(value, 1, 8));
        }

        [DefaultValue(3)]
        public int AoeEnemyCount
        {
            get => _aoeEnemyCount;
            set => SetField(ref _aoeEnemyCount, Clamp(value, 2, 8));
        }

        [DefaultValue(20)]
        public int ForceBarrierHealthPercent
        {
            get => _forceBarrierHealthPercent;
            set => SetField(ref _forceBarrierHealthPercent, Clamp(value, 1, 100));
        }

        [DefaultValue(80)]
        public int UnnaturalPreservationHealthPercent
        {
            get => _unnaturalPreservationHealthPercent;
            set => SetField(ref _unnaturalPreservationHealthPercent, Clamp(value, 1, 100));
        }

        [DefaultValue(30)]
        public int DarkHealHealthPercent
        {
            get => _darkHealHealthPercent;
            set => SetField(ref _darkHealHealthPercent, Clamp(value, 1, 100));
        }

        [DefaultValue(70)]
        public int CloudMindHealthPercent
        {
            get => _cloudMindHealthPercent;
            set => SetField(ref _cloudMindHealthPercent, Clamp(value, 1, 100));
        }

        [DefaultValue(30)]
        public int MedpacHealthPercent
        {
            get => _medpacHealthPercent;
            set => SetField(ref _medpacHealthPercent, Clamp(value, 1, 100));
        }

        public static bool IsAutomatic(RoutineActionMode mode) => mode == RoutineActionMode.Auto;

        private static int Clamp(int value, int minimum, int maximum) =>
            Math.Max(minimum, Math.Min(maximum, value));
    }
}
