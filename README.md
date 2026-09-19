# Jev File Search for Flow Launcher

A file search for Windows that behaves like the one you would design for yourself.
Type the way you would say it, `the pdf I just downloaded`, `latest screenshot`,
`folder invoice`, and the list ranks by intent rather than by string match.

Built on the [jev-launcher](https://github.com/dabit3/jev-experiments/tree/main/jev-launcher)
experiment by dabit3, ported to Flow Launcher on Windows: local indexing and fuzzy
matching in plain C#, one [Jev](https://docs.typesafe.ai) fan-out request per query
carrying three typed questions (intended target, action kind, ready), and a
deterministic merge in code.

```
score = 0.60 * P(target) + 0.16 * P(action matches kind) + 0.12 * fuzzy
      + 0.07 * recency + 0.05 * habit
```

Without a Jev answer (no key, offline, timeout, rate limit) the same list scores as
`0.80 * fuzzy + 0.12 * recency + 0.08 * habit`, so a keystroke is never blocked on the
network.

## Search that actually finds your files

- **Everything first.** When `es.exe` is installed, Everything supplies the candidates
  and covers whole volumes, including drives the folder scan never touches. The plugin
  ranks them; it does not try to re-index the disk itself.
- **Natural language to Everything syntax.** `the pdf I just downloaded` becomes
  `ext:pdf dm:today \downloads\`, `latest screenshot` becomes `pic: dm:thisweek`.
  Plans are tried from most specific to least, ending with your raw input, so people who
  know Everything syntax can still type it directly.
- **Recency matters.** A file you just downloaded usually beats a better keyword match
  from last year, so freshness is a ranking signal and recency is described to Jev in
  words ("modified 16 min ago").
- **Habit.** Items you open from the plugin get a small boost that decays over weeks, so
  the launcher leans towards the files you actually use.
- **Signal, not noise.** `.lnk`, `.url`, `.tmp`, `.log`, installer and system extensions
  are skipped, along with dotfiles, Office temp files, and anything under `AppData`,
  `Program Files`, `node_modules`, `.git` and friends. Both lists are editable.
- **Type icons.** Every row shows what kind of thing it is (PDF, image, video, code,
  sheet, folder, app, drive, system action) instead of repeating the plugin icon.

## Prefixes

| Query | Enter does |
|---|---|
| `invoice` | Opens the highlighted result |
| `folder invoice` | Opens the containing folder (or the folder itself) with the item selected |
| `copy invoice` | Copies the full path |

`reveal` and `dir` are aliases for `folder`, `path` for `copy`, `open` is explicit.

## What is local (code, not Jev)

- Index: Start Menu apps, Desktop / Downloads / Documents plus one nested level, every
  non-system fixed drive (a drive row with free space, plus a shallow root scan), and
  nine system actions (dark mode, Wi-Fi on/off, focus, sleep, lock, empty recycle bin,
  show/hide hidden files).
- Fuzzy prefilter: exact, prefix, word-initial and subsequence scoring with stopwords
  stripped, then the top 13 candidates go to Jev. The model never sees the whole index.
- Execution: `Process.Start` for apps and files, Explorer for `folder`, clipboard for
  `copy`, shell commands or settings pages for the system actions.
- Everything CLI: `es.exe -n <limit> -sort date-modified`, retried with older flag
  variants when a switch is unsupported, cached for 20 s per query string, and silently
  abandoned on any failure.

## Setup

1. Install the plugin (unzip a release into `%APPDATA%\FlowLauncher\Plugins` or install
   from the plugin store).
2. Install [Everything](https://www.voidtools.com) and its command line client `es.exe`
   for whole-volume search. Without it the plugin still searches the folders above.
3. Get a TypeSafe API key at https://console.typesafe.ai/settings/keys and either set
   `TYPESAFE_API_KEY` or paste it into the settings. Without a key the plugin is a fast
   fuzzy launcher with recency and habit ranking.
4. Type `jv` plus a query: `jv the pdf I just downloaded`.

## Settings

| Field | Meaning |
|---|---|
| TypeSafe API key | Used when `TYPESAFE_API_KEY` is not set |
| Folders to index | Semicolon separated. Blank means Desktop, Downloads, Documents |
| Max entries per folder | Cap per scanned folder, default 400 |
| Also index non-system fixed drives | Shallow scan of D:, E:, ... |
| Use the Everything index | Prefer `es.exe` candidates when available |
| es.exe path | Blank means auto-detect: PATH, then the usual install folders |
| Max candidates from Everything | Default 60 |
| Never index these extensions | Junk filter, dots optional |
| Never index paths containing | `AppData`, `node_modules`, `.git`, ... Clear it to search everything |

The bottom row of the results shows where the candidates came from, what the last
Everything invocation was, Jev round trip and estimated cost.

## Build, test, release

The plugin project is WPF (`net9.0-windows`) so it only builds on Windows. The pure
logic (fuzzy matching, Everything query planning, ranking, exclusions, Jev mapping)
compiles into a dependency-free test runner that runs anywhere:

```bash
dotnet run --project tools/logic-tests/LogicTests.csproj
```

`Build` runs on every push and PR to `main`: logic tests, then the WPF build, then an
assembly check against `plugin.json`. Publish is tag-driven:

```bash
git tag -a v0.3.0 -m "v0.3.0"
git push origin v0.3.0
gh run watch --exit-status
gh release view v0.3.0 --json url,assets
```

## Test on Windows

- `jv dark` tops out at Toggle Dark Mode, `jv wifi off` prefers Turn Wi-Fi Off.
- `jv the pdf I just downloaded` prefers the newest PDF in Downloads.
- `jv latest screenshot` returns images, not documents.
- `jv folder invoice` opens Explorer with the file selected.
- `jv copy roadmap` copies the path and hides the window.
- `jv lnk` finds no shortcuts; `jv appdata` finds nothing under AppData.
- With no key, the bottom row says so and the order is fuzzy plus recency plus habit.

## Credits

Ranking design, Jev question wording and the ready-badge rule come from the jev-launcher
experiment in dabit3/jev-experiments. Jev is TypeSafe AI's model, see
https://docs.typesafe.ai. Everything is voidtools' index, see https://www.voidtools.com.
