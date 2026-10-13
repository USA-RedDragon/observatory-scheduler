Observatory Scheduler 5.8.2.225: darks ramp the camera temperature at 1 °C per minute, with move times learned for the ramp.

- **Darks cooling ramp**: every setpoint change Target Scheduler Darks makes ramps at 1 °C per minute instead of jumping at full cooler power, and a restore keeps ramping in the background when the instruction is canceled.
- **Move times**: the planner estimates a move as the ramp plus the settle time it measured after earlier ramps, and Darks Ready counts the sequence's own cool-down before the darks.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.224, together with observatory-pc Main.json that ramps the sequence's own cool-downs.
