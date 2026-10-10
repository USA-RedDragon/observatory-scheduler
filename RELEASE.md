Observatory Scheduler 5.8.2.209: darks on closed-roof nights.

- **Target Scheduler Darks Ready**: a new instruction that uses no camera. It succeeds when the stacker's dark backlog is not empty, the sun is below -8° and a needed dark (with its cooling and save time) ends before the sun reaches -8°, the weather device reports the ambient temperature, and the cooler is known to reach a needed set temperature at that ambient. Otherwise it fails and logs why. Use it as the condition of a Sequencer Powerups *If Fails* to decide whether to power up for darks.
- **Cooler evidence**: Target Scheduler Darks now records each held set temperature (ambient, set temperature, peak cooler power) and each cooler failure with a known ambient in `observatory-scheduler-darks.json`, so they survive a NINA restart. The newest evidence wins when two disagree, and kept failures expire after 30 days. When no hold or failure covers a needed set temperature at the current ambient, Darks Ready allows one learning attempt per night. The attempt is used up when the first hold or failure is recorded.
- **While Unsafe** darks now run warmest set temperature first.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.208: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
