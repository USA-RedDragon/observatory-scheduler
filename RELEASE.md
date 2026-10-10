Observatory Scheduler 5.8.2.205: Seasonal Runway counts goals that the stacker has not measured yet, and the TS log shows each goal-driven completion decision.

- **Seasonal Runway**: a goal-driven filter without a measured `hours_needed` no longer counts as needing no time. It needs its remaining desired subs times the exposure length (the Target Scheduler count), and never less than the subs the 5.8.2.204 rules still need (the subs left to `min_subs`, or at least one sub). An `unmeasurable` goal needs the subs left to its `sub_limit`. Measured goals, done goals and plans without a goal score as in 5.8.2.204.
- **Goal completion log**: when a goal-driven plan opens or closes, or its state changes, the TS log has one Info line with the target, filter, old and new state, `stack_subs`/`min_subs`, `sub_limit`, `done`, `plateau`, `hours_needed` and the stacker's reason.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.204: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
