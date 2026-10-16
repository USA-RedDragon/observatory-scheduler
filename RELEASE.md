Observatory Scheduler 5.8.2.231: the status API reports what darks, bias and flats are doing.

- **Calibration status**: /os/v1/status has a `calibration` block with the set being taken, frames done, the exposure in progress, the queue with plain skip reasons and estimated times, the sets taken tonight and why the last run stopped.
- **State**: "calibrating" instead of "stopped" while calibration runs.
- **Cool-down**: the sequence's Cool Camera target and an estimated end are shown while it runs after a passed darks check.

No database changes. No change to how darks, bias or flats are taken. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.230.
