Observatory Scheduler 5.8.2.210: grading and the web preview follow goals.

- **Delayed grading on goals**: for a goal-driven exposure plan, delayed grading waits until the goal has the configured share of the subs it needs (stack_subs against min_subs while collecting, sub_limit while unmeasurable, or the subs the measured hours still need), not the plan's desired count. A reached or done goal grades all pending subs. Plans without a goal are unchanged.
- **Preview goal readiness**: the web preview counts the subs it plans toward the goal's stack_subs, so a target that reaches min_subs or sub_limit during the night stops being picked in the preview, as it does in the planner.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.209: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
