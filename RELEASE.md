Observatory Scheduler 5.8.2.230: no light until the sensor has held the setpoint for 60 s after any setpoint change.

- **Sensor gate hold**: after a cooler move, a ramp end, a darks restore, a camera connect or any other setpoint change, the next light waits until the sensor has been less than 0.5 °C from the final setpoint for 60 s without a break. On 2026-10-14 a 600 s light started 1 s after the cooler move to -4 °C ended with the sensor at -3.50 °C.
- **Exact boundary**: a sensor exactly 0.5 °C off the setpoint is now outside the band.
- **Logs**: each wait and release is logged with times and the reason the hold restarted.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.229.
