# Trailblazer.Gravitas

An optional, engine-agnostic 3D kinematic adapter between Trailblazer and Gravitas.
`GravitasNavigator3D` prepares a target pose, then commits the pose actually
accepted by Gravitas. Neither core library depends on the other.

Use `Trailblazer.Gravitas` with standard LSF packages, or
`Trailblazer.Gravitas.Lean` with the Lean family. Both target `netstandard2.1`
and `net10.0`; the adapter and core Trailblazer packages share a release version.
The only direct runtime package dependencies are Trailblazer and Gravitas in
the matching family. Do not mix standard and Lean packages.

## Start here

- [Integration guide](https://github.com/mrdav30/Trailblazer/wiki/Gravitas):
  ownership, setup, collision/support limits, shape replacement, and restore.
- [Accepted-motion tests](https://github.com/mrdav30/Trailblazer/blob/develop/tests/Trailblazer.Gravitas.Tests/GravitasNavigator3D.AcceptedMotion.Tests.cs)
  and [their shared host fixture](https://github.com/mrdav30/Trailblazer/blob/develop/tests/Trailblazer.Gravitas.Tests/Support/GravitasNavigatorScenarioFixture.cs):
  executable examples of guided movement, wall clipping, and the fixed-frame loop.
- [API reference](https://mrdav30.github.io/Trailblazer/api/Trailblazer.Navigation.GravitasNavigator3D.html).

The host owns the world, physics bodies, colliders, and stable actor order.
Every fixed frame runs Trailblazer simulate, Gravitas simulate, all Navigator
prepares, one Gravitas late resolution, all Navigator commits, then one
Trailblazer late simulation. Render only after this barrier; interpolated
presentation poses never become simulation input.
The contexts own independent long-lived clocks. Configure matching fixed steps;
the adapter neither advances time nor resets one context when the other resets.

## Repository development

For coordinated source development, use matching sibling `Chronicler`,
`FixedMathSharp`, `SwiftCollections`, `GridForge`, and `Gravitas` checkouts beside
Trailblazer. Package mode remains the default. To validate the sibling source
stack, run from the Trailblazer root:

```powershell
dotnet restore Trailblazer.slnx -p:Configuration=Release -p:UseLocalLsfStack=true
dotnet build Trailblazer.slnx -c Release -p:UseLocalLsfStack=true --no-restore
dotnet test tests/Trailblazer.Gravitas.Tests/Trailblazer.Gravitas.Tests.csproj -c Release -p:UseLocalLsfStack=true --no-build --settings tests/Trailblazer.Gravitas.Tests/coverlet.runsettings --results-directory artifacts/coverage/adapter-release
```

Repeat with `ReleaseLean` and a separate results directory. Restore again when
switching families. The adapter runsettings instrument `Trailblazer.Gravitas`,
not just core. Core tests use their own `tests/Trailblazer.Tests/coverlet.runsettings`.
Both assemblies must retain exact reachable line, branch, and fully covered
method coverage.

Build creates `.nupkg` and `.snupkg` files; it does not publish them. Validate
private packages with an isolated cache and explicit source mapping; never
replace published versions in the global cache with unreleased local builds.
Without `UseLocalLsfStack`, the adapter uses the `GravitasPackageVersion`
NuGet reference and an in-repository Trailblazer project reference. Final
dependency pins and released-package verification are a coordinated release
gate, not implied by local-source or private-feed success.
