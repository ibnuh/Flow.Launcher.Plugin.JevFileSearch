using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Optional integration with the Everything index (https://www.voidtools.com)
    /// through its command line client es.exe. When present, Everything supplies the
    /// candidate shortlist instead of the plugin's own folder scan, which covers
    /// every indexed volume including non-system drives.
    /// </summary>
    public static class Everything
    {
        public const int DefaultLimit = 60;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        private static bool _probed;
        private static string _cachedPath;

        /// <summary>es.exe from settings, then PATH, then the usual install folders.</summary>
        public static string LocateExe(Settings settings)
        {
            if (settings != null && !string.IsNullOrWhiteSpace(settings.EverythingPath))
            {
                string configured = Environment.ExpandEnvironmentVariables(settings.EverythingPath.Trim().Trim('"'));
                if (File.Exists(configured))
                    return configured;
            }
            if (_probed)
                return _cachedPath;

            var candidates = new List<string>();
            foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                try { candidates.Add(Path.Combine(dir.Trim(), "es.exe")); } catch { }
            }
            candidates.Add(@"C:\Program Files\Everything\es.exe");
            candidates.Add(@"C:\Program Files (x86)\Everything\es.exe");
            candidates.Add(@"C:\Program Files\Everything 1.5a\es.exe");
            candidates.Add(@"C:\Program Files (x86)\Everything 1.5a\es.exe");

            _cachedPath = candidates.FirstOrDefault(File.Exists);
            _probed = true;
            return _cachedPath;
        }

        public static bool IsAvailable(Settings settings)
        {
            return LocateExe(settings) != null;
        }

        /// <summary>
        /// Runs `es.exe -n &lt;limit&gt; -sort date-modified &lt;query&gt;` and turns each
        /// returned path into an open_file candidate. Returns an empty list when
        /// Everything is missing, not running, or returns nothing usable.
        /// </summary>
        public static List<Candidate> Search(string query, Settings settings, int limit = DefaultLimit)
        {
            string exe = LocateExe(settings);
            if (exe == null || string.IsNullOrWhiteSpace(query))
                return new List<Candidate>();

            var lines = Run(exe, query, limit);
            if (lines.Count == 0)
                return new List<Candidate>();

            var candidates = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;
            foreach (var line in lines)
            {
                string path = line.Trim();
                if (path.Length == 0 || path.Length > 400)
                    continue;
                // Keep only lines that look like absolute paths.
                bool hasSep = path.IndexOf('\\') >= 0 || path.IndexOf('/') >= 0;
                if (!hasSep || !Path.IsPathRooted(path))
                    continue;
                if (!seen.Add(path))
                    continue;
                if (settings != null && settings.IsExcludedFile(path))
                    continue;
                candidates.Add(LocalIndex.FileCandidate(path, now));
            }
            return candidates;
        }

        private static List<string> Run(string exe, string query, int limit)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = "-n " + limit.ToString() + " -sort date-modified " + Quote(query),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return new List<string>();
                    var stdout = new StringBuilder();
                    var buffer = new char[4096];
                    var reader = process.StandardOutput;
                    var deadline = DateTime.UtcNow + Timeout;
                    while (!reader.EndOfStream && DateTime.UtcNow < deadline)
                    {
                        int read = reader.Read(buffer, 0, buffer.Length);
                        if (read <= 0)
                            break;
                        stdout.Append(buffer, 0, read);
                        if (stdout.Length > 200000)
                            break;
                    }
                    if (!process.WaitForExit(500))
                    {
                        try { process.Kill(); } catch { }
                        return new List<string>();
                    }
                    return stdout.ToString()
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string Quote(string value)
        {
            if (value.IndexOf(' ') < 0)
                return value;
            return "\"" + value.Replace("\"", "") + "\"";
        }
    }
}
