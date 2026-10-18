Observatory Scheduler 5.8.2.235: a new target's filters take turns until the stacker can measure each one.

- **Bootstrap rotation**: while any non-H-alpha filter of a target still collects the subs for its first goal measurement, Filter Steering takes one exposure of each open non-H-alpha filter in turn. A poor night or the Moon no longer holds such a target on one filter (on 2026-10-18 Sculptor Galaxy and M 15 were planned on H-a only). H-alpha joins again when no other filter is collecting.
- **Filter cadence**: the cadence continues after the filter that Filter Steering shot.

Includes everything in 5.8.2.234 (camera warm-up before NINA stops, reconnect ramp, steady-state darks limit). No database changes. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
