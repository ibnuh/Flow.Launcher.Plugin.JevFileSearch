using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Builds the local candidate index: Start Menu apps, recent user files,
    /// and system toggles. Pure code, no model involved.
    /// Windows port of LocalIndex.swift from dabit3/jev-experiments/jev-launcher.
    /// </summary>
    public static class LocalIndex
    {
        public static List<Candidate> Build(Settings settings)
        {
            var all = new List<Candidate>();
            all.AddRange(ScanApps());
            all.AddRange(ScanFiles(settings));
            all.AddRange(Toggles.AllCandidates());
            return all;
        }

        private static List<Candidate> ScanApps()
        {
            var roots = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            };

            var seen = new HashSet<string>();
            var apps = new List<Candidate>();
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                    continue;
                var top = new List<string>();
                try { top.AddRange(Directory.GetFiles(root, "*.lnk")); } catch { }
                var sublinks = new List<string>();
                try
                {
                    foreach (var dir in Directory.GetDirectories(root))
                    {
                        try { sublinks.AddRange(Directory.GetFiles(dir, "*.lnk")); } catch { }
                    }
                }
                catch { }
                foreach (var link in top.Concat(sublinks))
                {
                    var title = Path.GetFileNameWithoutExtension(link);
                    if (string.IsNullOrWhiteSpace(title))
                        continue;
                    if (!seen.Add(title.ToLowerInvariant()))
                        continue;
                    apps.Add(new Candidate(
                        "app:" + link,
                        title,
                        "Application",
                        ActionKind.OpenApp,
                        new List<string> { "app", "application" },
                        PayloadKind.App,
                        path: link));
                }
            }
            return apps;
        }

        private static List<Candidate> ScanFiles(Settings settings)
        {
            var files = new List<Candidate>();
            var now = DateTime.Now;
            foreach (var folder in settings.EffectiveDirs())
            {
                if (!Directory.Exists(folder))
                    continue;
                var urls = new List<string>();
                try
                {
                    urls.AddRange(Directory.GetFileSystemEntries(folder));
                    foreach (var dir in Directory.GetDirectories(folder))
                    {
                        try { urls.AddRange(Directory.GetFileSystemEntries(dir)); } catch { }
                    }
                }
                catch { continue; }

                var folderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
                foreach (var entry in urls.Take(settings.MaxFilesPerFolder))
                    files.Add(FileCandidate(entry, folderName, now));
            }
            return files;
        }

        private static Candidate FileCandidate(string entry, string folder, DateTime now)
        {
            bool isDir = Directory.Exists(entry);
            DateTime modified;
            try { modified = isDir ? Directory.GetLastWriteTime(entry) : File.GetLastWriteTime(entry); }
            catch { modified = now; }

            double ageDays = Math.Max(0, (now - modified).TotalDays);
            string name = Path.GetFileName(entry);
            string ext = isDir ? "" : Path.GetExtension(entry).TrimStart('.').ToLowerInvariant();

            var keywords = new List<string> { folder.ToLowerInvariant(), "file" };
            if (folder.Equals("Downloads", StringComparison.OrdinalIgnoreCase))
                keywords.AddRange(new[] { "downloaded", "download" });
            if (isDir)
            {
                keywords.Add("folder");
            }
            else if (!string.IsNullOrEmpty(ext))
            {
                keywords.Add(ext);
                keywords.AddRange(FileTypeWords(ext));
            }
            if (ageDays < 1)
                keywords.AddRange(new[] { "recent", "latest", "new", "today" });

            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string location = Path.GetDirectoryName(entry) ?? folder;
            if (location.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                location = "~" + location.Substring(home.Length);

            string label = isDir ? "Folder" : FileTypeLabel(ext);
            string subtitle = label + " in " + location + " \u00b7 " + Recency(ageDays);

            return new Candidate(
                "file:" + entry,
                name,
                subtitle,
                ActionKind.OpenFile,
                keywords,
                PayloadKind.File,
                path: entry,
                ageDays: ageDays);
        }

        private static string[] FileTypeWords(string ext)
        {
            switch (ext)
            {
                case "pdf": return new[] { "document", "paper" };
                case "png":
                case "jpg":
                case "jpeg":
                case "gif":
                case "heic":
                case "webp":
                case "bmp":
                    return new[] { "image", "picture", "photo", "screenshot" };
                case "mov":
                case "mp4":
                case "m4v":
                case "mkv":
                    return new[] { "video", "movie", "recording" };
                case "zip":
                case "7z":
                case "rar":
                case "msi":
                case "exe":
                case "tar":
                case "gz":
                    return new[] { "archive", "installer" };
                case "md":
                case "txt":
                case "rtf":
                    return new[] { "text", "notes" };
                case "csv":
                case "xlsx":
                case "xls":
                    return new[] { "spreadsheet", "data" };
                case "docx":
                case "doc":
                case "pptx":
                case "ppt":
                    return new[] { "document" };
                case "cs":
                case "ts":
                case "js":
                case "py":
                case "java":
                    return new[] { "code", "source" };
                default: return new string[0];
            }
        }

        private static string FileTypeLabel(string ext)
        {
            return string.IsNullOrEmpty(ext) ? "File" : ext.ToUpperInvariant();
        }

        /// <summary>Human-readable recency phrase. Jev reads recency far better as words than as timestamps.</summary>
        public static string Recency(double ageDays)
        {
            double minutes = ageDays * 24 * 60;
            if (minutes < 2) return "modified just now";
            if (minutes < 60) return "modified " + (int)minutes + " min ago";
            if (ageDays < 1) return "modified " + (int)(minutes / 60) + " h ago";
            if (ageDays < 2) return "modified yesterday";
            if (ageDays < 30) return "modified " + Plural((int)ageDays, "day") + " ago";
            if (ageDays < 365) return "modified " + Plural((int)(ageDays / 30), "month") + " ago";
            return "modified over a year ago";
        }

        private static string Plural(int count, string unit)
        {
            return count == 1 ? "1 " + unit : count + " " + unit + "s";
        }
    }
}
