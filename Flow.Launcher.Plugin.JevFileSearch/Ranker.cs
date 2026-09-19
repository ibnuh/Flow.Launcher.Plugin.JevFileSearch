using System;
using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>One row in the results list.</summary>
    public sealed class RankedHit
    {
        public Candidate Candidate;
        public double Fuzzy;
        /// <summary>Jev's probability that this candidate is the intended target; null when Jev did not answer.</summary>
        public double? JevProbability;
        /// <summary>0..1 freshness signal from the file's modification age.</summary>
        public double Recency;
        /// <summary>0..1 signal from how often the user has opened this item before.</summary>
        public double Habit;
        public double Score;
    }

    public sealed class Prefiltered
    {
        public List<Candidate> Candidates = new List<Candidate>();
        public Dictionary<string, double> Fuzzy = new Dictionary<string, double>();
    }

    /// <summary>
    /// Deterministic prefilter and ranking. Jev only ever sees the output of
    /// Prefilter.
    ///
    /// Based on Ranker.swift from dabit3/jev-experiments/jev-launcher, with two extra
    /// local signals folded in for file search: recency (a downloaded file is usually
    /// the newest one) and habit (what this user actually opens). Jev still dominates
    /// when it answers; the local signals reorder its near-ties and keep the list
    /// sensible when it does not answer at all.
    /// </summary>
    public static class Ranker
    {
        public const int PrefilterLimit = 13;
        public const double MinimumFuzzy = 0.15;

        // Weights with a Jev answer (sum = 1.00).
        public const double TargetWeight = 0.60;
        public const double ActionWeight = 0.16;
        public const double FuzzyWeight = 0.12;
        public const double RecencyWeight = 0.07;
        public const double HabitWeight = 0.05;

        // Weights without a Jev answer (sum = 1.00).
        public const double LocalFuzzyWeight = 0.80;
        public const double LocalRecencyWeight = 0.12;
        public const double LocalHabitWeight = 0.08;

        /// <summary>Days over which a file's recency signal decays to zero.</summary>
        public const double RecencyHalfLifeDays = 14;

        /// <summary>Neutral recency for things with no modification time, like apps and toggles.</summary>
        public const double NeutralRecency = 0.5;

        /// <summary>Fuzzy-scores the pool and keeps the top-k, deduplicated by candidate id.</summary>
        public static Prefiltered Prefilter(string query, IEnumerable<Candidate> pool)
        {
            var result = new Prefiltered();
            string trimmed = (query ?? "").Trim();
            if (trimmed.Length == 0)
                return result;

            var scored = new List<KeyValuePair<Candidate, double>>();
            var seen = new HashSet<string>();
            foreach (var candidate in pool)
            {
                if (!seen.Add(candidate.Id))
                    continue;
                double score = Fuzzy.Score(trimmed, candidate);
                if (score >= MinimumFuzzy)
                    scored.Add(new KeyValuePair<Candidate, double>(candidate, score));
            }
            scored.Sort((a, b) =>
            {
                int cmp = b.Value.CompareTo(a.Value);
                return cmp != 0 ? cmp : string.Compare(a.Key.Title, b.Key.Title, StringComparison.OrdinalIgnoreCase);
            });

            foreach (var pair in scored.Take(PrefilterLimit))
            {
                result.Candidates.Add(pair.Key);
                result.Fuzzy[pair.Key.Id] = pair.Value;
            }
            return result;
        }

        /// <summary>0..1 freshness from modification age. Unknown age is neutral, not fresh.</summary>
        public static double RecencySignal(Candidate candidate)
        {
            if (candidate == null || !candidate.AgeDays.HasValue)
                return NeutralRecency;
            double age = Math.Max(0, candidate.AgeDays.Value);
            return Math.Max(0, 1 - age / RecencyHalfLifeDays);
        }

        /// <summary>
        /// Merges fuzzy, Jev and local signals into one score. With no judgment the
        /// fuzzy order still leads, nudged by recency and habit.
        /// </summary>
        public static List<RankedHit> Rank(Prefiltered prefiltered, JevJudgment judgment, HabitStore habit = null)
        {
            var hits = new List<RankedHit>();
            foreach (var candidate in prefiltered.Candidates)
            {
                prefiltered.Fuzzy.TryGetValue(candidate.Id, out double fuzzy);
                double recency = RecencySignal(candidate);
                double habitScore = habit == null ? 0 : habit.Boost(candidate.Id);
                double score;
                double? target = null;

                if (judgment == null)
                {
                    score = LocalFuzzyWeight * fuzzy + LocalRecencyWeight * recency + LocalHabitWeight * habitScore;
                }
                else
                {
                    judgment.TargetProbabilities.TryGetValue(candidate.Id, out double targetProbability);
                    judgment.ActionProbabilities.TryGetValue(candidate.Kind, out double action);
                    target = targetProbability;
                    score = TargetWeight * targetProbability
                        + ActionWeight * action
                        + FuzzyWeight * fuzzy
                        + RecencyWeight * recency
                        + HabitWeight * habitScore;
                }

                hits.Add(new RankedHit
                {
                    Candidate = candidate,
                    Fuzzy = fuzzy,
                    JevProbability = target,
                    Recency = recency,
                    Habit = habitScore,
                    Score = score,
                });
            }
            hits.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.Compare(a.Candidate.Title, b.Candidate.Title, StringComparison.OrdinalIgnoreCase);
            });
            return hits;
        }

        /// <summary>
        /// Ready badge rule from jev-launcher: green Enter hint when Jev says the
        /// query is unambiguous, or when one target is certain enough that the
        /// remaining candidates are by construction not plausible.
        /// </summary>
        public static bool IsReady(JevJudgment judgment, double topTargetProbability)
        {
            if (judgment == null)
                return false;
            return judgment.Ready >= 0.6 || topTargetProbability >= 0.9;
        }
    }
}
