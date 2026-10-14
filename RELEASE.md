Observatory Scheduler 5.8.2.228: the When Becomes Unsafe trigger runs within seconds during a plan.

- **Safety stop**: when the safety monitor stays unsafe for 4 s, the scheduler cancels the running exposure, slew, center, autofocus, dither or wait and runs the When Becomes Unsafe trigger at once. Before, the trigger waited for the running instruction to end (8 min 47 s on 2026-10-14).
- **No sequence stop**: only the plan is cancelled, so NINA keeps running with --exitaftersequence. No change without a connected safety monitor or a When Becomes Unsafe trigger.
- **No double count**: a cancelled exposure is not counted, and the next plan for the same target slews and centers again.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.227.
