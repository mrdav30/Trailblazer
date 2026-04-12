# Coverage Hardening Plan

## Goal

Drive Trailblazer toward near-100% line and branch coverage without bloating the test suite or
forcing low-value tests. Prioritize:

1. restoring trustworthy coverage reporting
2. closing the biggest real coverage gaps first
3. reducing high-CRAP hotspots where low coverage and high complexity overlap

## Current Baseline

### Current Reporting State

Command used:

```bash
dotnet test tests/Trailblazer.Tests/Trailblazer.Tests.csproj \
  --configuration Debug \
  --collect:"XPlat Code Coverage"
```

Current Trailblazer snapshot:

- line coverage: `96.20%`
- branch coverage: `88.88%`

### Subsystem Snapshot

| Subsystem | Line | Branch |
| --- | ---: | ---: |

### Biggest File Gaps By Missed Lines

These are the highest-value files to target first.

| File | Line | Branch | Missed Lines | Missed Branches |
| --- | ---: | ---: | ---: | ---: |

### Highest Observed CRAP Hotspots

These are approximate CRAP candidates derived from Cobertura method complexity and method
line-coverage. They are good prioritization signals even though the current workflow does not yet
publish CRAP directly.

| Method | Approx. CRAP | Notes |
| --- | ---: | --- |

## Phased Plan

### 1. Final Gap Closure

Targets:

- small public/internal helpers still below threshold
- branch-only gaps in otherwise well-covered files
- convenience or debug-facing helpers

Rules:

- prefer real tests first
- only exclude from coverage if the code is genuinely non-runtime, debug-only, or structurally not
  worth exercising through tests
- document every exclusion reason if any are added
- remove dead / unreachable code before trying to cover it with tests
- if refactoring is needed to make the code more testable / coverable, do that before adding high-ceremony
  tests around the existing structure
  - ensure code is highly optimized and doesn't introduce timing / performance regressions as part of the
    refactor to make it more testable

## Execution Strategy

### Testing Order

For each phase:

1. add focused tests for the target file(s)
2. run the smallest relevant test slice first
3. rerun the full coverage snapshot after the phase lands
4. update this plan with new totals and newly exposed hotspots

### Stop Conditions

We should pause and reconsider before pushing to 100% if any remaining gap is mostly caused by:

- debug/dev-only helpers
- impossible defensive branches
- extremely high ceremony for tiny value

The goal is near-total confidence, not artificial tests that make the suite harder to maintain.
