Observatory Scheduler 5.8.2.234: the sensor never warms or cools fast, and darks judge the 80% cooler limit at steady state.

- **Warm-up before NINA stops**: the installer asks the plugin to warm the camera before it stops NINA. The plugin holds lights, darks and bias frames, ramps the setpoint from the sensor to the ambient at 1.5 °C per minute and reports when the sensor is within 2 °C of it. Only then does the installer stop NINA. If the plugin does not answer (5.8.2.233 or older) or the warm-up fails, NINA keeps running.
- **Reconnect**: when the camera connects again while the plugin owns the cooler, the setpoint ramps from the sensor back to the last setpoint at 1 °C per minute.
- **Darks at steady state**: a set is judged against 80% cooler power only after the sensor has held within 0.3 °C of it for 3 minutes, using the median power of the last 2 minutes. While the power still falls after the pull-down, the darks wait for it. A set that does not settle in 15 minutes counts as over the limit. The guard no longer stops a cool-down at high power while the sensor still falls.

No database changes. Requires NINA 3.1.2.9001 or later. Install with the new install-plugin.ps1 and warm-camera.ps1, with the camera warm or not connected the first time.
