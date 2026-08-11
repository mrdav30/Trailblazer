# Issue Tracker

## Tracker Rules

- Issue IDs use `TRB-Issue-NNN`. The next available ID is `TRB-Issue-001`.
- Assign an ID when an issue enters this tracker, keep it through resolution,
  and never reuse an ID even if an entry is later removed. Check this file's Git
  history before advancing or repairing the counter.
- Add new items when feature work uncovers a suspected bug, stale doc, test
  smell, performance anomaly, or correctness risk.
- Keep each item scoped tightly enough to fix and verify independently.
- Record the date on the item, not in this filename.
- Move an item to `Resolved Issues` only after the fix has tests or documented
  verification evidence.
- Do not use this tracker as a substitute for tests, benchmarks, or release
  notes.
- Performance issues should stay in
  [`benchmark-signal-hardening-backlog.md`](benchmark-signal-hardening-backlog.md)
  unless they become a confirmed runtime defect. Do not add performance issues
  here until they have been investigated and confirmed as runtime defects.

## Active Issues

No active correctness issues remain. Add new discoveries here in explicit
execution order.

### Validation Workflow

- Use package references for normal development and release validation.
- Use `UseLocalLsfStack=true` only when an unreleased lower-stack change must be
  validated across sibling repositories.
- Resolve defects in the repository that owns the behavior, then release and
  validate packages in dependency order.
- Before release, run Gravitas `Release`, `ReleaseLean`, coverage, replay,
  allocation, and relevant benchmark gates against package dependencies.
- Keep volatile test and coverage counts in generated reports or dated
  verification records rather than this active section.

### Ordered Queue

No active items.

## Resolved Issues

No resolved correctness issues remain. Add new discoveries here in explicit
execution order.
