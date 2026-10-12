Observatory Scheduler 5.8.2.217: framing for comets and other moving bodies.

- **Framing**: a moving-body target can keep a frame rotation and a head offset; the mount and rotator frame the body that way and keep the offset as it moves.

On start-up the plugin adds `rotation_deg` and `offset_frac` to `ts_target_body`. The home database needs the same two columns, then a `sync-triggers ts_target_body` on the observatory so they sync. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.216.
