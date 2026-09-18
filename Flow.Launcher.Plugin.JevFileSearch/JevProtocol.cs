using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    public sealed class LaunchContext
    {
        [JsonPropertyName("frontmost_app")]
        public string FrontmostApp { get; set; }

        [JsonPropertyName("recent_apps")]
        public List<string> RecentApps { get; set; }

        [JsonPropertyName("clipboard_kind")]
        public string ClipboardKind { get; set; }

        [JsonPropertyName("time_of_day")]
        public string TimeOfDay { get; set; }

        [JsonPropertyName("weekday")]
        public string Weekday { get; set; }

        public static LaunchContext Current()
        {
            var now = DateTime.Now;
            return new LaunchContext
            {
                FrontmostApp = "Flow Launcher",
                RecentApps = new List<string> { "Flow Launcher" },
                ClipboardKind = "unknown",
                TimeOfDay = TimeOfDayFor(now.Hour),
                Weekday = now.DayOfWeek.ToString(),
            };
        }

        private static string TimeOfDayFor(int hour)
        {
            if (hour >= 5 && hour < 12) return "morning";
            if (hour >= 12 && hour < 17) return "afternoon";
            if (hour >= 17 && hour < 22) return "evening";
            return "night";
        }
    }

    public sealed class JevQuestion
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("instructions")]
        public string Instructions { get; set; }

        [JsonPropertyName("criteria")]
        public Dictionary<string, string> Criteria { get; set; }
    }

    public sealed class CandidateSummary
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("kind")]
        public string Kind { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("detail")]
        public string Detail { get; set; }
    }

    public sealed class JevState
    {
        [JsonPropertyName("query")]
        public string Query { get; set; }

        [JsonPropertyName("query_note")]
        public string QueryNote { get; set; }

        [JsonPropertyName("context")]
        public LaunchContext Context { get; set; }

        [JsonPropertyName("candidates")]
        public List<CandidateSummary> Candidates { get; set; }
    }

    public sealed class JevRequest
    {
        [JsonPropertyName("state")]
        public JevState State { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("questions")]
        public Dictionary<string, JevQuestion> Questions { get; set; }
    }

    public sealed class JevAnswer
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("choice")]
        public string Choice { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("probabilities")]
        public Dictionary<string, double> Probabilities { get; set; }

        [JsonPropertyName("noul")]
        public double? Noul { get; set; }
    }

    public sealed class JevUsage
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; set; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; set; }
    }

    public sealed class JevResponse
    {
        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("answers")]
        public Dictionary<string, JevAnswer> Answers { get; set; }

        [JsonPropertyName("usage")]
        public JevUsage Usage { get; set; }
    }

    /// <summary>The typed judgment extracted from a response, keyed back to real candidate ids.</summary>
    public sealed class JevJudgment
    {
        /// <summary>Probability that each candidate is the intended target. Missing candidates are 0.</summary>
        public Dictionary<string, double> TargetProbabilities = new Dictionary<string, double>();
        public double NoneProbability;
        public double TargetConfidence;
        public ActionKind Action = ActionKind.Unclear;
        public Dictionary<ActionKind, double> ActionProbabilities = new Dictionary<ActionKind, double>();
        public double ActionConfidence;
        /// <summary>Probability that Enter should execute the top hit without the user needing to see a list.</summary>
        public double Ready;
        public int InputTokens;
        public int OutputTokens;
    }

    /// <summary>
    /// Builds one fan-out request: three independent questions over one state.
    /// Wording follows the iterated prompts in dabit3/jev-experiments/jev-launcher,
    /// adapted from Spotlight/macOS to Flow Launcher/Windows.
    /// </summary>
    public static class JevQuestions
    {
        public const string Model = "jev-latest";
        public const string NoneOption = "none";
        public const int MaxCandidates = 15;

        public const string QueryNote =
            "Text the user has typed so far into a Flow Launcher query box on Windows. " +
            "It is often an incomplete prefix or a short natural-language phrase.";

        public static JevRequest BuildRequest(string query, LaunchContext context, List<Candidate> candidates)
        {
            var shown = candidates.Take(MaxCandidates).ToList();
            var summaries = new List<CandidateSummary>();
            var targetCriteria = new Dictionary<string, string>();
            for (int i = 0; i < shown.Count; i++)
            {
                string shortId = "c" + i;
                summaries.Add(new CandidateSummary
                {
                    Id = shortId,
                    Kind = shown[i].Kind.Raw(),
                    Title = shown[i].Title,
                    Detail = shown[i].Subtitle,
                });
                targetCriteria[shortId] = shown[i].Kind.Label() + ": " + shown[i].Title + " \u2014 " + shown[i].Subtitle;
            }
            targetCriteria[NoneOption] = "None of the listed candidates is what the user means.";

            var actionCriteria = new Dictionary<string, string>();
            foreach (var kind in new[] { ActionKind.OpenApp, ActionKind.OpenFile, ActionKind.SystemToggle, ActionKind.Unclear })
                actionCriteria[kind.Raw()] = kind.Rubric();

            return new JevRequest
            {
                State = new JevState
                {
                    Query = query,
                    QueryNote = QueryNote,
                    Context = context,
                    Candidates = summaries,
                },
                Model = Model,
                Questions = new Dictionary<string, JevQuestion>
                {
                    ["target"] = new JevQuestion
                    {
                        Type = "choice",
                        Instructions = "The user typed `query` into a launcher. Which entry in `candidates` is the item they intend to open or run? " +
                            "Treat `query` as a possibly incomplete prefix or paraphrase of the intended item. Match on meaning: a candidate's `title` and `detail` " +
                            "may use different words than `query` (for example `query` \"the pdf I just downloaded\" means the PDF in Downloads whose `detail` " +
                            "says it was modified most recently; \"wifi off\" means the candidate that disables Wi-Fi). Use `context.frontmost_app` and " +
                            "`context.recent_apps` only to break ties. Pick `none` only when no candidate plausibly matches.",
                        Criteria = targetCriteria,
                    },
                    ["action"] = new JevQuestion
                    {
                        Type = "choice",
                        Instructions = "What kind of action does `query` ask the launcher to perform? Judge from the words in `query` and, when `query` " +
                            "names one of the `candidates`, that candidate's `kind`.",
                        Criteria = actionCriteria,
                    },
                    ["ready"] = new JevQuestion
                    {
                        Type = "noul",
                        Instructions = "The launcher is about to run the best-matching candidate the instant the user presses Enter. Is `query` already " +
                            "unambiguous enough for that? `candidates` is the complete list of everything the launcher could do for this query. Short input " +
                            "is fine: \"empty rec\" unambiguously means the Empty Recycle Bin toggle if no other local candidate fits it, while a single " +
                            "letter that several local candidates start with is ambiguous.",
                        Criteria = new Dictionary<string, string>
                        {
                            ["true"] = "One local candidate is the obvious meaning of `query` and the remaining candidates are not plausible; running it on Enter would not surprise the user.",
                            ["false"] = "Several candidates fit `query` roughly equally, so the user should choose from the list.",
                        },
                    },
                },
            };
        }

        /// <summary>Maps the short ids in a response back to the candidates that were sent.</summary>
        public static JevJudgment Parse(JevResponse response, List<Candidate> candidates)
        {
            if (response?.Answers == null || !response.Answers.TryGetValue("target", out var targetAnswer))
                return null;
            if (targetAnswer?.Probabilities == null)
                return null;

            var shown = candidates.Take(MaxCandidates).ToList();
            var judgment = new JevJudgment();
            for (int i = 0; i < shown.Count; i++)
            {
                targetAnswer.Probabilities.TryGetValue("c" + i, out double p);
                judgment.TargetProbabilities[shown[i].Id] = p;
            }
            targetAnswer.Probabilities.TryGetValue(NoneOption, out double none);
            judgment.NoneProbability = none;
            judgment.TargetConfidence = targetAnswer.Confidence ?? 0;

            if (response.Answers.TryGetValue("action", out var actionAnswer) && actionAnswer != null)
            {
                judgment.Action = ActionKindExtensions.FromRaw(actionAnswer.Choice);
                judgment.ActionConfidence = actionAnswer.Confidence ?? 0;
                if (actionAnswer.Probabilities != null)
                {
                    foreach (var kv in actionAnswer.Probabilities)
                    {
                        var kind = ActionKindExtensions.FromRaw(kv.Key);
                        if (kind != ActionKind.Unclear || kv.Key == "unclear")
                            judgment.ActionProbabilities[kind] = kv.Value;
                    }
                }
            }

            if (response.Answers.TryGetValue("ready", out var readyAnswer) && readyAnswer?.Noul.HasValue == true)
                judgment.Ready = readyAnswer.Noul.Value;

            if (response.Usage != null)
            {
                judgment.InputTokens = response.Usage.InputTokens;
                judgment.OutputTokens = response.Usage.OutputTokens;
            }
            return judgment;
        }
    }
}
