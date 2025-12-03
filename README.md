[![License: MPL 2.0](https://img.shields.io/badge/License-MPL_2.0-brightgreen.svg)](LICENSE)
![Status: Experimental](https://img.shields.io/badge/status-experimental-orange)

<p align="center">
  <img src="Assets/logo.png" alt="rinne logo" width="20%" />
</p>

<h1 align="center">RINNE v0.9.1</h1>

<p align="center">
  Rinne is a history management tool that treats your project folder as a sequence of snapshots.<br/>
  It stores snapshots and metadata over time so you can restore, combine, and reconstruct any past state.
</p>
<br/>

---

# Rinne — Local Snapshot-Based Versioning Tool

> Rinne is a simple command-line tool that lets you keep a “point-in-time snapshot” of a folder exactly as it is.

It is designed to handle not only source code, but also images, videos, game assets, documents and any other files that live together in a project.
Rinne assumes that you want to save the whole project **as a folder**, including large binaries and projects that easily exceed 100k files.

- Everything runs locally on a single PC – no network, no dedicated server required
- Instead of line-based diffs, Rinne records the whole folder state at each point in time
- Text and binary files are treated equally – game assets and data files can be stored as they are
- Logical snapshots use deduplication + compression (Compact) so you can keep many snapshots without exploding disk usage

This README describes **v0.9.1 (experimental)**.  
All 0.x versions are treated as experimental; **backwards compatibility is not guaranteed** until v1.x.x.

## 1. Overview / What is Rinne?

Rinne is a local-only CLI tool that keeps snapshots of a folder in the exact state it was in when you saved it.

### Problems Rinne is Trying to Solve / Why Rinne?

Rinne is meant for situations like:

- You work with Unity / UE / DCC tools / video editing, etc., and your projects contain huge assets.  
  You want to keep local history of the whole folder, but spinning up a local server and configuring a diff-based VCS feels overkill.
- For work or personal reasons you **cannot** or **do not want to** host your project on external services or dedicated servers,  
  yet a pure “folder copy” workflow becomes hard to manage as the number of copies grows.
- You would like to analyze and process past states using search tools or scripts, but the history is buried inside
  app-specific formats or in the cloud, and you cannot easily treat it as a normal local folder.

### How Rinne Approaches This

Rinne uses three main mechanisms:

- **Logical snapshots with dedup + compression**  
  Under `.rinne/store` it stores chunked, deduplicated and compressed data so you can keep many snapshots even for very large projects.
- **Time + UUIDv7-based IDs**  
  Each snapshot gets an ID derived from UTC time + UUIDv7, guaranteeing a consistent timeline and uniqueness.  
  This is more robust than hand-crafted folder names containing dates or notes.
- **Physical snapshots**  
  `<id>/snapshots` contains a full copy of the files for that snapshot so that the OS and external tools can treat it as a normal folder.

### Main Characteristics

- **State-based history**  
  Rinne records the **entire folder at that point in time**, not diffs.

- **Physical snapshots / logical snapshots**  
  Rinne can keep both:  
  physical snapshots with real files, and logical snapshots based on deduplication + compression.

- **Local-only**  
  As long as you carry around the `.rinne` folder, you can move your project – including its history – without relying on external services.

- **Designed for heavy projects**  
  Rinne assumes tens of thousands or hundreds of thousands of files, and tens of gigabytes of data.
  There are commands to GC caches and metadata for such environments.

---

## 2. Quick Start (Use in 3 Minutes)

This section shows the minimum steps to try Rinne.

- Place `Rinne.exe` somewhere on your machine.
- Add that folder to your `PATH`.
- Open a terminal **at the project root folder**.

### 2.1 Initialize a Repository

```bash
rinne init
```

This creates `.rinne/` at the current directory and sets up the default space `main`.  
If `.rinne/` already exists, the command fails.

> Note (Windows): If `.rinne/` is not created, treat it as a failure even if no error is shown.
> Move the project to a user-writable path or run the terminal as Administrator.

### 2.2 Save the Current State (Physical Snapshot)

```bash
rinne save -m "first snapshot"
```

Saves the current folder into a **physical snapshot (Full)** in the `main` space.  
It creates `.rinne/snapshots/space/main/<id>/` with:

- a physical copy of the files
- `meta.json`
- `note.md`

You can attach a note with `-m` (you can also edit `note.md` later via the `note` command or any editor).

### 2.3 Show History

```bash
rinne history
```

Shows a list of snapshots in the current space (default is `main`).

### 2.4 Restore a Past State (from a Physical Snapshot)

```bash
rinne restore --back N   # N back from latest (0=latest)
```

Restores the working directory from a physical snapshot.  
Rinne will overwrite existing files in the target directory.

> Make sure to back up your current state (via `rinne save` or OS tools) **before** restoring.  
> Rinne does **not** guarantee rollback of overwritten data.

### 2.5 Save as a Logical (Compact) Snapshot

For large projects you can save directly as a **logical snapshot (Compact)**:

```bash
rinne save --compact -m "logical snapshot"
```

Rinne chunks, deduplicates and compresses the contents, storing them under `.rinne/store/`.  
The “definition” of that snapshot is written to `.rinne/store/manifests/<id>.json`.

- The initial save has no cache yet, so it will take as long as a full scan of your project (I/O bound).
- The recommended default is **not** to keep physical payloads under `.rinne/snapshots/...` for Compact snapshots (details below).

### 2.6 Restore a Logical Snapshot

```bash
rinne restore --back N --hydrate=tmp   # N back from latest (0=latest)
```

Restores the latest logical snapshot into the current directory.  
Because logical snapshots have no physical payload, Rinne temporarily re-materializes the folder from the store.

Again, make sure to back up your current state before restoring; Rinne does not guarantee rollback of overwritten content.

---

## 3. Core Concepts

### 3.1 Space (Workspace)

A **space** is a lightweight container for a timeline of snapshots.  
The default space is `main`.

You can create, switch and list spaces with the `space` command:

```bash
rinne space list        # list spaces
rinne space create exp  # create space 'exp'
rinne space use exp     # switch current space to 'exp'
```

Each space has its own set of snapshot IDs under `.rinne/snapshots/space/<name>/`.

### 3.2 Snapshot (Physical / Logical)

Rinne has two types of snapshots: **physical snapshots** and **logical snapshots**.

> **Terminology:** Physical = **Full**, Logical = **Compact**.

#### 3.2.1 Physical Snapshot (Full)

- Stored under `.rinne/snapshots/space/<space>/<id>/snapshots/...` as literal copies of the files at that time.
- From the OS point of view it is just a normal folder with normal files.
- You can browse it in Explorer/Finder, drag files into other apps, zip it, send it to someone, etc.
- Very intuitive and flexible, but saving time and disk usage scale roughly with the total size of the directory.

The main benefit of a physical snapshot is that you can treat it as a **plain folder**, without going through Rinne at all.

#### 3.2.2 Logical Snapshot (Compact)

- Contents are chunked and stored in `.rinne/store/` with deduplication and compression.
- No physical payload is kept; instead, `.rinne/store/manifests/<id>.json` describes how to reconstruct the state.
- This is suitable for keeping long histories of very large or binary-heavy projects.
- To touch the actual files you need to **hydrate** the snapshot or **pick** specific paths.

Rinne’s philosophy is to let you combine **physical snapshots (easy to handle)** and **logical snapshots (storage efficient)** depending on the situation.

### 3.3 Store / Manifest (Internal Structure of Logical Snapshots)

Logical (Compact) snapshots live under `.rinne/store/`:

- `.rinne/store/...`  
  The actual chunk data (deduplicated and compressed).

- `.rinne/store/manifests/<id>.json`  
  The manifest describing how to reconstruct a given snapshot ID – which files consist of which sequence of chunks, etc.

Multiple snapshots share chunks so that repeated files or binaries are stored only once.

### 3.4 meta.json (State Hash and Metadata)

Each snapshot has a metadata file (generated by `save` / `import` / `recompose` etc.):

- Path: `.rinne/snapshots/space/<space>/<id>/meta.json`

It contains a few simple fields:

- Hash algorithm used
- Snapshot hash (computed from contents; `"skip"`/`"SKIP"` when using `--hash-none`)
- Number of files
- Total bytes

---

## 4. Directory Layout (Example for v0.9.1)

The structure under `.rinne` looks like this (not compatible with older versions):

```text
your-project/
 ├─ ... (your normal project files)
 └─ .rinne/
     │
     ├─ config/
     │
     ├─ snapshots/
     │   └─ space/
     │       ├─ main/                   # space name
     │       │   └─ <id>/
     │       │       ├─ snapshots/      # physical payload (missing for pure logical snapshots)
     │       │       ├─ meta.json       # metadata
     │       │       └─ note.md         # note
     │       ├─ exp/
     │       │   └─ <id>/...
     │       └─ current                 # name of the current space
     │
     ├─ store/
     │   ├─ .meta/
     │   ├─ .tmp/
     │   └─ manifests/                  # Compact manifests (<id>.json)
     │ 
     └─ temp/                           # temporary files, etc.
```

---

## 5. Typical Workflows

### 5.1 Physical-Snapshot-First Backup

This is the simplest “folder backup” style workflow.

> 1. `rinne init`  
> 2. Work on your project  
> 3. At milestones: `rinne save -m "description"`  
> 4. When needed, use `rinne restore` or browse `.rinne/snapshots/...` in Explorer and copy files

Because physical snapshots keep real files under `.rinne/snapshots/`, checking and copying content is extremely easy.  
On the other hand, time and disk usage grow with file count and total size.

Use `rinne tidy` to prune older snapshots as needed.

### 5.2 Logical-Snapshot-First for Large Projects

This is intended for huge projects – Unity/UE assets, many binaries, etc. – where you want to keep many states efficiently.

> 1. `rinne init`  
> 2. `rinne save --compact -m "description"`  
> 3. Use `rinne history` to inspect  
> 4. When needed, use `rinne hydrate` or `rinne pick` to materialize data

All actual data lives in `.rinne/store/`.  
Chunks are shared by multiple snapshots, so keeping many snapshots does not grow disk usage as quickly.

### 5.3 Hybrid Workflow (Physical + Logical)

You can run with physical snapshots first, and later convert older ones to logical snapshots.

> 1. `rinne init`  
> 2. `rinne save -m "description"`  
> 3. After some time: `rinne compact --keep N`  # convert older snapshots to logical, keep N latest as physical

This lets you keep a few recent snapshots as easy-to-handle physical folders, and convert older ones into compact, storage-efficient logical snapshots.  
The goal is to balance usability (physical) and disk usage (logical).

---

## 6. Command Reference

> #### Note  
> Argument syntax may differ between shells.  
> For example, in PowerShell you often need to quote things like `'@0'`.

### 6.1 `init` — Initialize a New `.rinne` Repository

#### Example

```text
rinne init
```

#### Options

- none (current directory is used)

#### Description

- Creates the standard Rinne folder structure (`.rinne` etc.) and the default space `main` in the current directory.
- Fails if a `.rinne` directory already exists.
- This is equivalent to `git init` in spirit – it creates a Rinne repository.

---

### 6.2 `space` — Manage Spaces

#### Examples

```text
rinne space list
rinne space create main
rinne space create work
rinne space use work
rinne space rename work work-v2
rinne space current
rinne space delete old
```

#### Subcommands

- `list`  
  Show existing spaces.

- `create <name>`  
  Create a new empty space.  
  Fails if a space with the same name already exists.

- `use <name>`  
  Switch the current space.  
  Updates `.rinne/snapshots/current` to `<name>`.

- `rename <old> <new>`  
  Rename an existing space `<old>` to `<new>`.  
  Fails if `<new>` already exists.

- `current`  
  Print the name of the active space.

- `delete <name>`  
  Delete the specified space and **all snapshots** in it.  
  Fails if the space does not exist.  
  Cannot delete the current space.

#### Notes

- Each space corresponds to `.rinne/snapshots/space/<name>/` and represents an independent **timeline**.
- Typical usage is to have spaces like `main`, `work`, `experiment`, etc.

---

### 6.3 `save` — Create a Snapshot

#### Examples

```text
rinne save
rinne save main -m "first snapshot"
rinne save work --compact -m "save directly into CAS"
rinne save work --compact-full -m "strict verification for all files"
rinne save main --hash-none -m "skip hash calculation"
```

#### Options

- Space
  - `<space>`  
    If omitted, the current space (`rinne space current`) is used.

- Note
  - `-m <text>` / `--message <text>`  
    Set the initial contents of `<id>/note.md`.

- Storage mode
  - `--compact` / `-c`  
    Save directly as a logical snapshot **without** creating payload under `<id>/snapshots`.  
    Uses a metadata cache DB (mtime/size-based) to avoid re-hashing unchanged files.
  - `--compact-speed` (experimental)  
    Enables a faster variant of `--compact` that uses more memory.  
    On memory-constrained machines or very large setups handling tens of millions of files,
    plain `--compact` is recommended instead.
  - `--compact-full`  
    Same as `--compact`, but **fully verifies** all file contents without relying on the cache DB.

- Hash calculation
  - `--hash-none`  
    Skip computing the snapshot hash.  
    Writes `HashAlgorithm="skip"` and `SnapshotHash="SKIP"` into `meta.json`.  
    This mode maximizes speed, but such snapshots cannot be strictly verified with `verify`.

#### Description

- Saves the current directory as a snapshot under `.rinne/snapshots/space/<space>/<id>/` (physical mode).
- Always creates `<id>/note.md`; with `-m` the note content is initialized.
- `--compact` / `--compact-full` save directly as a logical snapshot (CAS + manifest), which is more storage-efficient.
- Use `--hash-none` only when you do not need later verification (e.g., frequent intermediate saves for speed).

> ### `rinneignore.json` (Excluding Files from Snapshots)

The `save` command reads `.rinne/rinneignore.json` at the project root and uses it to decide which files and directories to include.  
Rinne’s built-in ignore rules are merged on top of this file.

#### Location

- Path: `.rinne/rinneignore.json`
- If the file exists, `rinne save` loads it and merges it with Rinne’s default ignore rules.

> `.rinne` itself is always excluded, regardless of the config.

#### JSON Structure

The file is a simple JSON object with three arrays:

```jsonc
{
  // Patterns against full relative paths (both files and directories)
  "exclude": [
    ".rinne/**",
    ".git/**",
    "bin/**",
    "obj/**"
  ],

  // Patterns for file names / extensions
  "excludeFiles": [
    "*.tmp",
    "*.log",
    "*.user"
  ],

  // Patterns for directory names
  "excludeDirs": [
    "cache/",
    "temp/"
  ]
}
```

- **`exclude`** – evaluated against root-relative paths; matches both files and directories.
- **`excludeFiles`** – patterns for file names / extensions (e.g., `*.tmp`, `*.log`).
- **`excludeDirs`** – patterns for directory names (e.g., `cache/`, `temp/`).

All patterns apply **only to new snapshots** created by `rinne save`.  
Already existing snapshots are not modified.

#### Evaluation Timing

1. `rinne save` enumerates files and directories under the target folder.
2. `.rinne` is always excluded by the code.
3. For each path:
   - directories are filtered by `exclude` / `excludeDirs`
   - files are filtered by `exclude` / `excludeFiles`

#### Notes

- Changing `rinneignore.json` does **not** affect snapshots that already exist.  
  If you want to apply a new policy, run `rinne save` again.
- Because `.rinne` is always excluded, you will not accidentally include Rinne’s own metadata inside snapshots.

---

### 6.4 `import` — Import an External Directory as a Full Snapshot

#### Examples

```text
rinne import /path/to/project -m "initial import of external project"
rinne import ./backup --space archive --dry-run
rinne import ~/work/game --space main
```

#### Options

- Positional
  - `<source-directory>`  
    The directory to import. Everything under it is treated as a single full snapshot.

- Space
  - `--space <space>`  
    Target space. Defaults to the current space.

- Behavior
  - `--dry-run`  
    Show which snapshot/files would be created without actually copying (implementation-dependent).
  - `-m <text>` / `--message <text>`  
    Initial contents for `<id>/note.md`.

#### Description

- Reads the contents of `<source-directory>` and imports them as **one full snapshot** into the current Rinne repo.
- If `<source-directory>` has its own `.rinne` directory, it is ignored to keep histories separate.
- Snapshot IDs are generated from current UTC time + UUIDv7.
- `<id>/note.md` is always created; `-m` pre-fills it (you can later edit via `rinne note` or any editor).

---

### 6.5 `recompose` — Combine Multiple Snapshots into a New One

#### Examples

```text
rinne recompose main --src @0 --src @5 -m "merge latest and 5 snapshots ago"
rinne recompose --space main --src work:@2 --src main:@0 --hydrate
rinne recompose archive --src main:20251120T1234 --src main:@1 --hydrate=ephemeral
```

#### Options

- Basic forms
  - `rinne recompose [<target-space>] --src <spec> [--src <spec> ...] [options] [-m <text>]`
  - `rinne recompose --space <target-space> --src <spec> [--src <spec> ...] [options] [-m <text>]`

- `--src <spec>` (can be repeated; leftmost has highest priority)
  - `<spec>` forms:
    - `[space:]<idprefix>`
      - e.g. `main:20251120T...` / `20251120T...`
    - `[space:]@<N>`
      - e.g. `main:@0` / `@1`
  - If `space:` is omitted, the **target space** is assumed.
  - When multiple `--src` are given, earlier ones override later ones (“left wins”).

- Hydration
  - `--hydrate`  
    Permanently hydrate missing payloads for source snapshots before composing.
  - `--hydrate=ephemeral` / `--hydrate=tmp`  
    Hydrate missing payloads temporarily just for the compose; do not leave `<id>/snapshots` behind.

- Note
  - `-m <text>` / `--message <text>`  
    Set the initial note for the new snapshot.

#### Description

- Think of `recompose` as a **hierarchical merge of snapshots**.
  - Leftmost `--src` has highest priority; later ones only fill in missing files.
- Even if a source snapshot has no payload (logical only), `--hydrate` / `--hydrate=ephemeral` can reconstruct the data as needed.
- The new snapshot gets a normal ID and is created under `.rinne/snapshots/space/<target-space>/<id>/`.

---

### 6.6 `history` — List Snapshots

#### Examples

```text
rinne history
rinne history main --take 20
rinne history work --since 2025-01-01 --before 2025-12-31
rinne history --space archive --match 202511* --size
```

#### Options

- Filters
  - `--take N`  
    Show up to N snapshots from newest to oldest.
  - `--since YYYY-MM-DD`  
    Only snapshots on or after this date (local date 00:00 converted to UTC).
  - `--before YYYY-MM-DD`  
    Only snapshots **before** this date (local date 00:00 converted to UTC).
  - `--match GLOB`  
    Filter snapshot IDs by glob (`*`, `?`), e.g. `--match 202511*`.

- Other
  - `--size` / `--bytes`  
    Compute and show payload size for each snapshot (may be slow).
  - `<space>` / `--space <space>`  
    Target space (defaults to current).

#### Description

- Lists snapshots in a space in **newest-first** order.
- Date, count and ID pattern filters make it handy for inspection and housekeeping.
- With `--size` you can identify heavy snapshots.

---

### 6.7 `note` — List / View / Append / Overwrite / Clear Snapshot Notes

#### Examples

```text
rinne note
rinne note main
rinne note main @0 --view
rinne note main 20251120T1234 --append "Build succeeded."
rinne note @1 --overwrite "Replaced note content."
rinne note work @2 --clear
```

#### Options

- Subcommand-like usage
  - `rinne note [<space>]`  
    List snapshots that have a note file (usually `note.md`).
  - `rinne note [<space>] <id|@N> --view`  
    Print the note for the given snapshot.
  - `rinne note [<space>] <id|@N> --append <text>`  
    Append `<text>` to the existing note.
  - `rinne note [<space>] <id|@N> --overwrite <text>`  
    Replace the entire note.
  - `rinne note [<space>] <id|@N> --clear`  
    Clear the note (empty file; the file itself remains).

- Space
  - `<space>`  
    If present, target that space; otherwise use the current space.

- Snapshot selector
  - `<id>`  
    Full ID or any unique prefix.
  - `@N`  
    `@0` is latest, `@1` is one before that, and so on.  
    In PowerShell you need to quote it:
    ```text
    rinne note '@0' --view
    ```

#### Description

- Example of appending multi-line text in PowerShell:

  ```text
  rinne note '@0' --append @"
  line1
  line2
  "@
  ```

- Each snapshot has at most one note file (usually `note.md`); this command provides a lightweight way to manage them.
- Notes are **not** included in the snapshot hash, so editing them later does not break verification.
- For complex Markdown editing, use an external editor.

---

### 6.8 `export` — Safely Export Snapshots as Plain Files

#### Examples

```text
rinne export main @0 --to out
rinne export main 2025 --to out
rinne export --space work @1 @2 --to backup
rinne export --to latest-out
```

#### Options

- Selectors (optional, can be multiple)
  - Each selector can be:
    - `full-id`
    - `id-prefix`
    - `@N` (where `@0` is latest)
  - If no selector is given, the default is `@0` (latest only).

- Space
  - `<space>` (positional; the **first positional argument is always interpreted as space**)  
  - `--space <space>`
  - Examples:
    - OK: `rinne export main 2025 --to out`  
      → space = `main`, selector = `2025`
    - NG: `rinne export 2025 --to out`  
      → interpreted as space `2025`

- Destination
  - `--to <dir>` / `--dest <dir>` (required)  
    Root directory for exported snapshots.

- Overwrite
  - `--overwrite`  
    Overwrite (recreate) destination folders if they already exist.

#### Description

- If you omit `<space>` / `--space`, the current space from `.rinne/snapshots/current` is used.
- If you omit selectors, `@0` (latest) is assumed.
- Export layout is fixed:

  ```text
  <dest>/<space>/<id>/
      meta.json
      note.md
      snapshots/...
  ```

- `export` performs a **plain, safe copy**.  
  It is suitable for sending a snapshot to non-Rinne users or for simple folder backups.

---

### 6.9 `restore` — Restore a Snapshot into a Working Directory

#### Examples

```text
rinne restore main --back 1
rinne restore --space work --id 20251120T1234 --to ./restore-test
rinne restore main --back 0 --purge --hydrate
rinne restore --back 2 --hydrate=ephemeral
```

#### Options

- Selector (exactly one; default is “latest”)
  - `--id <prefix>`  
    Unique snapshot ID prefix (takes precedence over other selectors).
  - `--back N` / `--offset N`  
    Equivalent to `@N`; `0` is latest, `1` is one before, etc.

- Space
  - `<space>` / `--space <space>`  
    Defaults to the current space.

- Destination / behavior
  - `--to <dir>`  
    Root directory to restore into (default: current directory).
  - `--purge`  
    Remove all files and directories in the destination before restoring (except `.rinne`).  
    Use this if you want the destination to match the snapshot exactly.

- Hydration
  - `--hydrate`  
    Permanently hydrate missing payloads for the snapshot (create `<id>/snapshots` if absent).
  - `--hydrate=ephemeral` / `--hydrate=tmp`  
    Temporarily hydrate for restore and then discard the payload.

#### Description

- By default, `restore` is **non-destructive merge** – it does not delete extra files in the destination.  
  Use `--purge` for a clean rollback to exactly that snapshot.
- If space is omitted, it is read from `.rinne/snapshots/current`.
- Hydrate options enable smooth restore even for pure logical snapshots.

---

### 6.10 `pick` — Extract a Path from a Logical Snapshot

#### Examples

```text
rinne pick main 20251120T1234 src/Program.cs ./Program.cs
rinne pick work @0 Assets/Textures ./textures-latest
rinne pick @1 docs ./docs-from-prev
```

#### Positional Arguments

- `<space>` (optional)  
  If omitted, the current space is used.
- `<snapshot-id>`  
  Full ID, unique prefix, or an `@N` selector.  
  `@0` is the latest snapshot, `@1` is one before that, and so on.  
  The `@N` index counts **all snapshots (physical + logical)** in the space in newest-first order.
- `<selector>`  
  A path inside the **logical snapshot** (file or directory).
- `<out-path>`  
  Destination path on the local filesystem (file or directory).

#### Description

- Extracts **one file or one directory** from a logical snapshot via the store.
- If `<selector>` does not match any path in the snapshot, the command fails with a non-zero exit code.
- If `<space>` is omitted, the current space name is read from `.rinne/snapshots/current`.
- `@N` is useful when you want the latest or “N snapshots ago” without typing the full ID.  
  In PowerShell you usually need to quote it:
  ```text
  rinne pick '@0' Assets/Player.prefab out/Player.prefab
  ```
- This is especially handy when you want to recover just a part of a huge project (e.g. only a specific config or prefab).
- The `@N` index also counts physical snapshots, but **`pick` only works on logical (compact) snapshots**.  
  If the target snapshot has no manifest, the command fails with `manifest not found for snapshot`.

---

### 6.11 `hydrate` — Rebuild Payload from a Manifest

#### Examples

```text
rinne hydrate main --latest 3
rinne hydrate --space work --before 2025-01-01
rinne hydrate --space archive --id 20251120T1234 --rm-manifest
```

#### Options

- Selector (**exactly one** is required)
  - `--latest N`  
    Target the latest N snapshots.
  - `--ago <age>`  
    Target snapshots older than a relative age, e.g.:
    - `30d` – 30 days or older
    - `2w` – 2 weeks or older
    - `3mo` – 3 months or older
    - `1y` – 1 year or older
  - `--before YYYY-MM-DD`  
    Target snapshots before this local date.
  - `--id <prefix>`  
    A single unique ID prefix.
  - `--match GLOB`  
    Target IDs that match a glob pattern.

- Space
  - `<space>` / `--space <space>`  
    Defaults to the current space.

- Manifest deletion
  - `--rm-manifest`  
    If hydration for a given ID succeeds, delete its manifest file (`.rinne/store/manifests/<id>.json`).  
    Skipped/failed IDs keep their manifests.

#### Description

- Reads `.rinne/store/manifests/<id>.json` and reconstructs `<id>/snapshots/` from the chunk store.
- IDs that already have `<id>/snapshots/` are skipped.
- Omitting `<space>` / `--space` uses the current space from `.rinne/snapshots/current`.
- Be careful when hydrating many IDs at once – it can consume a lot of disk space and time.

---

### 6.12 `compact` — Convert Full Snapshots to Logical Snapshots

#### Examples

```text
rinne compact main --keep 50
rinne compact main --latest 10
rinne compact main --before 2025-01-01
rinne compact --space work --keep 20 --full
```

#### Options

- Selector (one of)
  - `--keep N`  
    Keep the latest N snapshots **as full** and compact everything older.
  - `--latest N`  
    Compact the latest N snapshots.
  - `--before YYYY-MM-DD`  
    Compact snapshots before this local date.

- Space
  - `<space>` (positional)  
  - `--space <space>`  
    If omitted, the current space is used.

- Verification
  - `--full`  
    When compacting, **fully verify all file contents** regardless of cache (mtime/size).  
    This is slower but more robust for important snapshots.

- Performance
  - `--speed` (experimental)  
    Enables a faster compact path that uses more memory.
    On memory-constrained machines or very large setups handling tens of millions of files,
    the default compact path is recommended instead.

#### Description

- For each target snapshot, reads `<id>/snapshots/`, stores its contents in `.rinne/store` (dedup + compression) and writes
  `.rinne/store/manifests/<id>.json`.
- On success, deletes `<id>/snapshots/`, leaving only the logical snapshot.
- With `--full`, content is always re-checked, which is useful for first-time or critical compactions.

---

### 6.13 `tidy` — Delete Snapshots + CAS Garbage Collect

#### Examples

```text
rinne tidy main --keep 50
rinne tidy main --latest 5
rinne tidy main --before 2024-01-01 --dry-run
rinne tidy --space archive --match 2025* --no-gc
```

#### Options

- Selector (**exactly one; cannot be combined**)
  - `--keep N`  
    Keep the latest N snapshots; delete older ones.
  - `--latest N` / `--newest N`  
    Delete the latest N snapshots (reverse of `--keep`).
  - `--before YYYY-MM-DD`  
    Delete snapshots older than this local date.  
    (Some implementations also accept full ISO 8601 timestamps.)
  - `--match GLOB`  
    Delete snapshots whose IDs match a glob pattern.  
    Multiple patterns may be allowed (implementation dependent).

- Behavior
  - `--dry-run` / `--dry`  
    Show what would be deleted and GC’d without actually executing.
  - `--no-gc`  
    Do not run CAS GC after deleting snapshots.  
    **Deprecated** and scheduled for removal in the future.

- Space
  - `<space>` / `--space <space>`  
    Defaults to the current space.

#### Description

- `tidy` deletes snapshots matching the selector and then runs CAS GC to remove unreachable chunks.
- Selector must be exactly one of `--keep`, `--latest`, `--before`, or `--match`.
- It is strongly recommended to run `tidy` with `--dry-run` first.

---

### 6.14 `cache-meta-gc` — GC for `filemeta.db` Entries

#### Examples

```text
rinne cache-meta-gc
rinne cache-meta-gc main
rinne cache-meta-gc main --keep 60
rinne cache-meta-gc --space work --keep 7
```

#### Options

- `--keep DAYS`  
  - Default: `30`  
  - Rows with `updated_at_ticks` within the last DAYS are kept.  
  - Rows older than that **and** referring to paths that no longer exist in the workspace are deleted.

- `--space <space>` / positional `<space>`  
  - Target space name.  
  - If omitted, the current space from `.rinne/snapshots/current` is used.

#### Description

- Only operates on `filemeta.db` (the metadata cache).  
- `filemeta.db` is treated as a **cache**. Deleting rows only forces re-hashing the next time; actual files and snapshots are not touched.
- This command is rarely needed, but for projects with 100k+ files and frequent structural changes, it can help prevent unnecessary growth and performance degradation.

---

### 6.15 `verify` — Verify Snapshot State Hashes

#### Examples

```text
rinne verify
rinne verify main
rinne verify main 2025 --policy error
rinne verify work @0 @1 --policy hydrate --show-details
rinne verify --space archive --policy temp --only-bad
```

#### Options

- Basic forms
  - `rinne verify [<space>] [ids...] [options...]`
  - `rinne verify --space <space> [ids...] [options...]`

- Space
  - `<space>` / `--space <space>`  
    Defaults to current space.

- Target snapshots
  - `ids...` (zero or more)  
    If omitted, all snapshots in the space are verified.  
    Each ID can be:
    - full ID
    - unique ID prefix
    - `@N` selector

- Policy for logical snapshots without payload
  - `--policy <mode>`:
    - `error` (default)  
      Snapshots without payload are treated as **errors**.
    - `skip`  
      Skip snapshots without payload; only verify those with payload.
    - `hydrate`  
      Permanently hydrate missing payloads and then verify.
    - `temp`  
      Temporarily hydrate for verification and then revert (implementation dependent).

- Output control
  - `--show-details`  
    Show OK results as well as failures (by default only mismatches/errors are shown).
  - `--only-bad`  
    With `--show-details`, suppress OK items and show only mismatches/errors.

#### Description

- `verify` recomputes snapshot hashes from actual files and compares them with `meta.json`.
- Snapshots created with `--hash-none` lack a meaningful `SnapshotHash`, so verification is not useful for them.  
  For important snapshots, use the normal hash-enabled modes.
- Verifying all snapshots of a huge project can take a long time.  
  Consider spot-checks or verifying only important milestones.

---

### 6.16 `diff` (Deprecated) — Simple Diff Between Two Snapshots

#### Examples

```text
rinne diff @1 @0
rinne diff --space work @3 @0
```

#### Options

- `--space <space>`  
  Target space (defaults to current).

#### Description

- `@N` means “N from the latest”:
  - `@0` – latest
  - `@1` – one before latest
  - `@2` – two before latest
- Shows a simple diff between two snapshots on the CLI.
- **Deprecated** – this command is scheduled for removal.  
  The long-term direction is to rely on external diff tools and/or AI-assisted workflows.

---

### 6.17 `textdiff` (Deprecated) — Text Diff Between Two Snapshots

#### Examples

```text
rinne textdiff main @1 @0
rinne textdiff --space work 20251120T1234 @0
rinne textdiff @2 @0
```

#### Options

- Space
  - `<space>` / `--space <space>`  
    Defaults to current space.

- Snapshots
  - `<A> <B>`  
    Full IDs, ID prefixes, or `@N` selectors.

#### Description

- Shows a unified diff of text files between two snapshots.
- If payload is missing, it may hydrate temporarily in order to diff.
- **Deprecated** – scheduled for removal; the long-term direction is external diff tools / AI assistants.
- This command accepts only positional arguments and no additional options.

---

## 7. Design Principles and Philosophy

### 7.1 “Save the Whole State”

Rinne treats **“the state at that point”** as the primary unit, not text diffs.

- Large refactors, massive renames or moves are still just a single snapshot.
- The tool cares about **which point in time** a state represents, not the exact edit path that led there.

---

### 7.2 Physical = Usability, Logical = Efficiency

- Physical snapshots (Full) are just folders from the OS point of view, and are the easiest to work with.  
  In exchange, save time and disk usage grow with the directory size.
- Logical snapshots (Compact) maximize storage efficiency with dedup + compression,  
  but require hydrate/pick to access content.

Rinne provides commands like `save`, `compact`, `hydrate`, `tidy`, etc., so that you can choose the right mix.

---

### 7.3 Adapted to Large Binary Environments

- CAS-style chunk deduplication avoids storing the same binary multiple times.
- `filemeta.db` caches metadata to reduce disk I/O when re-saving (Compact modes).
- Everything is built around local filesystems.

---

### 7.4 Local-Only and Minimal Dependencies

- No dedicated server or cloud services are assumed.
- Carrying the project folder + `.rinne` to another machine is enough to bring the history.

---

### 7.5 Finite History via Spaces and `tidy`

Rinne does **not** assume infinite history.

- Use spaces to separate histories by purpose.
- Use `tidy` to drop old snapshots and GC unused chunks.
- The design goal is to keep a **finite, manageable history** that matches your local storage.

---

## 8. Limitations and Cautions

- Manually deleting or moving files under `.rinne` can break history and consistency.  
  Flexibility and danger are a trade-off here – be careful.
- `restore` overwrites your working directory.  
  Take backups as needed before restoring.
- `tidy` permanently deletes history.  
  Double-check selectors before running.
- Snapshots created with `--hash-none` cannot be strictly verified by `verify`.
- Currently Rinne is CLI-only. GUIs and integrations are being explored as separate projects.
- The architecture is still evolving; heavy optimizations are intentionally deferred until things stabilize.
- Windows permissions (Access denied / silent failure)  
  Rinne must create and modify `.rinne/` (and may overwrite files during `restore`).  
  Under protected folders or restricted permissions, operations may fail (sometimes without a clear error) and expected outputs  
  such as `.rinne/` or new snapshot directories may simply not appear. Use a user-writable location (avoid `C:\`, `Program Files`)  
  or run the terminal as Administrator.
- Locked / in-use files  
  If files are locked by other processes (Unity/UE editor, Visual Studio, sync tools like OneDrive, antivirus, etc.),
  snapshot operations (`save`, `import`, `restore`, `compact`, `hydrate`) may fail.  
  Close the application, pause sync, or exclude the path via `rinneignore.json`.

---

## 9. Installation and Upgrade

### 9.1 First-Time Setup

- Install .NET 8 or newer.
- Download the Rinne CLI binary from the release page.
- Put it into a folder on your `PATH` and confirm `rinne --help` works.

---

### 9.2 Notes on v0.9.0 → v0.9.1 Compatibility

There is **no compatibility** between v0.9.0 and v0.9.1.

- Some commands were reorganized:
  - New / refreshed: `compact`, `save --compact`, `save --compact-full`, `cache-meta-gc`, etc.
  - Some older commands might have been merged or removed.
- Internal structures were completely revamped.  
  Because the architecture is still experimental, aggressive optimization is intentionally postponed.

---

## 10. Roadmap

- Further speed and stability improvements for save / verify / GC
- Cross-platform validation and improvements (experimental)
- GUI and other integrations as parallel experimental projects
- In-memory RAG integration in GUI (experimental)
- Automatic snapshot message generation on save (GUI, AI-assisted)
- Future experimental commands such as `stillverse`, `spacewalk`, `phantom`, `rebirth`
- Architecture clean-up and optimization (planned for v0.9.5 and beyond)

---

## 11. Troubleshooting (Windows)

### 11.1 `.rinne/` is not created (init/save seems to do nothing)
- This is usually a permissions issue (protected folders, restricted write access).
  Move the project to a user-writable path or run the terminal as Administrator.
- If you are using Windows Defender “Controlled folder access”, you may need to allow the terminal / Rinne binary.

### 11.2 Snapshot operations fail due to locked / in-use files
- Some files can be locked by Unity/UE, Visual Studio, sync tools (OneDrive), or antivirus.
  Close the application, pause sync, or exclude the path via `rinneignore.json`, then retry.

### 11.3 OneDrive (or other sync tools) causes failures or instability
- Avoid placing the repository under a synced folder, or exclude `.rinne/` from syncing.
  Sync tools can lock files and also slow down heavy I/O.

### 11.4 Antivirus makes `save` / `compact` extremely slow
- Consider excluding your project folder and `.rinne/` from real-time scanning for best performance.
