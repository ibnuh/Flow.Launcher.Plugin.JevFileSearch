# Jev File Search for Flow Launcher

A predictive file and app launcher for Windows. Type the way you would say it,
`dark`, `wifi off`, `the pdf I just downloaded`, and the list reranks by intent,
not just by string match.

It ports the [jev-launcher](https://github.com/dabit3/jev-experiments/tree/main/jev-launcher)
experiment by dabit3 to Flow Launcher on Windows: a local index plus fuzzy
prefilter in plain C#, with one [Jev](https://docs.typesafe.ai) fan-out request
per query carrying three typed questions (intended target, action kind, ready).
Ranking is deterministic given the answer:
`score = 0.65 * P(target) + 0.20 * P(action matches kind) + 0.15 * fuzzy`.
Without an answer (no key, offline, timeout) the order is pure fuzzy, so the
panel always has a sensible order and never waits on the network.

## What is local (code, not Jev)

- Index: Start Menu apps, files in Desktop / Downloads / Documents (top level
  plus one nested level, capped at 400 entries per folder), non-system fixed
  drives (a drive row plus a shallow scan of each root), nine system toggles.
- Everything: when `es.exe` is present, its index supplies extra candidates, so
  search covers whole volumes instead of just the scanned folders. Lookup order
  is the settings field, then `PATH`, then the usual install folders.
- Junk filter: `.lnk`, `.url`, `.tmp`, `.log`, installer and system extensions
  are never indexed, installer/uninstaller/readme Start Menu shortcuts are
  dropped, and dotfiles and Office temp files are skipped. The extension list
  is editable in the settings.
- Fuzzy prefilter: exact / prefix / word-initial / subsequence scoring with
  stopwords stripped, same design as the Swift original.
- Execution: `Process.Start` for apps and files, shell commands or settings
  pages for toggles.

## Setup

1. Install the plugin (Flow Launcher Plugin Store, or unzip a GitHub release
   into `%APPDATA%\FlowLauncher\Plugins`).
2. Get a TypeSafe API key at https://console.typesafe.ai/settings/keys.
3. Either set a `TYPESAFE_API_KEY` environment variable or paste the key into
   the plugin settings. Without a key the plugin still works as a fuzzy file
   launcher.
4. Optional: install [Everything](https://www.voidtools.com) with its command
   line client `es.exe` for whole-volume search.
5. Type `jv` plus your query, for example `jv the pdf I just downloaded`.

Recency is described to Jev in words ("modified 16 min ago"), which is what
lets "the pdf I just downloaded" prefer the newest file.

## Settings

| Field | Meaning |
|---|---|
| TypeSafe API key | Used when `TYPESAFE_API_KEY` is not set |
| Folders to index | Semicolon separated. Blank means Desktop, Downloads, Documents |
| Max entries per folder | Cap per scanned folder, default 400 |
| Also index non-system fixed drives | Shallow scan of D:, E:, ... |
| Use the Everything index | Prefer `es.exe` candidates when available |
| es.exe path | Blank means auto-detect |
| Never index these extensions | Junk filter, dots optional |

## Build and release

Publish is tag-driven, same as the IPDetails plugin. `Build` runs on every push
and PR to `main` so the plugin compiles before a tag exists.

1. Bump `Flow.Launcher.Plugin.JevFileSearch/plugin.json` `Version`.
2. Open a PR on a feature branch (`draft: false`).
3. Tag `v<Version>` on the release commit and push the tag.
4. Watch the `Publish` workflow and confirm the GitHub release has the zip.

```bash
git tag -a v0.2.0 -m "v0.2.0"
git push origin v0.2.0
gh run watch --exit-status
gh release view v0.2.0 --json url,assets
```

## Test on Windows

- `jv dark` should top-hit Toggle Dark Mode with a high Jev percentage.
- `jv wifi off` should prefer Turn Wi-Fi Off over Turn Wi-Fi On.
- `jv the pdf I just downloaded` should prefer the newest PDF in Downloads.
- `jv <word>` should not list `.lnk` shortcuts or hidden system files.
- With Everything installed, a file outside the scanned folders (a non-system
  drive, for example) should still appear.
- With no API key, the footer row says so and the order is fuzzy-only.
- Kill the network mid-query: the list must still show fuzzy results.

## Credits

Ranking design, Jev question wording and the ready-badge rule come from the
jev-launcher experiment in dabit3/jev-experiments. Jev API by TypeSafe AI,
see https://docs.typesafe.ai.
