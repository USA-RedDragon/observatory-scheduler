Observatory Scheduler 5.8.2.238: overlapping high-priority eclipses are taken nearest their middles.

- **Eclipse phase**: when two high-priority event windows overlap, the event nearest the middle of its window (as a share of half the window) is imaged; the score only picks between events within 0.05 of each other. On the night of 2026-10-19 the Garlic Nebula eclipse now runs from dusk and Cygnus Loop Panel 1 takes over at about 04:06 UTC, instead of Garlic Nebula getting time only after its eclipse ended.
- **Hand-over between events**: a running high-priority event gives way to another one when the other is at least 0.05 nearer its middle, also within the minimum time, so the switch happens at the next exposure and the two do not trade places back and forth.

Includes everything in 5.8.2.237. No database changes. Normal events and targets without an event window are planned as before. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
