using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    public enum SystemToggle
    {
        ToggleDarkMode,
        WifiOn,
        WifiOff,
        DoNotDisturb,
        Sleep,
        LockScreen,
        EmptyRecycleBin,
        ShowHiddenFiles,
        HideHiddenFiles
    }

    public sealed class ToggleInfo
    {
        public string Title;
        public string Subtitle;
        public string[] Keywords;
    }

    /// <summary>
    /// Fixed system actions executed by code (shell commands or settings pages).
    /// Never decided by Jev beyond ranking. Windows port of Executor.swift.
    /// </summary>
    public static class Toggles
    {
        public static readonly SystemToggle[] All =
        {
            SystemToggle.ToggleDarkMode,
            SystemToggle.WifiOn,
            SystemToggle.WifiOff,
            SystemToggle.DoNotDisturb,
            SystemToggle.Sleep,
            SystemToggle.LockScreen,
            SystemToggle.EmptyRecycleBin,
            SystemToggle.ShowHiddenFiles,
            SystemToggle.HideHiddenFiles,
        };

        public static ToggleInfo Get(SystemToggle toggle)
        {
            switch (toggle)
            {
                case SystemToggle.ToggleDarkMode:
                    return new ToggleInfo { Title = "Toggle Dark Mode", Subtitle = "Switch Windows between light and dark", Keywords = new[] { "dark", "light", "theme", "appearance", "night", "mode" } };
                case SystemToggle.WifiOn:
                    return new ToggleInfo { Title = "Turn Wi-Fi On", Subtitle = "Enable the Wi-Fi adapter", Keywords = new[] { "wifi", "wi-fi", "wireless", "network", "on", "enable", "connect" } };
                case SystemToggle.WifiOff:
                    return new ToggleInfo { Title = "Turn Wi-Fi Off", Subtitle = "Disable the Wi-Fi adapter", Keywords = new[] { "wifi", "wi-fi", "wireless", "network", "off", "disable", "airplane" } };
                case SystemToggle.DoNotDisturb:
                    return new ToggleInfo { Title = "Do Not Disturb", Subtitle = "Open Focus settings to silence notifications", Keywords = new[] { "dnd", "focus", "quiet", "silence", "notifications", "mute", "disturb" } };
                case SystemToggle.Sleep:
                    return new ToggleInfo { Title = "Sleep", Subtitle = "Put the PC to sleep now", Keywords = new[] { "sleep", "nap", "rest", "suspend", "standby" } };
                case SystemToggle.LockScreen:
                    return new ToggleInfo { Title = "Lock Screen", Subtitle = "Lock the PC immediately", Keywords = new[] { "lock", "afk", "away", "secure", "screen" } };
                case SystemToggle.EmptyRecycleBin:
                    return new ToggleInfo { Title = "Empty Recycle Bin", Subtitle = "Permanently delete everything in the Recycle Bin", Keywords = new[] { "trash", "bin", "recycle", "delete", "clean", "purge" } };
                case SystemToggle.ShowHiddenFiles:
                    return new ToggleInfo { Title = "Show Hidden Files", Subtitle = "Reveal hidden items in File Explorer", Keywords = new[] { "hidden", "dotfiles", "invisible", "reveal", "explorer" } };
                default:
                    return new ToggleInfo { Title = "Hide Hidden Files", Subtitle = "Hide hidden items in File Explorer", Keywords = new[] { "hidden", "dotfiles", "invisible", "conceal", "explorer" } };
            }
        }

        public static Candidate ToCandidate(SystemToggle toggle)
        {
            var info = Get(toggle);
            return new Candidate(
                "toggle:" + toggle,
                info.Title,
                info.Subtitle,
                ActionKind.SystemToggle,
                info.Keywords.ToList(),
                PayloadKind.Toggle,
                toggle: toggle);
        }

        public static List<Candidate> AllCandidates()
        {
            return All.Select(ToCandidate).ToList();
        }

        public static void Run(SystemToggle toggle)
        {
            switch (toggle)
            {
                case SystemToggle.ToggleDarkMode:
                    Shell("powershell", "-NoProfile -Command \"$v=(Get-ItemProperty HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize).AppsUseLightTheme; Set-ItemProperty HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize AppsUseLightTheme (1-$v); Set-ItemProperty HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize SystemUsesLightTheme (1-$v)\"");
                    break;
                case SystemToggle.WifiOn:
                    Shell("netsh", "interface set interface \"Wi-Fi\" admin=enable");
                    break;
                case SystemToggle.WifiOff:
                    Shell("netsh", "interface set interface \"Wi-Fi\" admin=disable");
                    break;
                case SystemToggle.DoNotDisturb:
                    Open("ms-settings:quiethours");
                    break;
                case SystemToggle.Sleep:
                    Shell("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
                    break;
                case SystemToggle.LockScreen:
                    Shell("rundll32.exe", "user32.dll,LockWorkStation");
                    break;
                case SystemToggle.EmptyRecycleBin:
                    Shell("powershell", "-NoProfile -Command \"Clear-RecycleBin -Force -ErrorAction SilentlyContinue\"");
                    break;
                case SystemToggle.ShowHiddenFiles:
                    Shell("powershell", "-NoProfile -Command \"Set-ItemProperty HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced Hidden 1; Stop-Process -Name explorer -Force\"");
                    break;
                default:
                    Shell("powershell", "-NoProfile -Command \"Set-ItemProperty HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced Hidden 2; Stop-Process -Name explorer -Force\"");
                    break;
            }
        }

        private static void Shell(string file, string args)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        private static void Open(string path)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
    }
}
