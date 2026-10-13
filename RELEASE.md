Observatory Scheduler 5.8.2.223: darks ramp the camera temperature at 1 °C per minute.

- **Darks cooling ramp**: every setpoint change Target Scheduler Darks makes (to a dark temperature and back to the imaging setpoint) ramps at 1 °C per minute instead of jumping at full cooler power. A 10 °C move now takes about 10 minutes each way, and the planner counts that time, so Before Wait darks still end in time for the restore.
- **Preview**: tonight's preview uses the live planner's window, skips and end of imaging.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.222.
