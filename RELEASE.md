Observatory Scheduler 5.8.2.204: goal-driven plans finish on the goal, not on the desired count.

- **Goal-driven plans** (a filter with a goal row) ignore their desired count. They are done when the goal is met, or at the plateau when the plateau stop is on.
- **First measurement**: until the stacker has measured a goal, the plan keeps imaging until the stacker has the minimum stack it needs (`min_subs`, published by the stacker, 8 today). Then it waits for the measurement.
- **Goals that cannot be measured** (no plate solution, too few Gaia stars for a depth goal) stop at the stacker's published bound (`sub_limit`), and the stacker's reason shows in the web UI.
- **Plans without a goal** keep Target Scheduler's count behaviour exactly.

On start-up the plugin adds `state`, `reason`, `stack_subs`, `min_subs` and `sub_limit` to `ts_goal_progress`. The home database needs the same columns before the stacker writes them. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.203: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
