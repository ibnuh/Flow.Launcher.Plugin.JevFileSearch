using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    public class Settings
    {
        public string ApiKey { get; set; } = "";
        public string IndexDirs { get; set; } = "";
        public int MaxFilesPerFolder { get; set; } = 400;

        /// <summary>Prefer the Everything index when es.exe is available.</summary>
        public bool UseEverything { get; set; } = true;

        /// <summary>Path to es.exe. Blank means auto-detect from PATH and the usual install folders.</summary>
        public string EverythingPath { get; set; } = "";

        /// <summary>Include non-system fixed drives in the local folder scan.</summary>
        public bool IncludeOtherDrives { get; set; } = true;

        /// <summary>Semicolon separated, dots optional. Files with these extensions are never indexed.</summary>
        public string ExcludeExtensions { get; set; } = DefaultExcludeExtensions;

        public const string DefaultExcludeExtensions =
            "lnk;url;tmp;temp;log;crdownload;part;partial;bak;old;sys;dll;ini;db;dat;msi;msp;manifest;pf;etl;pdb";

        /// <summary>Start Menu shortcut names that are installer or documentation noise, not apps.</summary>
        private static readonly string[] AppLinkNoise =
        {
            "uninstall", "unins", "readme", "read me", "help", "help center", "license", "licence",
            "release notes", "changelog", "change log", "website", "homepage", "documentation",
            "docs", "support", "repair", "setup", "install", "manual", "report a problem",
        };

        public static bool IsAppLinkNoise(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return true;
            string lower = title.Trim().ToLowerInvariant();
            foreach (var noise in AppLinkNoise)
            {
                if (lower == noise
                    || lower.StartsWith(noise + " ", StringComparison.Ordinal)
                    || lower.EndsWith(" " + noise, StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public HashSet<string> ExcludedExtensionSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in (ExcludeExtensions ?? "").Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var ext = raw.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext))
                    set.Add(ext);
            }
            return set;
        }

        public bool IsExcludedFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return true;
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
                return true;
            // Hidden files and editor/Office temp files.
            if (name.StartsWith("~$") || name.StartsWith("."))
                return true;
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)
                || name.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase))
                return true;
            return IsExcludedExtension(Path.GetExtension(name));
        }

        public bool IsExcludedExtension(string extension)
        {
            string ext = (extension ?? "").Trim().TrimStart('*').TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
                return false;
            return ExcludedExtensionSet().Contains(ext);
        }

        /// <summary>
        /// Folders to index. Blank means Desktop, Downloads and Documents under the
        /// user profile. Only existing directories are returned.
        /// </summary>
        public List<string> EffectiveDirs()
        {
            if (!string.IsNullOrWhiteSpace(IndexDirs))
            {
                return IndexDirs
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => d.Trim().Trim('"'))
                    .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                    .ToList();
            }

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { "Desktop", "Downloads", "Documents" }
                .Select(name => Path.Combine(home, name))
                .Where(Directory.Exists)
                .ToList();
        }

        /// <summary>
        /// Fixed drives other than the one Windows is installed on, scanned shallowly so
        /// files on D:\ and friends are searchable without extra setup.
        /// </summary>
        public List<string> OtherFixedDriveRoots()
        {
            var roots = new List<string>();
            if (!IncludeOtherDrives)
                return roots;
            string systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            try
            {
                foreach (var drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                        continue;
                    if (string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
                        continue;
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch { }
            return roots;
        }
    }
}
