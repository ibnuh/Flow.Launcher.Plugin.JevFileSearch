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
        public double Score;
    }

    public sealed class Prefiltered
    {
        public List<Candidate> Candidates = new List<Candidate>();
        public Dictionary<string, double> Fuzzy = new Dictionary<string, double>();
    }

    /// <summary>
    /// Deterministic prefilter and ranking. Jev only ever sees the output of
    /// Prefilter. Port of Ranker.swift from dabit3/jev-experiments/jev-launcher.
    /// </summary>
    public static class Ranker
    {
        public const int PrefilterLimit = 13;
        public const double MinimumFuzzy = 0.15;

        public const double TargetWeight = 0.65;
        public const double ActionWeight = 0.20;
        public const double FuzzyWeight = 0.15;

        /// <summary>Fuzzy-scores the pool and keeps the top-k, deduplicated by candidate id.</summary>
        public static Prefiltered Prefilter(string query, IEnumerable<Candidate> pool)
        {
            var result = new Prefiltered();
            string trimmed = query.Trim();
            if (string.IsNullOrEmpty(trimmed))
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
                return cmp != 0 ? cmp : string.Compare(a.Key.Title, b.Key.Title);
            });

            foreach (var pair in scored.Take(PrefilterLimit))
            {
                result.Candidates.Add(pair.Key);
                result.Fuzzy[pair.Key.Id] = pair.Value;
            }
            return result;
        }

        /// <summary>Merges fuzzy scores with Jev's judgment. With no judgment the order is pure fuzzy.</summary>
        public static List<RankedHit> Rank(Prefiltered prefiltered, JevJudgment judgment)
        {
            var hits = new List<RankedHit>();
            foreach (var candidate in prefiltered.Candidates)
            {
                prefiltered.Fuzzy.TryGetValue(candidate.Id, out double fuzzy);
                if (judgment == null)
                {
                    hits.Add(new RankedHit { Candidate = candidate, Fuzzy = fuzzy, JevProbability = null, Score = fuzzy });
                    continue;
                }
                judgment.TargetProbabilities.TryGetValue(candidate.Id, out double target);
                judgment.ActionProbabilities.TryGetValue(candidate.Kind, out double action);
                double score = TargetWeight * target + ActionWeight * action + FuzzyWeight * fuzzy;
                hits.Add(new RankedHit { Candidate = candidate, Fuzzy = fuzzy, JevProbability = target, Score = score });
            }
            hits.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.Compare(a.Candidate.Title, b.Candidate.Title);
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
