using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Fast, deterministic fuzzy matcher used to prefilter the local index before
    /// anything is sent to Jev, and as the complete ranking when Jev is off or
    /// unavailable. Port of Fuzzy.swift from dabit3/jev-experiments/jev-launcher.
    /// </summary>
    public static class Fuzzy
    {
        public static readonly HashSet<string> Stopwords = new HashSet<string>
        {
            "the", "a", "an", "i", "my", "me", "to", "of", "that", "just",
            "please", "open", "launch", "run", "go", "show", "find", "get", "up", "it",
        };

        public static List<string> Tokens(string text)
        {
            var tokens = new List<string>();
            var current = new System.Text.StringBuilder();
            foreach (var ch in text.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(ch) || ch == '-')
                    current.Append(ch);
                else if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            if (current.Length > 0)
                tokens.Add(current.ToString());
            return tokens;
        }

        /// <summary>Score in 0..1. Zero means the candidate should not be shown for this query.</summary>
        public static double Score(string query, Candidate candidate)
        {
            var all = Tokens(query);
            if (all.Count == 0)
                return 0;

            var meaningful = all.Where(t => !Stopwords.Contains(t)).ToList();
            if (meaningful.Count == 0)
                meaningful = all;

            var terms = candidate.SearchTerms();
            var titleTokens = Tokens(candidate.Title);
            var joinedTitle = string.Concat(titleTokens);
            var initials = new string(titleTokens.Where(t => t.Length > 0).Select(t => t[0]).ToArray());

            double total = 0;
            int unmatched = 0;
            foreach (var token in meaningful)
            {
                var best = BestMatch(token, terms, joinedTitle, initials);
                if (best == 0)
                    unmatched++;
                total += best;
            }

            if (total <= 0)
                return 0;

            double score = total / meaningful.Count;
            if (unmatched > 0)
                score *= 0.5;
            // Prefer shorter titles when everything else is equal.
            score += 0.02 * System.Math.Max(0, 1 - (double)candidate.Title.Length / 40);
            return System.Math.Min(1, score);
        }

        private static double BestMatch(string token, List<string> terms, string joinedTitle, string initials)
        {
            double best = 0;
            foreach (var term in terms)
            {
                if (term == token)
                    return 1;
                if (term.StartsWith(token))
                    best = System.Math.Max(best, 0.8 + 0.15 * (double)token.Length / term.Length);
            }
            if (best > 0)
                return best;
            if (token.Length >= 2 && initials.StartsWith(token))
                return 0.7;
            if (joinedTitle.Contains(token))
                return 0.55;
            if (token.Length >= 3)
            {
                var contiguity = SubsequenceContiguity(token, joinedTitle);
                if (contiguity.HasValue)
                    return 0.2 + 0.2 * contiguity.Value;
            }
            return 0;
        }

        /// <summary>Fraction of adjacent matches when needle is a subsequence of haystack.</summary>
        public static double? SubsequenceContiguity(string needle, string haystack)
        {
            int ni = 0;
            int lastMatch = -1;
            int adjacent = 0;
            for (int hi = 0; hi < haystack.Length && ni < needle.Length; hi++)
            {
                if (haystack[hi] == needle[ni])
                {
                    if (lastMatch >= 0 && lastMatch + 1 == hi)
                        adjacent++;
                    lastMatch = hi;
                    ni++;
                }
            }
            if (ni != needle.Length)
                return null;
            return needle.Length > 1 ? (double)adjacent / (needle.Length - 1) : 1;
        }
    }
}
