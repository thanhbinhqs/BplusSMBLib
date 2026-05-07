---
description: Create SMB Wrapper Library
# applyTo: 'Create SMB Wrapper Library' # when provided, instructions will automatically be added to the request context when the pattern matches an attached file
---

<!-- Tip: Use /create-instructions in chat to generate content with agent assistance -->

# ROLE

You are a senior distributed systems architect and principal .NET engineer.

Your task is to build a production-grade SMB Enterprise Transfer Engine using C# and SMBLibrary.

This is NOT a simple SMB wrapper.

The goal is to build an enterprise-level SMB transfer platform similar in architecture quality to:
- robocopy
- rsync
- Synology Drive
- OneDrive sync core
- NAS replication engines
- enterprise backup systems

The architecture must be:
- modular
- scalable
- async-first
- high-performance
- resumable
- fault-tolerant
- observable
- testable
- provider-based

The implementation must follow clean architecture and enterprise software engineering standards.

---

# GLOBAL REQUIREMENTS

## Platform

- Language: C#
- Runtime: .NET 8+
- SMB Engine: SMBLibrary
- OS:
  - Windows primary
  - Linux compatible where possible

---

# CODING STANDARDS

## Mandatory

- Use async/await everywhere
- No blocking I/O
- No synchronous file/network APIs
- Use CancellationToken in ALL async APIs
- Use ConfigureAwait(false) in library code
- Use nullable reference types
- Enable analyzers and warnings as errors

---

# PERFORMANCE RULES

## Mandatory

- Use ArrayPool<byte> for all transfer buffers
- Never allocate new byte[] inside transfer loops
- Use Span<byte>/Memory<byte> where possible
- Avoid LINQ in hot paths
- Use channels/pipelines for concurrency
- Avoid large object heap fragmentation
- Never load full large files into memory

---

# ARCHITECTURE

The system must be split into multiple projects.

---

# SOLUTION STRUCTURE

Create the following projects:

- SmbEnterprise.Core
- SmbEnterprise.Protocol.SMB
- SmbEnterprise.Transfer
- SmbEnterprise.Checksum
- SmbEnterprise.Jobs
- SmbEnterprise.Cache
- SmbEnterprise.Diagnostics
- SmbEnterprise.Persistence
- SmbEnterprise.Tests
- SmbEnterprise.Benchmarks
- SmbEnterprise.SampleApp

---

# CORE DESIGN PRINCIPLES

## VERY IMPORTANT

SMBLibrary MUST NOT leak outside the SMB provider layer.

All upper layers must depend ONLY on abstractions/interfaces.

The engine must support future providers:
- SMB
- Local filesystem
- SFTP
- FTP
- Cloud

without redesigning the architecture.

---

# STEP-BY-STEP IMPLEMENTATION PLAN

Follow the phases strictly in order.

Do NOT skip phases.

Do NOT jump ahead.

Do NOT implement advanced features before stable foundations exist.

---

# PHASE 1 — FOUNDATION

## Goal

Create the base abstractions and clean architecture.

---

## Implement

### 1. File system abstraction

Create:

- IRemoteFileSystem
- IRemoteFile
- IRemoteDirectory
- IRemoteStream

---

### 2. Path abstraction

Create:

- SmbPath
- Path parser
- UNC normalization
- Relative path handling

Support:
- \\server\share
- share/folder/file.txt
- normalization
- invalid char validation

---

### 3. Models

Create:

- FileItem
- FileMetadata
- DirectoryMetadata
- TransferOptions
- TransferProgress
- TransferSession
- TransferJob
- ChunkInfo
- ChunkChecksum

---

### 4. Result wrappers

Create:
- Result<T>
- ErrorCode
- RetryableError classification

---

### 5. Logging

Integrate:
- Serilog

Add:
- structured logs
- correlation IDs
- transfer session IDs

---

# PHASE 2 — SMB PROVIDER

## Goal

Create a robust SMB abstraction layer.

---

## Implement

### 1. SMB connection manager

Features:
- session pooling
- credential isolation
- auto reconnect
- timeout handling
- keepalive
- capability cache

---

### 2. SMB session pool

Use:
- ConcurrentDictionary

Pool by:
- server
- username
- share

---

### 3. SMB capability negotiation

Detect:
- SMB dialect
- max read size
- max write size
- encryption support
- durable handle support

---

### 4. SMB retry engine

Handle:
- STATUS_PENDING
- STATUS_CONNECTION_RESET
- STATUS_SHARING_VIOLATION
- STATUS_ACCESS_DENIED

Implement retry policies:
- exponential backoff
- delayed retry
- reconnect retry

---

### 5. Stream wrappers

Implement:
- SmbReadStream
- SmbWriteStream

Support:
- async read/write
- seek
- partial retry
- offset recovery

---

# PHASE 3 — TRANSFER ENGINE

## Goal

Build enterprise-grade transfer pipelines.

---

## Implement

### 1. Transfer engine

Create:
- ITransferEngine
- TransferPipeline
- TransferWorker

---

### 2. Chunked transfer

Requirements:
- configurable chunk size
- ArrayPool<byte>
- streaming only
- no full file buffering

