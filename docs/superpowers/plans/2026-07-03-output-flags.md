# Output Flags Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make payload fields optional in report output and make group output independently configurable while keeping the default report minimal and stable.

**Architecture:** Add a small report-output options object that flows from the CLI into report creation. Use nullable JSON properties for optional fields so the serializer can omit them cleanly, and keep the existing aggregation logic unchanged except for the output projection and group inclusion toggle.

**Tech Stack:** C# 12, `System.Text.Json`, existing console app and report models

---

### Task 1: Add output options and CLI flags

**Files:**
- Modify: `src/AnalyzerApp.cs`
- Modify: `Program.cs`
- Modify: `README.md`

- [ ] **Step 1: Write the failing test**

```csharp
var options = ParseOptions(new[] { "--include-payload-values", "--include-chunk-grouping" });
if (!options.IncludePayloadValues || !options.IncludeChunkGrouping)
{
    throw new InvalidOperationException("Expected both output flags to be enabled.");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test`
Expected: compile or assertion failure before the new options exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
public sealed record ReportOutputOptions(bool IncludePayloadValues, bool IncludeChunkGrouping);
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test`
Expected: pass with the new flag parsing and option plumbing.

- [ ] **Step 5: Commit**

```bash
git add src/AnalyzerApp.cs Program.cs README.md
git commit -m "feat: add report output flags"
```

### Task 2: Make payload fields optional in report models

**Files:**
- Modify: `src/Analysis/AnalysisModels.cs`
- Modify: `src/AnalyzerApp.cs`

- [ ] **Step 1: Write the failing test**

```csharp
var report = AnalyzerApp.AnalyzeOffline(dataPath, new ReportOutputOptions(false, false));
var json = JsonSerializer.Serialize(report);
if (json.Contains("totalPayloadSize") || json.Contains("eventPayloadBytes"))
{
    throw new InvalidOperationException("Payload fields should be omitted when disabled.");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test`
Expected: payload fields still appear in JSON.

- [ ] **Step 3: Write minimal implementation**

```csharp
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public long? TotalPayloadSize { get; init; }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test`
Expected: payload fields omitted when the flag is off.

- [ ] **Step 5: Commit**

```bash
git add src/Analysis/AnalysisModels.cs src/AnalyzerApp.cs
git commit -m "feat: make payload report fields optional"
```

### Task 3: Make group output configurable and verify the final shape

**Files:**
- Modify: `src/AnalyzerApp.cs`
- Modify: `src/Analysis/EventAggregator.cs`
- Modify: `src/SelfTest.cs`

- [ ] **Step 1: Write the failing test**

```csharp
var report = AnalyzerApp.AnalyzeOffline(dataPath, new ReportOutputOptions(false, false));
if (report.Groups.Count != 0)
{
    throw new InvalidOperationException("Group output should be omitted when disabled.");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test`
Expected: group output still appears by default.

- [ ] **Step 3: Write minimal implementation**

```csharp
var groups = options.IncludeChunkGrouping ? aggregator.Snapshot() : Array.Empty<EventGroup>();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test`
Expected: minimal output shape and stable ordering still compile and serialize.

- [ ] **Step 5: Commit**

```bash
git add src/AnalyzerApp.cs src/Analysis/EventAggregator.cs src/SelfTest.cs
git commit -m "feat: make report groups configurable"
```

