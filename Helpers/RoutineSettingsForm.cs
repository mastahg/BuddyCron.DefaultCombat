// Copyright (C) 2011-2018 Bossland GmbH
// See the file LICENSE for the source code's detailed license

using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace DefaultCombat.Helpers
{
    /// <summary>Editable copy of the persisted settings used by the PropertyGrid.</summary>
    internal sealed class RoutineSettingsModel
    {
        [Browsable(false)]
        public RoutineProfile Profile { get; set; }

        [Category("Automatic Actions"), DisplayName("Core rotation")]
        [Description("Auto lets the routine perform the normal damage rotation. Manual leaves damage abilities to your SWTOR keybinds. Disabled blocks them from the routine.")]
        public RoutineActionMode CoreRotation { get; set; }

        [Category("Automatic Actions"), DisplayName("Area damage")]
        [Description("Controls automatic AoE damage. F7 remains the temporary runtime toggle unless this is Disabled.")]
        public RoutineActionMode AreaDamage { get; set; }

        [Category("Automatic Actions"), DisplayName("Interrupts")]
        [Description("Controls priority-only interrupts, including important off-target casts. Encounter Assist Interrupt rules are included automatically. F5 remains the temporary runtime toggle unless this is Disabled.")]
        public RoutineActionMode Interrupts { get; set; }

        [Category("Automatic Actions"), DisplayName("Knockbacks")]
        [Description("Controls automatic spacing abilities such as Overload.")]
        public RoutineActionMode Knockbacks { get; set; }

        [Category("Automatic Actions"), DisplayName("Crowd control")]
        [Description("Controls automatic hard crowd control such as Electrocute.")]
        public RoutineActionMode CrowdControl { get; set; }

        [Category("Automatic Actions"), DisplayName("Defensives")]
        [Description("Controls shields, threat drops, crowd-control breaks, and emergency defensive abilities.")]
        public RoutineActionMode Defensives { get; set; }

        [Category("Automatic Actions"), DisplayName("Self healing")]
        [Description("Controls class self-healing abilities, including Unnatural Preservation and Dark Heal.")]
        public RoutineActionMode SelfHealing { get; set; }

        [Category("Automatic Actions"), DisplayName("Cleanse")]
        [Description("Controls automatic self-cleansing abilities such as Expunge.")]
        public RoutineActionMode Cleanse { get; set; }

        [Category("Automatic Actions"), DisplayName("Offensive cooldowns")]
        [Description("Controls Polarity Shift, Recklessness, Force Speed damage windows, and similar offensive cooldowns.")]
        public RoutineActionMode OffensiveCooldowns { get; set; }

        [Category("Automatic Actions"), DisplayName("Raid buffs")]
        [Description("Controls raid-wide buffs such as Unlimited Power. F4 can temporarily enable Manual mode; Disabled blocks it.")]
        public RoutineActionMode RaidBuffs { get; set; }

        [Category("Automatic Actions"), DisplayName("Companion cooldowns")]
        [Description("Controls companion-dependent cooldowns such as Unity.")]
        public RoutineActionMode CompanionCooldowns { get; set; }

        [Category("Automatic Actions"), DisplayName("Heroic Moment abilities")]
        [Description("Controls legacy abilities after you manually activate Heroic Moment.")]
        public RoutineActionMode HeroicMoment { get; set; }

        [Category("Automatic Actions"), DisplayName("Medpac")]
        [Description("Controls automatic medpac use at the configured health threshold.")]
        public RoutineActionMode Medpac { get; set; }

        [Category("Automatic Actions"), DisplayName("Routine movement")]
        [Description("Controls movement requested by the combat routine. Combat Assist still uses its NullMover. F6 remains the charge/gap-closer runtime toggle.")]
        public RoutineActionMode Movement { get; set; }

        [Category("Thresholds"), DisplayName("Normal enemies for Overload")]
        [Description("Normal melee enemies required before Overload is automatic. Strong, elite, and knockable boss-tier enemies still trigger it immediately.")]
        public int NormalOverloadEnemyCount { get; set; }

        [Category("Thresholds"), DisplayName("Enemies for AoE")]
        [Description("Enemies in the best available cluster before the routine enters its AoE priority. AoE casts can use a better anchor than the selected target.")]
        public int AoeEnemyCount { get; set; }

        [Category("Health Thresholds"), DisplayName("Force Barrier (%)")]
        public int ForceBarrierHealthPercent { get; set; }

        [Category("Health Thresholds"), DisplayName("Unnatural Preservation (%)")]
        public int UnnaturalPreservationHealthPercent { get; set; }

        [Category("Health Thresholds"), DisplayName("Dark Heal (%)")]
        public int DarkHealHealthPercent { get; set; }

        [Category("Health Thresholds"), DisplayName("Cloud Mind (%)")]
        public int CloudMindHealthPercent { get; set; }

        [Category("Health Thresholds"), DisplayName("Medpac (%)")]
        public int MedpacHealthPercent { get; set; }

        internal static RoutineSettingsModel Load()
        {
            var settings = RoutineSettings.Instance;
            return new RoutineSettingsModel
            {
                Profile = settings.Profile,
                CoreRotation = settings.CoreRotation,
                AreaDamage = settings.AreaDamage,
                Interrupts = settings.Interrupts,
                Knockbacks = settings.Knockbacks,
                CrowdControl = settings.CrowdControl,
                Defensives = settings.Defensives,
                SelfHealing = settings.SelfHealing,
                Cleanse = settings.Cleanse,
                OffensiveCooldowns = settings.OffensiveCooldowns,
                RaidBuffs = settings.RaidBuffs,
                CompanionCooldowns = settings.CompanionCooldowns,
                HeroicMoment = settings.HeroicMoment,
                Medpac = settings.Medpac,
                Movement = settings.Movement,
                NormalOverloadEnemyCount = settings.NormalOverloadEnemyCount,
                AoeEnemyCount = settings.AoeEnemyCount,
                ForceBarrierHealthPercent = settings.ForceBarrierHealthPercent,
                UnnaturalPreservationHealthPercent = settings.UnnaturalPreservationHealthPercent,
                DarkHealHealthPercent = settings.DarkHealHealthPercent,
                CloudMindHealthPercent = settings.CloudMindHealthPercent,
                MedpacHealthPercent = settings.MedpacHealthPercent
            };
        }

        internal void ApplyPreset(RoutineProfile profile)
        {
            Profile = profile;
            if (profile == RoutineProfile.Custom)
                return;

            CoreRotation = RoutineActionMode.Auto;
            AreaDamage = RoutineActionMode.Auto;
            Defensives = RoutineActionMode.Auto;
            SelfHealing = RoutineActionMode.Auto;
            Cleanse = RoutineActionMode.Auto;
            Medpac = RoutineActionMode.Auto;
            NormalOverloadEnemyCount = 2;
            AoeEnemyCount = 3;
            ForceBarrierHealthPercent = 20;
            UnnaturalPreservationHealthPercent = 80;
            DarkHealHealthPercent = 30;
            CloudMindHealthPercent = 70;
            MedpacHealthPercent = 30;

            switch (profile)
            {
                case RoutineProfile.Leveling:
                    Interrupts = RoutineActionMode.Auto;
                    Knockbacks = RoutineActionMode.Auto;
                    CrowdControl = RoutineActionMode.Auto;
                    OffensiveCooldowns = RoutineActionMode.Auto;
                    RaidBuffs = RoutineActionMode.Manual;
                    CompanionCooldowns = RoutineActionMode.Auto;
                    HeroicMoment = RoutineActionMode.Auto;
                    Movement = RoutineActionMode.Auto;
                    break;

                case RoutineProfile.Dungeon:
                    Interrupts = RoutineActionMode.Auto;
                    Knockbacks = RoutineActionMode.Manual;
                    CrowdControl = RoutineActionMode.Manual;
                    OffensiveCooldowns = RoutineActionMode.Auto;
                    RaidBuffs = RoutineActionMode.Manual;
                    CompanionCooldowns = RoutineActionMode.Disabled;
                    HeroicMoment = RoutineActionMode.Disabled;
                    Movement = RoutineActionMode.Manual;
                    break;

                case RoutineProfile.Raid:
                    Interrupts = RoutineActionMode.Manual;
                    Knockbacks = RoutineActionMode.Disabled;
                    CrowdControl = RoutineActionMode.Manual;
                    OffensiveCooldowns = RoutineActionMode.Manual;
                    RaidBuffs = RoutineActionMode.Manual;
                    CompanionCooldowns = RoutineActionMode.Disabled;
                    HeroicMoment = RoutineActionMode.Disabled;
                    Movement = RoutineActionMode.Manual;
                    break;

                case RoutineProfile.PvP:
                    Interrupts = RoutineActionMode.Auto;
                    Knockbacks = RoutineActionMode.Auto;
                    CrowdControl = RoutineActionMode.Auto;
                    OffensiveCooldowns = RoutineActionMode.Manual;
                    RaidBuffs = RoutineActionMode.Manual;
                    CompanionCooldowns = RoutineActionMode.Disabled;
                    HeroicMoment = RoutineActionMode.Disabled;
                    Movement = RoutineActionMode.Manual;
                    break;
            }
        }

        internal void Save()
        {
            var settings = RoutineSettings.Instance;
            settings.Profile = Profile;
            settings.CoreRotation = CoreRotation;
            settings.AreaDamage = AreaDamage;
            settings.Interrupts = Interrupts;
            settings.Knockbacks = Knockbacks;
            settings.CrowdControl = CrowdControl;
            settings.Defensives = Defensives;
            settings.SelfHealing = SelfHealing;
            settings.Cleanse = Cleanse;
            settings.OffensiveCooldowns = OffensiveCooldowns;
            settings.RaidBuffs = RaidBuffs;
            settings.CompanionCooldowns = CompanionCooldowns;
            settings.HeroicMoment = HeroicMoment;
            settings.Medpac = Medpac;
            settings.Movement = Movement;
            settings.NormalOverloadEnemyCount = NormalOverloadEnemyCount;
            settings.AoeEnemyCount = AoeEnemyCount;
            settings.ForceBarrierHealthPercent = ForceBarrierHealthPercent;
            settings.UnnaturalPreservationHealthPercent = UnnaturalPreservationHealthPercent;
            settings.DarkHealHealthPercent = DarkHealHealthPercent;
            settings.CloudMindHealthPercent = CloudMindHealthPercent;
            settings.MedpacHealthPercent = MedpacHealthPercent;
            settings.Save();
        }
    }

    /// <summary>WinForms configuration surface opened by BuddyCron's routine-settings button.</summary>
    internal sealed class RoutineSettingsForm : Form
    {
        private readonly ComboBox _profile;
        private readonly PropertyGrid _grid;
        private readonly RoutineSettingsModel _model;
        private bool _loading;

        internal RoutineSettingsForm(string routineName)
        {
            Text = "Routine Settings - " + routineName;
            ClientSize = new Size(560, 650);
            MinimumSize = new Size(500, 560);
            StartPosition = FormStartPosition.CenterScreen;

            _model = RoutineSettingsModel.Load();

            var header = new Panel { Dock = DockStyle.Top, Height = 94, Padding = new Padding(12) };
            var profileLabel = new Label
            {
                Text = "Combat profile:",
                AutoSize = true,
                Location = new Point(12, 16)
            };
            _profile = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(118, 12),
                Width = 180,
                DataSource = Enum.GetValues(typeof(RoutineProfile))
            };
            var help = new Label
            {
                Text = "Auto = routine controlled. Manual = use your SWTOR keybind; F4-F7 can temporarily enable their matching category. Disabled = blocked by this profile.",
                AutoSize = false,
                Location = new Point(12, 46),
                Size = new Size(520, 40)
            };
            header.Controls.Add(profileLabel);
            header.Controls.Add(_profile);
            header.Controls.Add(help);

            _grid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                SelectedObject = _model,
                ToolbarVisible = false,
                HelpVisible = true,
                PropertySort = PropertySort.Categorized
            };

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                Padding = new Padding(8),
                FlowDirection = FlowDirection.RightToLeft
            };
            var cancel = new Button { Text = "Cancel", Width = 92, DialogResult = DialogResult.Cancel };
            var save = new Button { Text = "Save", Width = 92 };
            save.Click += SaveClicked;
            footer.Controls.Add(cancel);
            footer.Controls.Add(save);

            Controls.Add(_grid);
            Controls.Add(footer);
            Controls.Add(header);
            AcceptButton = save;
            CancelButton = cancel;

            _loading = true;
            _profile.SelectedItem = _model.Profile;
            _loading = false;
            _profile.SelectedIndexChanged += ProfileChanged;
            _grid.PropertyValueChanged += PropertyChanged;
        }

        private void ProfileChanged(object sender, EventArgs e)
        {
            if (_loading || !(_profile.SelectedItem is RoutineProfile profile))
                return;

            _model.ApplyPreset(profile);
            _grid.Refresh();
        }

        private void PropertyChanged(object sender, PropertyValueChangedEventArgs e)
        {
            if (_loading)
                return;

            _model.Profile = RoutineProfile.Custom;
            _loading = true;
            _profile.SelectedItem = RoutineProfile.Custom;
            _loading = false;
        }

        private void SaveClicked(object sender, EventArgs e)
        {
            if (_profile.SelectedItem is RoutineProfile profile)
                _model.Profile = profile;

            _model.Save();
            BoostedCombatHotkeys.ApplySettingsDefaults();
            Logger.Write("Routine Settings saved. Profile: " + _model.Profile);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
