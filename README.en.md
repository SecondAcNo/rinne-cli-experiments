[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
![Status: Experimental](https://img.shields.io/badge/status-experimental-orange)

<p align="center">
  <img src="Assets/logo.svg" alt="rinne logo" width="30%" />
</p>

<h1 align="center">RINNE CLI</h1>

<p align="center">
  Rinne is a history management application that handles project folders as <strong>complete snapshots</strong> rather than diffs.<br/>  
  It stores snapshots and metadata as historical records, enabling restoration, composition, and reconstruction at any point in time.
</p>
<br/>

[>日本語版](./README.md)

---

## Overview

Rinne is a snapshot-based history management tool that saves the exact state of your files or projects at a given moment.  
It provides a mechanism for replaying, verifying, and recomposing past states to make reuse easier.

---

## Features (Planned)

RINNE primarily targets large binary assets (tens of gigabytes or more).  
While keeping full snapshots as the core design, the following features are being tested in stages:

- **compress**: Recompression of existing snapshots *(experimental)*
- **deep archive**: Long-term storage with chunk sharing and compression *(experimental)*
- **internal refactor**: Structural reorganization for future extensibility *(experimental)*

---


## Philosophy

Rather than tracking changes, Rinne focuses on preserving entire states for replay.  
It treats history as complete states instead of diffs, aiming for reversibility, simplicity, and finite circulation.

### 1. Reversibility
Every snapshot is self-contained and fully restorable.  
Instead of rewinding to the past, Rinne regenerates the specific point in time.

### 2. Simplicity
Rinne has no special dependencies.  
It works with standard OS operations and can easily integrate with other tools.

### 3. Finite Circulation
Keeping all history indefinitely is excessive for most use cases.  
Rinne encourages organization and consolidation, leaving only essential states for intuitive management.

---

## Directory Structure

```
.rinne/
 ├─ config/                                       # Configuration files
 ├─ data/
 │   ├─ main/                                     # Default working space
 │   │   ├─ 00000001_20251024T091530123.zip       # Snapshot archive
 │   │   └─ meta/
 │   │       └─ 00000001_20251024T091620223.json  # Snapshot metadata
 │   └─ other.../                                 # Other working spaces
 ├─ logs/                                         # Log outputs
 ├─ state/                                        # Current state
 │   └─ current                                   # Name of the current space
 └─ temp/                                         # Temporary files

.rinneignore                                      # Ignore rules
```

---

## Main Commands

### init — Initialize repository
```text
rinne init
```
Creates the standard `.rinne/` structure in the current directory.

---

### save — Save a snapshot
```text
rinne save [-s|--space <name>] [-m|--message "<text>"]
```
Saves the current working tree to `.rinne/data/<space>/` as a snapshot.  
A corresponding metadata file (`meta/<id>.json`) is also generated.

---

### space — Manage working spaces
```text
rinne space current                   # Show current space name
rinne space list                      # List all spaces
rinne space select <name> [--create]  # Select space
rinne space create <name>             # Create new space
rinne space rename <old> <new>        # Rename space
rinne space delete <name> [--force]   # Delete space
```
Manages independent working spaces.

---

### verify — Verify integrity
```text
rinne verify [--space <name>] [--meta <path>]
```
Verifies the consistency of metadata and the hash chain.

---

### restore — Restore a snapshot
```text
rinne restore <space> <id>
```
Restores the specified snapshot, reconstructing the project state at that time.

---

### diff — Show folder or file differences
```text
rinne diff <id1> <id2> [space]
```
Compares two snapshots and shows differences in folder structure or file contents.  
The result is formatted and output to the console.

---

### textdiff — Show text differences
```text
rinne textdiff [<old_id> <new_id> [space]]
```
Displays only text-based differences.  
Useful for comparing source code or configuration files.

---

### log — Show space history
```text
rinne log [space]
```
Displays the history of the specified space (or the current space).

---

### show — Display metadata
```text
rinne show <id> [space]
```
Shows formatted metadata (`meta.json`) for the specified snapshot.  
Includes details such as chain hash and timestamps.

---

### recompose — Recompose (history merge)
```text
rinne recompose <outspace> <space1> <id1> [, <space2> <id2> ...]
```
Merges multiple snapshots in priority order to create a new state and save it to the specified space.  
Snapshots on the **left take precedence**.

---

### backup — Create a backup
```text
rinne backup <outputdir>
```
Saves the entire `.rinne` folder as a backup archive.   

---

### import — Import a space from another repository
```text
rinne import <source_root> <space> [--mode fail|rename|clean]
```
Imports the specified space from another `.rinne` repository.  
Use `--mode` to control how conflicts are handled.  

| Mode | Description |
|------|-------------|
| fail   | Default. Cancels if the space already exists. |
| rename | Copies with a different name when conflict occurs. |
| clean  | Deletes the existing space and overwrites it. |

---

### drop-last — Delete the latest snapshot
```text
rinne drop-last [space] [--yes]
```
Deletes the latest snapshot of the specified space (ZIP and metadata pair).  
Use `--yes` to skip confirmation.  

---

### tidy — Clean up old history
```text
rinne tidy [space|--all] <keepCount>
```
Removes older history and recalculates hash chains (`prev/this`) to maintain consistency.  
Keeps only the latest `<keepCount>` snapshots in the target space.  
Use `--all` to apply cleanup to all spaces.

---

### log-output — Control log output
```text
rinne log-output <on|off|clean>
```
Enables, disables, or clears log file output.

---

## Usage Example

```text
rinne init
rinne save -m "Initial snapshot"
rinne space create feature-x
rinne space select feature-x
# ... work ...
rinne save -m "Feature added"
rinne recompose main feature-x 00000003_20251027T120000 dev-a 00000063_20251029T140000
rinne verify
rinne backup backups/
```

---

## Design Principles & Features

### 1. Full Snapshot Architecture
Rinne saves the entire directory as an archive instead of diffs.  
Each snapshot is self-contained and can be restored independently.  

### 2. Metadata and Hash Chain
Each snapshot has metadata that stores hash values, enabling integrity verification across history.  

### 3. Independent History Spaces
Rinne manages histories in independent spaces.  
Each space is isolated, allowing safe parallel work and experimentation.

### 4. Recomposition
Multiple snapshots can be merged in order of priority, selectively combining parts from other spaces.

### 5. Finite History Design
Rinne is not intended for infinite history storage.  
It assumes periodic cleanup and consolidation, leaving only meaningful states for cyclic management.

### 6. Low Dependency on OS and Tools
History consists of plain folders and files.  
You can browse, copy, and restore them using standard OS functions.  
No special tools are required — everything runs on the filesystem level.

### 7. Compatibility with AI and External Tools
Since Rinne preserves complete states at each point in time,  
AI systems, external tools, or server scripts can access the exact state directly.  
It enables summarization, validation, or analysis **without any diff processing**.

### 8. Limitations and Notes
Rinne has a simple, low-dependency structure.  
However, because it saves the entire directory instead of diffs,  
it consumes more storage and takes longer to save compared to diff-based systems.

---

## Environment

- .NET 8.0 or later  
- Windows  
- CLI executable (standalone)

---

## Installation

1. Place the executable files in any folder.  
2. Add that folder to your system `PATH` to make the `rinne` command available globally.  

---
