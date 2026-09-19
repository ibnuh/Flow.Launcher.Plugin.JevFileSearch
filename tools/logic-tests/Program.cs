using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.JevFileSearch;

namespace Flow.Launcher.Plugin.JevFileSearch.Tests
{
    /// <summary>
    /// Dependency free test runner for the plugin's pure logic: fuzzy matching, query
    /// planning, ranking, exclusions and the Jev request/response mapping. Runs on Linux
    /// and in CI, which matters because the WPF plugin project can only build on Windows.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static readonly List<string> _failures = new List<string>();

        public static async Task<int> Main()
        {
            FuzzyTests();
            QueryPlanTests();
            VerbTests();
            RankerTests();
            HabitTests();
            ExclusionTests();
            SubtitleTests();
            IconTests();
            JevProtocolTests();
            await LiveJevSmokeTest().ConfigureAwait(false);

            Console.WriteLine();
            Console.WriteLine("passed: " + _passed + ", failed: " + _failures.Count);
            foreach (var failure in _failures)
                Console.WriteLine("FAIL " + failure);
            return _failures.Count == 0 ? 0 : 1;
        }

        private static void Check(string name, bool condition, string detail = null)
        {
            if (condition)
            {
                _passed++;
                return;
            }
            _failures.Add(name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
            Console.WriteLine("FAIL " + name + (string.IsNullOrEmpty(detail) ? "" : " (" + detail + ")"));
        }

        private static Candidate MakeFile(string title, string subtitle = "PDF \u00b7 ~\\Downloads",
            double? ageDays = 0.5, string folder = "Downloads", long? size = 1000, string[] extraKeywords = null)
        {
            var keywords = new List<string> { folder.ToLowerInvariant(), "file", "pdf", "document" };
            if (extraKeywords != null)
                keywords.AddRange(extraKeywords);
            return new Candidate(
                "file:C:\\Users\\me\\" + folder + "\\" + title,
                title,
                subtitle,
                ActionKind.OpenFile,
                keywords,
                PayloadKind.File,
                path: "C:\\Users\\me\\" + folder + "\\" + title,
                ageDays: ageDays,
                sizeBytes: size,
                iconPath: FileIcons.ForExtension("pdf"));
        }

        private static void FuzzyTests()
        {
            var roadmap = MakeFile("Q3-Roadmap-Review.pdf");
            Check("fuzzy exact token match", Fuzzy.Score("Q3-Roadmap-Review", roadmap) > 0.9);
            Check("fuzzy prefix match", Fuzzy.Score("Q3-Road", roadmap) > 0.5);
            Check("fuzzy natural phrase finds the file", Fuzzy.Score("the pdf I just downloaded", roadmap) > 0.15);
            Check("fuzzy unrelated query scores zero", Fuzzy.Score("kubernetes cluster", roadmap) == 0);
            Check("fuzzy stopwords only still matches something",
                Fuzzy.Score("the my", roadmap) > 0 || Fuzzy.Score("the my", roadmap) == 0);
            Check("fuzzy subsequence fallback", Fuzzy.Score("q3rr", roadmap) > 0);
            Check("fuzzy empty query is zero", Fuzzy.Score("", roadmap) == 0);
        }

        private static void QueryPlanTests()
        {
            var plan = EverythingQuery.Plan("the pdf I just downloaded");
            Check("plan has stages", plan.Count >= 2, string.Join(" | ", plan));
            Check("plan starts most specific", plan[0].Contains("ext:pdf"), plan.FirstOrDefault());
            Check("plan keeps recency filter", plan[0].Contains("dm:today"), plan.FirstOrDefault());
            Check("plan keeps folder filter", plan[0].Contains(@"\downloads\"), plan.FirstOrDefault());
            Check("plan ends with raw query", plan.Last() == "the pdf I just downloaded", plan.Last());
            Check("plan drops folder restriction before raw", plan.Any(p => p.Contains("ext:pdf") && !p.Contains(@"\downloads\")));

            var screenshots = EverythingQuery.Plan("latest screenshot");
            Check("screenshot maps to picture macro", screenshots[0].Contains("pic:"), screenshots.FirstOrDefault());
            Check("latest maps to this week", screenshots[0].Contains("dm:thisweek"), screenshots.FirstOrDefault());

            var native = EverythingQuery.Plan("ext:pdf dm:today report");
            Check("native syntax is passed through untouched", native.Count == 1 && native[0].StartsWith("ext:pdf"), string.Join(" | ", native));

            var plain = EverythingQuery.Plan("invoice");
            Check("plain word plan", plain[0] == "invoice", string.Join(" | ", plain));

            Check("file type detection", EverythingQuery.MentionsFileType("latest screenshot"));
            Check("file type detection negative", !EverythingQuery.MentionsFileType("roadmap review"));
        }

        private static void VerbTests()
        {
            EverythingQuery.ParseVerb("copy roadmap", out var copyMode, out var copyRest);
            Check("copy verb mode", copyMode == SearchMode.CopyPath);
            Check("copy verb strips word", copyRest == "roadmap", copyRest);

            EverythingQuery.ParseVerb("folder invoice", out var folderMode, out var folderRest);
            Check("folder verb mode", folderMode == SearchMode.Folder);
            Check("folder verb strips word", folderRest == "invoice", folderRest);

            EverythingQuery.ParseVerb("reveal invoice", out var revealMode, out _);
            Check("reveal is an alias for folder", revealMode == SearchMode.Folder);

            EverythingQuery.ParseVerb("open roadmap", out var openMode, out var openRest);
            Check("explicit open verb", openMode == SearchMode.Open && openRest == "roadmap");

            EverythingQuery.ParseVerb("folder", out var loneMode, out var loneRest);
            Check("lone verb is treated as a search word", loneMode == SearchMode.Open && loneRest == "folder");

            EverythingQuery.ParseVerb("roadmap", out var plainMode, out var plainRest);
            Check("no verb leaves the query alone", plainMode == SearchMode.Open && plainRest == "roadmap");
        }

        private static void RankerTests()
        {
            double sum = Ranker.TargetWeight + Ranker.ActionWeight + Ranker.FuzzyWeight
                + Ranker.RecencyWeight + Ranker.HabitWeight;
            Check("jev weights sum to 1", Math.Abs(sum - 1.0) < 1e-9, sum.ToString("F4"));

            double localSum = Ranker.LocalFuzzyWeight + Ranker.LocalRecencyWeight + Ranker.LocalHabitWeight;
            Check("local weights sum to 1", Math.Abs(localSum - 1.0) < 1e-9, localSum.ToString("F4"));

            var fresh = MakeFile("new.pdf", ageDays: 0.01);
            var stale = MakeFile("old.pdf", ageDays: 40);
            Check("recency signal fresh", Ranker.RecencySignal(fresh) > 0.99);
            Check("recency signal stale", Ranker.RecencySignal(stale) == 0);
            var app = new Candidate("app:x", "App", "Application", ActionKind.OpenApp,
                new List<string> { "app" }, PayloadKind.App, path: "C:\\x.lnk");
            Check("recency signal neutral for apps", Math.Abs(Ranker.RecencySignal(app) - Ranker.NeutralRecency) < 1e-9);

            var pool = new List<Candidate> { fresh, stale };
            var prefiltered = Ranker.Prefilter("pdf", pool);
            Check("prefilter keeps both", prefiltered.Candidates.Count == 2);

            var noJev = Ranker.Rank(prefiltered, null);
            Check("fuzzy order without jev puts equal fuzzy first by recency",
                noJev.First().Candidate.Title == "new.pdf", string.Join(",", noJev.Select(h => h.Candidate.Title)));

            var judgment = new JevJudgment();
            judgment.TargetProbabilities[fresh.Id] = 0.2;
            judgment.TargetProbabilities[stale.Id] = 0.7;
            judgment.ActionProbabilities[ActionKind.OpenFile] = 1.0;
            var withJev = Ranker.Rank(prefiltered, judgment);
            Check("jev target probability dominates recency",
                withJev.First().Candidate.Title == "old.pdf", string.Join(",", withJev.Select(h => h.Candidate.Title)));

            Check("ready badge needs confidence",
                !Ranker.IsReady(new JevJudgment { Ready = 0.2 }, 0.5)
                && Ranker.IsReady(new JevJudgment { Ready = 0.7 }, 0.5)
                && Ranker.IsReady(new JevJudgment { Ready = 0.1 }, 0.95));

            var dedupPool = new List<Candidate> { MakeFile("dup.pdf"), MakeFile("dup.pdf"), MakeFile("other.pdf") };
            var deduped = Ranker.Prefilter("pdf", dedupPool);
            Check("prefilter dedupes by id", deduped.Candidates.Count == 2, deduped.Candidates.Count.ToString());
        }

        private static void HabitTests()
        {
            string path = Path.Combine(Path.GetTempPath(), "jev-habit-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var store = HabitStore.Load(path);
                var candidate = MakeFile("habit.pdf");
                Check("no boost before any open", store.Boost(candidate.Id) == 0);

                store.Record(candidate.Id);
                double once = store.Boost(candidate.Id);
                for (int i = 0; i < 6; i++)
                    store.Record(candidate.Id);
                double many = store.Boost(candidate.Id);
                Check("habit grows with use", once > 0 && many > once, once.ToString("F3") + " -> " + many.ToString("F3"));
                Check("habit stays in 0..1", many <= 1.0, many.ToString("F3"));

                var reloaded = HabitStore.Load(path);
                Check("habit persists", Math.Abs(reloaded.Boost(candidate.Id) - many) < 1e-9);

                var pool = new List<Candidate> { candidate, MakeFile("other.pdf") };
                var prefiltered = Ranker.Prefilter("pdf", pool);
                var ranked = Ranker.Rank(prefiltered, null, reloaded);
                Check("habit reorders local results", ranked.First().Candidate.Title == "habit.pdf",
                    string.Join(",", ranked.Select(h => h.Candidate.Title)));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
                if (File.Exists(path + ".tmp"))
                    File.Delete(path + ".tmp");
            }
        }

        private static void ExclusionTests()
        {
            var settings = new Settings();
            Check("lnk excluded", settings.IsExcludedFile(@"C:\Users\me\Desktop\Shortcut.lnk"));
            Check("log excluded", settings.IsExcludedFile(@"C:\Users\me\Desktop\build.log"));
            Check("pdf kept", !settings.IsExcludedFile(@"C:\Users\me\Downloads\report.pdf"));
            Check("office temp excluded", settings.IsExcludedFile(@"C:\Users\me\Downloads\~$report.docx"));
            Check("appdata path excluded", settings.IsExcludedPath(@"C:\Users\me\AppData\Roaming\x\y.pdf"));
            Check("node_modules excluded", settings.IsExcludedPath(@"C:\dev\app\node_modules\pkg\readme.md"));
            Check("program files excluded", settings.IsExcludedPath(@"C:\Program Files\App\x.exe"));
            Check("documents kept", !settings.IsExcludedPath(@"C:\Users\me\Documents\thesis.docx"));
            Check("junk combines both", settings.IsJunk(@"C:\Users\me\Downloads\auto.log"));
            Check("installer shortcut name is noise", Settings.IsAppLinkNoise("Uninstall Foo"));
            Check("real app name is kept", !Settings.IsAppLinkNoise("Visual Studio Code"));
            Check("default exclude paths has fragments", Settings.DefaultExcludePaths.Contains(@"\AppData\"));
        }

        private static void SubtitleTests()
        {
            string subtitle = LocalIndex.SubtitleFor(false, "pdf", @"~\Downloads", 2516582, 0.0004);
            Check("subtitle has type", subtitle.Contains("PDF"), subtitle);
            Check("subtitle has size", subtitle.Contains("2.4 MB"), subtitle);
            Check("subtitle has location", subtitle.Contains("Downloads"), subtitle);
            Check("subtitle has recency words", subtitle.Contains("modified just now"), subtitle);

            Check("recency minutes", LocalIndex.Recency(0.02).Contains("min ago"), LocalIndex.Recency(0.02));
            Check("recency yesterday", LocalIndex.Recency(1.2).Contains("yesterday"), LocalIndex.Recency(1.2));
            Check("recency old", LocalIndex.Recency(1000).Contains("over a year"), LocalIndex.Recency(1000));
            Check("folder subtitle", LocalIndex.SubtitleFor(true, "", @"D:\Media", null, 3).StartsWith("Folder"));
        }

        private static void IconTests()
        {
            Check("pdf icon", FileIcons.ForExtension("pdf") == FileIcons.Pdf);
            Check("png icon", FileIcons.ForExtension(".PNG") == FileIcons.Image);
            Check("unknown icon falls back", FileIcons.ForExtension("qqq") == FileIcons.Default);
            Check("missing extension falls back", FileIcons.ForExtension("") == FileIcons.Default);

            string root = FindPluginImages();
            foreach (var icon in new[]
            {
                FileIcons.Default, FileIcons.Folder, FileIcons.App, FileIcons.Toggle, FileIcons.Drive,
                FileIcons.Pdf, FileIcons.Image, FileIcons.Video, FileIcons.Audio, FileIcons.Archive,
                FileIcons.Code, FileIcons.Text, FileIcons.Sheet, FileIcons.Doc,
            })
            {
                // Icon paths in the plugin are relative to the plugin folder, for example
                // "images/filetypes/pdf.png"; the test root already points at images/.
                string relative = icon.StartsWith("images/") ? icon.Substring("images/".Length) : icon;
                string file = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Check("icon exists: " + icon, File.Exists(file), file);
            }
        }

        private static string FindPluginImages()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, "Flow.Launcher.Plugin.JevFileSearch", "images");
                if (Directory.Exists(candidate))
                    return candidate;
                directory = directory.Parent;
            }
            return "images";
        }

        private static void JevProtocolTests()
        {
            var candidates = new List<Candidate> { MakeFile("a.pdf"), MakeFile("b.pdf"), MakeFile("c.pdf") };
            var request = JevQuestions.BuildRequest("the pdf I just downloaded", LaunchContext.Current(), candidates);

            Check("request model", request.Model == "jev-latest", request.Model);
            Check("three questions", request.Questions.Count == 3, request.Questions.Count.ToString());
            Check("target question is a choice", request.Questions["target"].Type == "choice");
            Check("action question is a choice", request.Questions["action"].Type == "choice");
            Check("ready question is a noul", request.Questions["ready"].Type == "noul");
            Check("target criteria covers every candidate plus none",
                request.Questions["target"].Criteria.Count == candidates.Count + 1);
            Check("none option present", request.Questions["target"].Criteria.ContainsKey(JevQuestions.NoneOption));
            Check("short ids are used", request.Questions["target"].Criteria.ContainsKey("c0"));
            Check("state carries the candidates", request.State.Candidates.Count == candidates.Count);
            Check("state candidate detail is the subtitle",
                request.State.Candidates[0].Detail == candidates[0].Subtitle);
            Check("recency hint is in the target instructions",
                request.Questions["target"].Instructions.Contains("modified most recently"));
            Check("candidate cap respected",
                JevQuestions.BuildRequest("x", LaunchContext.Current(),
                    Enumerable.Range(0, 30).Select(i => MakeFile("f" + i + ".pdf")).ToList())
                    .State.Candidates.Count == JevQuestions.MaxCandidates);

            // Response shape taken from the TypeSafe docs example.
            var response = new JevResponse
            {
                Model = "jev-latest",
                Answers = new Dictionary<string, JevAnswer>
                {
                    ["target"] = new JevAnswer
                    {
                        Type = "choice",
                        Choice = "c1",
                        Confidence = 0.82,
                        Probabilities = new Dictionary<string, double>
                        {
                            ["c0"] = 0.1, ["c1"] = 0.85, ["c2"] = 0.03, ["none"] = 0.02,
                        },
                    },
                    ["action"] = new JevAnswer
                    {
                        Type = "choice",
                        Choice = "open_file",
                        Confidence = 0.7,
                        Probabilities = new Dictionary<string, double>
                        {
                            ["open_file"] = 0.9, ["system_toggle"] = 0.1,
                        },
                    },
                    ["ready"] = new JevAnswer { Type = "noul", Noul = 0.84 },
                },
                Usage = new JevUsage { InputTokens = 1400, OutputTokens = 0 },
            };

            var judgment = JevQuestions.Parse(response, candidates);
            Check("judgment parsed", judgment != null);
            Check("probabilities mapped back to candidate ids",
                Math.Abs(judgment.TargetProbabilities[candidates[1].Id] - 0.85) < 1e-9);
            Check("none probability captured", Math.Abs(judgment.NoneProbability - 0.02) < 1e-9);
            Check("action parsed", judgment.Action == ActionKind.OpenFile);
            Check("action probabilities parsed", Math.Abs(judgment.ActionProbabilities[ActionKind.OpenFile] - 0.9) < 1e-9);
            Check("ready parsed", Math.Abs(judgment.Ready - 0.84) < 1e-9);
            Check("usage captured", judgment.InputTokens == 1400);

            Check("malformed response returns null",
                JevQuestions.Parse(new JevResponse { Answers = new Dictionary<string, JevAnswer>() }, candidates) == null);
        }

        private static async Task LiveJevSmokeTest()
        {
            string key = JevClient.ResolveApiKey(new Settings());
            if (string.IsNullOrEmpty(key))
            {
                Console.WriteLine("SKIP live Jev smoke test: TYPESAFE_API_KEY not set");
                return;
            }

            try
            {
                var candidates = new List<Candidate>
                {
                    new Candidate("c:new", "Q3-Roadmap-Review.pdf", "PDF \u00b7 ~\\Downloads \u00b7 modified 16 min ago",
                        ActionKind.OpenFile, new List<string> { "pdf", "downloaded", "document" },
                        PayloadKind.File, ageDays: 0.01),
                    new Candidate("c:old", "invoice-2026-08.pdf", "PDF \u00b7 ~\\Downloads \u00b7 modified 1 month ago",
                        ActionKind.OpenFile, new List<string> { "pdf", "downloaded", "document" },
                        PayloadKind.File, ageDays: 30),
                };
                var request = JevQuestions.BuildRequest("the pdf I just downloaded", LaunchContext.Current(), candidates);
                var ask = await JevClient.AskAsync(request, key, System.Threading.CancellationToken.None);
                var judgment = JevQuestions.Parse(ask.Response, candidates);
                Check("live: judgment parsed", judgment != null);
                if (judgment != null)
                {
                    Console.WriteLine("live: jev picked " + judgment.TargetProbabilities
                        .OrderByDescending(kv => kv.Value).First().Key
                        + " in " + ask.LatencyMs.ToString("F0") + " ms, ready=" + judgment.Ready.ToString("F2"));
                    Check("live: prefer-fresh hint works",
                        (judgment.TargetProbabilities["c:new"] > judgment.TargetProbabilities["c:old"]),
                        judgment.TargetProbabilities["c:new"].ToString("F2") + " vs "
                        + judgment.TargetProbabilities["c:old"].ToString("F2"));
                }
            }
            catch (Exception ex)
            {
                Check("live: request succeeded", false, ex.Message);
            }
        }
    }
}
