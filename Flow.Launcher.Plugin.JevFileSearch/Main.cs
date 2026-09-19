using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <inheritdoc cref="Flow.Launcher.Plugin.IAsyncPlugin" />
    public class Main : IAsyncPlugin, ISettingProvider
    {
        private PluginInitContext Context { get; set; }
        private Settings _settings = new Settings();
        private HabitStore _habits;
        private List<Candidate> _index = new List<Candidate>();
        private DateTime _indexBuiltAt = DateTime.MinValue;

        private static readonly TimeSpan IndexRefreshAfter = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan EverythingCacheTtl = TimeSpan.FromSeconds(20);
        private static readonly Dictionary<string, EverythingCacheEntry> EverythingCache =
            new Dictionary<string, EverythingCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly object EverythingCacheLock = new object();

        private const string Icon = "images/icon.png";

        private sealed class EverythingCacheEntry
        {
            public DateTime At;
            public List<Candidate> Candidates;
            public string Diagnostics;
        }

        /// <inheritdoc />
        public Task InitAsync(PluginInitContext context)
        {
            Context = context;
            try
            {
                _settings = context.API.LoadSettingJsonStorage<Settings>();
            }
            catch
            {
                _settings = new Settings();
            }
            try
            {
                _habits = HabitStore.Load(Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "habit.json"));
            }
            catch
            {
                _habits = HabitStore.Load(Path.Combine(Path.GetTempPath(), "jev-files-habit.json"));
            }
            Task.Run(() => RebuildIndex());
            return Task.CompletedTask;
        }

        public Control CreateSettingPanel()
        {
            return new SettingsControl(_settings, SaveSettings);
        }

        private void SaveSettings()
        {
            try { Context.API.SaveSettingJsonStorage<Settings>(); } catch { }
            Task.Run(() => RebuildIndex());
        }

        private void RebuildIndex()
        {
            try
            {
                var built = LocalIndex.Build(_settings);
                lock (_index)
                {
                    _index = built;
                    _indexBuiltAt = DateTime.Now;
                }
            }
            catch { }
        }

        private List<Candidate> SnapshotIndex()
        {
            lock (_index)
                return _index.ToList();
        }

        /// <summary>
        /// Everything is the primary source when es.exe is available: it indexes whole
        /// volumes, including drives the folder scan never touches. Results are cached
        /// briefly because Flow re-queries on every keystroke.
        /// </summary>
        private static List<Candidate> EverythingCandidates(string query, Settings settings, out string diagnostics)
        {
            diagnostics = "";
            if (settings == null || !settings.UseEverything || query.Length < 2)
                return new List<Candidate>();

            lock (EverythingCacheLock)
            {
                if (EverythingCache.TryGetValue(query, out var cached) &&
                    DateTime.UtcNow - cached.At < EverythingCacheTtl)
                {
                    diagnostics = cached.Diagnostics;
                    return cached.Candidates;
                }
            }

            var found = Everything.Search(query, settings);
            diagnostics = Everything.LastDiagnostics;
            lock (EverythingCacheLock)
            {
                if (EverythingCache.Count > 40)
                    EverythingCache.Clear();
                EverythingCache[query] = new EverythingCacheEntry
                {
                    At = DateTime.UtcNow,
                    Candidates = found,
                    Diagnostics = diagnostics,
                };
            }
            return found;
        }

        /// <inheritdoc />
        public async Task<List<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
        {
            string raw = (query.Search ?? "").Trim();
            if (raw.Length == 0)
                return EmptyStateResults();

            EverythingQuery.ParseVerb(raw, out var mode, out string search);
            if (search.Length == 0)
                return EmptyStateResults();

            if (DateTime.Now - _indexBuiltAt > IndexRefreshAfter)
                Task.Run(() => RebuildIndex());

            var results = new List<Result>();
            try
            {
                var pool = SnapshotIndex();
                string everythingDiagnostics;
                var everything = await Task.Run(
                        () => EverythingCandidates(search, _settings, out everythingDiagnostics),
                        cancellationToken)
                    .ConfigureAwait(false);
                if (everything.Count > 0)
                    pool.AddRange(everything);

                var prefiltered = Ranker.Prefilter(search, pool);
                if (prefiltered.Candidates.Count == 0)
                {
                    results.Add(new Result
                    {
                        Title = "No file, folder or app matches \"" + search + "\"",
                        SubTitle = "Everything finds top 60 matches only; try fewer words or a shorter prefix",
                        IcoPath = Icon,
                    });
                    return results;
                }

                JevJudgment judgment = null;
                double latencyMs = 0;
                string apiKey = JevClient.ResolveApiKey(_settings);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    try
                    {
                        var request = JevQuestions.BuildRequest(search, LaunchContext.Current(), prefiltered.Candidates);
                        var ask = await JevClient.AskAsync(request, apiKey, cancellationToken).ConfigureAwait(false);
                        cancellationToken.ThrowIfCancellationRequested();
                        judgment = JevQuestions.Parse(ask.Response, prefiltered.Candidates);
                        latencyMs = ask.LatencyMs;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Fall through to local ranking.
                        judgment = null;
                    }
                }

                var hits = Ranker.Rank(prefiltered, judgment, _habits);
                foreach (var hit in hits)
                {
                    var candidate = hit.Candidate;
                    var details = new List<string>();
                    details.Add(candidate.Subtitle);
                    if (hit.JevProbability.HasValue)
                        details.Add("Jev " + hit.JevProbability.Value.ToString("P0"));
                    if (hit.Habit > 0.35)
                        details.Add("habit");
                    results.Add(new Result
                    {
                        Title = candidate.Title,
                        SubTitle = string.Join("  \u00b7  ", details),
                        IcoPath = candidate.IconPath,
                        Score = Math.Max(0, Math.Min(100, (int)Math.Round(hit.Score * 100))),
                        Action = CreateOpenAction(candidate, mode, _habits),
                    });
                }

                if (judgment != null && hits.Count > 0)
                {
                    double topProb = hits[0].JevProbability ?? 0;
                    if (Ranker.IsReady(judgment, topProb))
                        results[0].Title = "\u21b5 " + results[0].Title;
                }

                results.Add(FallbackRow(mode, search, hits.Count, apiKey, judgment, latencyMs, everythingDiagnostics));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new Result
                {
                    Title = "Jev File Search hit an error",
                    SubTitle = ex.Message,
                    IcoPath = Icon,
                });
            }

            return results;
        }

        /// <summary>Bottom row: what Enter will do, where the candidates came from, and Jev cost.</summary>
        private static Result FallbackRow(SearchMode mode, string search, int hitCount, string apiKey,
            JevJudgment judgment, double latencyMs, string everythingDiagnostics)
        {
            var parts = new List<string>();
            if (mode != SearchMode.Open)
                parts.Add(mode.Hint());
            if (!string.IsNullOrEmpty(everythingDiagnostics))
                parts.Add(everythingDiagnostics);

            string title;
            if (judgment != null)
            {
                double cost = judgment.InputTokens / 1000000.0 * 0.042;
                title = "Jev " + latencyMs.ToString("F0") + " ms \u00b7 ~$" + cost.ToString("F4")
                    + " \u00b7 " + hitCount + " ranked";
            }
            else if (string.IsNullOrEmpty(apiKey))
            {
                title = hitCount + " ranked locally \u00b7 no Jev key, fuzzy order with recency and habit";
                parts.Add("Set TYPESAFE_API_KEY or add the key in settings for intent reranking");
            }
            else
            {
                title = hitCount + " ranked locally \u00b7 Jev did not answer this keystroke";
            }
            parts.Add("try: folder " + search + " \u00b7 copy " + search);

            return new Result
            {
                Title = title,
                SubTitle = string.Join("  \u00b7  ", parts.Where(p => !string.IsNullOrWhiteSpace(p))),
                IcoPath = Icon,
                Score = 0,
            };
        }

        private static List<Result> EmptyStateResults()
        {
            var examples = new[]
            {
                "the pdf I just downloaded",
                "latest screenshot",
                "invoice",
                "folder downloads",
                "copy roadmap",
                "dark",
            };
            var results = new List<Result>
            {
                new Result
                {
                    Title = "Search files, folders and apps by what you mean",
                    SubTitle = "Examples below run immediately, Enter opens the highlighted row",
                    IcoPath = Icon,
                    Score = 100,
                }
            };
            int score = 90;
            foreach (var example in examples)
            {
                string command = example;
                results.Add(new Result
                {
                    Title = "jv " + example,
                    SubTitle = (command.StartsWith("folder ") || command.StartsWith("copy "))
                        ? "Prefix sets what Enter does: folder opens the location, copy copies the path"
                        : "Jev reranks the local candidates by intent",
                    IcoPath = Icon,
                    Score = score--,
                    Action = _ => false,
                });
            }
            results.Add(new Result
            {
                Title = "Everything index: " + (Everything.LocateExe(new Settings()) ?? "es.exe not found")
                    + " \u00b7 limit " + new Settings().EverythingLimit,
                SubTitle = "Everything supplies whole-volume candidates when installed",
                IcoPath = Icon,
                Score = 0,
            });
            return results;
        }

        private static Func<ActionContext, bool> CreateOpenAction(Candidate candidate, SearchMode mode, HabitStore habits)
        {
            return _ =>
            {
                try
                {
                    if (candidate.Payload == PayloadKind.Toggle)
                    {
                        Toggles.Run(candidate.Toggle);
                    }
                    else if (!string.IsNullOrEmpty(candidate.Path))
                    {
                        if (mode == SearchMode.CopyPath)
                        {
                            Clipboard.SetDataObject(candidate.Path);
                            return false;
                        }
                        if (mode == SearchMode.Folder)
                        {
                            bool isDirectory = candidate.IsDirectory;
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "explorer.exe",
                                Arguments = isDirectory
                                    ? "\"" + candidate.Path + "\""
                                    : "/select,\"" + candidate.Path + "\"",
                                UseShellExecute = true,
                            });
                            if (habits != null)
                                habits.Record(candidate.Id);
                            return true;
                        }
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = candidate.Path,
                            UseShellExecute = true,
                        });
                    }
                    if (habits != null)
                        habits.Record(candidate.Id);
                }
                catch { }
                return true;
            };
        }
    }
}
