# KurrentDB Data Analyzer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable .NET console app that can analyze a KurrentDB data directory or a live node and emit JSON grouped by event type, source stream, link status, total count, total size, and average size.

**Architecture:** The app will keep parsing and aggregation separate. One reader will scan chunk files in a data folder, another will query a live node over HTTP, and both will feed a shared in-memory aggregator keyed by event type, source stream, and linked status. The offline reader will resolve linked events by reading the link target from the record payload and by tracking already-seen stream event numbers.

**Tech Stack:** .NET 8 console app, `System.Text.Json`, `HttpClient`, no external NuGet dependencies.

---

### Task 1: Scaffold the console app

**Files:**
- Create: `ESAnalyser.csproj`
- Create: `Program.cs`
- Create: `README.md`

- [ ] **Step 1: Write the project skeleton**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Run the app once to verify the template builds**

Run: `dotnet run --project ESAnalyser.csproj -- --help`
Expected: the app starts and prints usage help or a short argument error, not a compiler failure.

- [ ] **Step 3: Keep the entry point thin**

```csharp
using ESAnalyser;

return await AnalyzerApp.RunAsync(args);
```

- [ ] **Step 4: Add a brief README usage block**

```md
## Usage

`dotnet run --project ESAnalyser.csproj -- offline --path "C:\EventStore\DB\Data"`

`dotnet run --project ESAnalyser.csproj -- live --url "http://localhost:2113"`
```

- [ ] **Step 5: Commit**

```bash
git add ESAnalyser.csproj Program.cs README.md docs/superpowers/plans/2026-07-02-kurrentdb-analyser.md
git commit -m "feat: scaffold kurrentdb analyzer"
```

### Task 2: Add report models and aggregation

**Files:**
- Create: `src/Analysis/AnalysisModels.cs`
- Create: `src/Analysis/EventAggregator.cs`

- [ ] **Step 1: Write the failing model/aggregation test harness in code**

```csharp
namespace ESAnalyser.Analysis;

public sealed record EventRecord(
    string EventType,
    string SourceStream,
    bool IsLinkedEvent,
    int PayloadSize);

public sealed record EventGroup(
    string EventType,
    string SourceStream,
    bool IsLinkedEvent,
    long TotalCount,
    long TotalSize,
    double AvgSizePerEvent);
```

- [ ] **Step 2: Implement the aggregator**

```csharp
using System.Collections.Generic;
using System.Linq;

namespace ESAnalyser.Analysis;

public sealed class EventAggregator
{
    private readonly Dictionary<(string EventType, string SourceStream, bool IsLinkedEvent), (long Count, long Size)> _groups = new();

    public void Add(EventRecord record)
    {
        var key = (record.EventType, record.SourceStream, record.IsLinkedEvent);
        var current = _groups.TryGetValue(key, out var value) ? value : default;
        _groups[key] = (current.Count + 1, current.Size + record.PayloadSize);
    }

    public IReadOnlyList<EventGroup> Snapshot() =>
        _groups
            .Select(pair => new EventGroup(
                pair.Key.EventType,
                pair.Key.SourceStream,
                pair.Key.IsLinkedEvent,
                pair.Value.Count,
                pair.Value.Size,
                pair.Value.Count == 0 ? 0 : (double)pair.Value.Size / pair.Value.Count))
            .OrderBy(x => x.EventType)
            .ThenBy(x => x.SourceStream)
            .ThenBy(x => x.IsLinkedEvent)
            .ToList();
}
```

- [ ] **Step 3: Verify grouping math with a tiny in-memory run**

Run: `dotnet run --project ESAnalyser.csproj -- selftest`
Expected: one normal group and one linked group produce the expected counts, sizes, and averages.

- [ ] **Step 4: Commit**

```bash
git add src/Analysis/AnalysisModels.cs src/Analysis/EventAggregator.cs
git commit -m "feat: add event aggregation model"
```

### Task 3: Implement offline chunk scanning

**Files:**
- Create: `src/Offline/ChunkDirectoryScanner.cs`
- Create: `src/Offline/ChunkRecordParser.cs`
- Create: `src/Offline/OfflineDataReader.cs`

- [ ] **Step 1: Parse chunk files in append order**

```csharp
public sealed record OfflineRecord(
    string EventType,
    string SourceStream,
    bool IsLinkedEvent,
    int RecordSize);
```

- [ ] **Step 2: Implement a conservative record parser**

```csharp
// Reads a length-prefixed chunk record, extracts the stream name, event type,
// payload bytes, and link target when present.
```

- [ ] **Step 3: Resolve linked events**

```csharp
// Keep a per-stream event counter so link targets can be matched by
// "eventNumber@streamId" when scanning $ce- and $et- style derived streams.
```

- [ ] **Step 4: Verify on the provided data folder**

Run: `dotnet run --project ESAnalyser.csproj -- offline --path "C:\EventStore\DB\Data" --json`
Expected: JSON output with non-zero counts and no parse errors for the sample data.

- [ ] **Step 5: Commit**

```bash
git add src/Offline/ChunkDirectoryScanner.cs src/Offline/ChunkRecordParser.cs src/Offline/OfflineDataReader.cs
git commit -m "feat: read kurrentdb chunk files offline"
```

### Task 4: Implement live HTTP enumeration

**Files:**
- Create: `src/Live/LiveDataReader.cs`
- Create: `src/Live/KurrentHttpClient.cs`

- [ ] **Step 1: Read from the live node using HTTP**

```csharp
// Enumerate `$all` forwards with link resolution enabled, page through until
// no more events remain, and map each response to the shared event record.
```

- [ ] **Step 2: Verify the live mode against a running node**

Run: `dotnet run --project ESAnalyser.csproj -- live --url "http://localhost:2113" --json`
Expected: valid JSON matching the same report shape as offline mode.

- [ ] **Step 3: Commit**

```bash
git add src/Live/LiveDataReader.cs src/Live/KurrentHttpClient.cs
git commit -m "feat: add live kurrentdb reader"
```

### Task 5: Wire the CLI and JSON output

**Files:**
- Modify: `Program.cs`
- Create: `src/Output/JsonReportWriter.cs`

- [ ] **Step 1: Parse `offline` and `live` commands**

```csharp
// `offline --path <dir> --json`
// `live --url <url> --json`
// `selftest`
```

- [ ] **Step 2: Emit the final JSON document**

```csharp
public sealed record ReportEnvelope(
    DateTime GeneratedAtUtc,
    string Source,
    IReadOnlyList<EventGroup> Groups);
```

- [ ] **Step 3: Smoke test both modes**

Run:
`dotnet run --project ESAnalyser.csproj -- offline --path "C:\EventStore\DB\Data" --json`
`dotnet run --project ESAnalyser.csproj -- selftest`
Expected: one JSON payload and one passing self-test run.

- [ ] **Step 4: Final commit**

```bash
git add .
git commit -m "feat: add reusable kurrentdb analyzer"
```
