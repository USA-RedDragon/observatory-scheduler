Observatory Scheduler 5.8.2.237: the poor-sky gate holds steady, sees the Moon, and makes a target yield instead of shooting a filter that will fail.

- **Poor-sky gate**: a broadband filter that sits out for a poor sky needs its expected score 0.03 over the cut to come back, and a decision stands for 20 minutes unless the sky clearly changes, so a filter no longer flaps when the numbers sit near the cut. The expected score is scaled by the filter's moon keep worth under the current Moon.
- **Yield instead of a failing filter**: when every open filter of a target is under its cut, steering does not pick one, and Condition Match scores the target 0, so another target gets the time (Barnard's E took 25 Blue subs under its cut on 2026-10-17).
- **Condition Match**: a target is scored for the filter Filter Steering will actually shoot, not for H-alpha while the bootstrap rotation shoots broadband.
- **Darks**: the time a set needs to reach a steady cooler power is in the darks time budget, and a set cut by the deadline never records a cooler limit.
- **Override order**: plans left out of an override exposure order are rejected again with Filter Steering on.

Includes everything in 5.8.2.236. No database changes. While the stacker publishes no expected_score, the gate stays off and steering is as in 5.8.2.235, except that Condition Match follows the filter steering picks. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
