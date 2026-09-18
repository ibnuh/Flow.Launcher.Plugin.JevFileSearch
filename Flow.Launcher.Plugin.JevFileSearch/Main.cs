using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <inheritdoc cref="Flow.Launcher.Plugin.IAsyncPlugin" />
    public class Main : IAsyncPlugin, ISettingProvider
    {
        private PluginInitContext Context { get; set; }
        private Settings _settings = new Settings();
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
        /// Everything's index covers whole volumes, including non-system drives, so it
        /// supplies extra candidates whenever es.exe is available. Results are cached
        /// briefly so a per-keystroke query does not relaunch the process each time.
        /// </summary>
        private static List<Candidate> EverythingCandidates(string query, Settings settings)
        {
            if (settings == null || !settings.UseEverything || query.Length < 2)
                return new List<Candidate>();

            lock (EverythingCacheLock)
            {
                if (EverythingCache.TryGetValue(query, out var cached) &&
                    DateTime.UtcNow - cached.At < EverythingCacheTtl)
                    return cached.Candidates;
            }

            var found = Everything.Search(query, settings);
            lock (EverythingCacheLock)
            {
                if (EverythingCache.Count > 40)
                    EverythingCache.Clear();
                EverythingCache[query] = new EverythingCacheEntry { At = DateTime.UtcNow, Candidates = found };
            }
            return found;
        }

        /// <inheritdoc />
        public async Task<List<Result>> QueryAsync(Query query, CancellationToken cancellationToken)
        {
            string search = (query.Search ?? "").Trim();
            if (string.IsNullOrEmpty(search))
            {
                return new List<Result>
                {
                    new Result
                    {
                        Title = "Type a file, app, or phrase like 'the pdf I just downloaded'",
                        SubTitle = "Jev File Search: fuzzy locally, Jev reranks by intent",
                        IcoPath = Icon,
                    }
                };
            }

            if (DateTime.Now - _indexBuiltAt > IndexRefreshAfter)
                Task.Run(() => RebuildIndex());

            var results = new List<Result>();
            try
            {
                var pool = SnapshotIndex();
                var everything = await Task.Run(() => EverythingCandidates(search, _settings), cancellationToken)
                    .ConfigureAwait(false);
                if (everything.Count > 0)
                    pool.AddRange(everything);

                var prefiltered = Ranker.Prefilter(search, pool);
                if (prefiltered.Candidates.Count == 0)
                {
                    results.Add(new Result
                    {
                        Title = "No files or apps match \"" + search + "\"",
                        SubTitle = "Try fewer words, or add folders in the plugin settings",
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
                        if (judgment != null)
                        {
                            judgment.InputTokens = ask.Response?.Usage?.InputTokens ?? 0;
                            judgment.OutputTokens = ask.Response?.Usage?.OutputTokens ?? 0;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // Fall through to fuzzy-only ranking.
                        judgment = null;
                    }
                }

                var hits = Ranker.Rank(prefiltered, judgment);
                foreach (var hit in hits)
                {
                    var candidate = hit.Candidate;
                    string sub = candidate.Subtitle;
                    if (hit.JevProbability.HasValue)
                        sub += "  \u00b7  Jev " + hit.JevProbability.Value.ToString("P0");
                    var result = new Result
                    {
                        Title = candidate.Title,
                        SubTitle = sub,
                        IcoPath = Icon,
                        Score = Math.Max(0, Math.Min(100, (int)Math.Round(hit.Score * 100))),
                        Action = CreateOpenAction(candidate),
                    };
                    results.Add(result);
                }

                if (judgment != null && hits.Count > 0)
                {
                    double topProb = hits[0].JevProbability ?? 0;
                    if (Ranker.IsReady(judgment, topProb))
                        results[0].Title = "\u21b5 " + results[0].Title;

                    // Live footer mirroring jev-launcher: last round-trip and cost estimate.
                    double cost = judgment.InputTokens / 1000000.0 * 0.042;
                    results.Add(new Result
                    {
                        Title = "Jev " + latencyMs.ToString("F0") + " ms \u00b7 ~$" + cost.ToString("F4"),
                        SubTitle = apiKey == null ? "" : "Intent rerank by Jev; fuzzy order is the fallback",
                        IcoPath = Icon,
                        Score = 0,
                    });
                }
                else if (string.IsNullOrEmpty(apiKey))
                {
                    results.Add(new Result
                    {
                        Title = "Jev API key is not set, showing local fuzzy order",
                        SubTitle = "Set TYPESAFE_API_KEY or add the key in the plugin settings",
                        IcoPath = Icon,
                        Score = 0,
                    });
                }
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

        private static Func<ActionContext, bool> CreateOpenAction(Candidate candidate)
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
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = candidate.Path,
                            UseShellExecute = true,
                        });
                    }
                }
                catch { }
                return true;
            };
        }
    }
}
