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
        }
    }
}
