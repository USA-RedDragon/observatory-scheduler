Observatory Scheduler 5.8.2.200: hotfix 5.8.2.101 plus web control, goals and new scoring rules.

- Web control: an optional HTTP API (port 8189, bearer token, off by default) and a durable command queue through the `ts_command` and `ts_command_result` tables. Skip, pause, re-plan and edits apply when the current exposure ends, in the order they were made.
- Web editing switch: when on, the Target Management editor in NINA is read-only and edits come from the astro-stacker web UI.
- Goals: per-filter SNR or depth goals (`ts_goal`), with progress fed from astro-stacker (`ts_goal_progress`). A target with goals finishes its exposure plans on the goal instead of the image count.
- New scoring rules Novelty, Rarity and Panel Deficit. They are added to every existing project at weight 0, so nothing changes until you raise a weight. Rarity reads a season cache (`ts_target_season`) computed in the background.
- Lights get TS project, target, exposure plan and mosaic panel FITS keywords.
- The plugin options page shows again (the 5.8.2.101 fix).

On first start the plugin creates the five `ts_*` tables in the scheduler database and `observatory-scheduler.json` in the SchedulerPlugin folder. It also adds a weight-0 rule weight row for each new rule to every project. No existing row is changed. The zip adds `EmbedIO.dll` and `Swan.Lite.dll`. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.101: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
