Observatory Scheduler 5.8.2.239: the camera warms slowly when darks leave it idle, and darks plan against the inside air with the roof closed.

- **Stepped setpoint**: every setpoint move holds the sensor temperature first, then steps 1 °C at most once a minute while the sensor follows. NINA's ramped CoolCamera and WarmCamera are no longer used. The installer warm-up is 1 °C per minute.
- **Idle warm-up**: when darks end with nothing left and the roof stays closed, the camera warms to the air temperature at 1 °C per minute and the cooler goes off. It stops when it is safe again or before the next light, and the cooler controller cools again from a cooler that is off, waiting for the sensor.
- **Roof-closed dark planning**: darks use the focuser's inside temperature and a fit from roof-closed nights (power = -36.7 + 3.82 x (inside - setpoint)). On 2026-11-08 it puts the 80% limit at about -12.4 °C, so -15 and -16 °C are skipped instead of running the cooler at 100% all night.
- **One reason**: every "can't hold" skip names the one temperature the cooler holds at 80% power.

Includes everything in 5.8.2.238. No database changes. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
