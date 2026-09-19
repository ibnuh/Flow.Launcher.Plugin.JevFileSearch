using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Integration with the Everything index (https://www.voidtools.com) through its
    /// command line client es.exe. Everything is the index that makes whole-volume file
    /// search possible on Windows, so when it is present it supplies the candidates and
    /// the plugin's own folder scan becomes the fallback instead of the main source.
    ///
    /// es.exe flags vary between Everything 1.4 and 1.5, and the search syntax is not
    /// what people type, so the query is planned in code (EverythingQuery) and each plan
    /// is tried against a few argument variants until one returns something. Every
    /// failure path returns an empty list rather than an error, because Everything is a
    /// bonus and the local index still works without it.
    /// </summary>
    public static class Everything
    {
        public const int DefaultLimit = 60;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

        /// <summary>Exit codes from the Everything CLI docs.</summary>
        private const int ExitBadSwitch = 6;
        private const int ExitBadOption = 4;
        private const int ExitNotRunning = 8;

        private static bool _probed;
        private static string _cachedPath;

        public static string LastDiagnostics { get; private set; } = "";

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

        /// <summary>Argument variants, newest Everything first, 1.4 compatible last.</summary>
        private static List<string> ArgumentVariants(int limit)
        {
            return new List<string>
            {
                "-n " + limit + " -sort date-modified -a-d",
                "-n " + limit + " -sort date-modified",
                "-n " + limit + " -a-d",
                "-n " + limit,
            };
        }

        public static List<Candidate> Search(string userQuery, Settings settings)
        {
            LastDiagnostics = "";
            string exe = LocateExe(settings);
            if (exe == null)
            {
                LastDiagnostics = "es.exe not found";
                return new List<Candidate>();
            }
            if (string.IsNullOrWhiteSpace(userQuery))
                return new List<Candidate>();

            int limit = settings != null && settings.EverythingLimit > 0
                ? settings.EverythingLimit
                : DefaultLimit;
            var plans = EverythingQuery.Plan(userQuery);
            if (plans.Count == 0)
                return new List<Candidate>();

            foreach (var variant in ArgumentVariants(limit))
            {
                bool switchUnsupported = false;
                foreach (var plan in plans)
                {
                    int exitCode;
                    var lines = Run(exe, variant, plan, out exitCode);
                    if (exitCode == ExitNotRunning)
                    {
                        LastDiagnostics = "es.exe: Everything client is not running (exit 8)";
                        return new List<Candidate>();
                    }
                    if (exitCode == ExitBadSwitch || exitCode == ExitBadOption)
                    {
                        switchUnsupported = true;
                        break;
                    }
                    if (exitCode == 0 && lines.Count > 0)
                    {
                        var candidates = Parse(lines, settings);
                        LastDiagnostics = "es.exe \"" + variant + "\" plan \"" + plan + "\" -> "
                            + candidates.Count + " usable of " + lines.Count + " lines";
                        if (candidates.Count > 0)
                            return candidates;
                    }
                }
                if (switchUnsupported)
                {
                    LastDiagnostics = "es.exe rejected a switch in \"" + variant + "\", trying a simpler variant";
                    continue;
                }
            }

            if (LastDiagnostics.Length == 0)
                LastDiagnostics = "es.exe returned no usable paths for \"" + userQuery + "\"";
            return new List<Candidate>();
        }

        private static List<Candidate> Parse(List<string> lines, Settings settings)
        {
            var candidates = new List<Candidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var now = DateTime.Now;
            foreach (var line in lines)
            {
                string path = line.Trim().Trim('"');
                if (path.Length == 0 || path.Length > 400)
                    continue;
                bool hasSeparator = path.IndexOf('\\') >= 0 || path.IndexOf('/') >= 0;
                if (!hasSeparator || !Path.IsPathRooted(path))
                    continue;
                if (!seen.Add(path))
                    continue;
                if (settings != null && settings.IsJunk(path))
                    continue;
                candidates.Add(LocalIndex.FileCandidate(path, now));
            }
            return candidates;
        }

        private static List<string> Run(string exe, string variant, string plan, out int exitCode)
        {
            exitCode = -1;
            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = variant + " " + Quote(plan),
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
                        if (stdout.Length > 400000)
                            break;
                    }

                    if (!process.WaitForExit(500))
                    {
                        try { process.Kill(); } catch { }
                        exitCode = -2;
                        return new List<string>();
                    }

                    exitCode = process.ExitCode;
                    return stdout.ToString()
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                        .ToList();
                }
            }
            catch
            {
                exitCode = -3;
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
