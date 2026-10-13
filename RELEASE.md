Observatory Scheduler 5.8.2.224: darks ramp the camera temperature at 1 °C per minute.

- **Darks cooling ramp**: every setpoint change Target Scheduler Darks makes (to a dark temperature and back to the imaging setpoint) ramps at 1 °C per minute instead of jumping at full cooler power. A 10 °C move now takes about 10 minutes each way, and the planner counts that time, so Before Wait darks still end in time for the restore.
- **Restore survives a cancel**: when the safety loop or a trigger cancels darks, the ramp back to the imaging setpoint keeps running in the background, so imaging does not resume at the dark temperature. 5.8.2.223 had this bug; do not install it.
- **Preview**: tonight's preview uses the live planner's window, skips and end of imaging.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.222, together with observatory-pc Main.json that ramps the sequence's own cool-downs.
