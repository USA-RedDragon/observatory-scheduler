Observatory Scheduler 5.8.2.233: darks stop chasing a set temperature the cooler can't reach and go on with warmer sets.

- **Cool-down guard**: 2 minutes at 80% cooler power or more, or 3 minutes without cooling, with the sensor more than 0.5 °C from the set, stops the move or the dark. That set and every set colder than the reached temperature plus 1 °C are skipped with "the cooler can't hold X °C at Y °C air (at 80% power it reached about Z °C)".
- **Fit**: the measured point sets tonight's 80% limit. Tonight's samples replace the prior fit as soon as one is at 60% power or more.
- **Status**: the darks start line says "coldest first" and names the first set.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.232.
