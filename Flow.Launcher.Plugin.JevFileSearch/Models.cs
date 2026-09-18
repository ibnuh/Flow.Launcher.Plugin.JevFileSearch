using System;
using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    public enum ActionKind
    {
        OpenApp,
        OpenFile,
        SystemToggle,
        Unclear
    }

    public static class ActionKindExtensions
    {
        public static string Raw(this ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.OpenApp: return "open_app";
                case ActionKind.OpenFile: return "open_file";
                case ActionKind.SystemToggle: return "system_toggle";
                default: return "unclear";
            }
        }

        public static string Label(this ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.OpenApp: return "App";
                case ActionKind.OpenFile: return "File";
                case ActionKind.SystemToggle: return "System";
                default: return "?";
            }
        }

        public static string Rubric(this ActionKind kind)
        {
            switch (kind)
            {
                case ActionKind.OpenApp: return "Launch an installed application.";
                case ActionKind.OpenFile: return "Open a document, folder or file from disk.";
                case ActionKind.SystemToggle: return "Change a system setting: appearance, wi-fi, sleep, lock, recycle bin, notifications.";
                default: return "Too little typed or too ambiguous to tell what kind of action is meant.";
            }
        }

        public static ActionKind FromRaw(string raw)
        {
            switch (raw)
            {
                case "open_app": return ActionKind.OpenApp;
                case "open_file": return ActionKind.OpenFile;
                case "system_toggle": return ActionKind.SystemToggle;
                default: return ActionKind.Unclear;
            }
        }
    }

    public enum PayloadKind
    {
        App,
        File,
        Toggle
    }

    /// <summary>
    /// Something the launcher can execute. Produced by the local index.
    /// Port of the jev-launcher Candidate type (dabit3/jev-experiments).
    /// </summary>
    public sealed class Candidate
    {
        public string Id { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public ActionKind Kind { get; }
        public List<string> Keywords { get; }
        public PayloadKind Payload { get; }
        public string Path { get; }
        public SystemToggle Toggle { get; }
        public double? AgeDays { get; }

        public Candidate(string id, string title, string subtitle, ActionKind kind,
            List<string> keywords, PayloadKind payload, string path = null,
            SystemToggle toggle = SystemToggle.ToggleDarkMode, double? ageDays = null)
        {
            Id = id;
            Title = title;
            Subtitle = subtitle;
            Kind = kind;
            Keywords = keywords ?? new List<string>();
            Payload = payload;
            Path = path;
            Toggle = toggle;
            AgeDays = ageDays;
        }

        /// <summary>Lower-cased text the fuzzy matcher searches: title words plus keywords.</summary>
        public List<string> SearchTerms()
        {
            var terms = new List<string>();
            foreach (var token in Fuzzy.Tokens(Title))
                terms.Add(token);
            foreach (var keyword in Keywords)
                terms.Add(keyword.ToLowerInvariant());
            return terms;
        }
    }
}
