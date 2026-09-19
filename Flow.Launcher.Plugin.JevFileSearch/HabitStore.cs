using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flow.Launcher.Plugin.JevFileSearch
{
    /// <summary>
    /// Remembers which items the user actually opened, so the launcher can lean towards
    /// habit. Small JSON file next to the plugin, loaded at init, written after each open.
    /// </summary>
    public sealed class HabitStore
    {
        private sealed class Entry
        {
            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("last")]
            public long LastUnix { get; set; }
        }

        private readonly Dictionary<string, Entry> _entries;
        private readonly string _path;
        private readonly object _lock = new object();
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
        };

        private HabitStore(string path, Dictionary<string, Entry> entries)
        {
            _path = path;
            _entries = entries ?? new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        }

        public static HabitStore Load(string storePath)
        {
            var entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(storePath))
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, Entry>>(
                        File.ReadAllText(storePath));
                    if (parsed != null)
                    {
                        foreach (var pair in parsed)
                        {
                            if (!string.IsNullOrEmpty(pair.Key) && pair.Value != null)
                                entries[pair.Key] = pair.Value;
                        }
                    }
                }
            }
            catch
            {
                entries.Clear();
            }
            return new HabitStore(storePath, entries);
        }

        public int Count
        {
            get { lock (_lock) return _entries.Count; }
        }

        public void Record(string id)
        {
            if (string.IsNullOrEmpty(id))
                return;
            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out var entry))
                {
                    entry = new Entry();
                    _entries[id] = entry;
                }
                entry.Count = Math.Min(100, entry.Count + 1);
                entry.LastUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                Trim();
            }
            Save();
        }

        /// <summary>0..1 habit signal: how often, decayed by how long ago the last open was.</summary>
        public double Boost(string id)
        {
            if (string.IsNullOrEmpty(id))
                return 0;
            Entry entry;
            lock (_lock)
            {
                if (!_entries.TryGetValue(id, out entry) || entry == null)
                    return 0;
            }
            double countFactor = Math.Min(1.0, Math.Log(1 + entry.Count) / Math.Log(8));
            double days = Math.Max(0, (DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.LastUnix) / 86400.0);
            double decay = Math.Max(0.15, 1 - days / 60.0);
            return countFactor * decay;
        }

        public void Save()
        {
            try
            {
                Dictionary<string, Entry> snapshot;
                lock (_lock)
                    snapshot = new Dictionary<string, Entry>(_entries, StringComparer.OrdinalIgnoreCase);

                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, JsonOptions));
                if (File.Exists(_path))
                    File.Delete(_path);
                File.Move(temp, _path);
            }
            catch
            {
                // Habit data is a bonus signal; never let it break a query.
            }
        }

        /// <summary>Keeps the file bounded: most used first, 2000 entries maximum.</summary>
        private void Trim()
        {
            if (_entries.Count <= 2000)
                return;
            var keep = new List<KeyValuePair<string, Entry>>(_entries);
            keep.Sort((a, b) => b.Value.Count.CompareTo(a.Value.Count));
            _entries.Clear();
            for (int i = 0; i < 1500 && i < keep.Count; i++)
                _entries[keep[i].Key] = keep[i].Value;
        }
    }
}
