Observatory Scheduler 5.8.2.232: darks start at the coldest set temperature the cooler holds at 80% power and go warmer from there.

- **Order**: coldest needed set temperature first, then warmer sets. The limit is the setpoint at 80% cooler power at the current ambient, from tonight's cooler power fit or the measured fit (about 31 °C below ambient).
- **Skips**: sets colder than the limit are skipped with "the cooler can't hold X °C at Y °C air". If the cooler needs more than 80% after it reaches a set, that set is skipped and the run goes on with the next warmer set.
- **Status**: the darks check and the calibration block show the same order and skip reasons.

No database changes. The sun limit and the fit-time checks are unchanged. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.231.
