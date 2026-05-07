---
description: Create a winform application to implement and verify all feature of library
# applyTo: 'Create a winform application to implement and verify all feature of library' # when provided, instructions will automatically be added to the request context when the pattern matches an attached file
---

<!-- Tip: Use /create-instructions in chat to generate content with agent assistance -->

````text id="f3l9kp"
# ROLE

You are a senior .NET desktop architect and enterprise UI engineer.

Your task is to build a professional Windows Explorer-like WinForms application for testing and operating the SMB Enterprise Transfer Engine.

The application is NOT a demo.

It is a production-grade SMB Explorer and Transfer Management Tool.

The application must look and behave similarly to:
- Windows File Explorer
- Robocopy GUI tools
- Synology Drive Client
- NAS management tools
- Enterprise transfer managers

The application is used for:
- testing all SMB engine features
- diagnostics
- benchmarking
- transfer monitoring
- integrity verification
- reconnect testing
- huge folder testing
- enterprise file operations

The UI must be responsive, asynchronous, scalable, and modular.

---

# PLATFORM

## Mandatory

- .NET 8
- WinForms
- Windows 10/11
- x64

---

# PROJECT

Create a new project:

```text
SmbEnterprise.Explorer
````

The project must reference:

* SmbEnterprise.Core
* SmbEnterprise.Protocol.SMB
* SmbEnterprise.Transfer
* SmbEnterprise.Checksum
* SmbEnterprise.Diagnostics
* SmbEnterprise.Jobs
* SmbEnterprise.Cache
* SmbEnterprise.Persistence

---

# UI REQUIREMENTS

The UI must mimic modern Windows Explorer behavior.

The application must support:

* ribbon UI
* navigation tree
* virtualized file listing
* transfer manager
* context menus
* diagnostics
* benchmark tools
* dark mode
* multi-tab navigation
* drag & drop
* background transfers

---

# GLOBAL RULES

## Mandatory

* UI MUST NEVER block
* All operations async
* No SMB logic inside forms
* No business logic inside controls
* Use services/controllers
* Use dependency injection
* Use structured logging
* Use cancellation tokens everywhere
* UI must remain responsive during huge transfers

---

# ARCHITECTURE

Use layered architecture.

```text
UI
 ↓
Controllers / ViewModels
 ↓
Application Services
 ↓
SMB Engine
 ↓
