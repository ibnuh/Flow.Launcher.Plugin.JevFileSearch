using System;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    public partial class SettingsControl : UserControl
    {
        private readonly Settings _settings;
        private readonly Action _save;

        public SettingsControl(Settings settings, Action save)
        {
            _settings = settings;
            _save = save;
            InitializeComponent();

            ApiKeyBox.Text = _settings.ApiKey ?? "";
            DirsBox.Text = _settings.IndexDirs ?? "";
            MaxBox.Text = _settings.MaxFilesPerFolder.ToString();
            OtherDrivesBox.IsChecked = _settings.IncludeOtherDrives;
            EverythingBox.IsChecked = _settings.UseEverything;
            EverythingPathBox.Text = _settings.EverythingPath ?? "";
            ExcludeBox.Text = _settings.ExcludeExtensions ?? Settings.DefaultExcludeExtensions;

            ApiKeyBox.TextChanged += (_, __) => { _settings.ApiKey = ApiKeyBox.Text.Trim(); _save(); };
            DirsBox.TextChanged += (_, __) => { _settings.IndexDirs = DirsBox.Text.Trim(); _save(); };
            MaxBox.TextChanged += (_, __) =>
            {
                if (int.TryParse(MaxBox.Text.Trim(), out int max) && max > 0)
                {
                    _settings.MaxFilesPerFolder = max;
                    _save();
                }
            };
            OtherDrivesBox.Checked += (_, __) => { _settings.IncludeOtherDrives = true; _save(); };
            OtherDrivesBox.Unchecked += (_, __) => { _settings.IncludeOtherDrives = false; _save(); };
            EverythingBox.Checked += (_, __) => { _settings.UseEverything = true; _save(); };
            EverythingBox.Unchecked += (_, __) => { _settings.UseEverything = false; _save(); };
            EverythingPathBox.TextChanged += (_, __) => { _settings.EverythingPath = EverythingPathBox.Text.Trim(); _save(); };
            ExcludeBox.TextChanged += (_, __) => { _settings.ExcludeExtensions = ExcludeBox.Text.Trim(); _save(); };
        }
    }
}