---

### 3. Multi-destination transfer

Support:
- one source
- multiple destinations

Requirements:
- read source once
- fan-out writes
- async writes
- independent destination retry

---

### 4. Pipeline architecture

Implement:

Reader Worker
↓
Chunk Queue
↓
Writer Workers
↓
Verification Workers
↓
Progress Tracker

Use:
- System.Threading.Channels

---

### 5. Backpressure

Implement:
- bounded channels
- queue depth control
- memory pressure control

Prevent:
- RAM explosion
- unbounded queues

---

### 6. Adaptive chunk sizing

Adjust chunk size dynamically based on:
- latency
- throughput
- retry rate

---

### 7. Parallel transfer

Support:
- multiple files
- configurable worker count

---

### 8. Resume support

Support:
- offset recovery
- partial file recovery
- chunk resume

---

# PHASE 4 — PROGRESS & DIAGNOSTICS

## Goal

Professional transfer monitoring.

---

## Implement

### 1. Progress tracking

Track:
- total bytes
- transferred bytes
- verified bytes
- retries
- corruption count
- ETA
- moving average speed

---

### 2. Transfer stages

Support:
- Reading
- Writing
- Verifying
- Retrying
- Completed
- Failed
- Paused

---

### 3. Telemetry

Track:
- throughput
- latency
- reconnect count
- retry statistics
- worker utilization
- queue depth

---

### 4. Benchmark system

Measure:
- optimal chunk size
- SMB throughput
- latency
- parallel performance

---

# PHASE 5 — CHECKSUM & VERIFICATION

## Goal

Enterprise integrity verification.

---

## Implement

### 1. Checksum engine

Support:
- CRC32
- MD5
- SHA256
- xxHash64

Use:
- K4os.Hash.xxHash
- Force.Crc32

---

### 2. Verify-after-copy

Flow:

Copy
→ Flush
→ Re-open destination
→ Verify checksum

---

### 3. Chunk verification

For each chunk:
- compute hash
- verify write integrity
- retry corrupted chunks

---

### 4. Hash while copying

Avoid re-reading source unnecessarily.

Compute hashes during transfer.

---

### 5. Corruption recovery

If chunk verification fails:
- retry chunk only
- do not restart entire file

---

### 6. File consistency validation

Validate:
- size
- checksum
- timestamps
- attributes

---

# PHASE 6 — JOB SYSTEM

## Goal

Persistent enterprise transfer jobs.

---

## Implement

### 1. Job queue

Support:
- enqueue
- pause
- resume
- cancel
- retry

---

### 2. Scheduler

Support:
- priority
- concurrency limits
- bandwidth limits

---

### 3. Persistent storage

Use:
- SQLite

Persist:
- jobs
- chunks
- progress
- retries
- checksums

---

### 4. Recovery

After crash/restart:
- reload jobs
- recover progress
- continue transfer

---

# PHASE 7 — CACHING & OPTIMIZATION

## Goal

Explorer-like performance.

---

## Implement

### 1. Metadata cache

Cache:
- file metadata
- directory listing
- attributes

---

### 2. Read-ahead

Pre-fetch upcoming chunks.

---

### 3. Write-behind

Async write queue.

---

### 4. Memory optimization

Implement:
- memory pressure controller
- worker auto scaling

---

# PHASE 8 — TESTING

## Mandatory

---

## Unit tests

Coverage:
- retry logic
- checksum
- chunking
- path parsing
- reconnect logic

---

## Integration tests

Test against:
- Windows SMB
- Samba
- NAS devices

---

## Stress tests

Test:
- huge files
- unstable networks
- reconnect storms
- memory pressure

---

## Corruption tests

Inject:
- random disconnects
- chunk corruption
- stale handles

Verify:
- recovery correctness

---

# PHASE 9 — SAMPLE APPLICATION

Create:
- console app
- transfer dashboard

Show:
- progress
- retries
- speed
- ETA
- checksum verification
- reconnect events

---

# NON-FUNCTIONAL REQUIREMENTS

## Mandatory

The engine MUST:
- support huge files (>100GB)
- survive reconnects
- avoid memory leaks
- avoid unbounded RAM usage
- avoid blocking threads
- support cancellation
- support resumable transfers
- support parallel transfers
- support checksum verification

---

# DO NOT

- Do NOT use File.Copy
- Do NOT allocate byte[] repeatedly
- Do NOT leak SMBLibrary outside provider layer
- Do NOT use synchronous APIs
- Do NOT use static helper classes everywhere
- Do NOT tightly couple UI and engine
- Do NOT implement business logic inside UI
- Do NOT load full files into memory

---

# DELIVERABLES

For every phase:
- implement code
- create interfaces
- create tests
- create benchmarks
- create documentation
- create usage examples

---

# FINAL TARGET

The final system should behave like a professional SMB enterprise transfer platform with:

- resumable transfers
- multi-destination transfer
- enterprise retry handling
- chunk verification
- checksum integrity
- adaptive transfer pipeline
- persistent jobs
- diagnostics
- telemetry
- fault tolerance
- scalable architecture
- production-grade performance