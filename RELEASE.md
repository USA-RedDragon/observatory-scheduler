Observatory Scheduler 5.8.2.208: no imaging planned past the sequence's end of imaging, and no dark started on an unmeasured setpoint move near a deadline.

- **End of imaging**: the planner reads the Sun Altitude loop conditions on the Target Scheduler container and its parents (for example "loop until the sun is above -14°") and does not plan, continue or start an exposure that cannot end before that loop ends. A NINA meridian flip that is due before the exposure counts with its wait to pass the meridian and its measured slew, recenter and settle time; until a flip has been measured, no flip is started within an hour of the end of imaging. Without such a condition, planning is unchanged.
- **Darks**: Target Scheduler Darks never treats an unmeasured setpoint move as instant. It skips the combo when the wait end or the sun limit (-8°) is near, and still measures the rate when no deadline is near. Each move stops at the latest start that keeps the dark before the sun limit.
- The TS log states the end of imaging, its basis and the flip time it uses.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.207: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