SMB Provider
```

---

# DO NOT

* Do NOT place SMB logic directly in forms
* Do NOT call SMBLibrary directly from UI
* Do NOT use blocking Invoke patterns excessively
* Do NOT freeze UI during transfer
* Do NOT load huge folders fully into memory
* Do NOT use naive ListView loading
* Do NOT tightly couple controls

---

# SOLUTION STRUCTURE

Create folders:

```text
/Forms
/Controls
/Services
/ViewModels
/Models
/Commands
/Dialogs
/Transfer
/Diagnostics
/Themes
/Icons
```

---

# PHASE 1 — APPLICATION FOUNDATION

## Goal

Create stable WinForms infrastructure.

---

# Implement

## 1. Dependency Injection

Use:

* Microsoft.Extensions.DependencyInjection

Register:

* transfer engine
* SMB services
* checksum services
* diagnostics services
* logging

---

## 2. Logging

Use:

* Serilog

Implement:

* rolling logs
* structured logs
* UI log viewer

---

## 3. Global exception handling

Catch:

* UI exceptions
* background task exceptions
* transfer exceptions

Show:

* friendly dialogs
* detailed logs

---

## 4. Theme system

Support:

* dark mode
* light mode

Create:

* centralized theme manager

---

# PHASE 2 — MAIN EXPLORER WINDOW

## Goal

Build Explorer-like UI.

---

# Main Window Layout

```text
┌──────────────── Ribbon ────────────────┐
│ File Home Transfer View Tools          │
├──── Navigation ─┬──── File List ──────┤
│ SMB Tree        │ Explorer Grid        │
│ Shares          │                      │
│ Folders         │                      │
├─────────────────┴──────────────────────┤
│ Transfer Manager / Status / Logs       │
└────────────────────────────────────────┘
```

---

# Implement

## 1. Ribbon UI

Create ribbon tabs:

### File

* Connect
* Disconnect
* Open Session
* Exit

### Home

* Copy
* Move
* Rename
* Delete
* New Folder
* Refresh

### Transfer

* Pause
* Resume
* Retry
* Cancel
* Verify

### View

* Details
* Large Icons
* Small Icons
* Sort
* Refresh

### Tools

* Benchmark
* Diagnostics
* Session Manager
* Logs

---

## 2. Navigation Tree

Create SMB tree navigation.

Support:

* lazy loading
* async expansion
* reconnect indicators
* server/share/folder hierarchy

Do NOT preload entire trees.

---

## 3. Address Bar

Support:

* UNC paths
* breadcrumb navigation
* manual path entry

Examples:

* \server\share
* \server\share\folder

---

## 4. File List View

CRITICAL REQUIREMENTS:

* virtual mode
* async loading
* sorting
* multi-select
* huge folder support

Columns:

* Name
* Size
* Modified
* Attributes
* Checksum
* Transfer Status

---

# PHASE 3 — CONTEXT MENUS

## Goal

Explorer-like interactions.

---

# File Context Menu

Implement:

* Open
* Copy
* Move
* Rename
* Delete
* Verify Checksum
* Properties

---

# Folder Context Menu

Implement:

* New Folder
* Paste
* Refresh
* Benchmark
* Properties

---

# Transfer Context Menu

Implement:

* Pause
* Resume
* Retry
* Cancel
* Open Destination

---

# PHASE 4 — TRANSFER MANAGER

## Goal

Professional transfer monitoring.

---

# Create Transfer Manager Panel

Display:

* filename
* progress
* speed
* ETA
* retries
* checksum state
* transfer stage
* source
* destination

---

# Transfer Stages

Support:

* Queued
* Reading
* Writing
* Verifying
* Retrying
* Completed
* Failed
* Paused

---

# Implement

## 1. Live progress updates

Use:

* throttled UI updates
* background progress dispatcher

Do NOT update UI excessively.

---

## 2. Multi-transfer support

Allow:

* multiple concurrent jobs
* queueing
* prioritization

---

## 3. Transfer graphs

Display:

* throughput
* speed history
* retry spikes

---

# PHASE 5 — DIAGNOSTICS PANEL

## Goal

Professional SMB diagnostics.

---

# Create diagnostics window

Display:

* SMB dialect
* active sessions
* reconnect count
* latency
* throughput
* chunk size
* worker count
* queue depth
* memory usage

---

# Implement

## 1. Session viewer

Show:

* server
* share
* connection state
* reconnect attempts

---

## 2. Retry viewer

Show:

* failed chunks
* retry history
* corruption recovery

---

## 3. Live telemetry

Display realtime:

* transfer rates
* active workers
* buffer pool usage

---

# PHASE 6 — CHECKSUM UI

## Goal

Integrity verification UI.

---

# Implement

## 1. Checksum dialog

Display:

* SHA256
* xxHash64
* CRC32

---

## 2. Verify dialog

Compare:

* source checksum
* destination checksum

Display:

* match
* mismatch
* corrupted chunks

---

## 3. Background verification

Allow:

* verify after transfer
* delayed verification
* batch verification

---

# PHASE 7 — HUGE FOLDER SUPPORT

## Goal

Explorer scalability.

---

# Mandatory

Support:

* 100k+ files
* virtualized loading
* pagination
* lazy metadata

---

# Requirements

* NEVER fully load huge directories
* Use async enumeration
* Use cancellation tokens
* Support fast scrolling

---

# PHASE 8 — DRAG & DROP

## Goal

Explorer-like UX.

---

# Implement

Support:

* local → SMB
* SMB → SMB
* multi-file drag
* folder drag

---

# Transfer behavior

Dragging files should:

* enqueue transfer jobs
* show transfer dialog
* support cancellation

---

# PHASE 9 — PROPERTIES WINDOW

## Goal

Explorer-style properties.

---

# Create property dialog

Tabs:

### General

* size
* timestamps
* attributes

### SMB

* dialect
* permissions
* share

### Checksum

* hashes

### Transfer History

* copy history
* retries
* verification state

---

# PHASE 10 — SEARCH

## Goal

Enterprise search.

---

# Support

* wildcard
* regex
* recursive search
* async search
* cancellation

---

# PHASE 11 — BENCHMARK TOOL

## Goal

Performance testing.

---

# Create benchmark window

Test:

* read speed
* write speed
* parallel transfer
* checksum performance
* reconnect recovery

Display:

* charts
* throughput
* latency

---

# PHASE 12 — LOG VIEWER

## Goal

Enterprise diagnostics.

---

# Create log viewer

Display:

* structured logs
* filters
* transfer logs
* reconnect events
* checksum failures

Support:

* export logs
* search logs

---

# PHASE 13 — ADVANCED UX

## Implement

### 1. Multi-tab browsing

Support:

* multiple explorer tabs

---

### 2. Notifications

Show:

* transfer completed
* reconnect success
* corruption detected

---

### 3. Status bar

Display:

* active transfers
* throughput
* memory usage
* connection state

---

# PERFORMANCE REQUIREMENTS

## Mandatory

The application MUST:

* stay responsive during transfers
* support huge files
* support huge folders
* avoid UI freezes
* avoid excessive memory usage
* use virtualized controls
* throttle UI updates

---

# TESTING REQUIREMENTS

## Test scenarios

* unstable SMB connection
* reconnect storms
* huge folder browsing
* 100GB+ transfer
* checksum corruption
* session recovery

---

# FINAL GOAL

The final application must behave like a professional SMB Explorer and Transfer Management Platform with:

* Windows Explorer-like UX
* enterprise transfer monitoring
* diagnostics
* integrity verification
* reconnect handling
* huge folder scalability
* responsive async UI
* modern ribbon interface
* production-grade stability

```
```
