Observatory Scheduler 5.8.2.216: comets and other moving bodies, and sky event windows.

- **Moving bodies**: targets with a body designation follow the stacker's JPL Horizons positions for visibility, scoring, slew and center, re-center as the body drifts, and cap sub lengths so the body stays sharp.
- **Event windows**: time-boxed targets with a priority; normal events fill gaps, high-priority events may interrupt at the next exposure boundary.

On start-up the plugin adds `ts_target_body`, `ts_body_position` and `ts_target_window` to its SQLite database. The home database needs those tables and their SymmetricDS triggers (install the plugin first). Requires NINA 3.1.2.9001 or later. Install like 5.8.2.215.
