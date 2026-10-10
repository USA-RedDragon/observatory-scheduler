# Observatory Scheduler

A personal build of [Target Scheduler](https://github.com/tcpalmer/nina.plugin.targetscheduler) by Tom Palmer, for one observatory. It is not a fork for general use and is not supported by Target Scheduler's author. If you want Target Scheduler, install the official plugin from the NINA plugin manager.

Each release is upstream Target Scheduler at a fixed base plus a small set of fixes, listed in the GitHub release notes. Release 1 (5.8.2.100) is based on upstream v5.8.2.0.

The plugin has its own NINA plugin identifier and name, so NINA never offers to update it to the official plugin. It deliberately keeps Target Scheduler's assembly name, namespaces, sequence item types and the `%LOCALAPPDATA%\NINA\SchedulerPlugin` data folder, so existing sequences and the existing database keep working. Never install it alongside the official plugin.

Build with the .NET 8 SDK:

```
dotnet build NINA.Plugin.TargetScheduler/NINA.Plugin.TargetScheduler.csproj -c Release -p:EnableWindowsTargeting=true
```

It builds and tests on Linux; no Windows tools are needed. The remote-control code under `NINA.Plugin.TargetScheduler/Remote` is free of NINA and WPF types, so its tests run on Linux:

```
dotnet test NINA.Plugin.TargetScheduler.Remote.Test/NINA.Plugin.TargetScheduler.Remote.Test.csproj
Utilities/package.sh
```

`package.sh` writes the plugin zip to `dist/`. CI (`.github/workflows/ci.yaml`, ubuntu-latest) builds, tests and packages every push, and publishes the zip as a GitHub release for a `v*` tag.

Licensed under the [Mozilla Public License 2.0](LICENSE.txt), as is the upstream project.

## Upstream: Target Scheduler NINA Plugin

The Target Scheduler Plugin is designed to provide a higher level of automation than is typically achievable with NINA. Specifically, it maintains a database of imaging projects describing DSO targets and associated exposure plans. Based on various criteria and preferences, it can decide at any given time what project/target should be actively imaging. A user will enter the desired projects, targets, and preferences into a UI exposed by the plugin. At runtime, a single new instruction for the NINA Advanced Sequencer will interact with the planning engine to determine the best target for imaging at each point throughout a night. The instruction will manage the slew/center to the target, switching filters, taking exposures, and dithering - all while transparently interacting with the surrounding NINA triggers and conditions.

See the [plugin documentation](https://tcpalmer.github.io/nina-scheduler/) for more information.
