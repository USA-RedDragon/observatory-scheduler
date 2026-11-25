Observatory Scheduler 5.8.2.243: a coldest setpoint (default -18 °C) that the cooler controller and the darks never go past.

- **Floor**: the start setpoint and the colder step stop at the floor, and the log says when it limited a choice. Set it in the Cooler options, -40 to +10 °C.
- **Too cold now**: a camera colder than the floor is ramped warmer at 1 °C per minute to the allowed setpoint.
- **Darks**: sets colder than the floor are skipped with a reason, and the restore after darks stays at or warmer than it.

Includes everything in 5.8.2.242. No database changes. Requires NINA 3.1.2.9001 or later and an astro-stacker that publishes expected scores per target. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
