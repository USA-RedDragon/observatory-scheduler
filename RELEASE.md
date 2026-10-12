Observatory Scheduler 5.8.2.218: seasons for comets and asteroids.

- **Seasons**: comet and asteroid targets get a season from the stacker's nightly positions and brightness, and score like fixed targets.

On start-up the plugin adds `ts_body_night` to its SQLite database. The home database needs the same table with its SymmetricDS channel `bodynight` and trigger (install the plugin first). Requires NINA 3.1.2.9001 or later. Install like 5.8.2.217.
