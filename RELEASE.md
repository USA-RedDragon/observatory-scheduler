Observatory Scheduler 5.8.2.214: condition-aware filter steering, mosaic panel matching, system sub lengths, bias frames and requested flats.

- **Filter Steering by sky**: steering picks narrowband or broadband by the moon and the measured night quality, and Condition Match credits only the filter kind steering would shoot.
- **Mosaics**: each panel prefers the moon conditions its sibling panels shot each filter in. The new opt-in rule Even Panels spreads a season's hours across the panels of mosaics with 4 or more panels.
- **Sub length**: the planner, previews, sync clients, status and log use the length the stacker publishes per target, filter, gain and moon glare bucket in `ts_sub_length`, and fall back to the plan or template length. A plan length of 0 now means the template length.
- **Calibration**: Target Scheduler Darks takes the stacker's bias backlog (`ts_bias_need`) first, skips set temperatures colder than the cooler has reached, retries a failed set temperature only after the ambient drops 1 °C, and takes darks that match tonight's imaging setpoint first. Target Scheduler Flats takes flats requested from the web (`ts_flat_request`).
- **Read-only panes**: while web editing is on, the Target Scheduler panes in NINA stay browsable and refuse every write.
- **Cancel**: a canceled sequence waits up to 30 s for a light still being saved, so it gets its acquiredimage row.
- **Horizon**: new endpoint `GET /os/v1/horizon` returns the profile's custom horizon.

On start-up the plugin adds `ts_sub_length`, `ts_bias_need`, `ts_flat_request` and the local ledgers `ts_bias_taken` and `ts_flat_request_taken` to its SQLite database. The home database needs `ts_sub_length`, `ts_bias_need` and `ts_flat_request` and their SymmetricDS triggers before the stacker or the web app publishes them. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.213: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
