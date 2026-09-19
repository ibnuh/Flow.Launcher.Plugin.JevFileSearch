using System;
using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Turns a natural-language query into Everything search syntax, and returns the
    /// plans to try in order. Everything is the right index for whole-volume search,
    /// but its syntax is not what people type, so the translation happens here in code
    /// rather than being asked of a model.
    /// </summary>
    public static class EverythingQuery
    {
        /// <summary>Words that carry no search value in a filename query.</summary>
        public static readonly HashSet<string> FillerWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "a", "an", "i", "my", "me", "to", "of", "that", "please", "for", "with", "from",
            "file", "files", "folder", "folders", "doc", "docs", "document", "documents",
            "just", "some", "any", "was", "were", "is", "are", "and", "or", "in", "on", "at", "by"
        };

        /// <summary>Leading verbs that switch what Enter does, stripped before searching.</summary>
        private static readonly Dictionary<string, SearchMode> Verbs = new Dictionary<string, SearchMode>(StringComparer.OrdinalIgnoreCase)
        {
            ["open"] = SearchMode.Open,
            ["folder"] = SearchMode.Folder,
            ["dir"] = SearchMode.Folder,
            ["explorer"] = SearchMode.Folder,
            ["reveal"] = SearchMode.Folder,
            ["copy"] = SearchMode.CopyPath,
            ["path"] = SearchMode.CopyPath,
        };

        private static readonly Dictionary<string, string> TypeFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = "ext:pdf",
            ["image"] = "pic:",
            ["images"] = "pic:",
            ["photo"] = "pic:",
            ["photos"] = "pic:",
            ["picture"] = "pic:",
            ["pictures"] = "pic:",
            ["screenshot"] = "pic:",
            ["screenshots"] = "pic:",
            ["video"] = "video:",
            ["videos"] = "video:",
            ["movie"] = "video:",
            ["movies"] = "video:",
            ["audio"] = "audio:",
            ["music"] = "audio:",
            ["song"] = "audio:",
            ["songs"] = "audio:",
            ["zip"] = "zip:",
            ["archive"] = "zip:",
            ["archives"] = "zip:",
            ["spreadsheet"] = "ext:xlsx;xls;csv;ods",
            ["excel"] = "ext:xlsx;xls;csv",
            ["csv"] = "ext:csv",
            ["xlsx"] = "ext:xlsx",
            ["docx"] = "ext:docx",
            ["word"] = "ext:doc;docx;rtf",
            ["pptx"] = "ext:pptx;ppt",
            ["slides"] = "ext:pptx;ppt",
            ["code"] = "ext:cs;ts;js;py;java;go;rs;cpp;h;sh;ps1;json;yml;yaml",
            ["source"] = "ext:cs;ts;js;py;java;go;rs;cpp;h",
            ["log"] = "ext:log;txt",
            ["txt"] = "ext:txt",
            ["text"] = "ext:txt;md;rtf",
            ["note"] = "ext:txt;md",
            ["notes"] = "ext:txt;md",
            ["exe"] = "ext:exe;msi;bat;cmd;ps1",
            ["installer"] = "ext:exe;msi",
        };

        private static readonly Dictionary<string, string> RecencyFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["today"] = "dm:today",
            ["yesterday"] = "dm:yesterday",
            ["week"] = "dm:thisweek",
            ["weekly"] = "dm:thisweek",
            ["recent"] = "dm:thisweek",
            ["recently"] = "dm:thisweek",
            ["latest"] = "dm:thisweek",
            ["newest"] = "dm:thisweek",
            ["new"] = "dm:thisweek",
            ["just"] = "dm:today",
            ["month"] = "dm:thismonth",
            ["year"] = "dm:thisyear",
        };

        private static readonly Dictionary<string, string> FolderFilters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["download"] = @"\downloads\",
            ["downloaded"] = @"\downloads\",
            ["downloads"] = @"\downloads\",
            ["desktop"] = @"\desktop\",
            ["document"] = @"\documents\",
            ["documents"] = @"\documents\",
            ["screenshot"] = @"\screenshots\",
            ["screenshots"] = @"\screenshots\",
        };

        /// <summary>
        /// Builds Everything query strings from most specific to least, so the caller can
        /// retry when a narrower plan returns nothing. The last plan is always the raw
        /// query, which keeps behaviour predictable for people who know the syntax.
        /// </summary>
        public static List<string> Plan(string userQuery)
        {
            var plans = new List<string>();
            string raw = (userQuery ?? "").Trim();
            if (raw.Length == 0)
                return plans;

            // Someone typing Everything syntax already gets it passed through untouched.
            if (raw.Contains("\"") || raw.Contains(":") || raw.Contains("\\") || raw.Contains("*"))
            {
                plans.Add(raw);
                return plans;
            }

            var terms = new List<string>();
            var types = new List<string>();
            var recency = new List<string>();
            var folders = new List<string>();

            foreach (var token in Fuzzy.Tokens(raw))
            {
                if (TypeFilters.TryGetValue(token, out var typeFilter))
                {
                    if (!types.Contains(typeFilter))
                        types.Add(typeFilter);
                    continue;
                }
                if (RecencyFilters.TryGetValue(token, out var recencyFilter))
                {
                    if (!recency.Contains(recencyFilter))
                        recency.Add(recencyFilter);
                    continue;
                }
                if (FolderFilters.TryGetValue(token, out var folderFilter))
                {
                    if (!folders.Contains(folderFilter))
                        folders.Add(folderFilter);
                    continue;
                }
                if (FillerWords.Contains(token))
                    continue;
                terms.Add(token);
            }

            // Everything needs something to match on. A query of pure filters is fine.
            var content = terms.Count > 0 ? new[] { string.Join(" ", terms) } : new string[0];

            void Add(IEnumerable<string> parts)
            {
                var plan = string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
                plan = plan.Trim();
                if (plan.Length > 0 && !plans.Contains(plan))
                    plans.Add(plan);
            }

            var filters = types.Concat(recency).ToList();

            Add(content.Concat(filters).Concat(folders));            // most specific
            Add(content.Concat(filters));                            // drop folder restriction
            Add(content);                                            // content only
            Add(filters);                                            // filters only
            Add(new[] { raw });                                      // raw passthrough

            return plans;
        }

        /// <summary>Splits a leading verb off a query, for example "copy roadmap" or "folder invoice".</summary>
        public static void ParseVerb(string search, out SearchMode mode, out string rest)
        {
            mode = SearchMode.Open;
            rest = (search ?? "").Trim();
            if (rest.Length == 0)
                return;

            int space = rest.IndexOf(' ');
            string head = space < 0 ? rest : rest.Substring(0, space);
            if (space < 0)
                return;
            if (!Verbs.TryGetValue(head, out var parsed))
                return;

            mode = parsed;
            rest = rest.Substring(space + 1).Trim();
            if (rest.Length == 0)
            {
                mode = SearchMode.Open;
                rest = head;
            }
        }

        /// <summary>Extra words that only make sense as a file-type request, used to hint the index scan.</summary>
        public static bool MentionsFileType(string query)
        {
            foreach (var token in Fuzzy.Tokens(query ?? ""))
            {
                if (TypeFilters.ContainsKey(token))
                    return true;
            }
            return false;
        }
    }

    public enum SearchMode
    {
        Open,
        Folder,
        CopyPath,
    }

    public static class SearchModeExtensions
    {
        public static string Verb(this SearchMode mode)
        {
            switch (mode)
            {
                case SearchMode.Folder: return "folder";
                case SearchMode.CopyPath: return "copy";
                default: return "open";
            }
        }

        public static string Hint(this SearchMode mode)
        {
            switch (mode)
            {
                case SearchMode.Folder: return "Enter opens the containing folder";
                case SearchMode.CopyPath: return "Enter copies the full path";
                default: return "Enter opens";
            }
        }
    }
}
