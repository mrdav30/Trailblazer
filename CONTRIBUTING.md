# Contributing to Trailblazer

Thanks for helping improve Trailblazer. Focused bug fixes, tests, documentation,
benchmarks, and proposals that strengthen deterministic navigation are welcome.
Open an issue before a large or breaking change so its map, query,
serialization, and migration impact can be discussed first.

By participating, you agree to follow the code of conduct below.

## Development setup

Trailblazer uses the .NET 10 SDK for solution tooling and runs its test and
benchmark projects on .NET 8. From the repository root:

```bash
dotnet restore Trailblazer.slnx --property:Configuration=Release
dotnet build Trailblazer.slnx --configuration Release --no-restore
dotnet test Trailblazer.slnx --configuration Release --no-build
```

Repeat the same commands with `ReleaseLean` when a change touches public APIs,
serialization, dependencies, or packaging. Do not mix standard and Lean LSF
packages in one validation run.

To build the documentation after a Release build:

```bash
dotnet tool restore
dotnet tool run docfx docs/api/docfx.json --warningsAsErrors
```

Benchmark commands and evidence rules live in
[`tests/Trailblazer.Benchmarks/README.md`](tests/Trailblazer.Benchmarks/README.md).

### Coordinated source development and IDE restore

For coordinated unreleased LSF changes, use matching `FixedMathSharp`,
`SwiftCollections`, `GridForge`, and `Gravitas` sibling checkouts. Published
dependencies do not include those fixes. Command-line invocations can pass
`-p:UseLocalLsfStack=true` to restore/build/test.

For VS Code design-time restore, fully exit **all VS Code windows** first, then
run the following from an external PowerShell terminal at the Trailblazer root:

```powershell
$env:UseLocalLsfStack = 'true'
dotnet restore Trailblazer.slnx -p:Configuration=Debug
dotnet build Trailblazer.slnx -c Debug --no-restore
code .
```

MSBuild reads this process environment property in every project, including
sibling repositories during NuGet restore. Newly launched VS Code services and
terminals inherit it. An existing VS Code process does not acquire a new
environment from `code .`, `--new-window`, or a terminal opened inside it; a
window reload alone is not sufficient. Repeat this launch when returning to
coordinated source work. This does not set a persistent user/machine variable or
change ordinary package-mode checkouts.

An `NU1102` for an unreleased Gravitas version means the IDE restore is still
selecting packages. A terminal-only `-p:UseLocalLsfStack=true` does not configure
its parent design-time service. Use the same mode for restore and build. For
package validation in a source-enabled shell, pass `-p:UseLocalLsfStack=false`
explicitly; for a package-mode IDE, exit it and relaunch from a shell without the
environment variable. Do not lower package pins, hide unpublished fixtures in
the global NuGet cache, or treat source-mode packages as release artifacts.

Do not substitute a Trailblazer-only `Directory.Build.props` assignment for the
environment/global property. NuGet evaluates referenced sibling projects
separately: build-time reference metadata alone does not make their restore
graph select the same source stack.

## Pull request process

The
[adapter README](src/Trailblazer.Gravitas/README.md) lists focused test/coverage
commands. Final validation against released dependencies remains a separate
gate.

Core and adapter coverage use separate runsettings. Never override the adapter
with the core-only include filter. CI requires both assemblies to be present and
fully covered. DocFX consumes both Release DLL/XML pairs.

1. Keep the change focused and preserve deterministic, engine-agnostic runtime
   behavior.
2. Add or update tests for meaningful behavior changes. Cover exact ordering,
   fixed-point cost, boundaries, failure status, and serialization where they
   are part of the contract.
3. Update public XML comments, the root README, the matching wiki page,
   generated API-site authored guidance, and migration guidance when their
   contract changes.
4. Run focused checks first, then the full applicable `Release` and
   `ReleaseLean` matrix. Describe the exact results in the pull request.
5. Do not manually bump package versions. Release workflows derive versions
   through GitVersion.
6. Call out map publication, cache invalidation, action ownership, serialized
   schema, or package-family changes explicitly; these are high-risk boundaries.

For scheduled Navigator changes, verify moving-foot adoption and waiting motion,
not just ticket completion. A\* connector proofs belong to one lease and consume
the existing active-byte ceiling and fair scheduler validation turns. Include
shared-payload isolation, repeated host movement, mode changes, transactional
loads, and reset/disposal in lifecycle tests. See
[NavSteering](docs/wiki/NavSteering.md#scheduled-acquisition-and-moving-origins).

For short, synchronous current-thread allocation assertions, use
[`AllocationTestUtility`](tests/Trailblazer.Tests/Support/AllocationTestUtility.cs).
The core test executable sets `ConcurrentGarbageCollection=false` so .NET 8
background-GC allocation-context accounting cannot inflate the byte counter.
Keep that test-host setting enabled. The non-inlined helper measures exactly
one invocation; it does not warm up, retry, force collection or reserve a no-GC
region. Callers own warmup and keep assertions outside the measured action.
Real allocations still count across blocking collections, and action exceptions
propagate unchanged. This setup does not change library/consumer or benchmark
GC settings and is not suitable for asynchronous/global allocation measurements.

## Documentation expectations

- Keep the root README concise and safe for both GitHub and NuGet rendering.
- Keep behavioral and integration guidance under `docs/wiki`; it is synced to
  the GitHub Wiki after a successful `main` build.
- Keep DocFX configuration, landing content, namespace overrides, and theme
  files under `docs/api`. Never edit or commit generated `docs/api/obj` output.
- Use current public signatures in runnable examples. Label host placeholders,
  partial snippets, and engine-adapter pseudocode clearly.

## Code of conduct

### Our pledge

In the interest of fostering an open and welcoming environment, we as
contributors and maintainers pledge to make participation in our project and our
community a harassment-free experience for everyone, regardless of age, body
size, disability, ethnicity, gender identity and expression, level of
experience, nationality, personal appearance, race, religion, or sexual identity
and orientation.

### Our standards

Examples of behavior that contributes to creating a positive environment
include:

- Using welcoming and inclusive language
- Being respectful of differing viewpoints and experiences
- Gracefully accepting constructive criticism
- Focusing on what is best for the community
- Showing empathy towards other community members

Examples of unacceptable behavior include:

- The use of sexualized language or imagery and unwelcome sexual attention or
  advances
- Trolling, insulting or derogatory comments, and personal or political attacks
- Public or private harassment
- Publishing others' private information without explicit permission
- Other conduct that could reasonably be considered inappropriate in a
  professional setting

### Maintainer responsibilities

Project maintainers are responsible for clarifying these standards and taking
appropriate and fair corrective action. Maintainers may remove, edit, or reject
contributions that do not align with this code of conduct, and may temporarily
or permanently ban contributors for behavior they deem inappropriate,
threatening, offensive, or harmful.

### Scope

This code of conduct applies within project spaces and in public spaces when an
individual represents the project or community.

### Enforcement

Report abusive, harassing, or otherwise unacceptable behavior to
`david.oravsky@gmail.com`. Reports will be reviewed and investigated with
confidentiality for the reporter. Maintainers who do not follow or enforce this
code of conduct in good faith may face temporary or permanent repercussions.

### Attribution

This code of conduct is adapted from the
[Contributor Covenant](https://www.contributor-covenant.org/), version 1.4.
