Observatory Scheduler 5.8.2.229: the scheduler steers the camera cooler setpoint to hold cooler power near 80%.

- **Cooler power controller**: before each light, the setpoint moves on a 2 °C grid: warmer above 88% power, colder after 15 min below 70% when the colder point is predicted at 85% or less. Settings are on the options page under TS Cooler.
- **Sensor gate**: each light waits until the sensor holds within 0.5 °C of the setpoint.
- **End time and After Each Exposure**: a target the end time trigger stopped is not continued, and After Each Exposure runs only when the plan saved a frame.

New table ts_cooler_setpoint (created at plugin start). Requires NINA 3.1.2.9001 or later. Install like 5.8.2.228, with the Main.json that drops the cool_again triggers.
