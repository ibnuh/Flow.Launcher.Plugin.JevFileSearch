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

        /// <summary>
        /// Folders to index. Blank means Desktop, Downloads and Documents
        /// under the user profile. Only existing directories are returned.
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
    }
}
