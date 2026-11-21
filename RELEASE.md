Observatory Scheduler 5.8.2.240: goal-driven exposure plans grade from 20 subs on.

- **Goal grading**: delayed grading of a plan with a goal starts at 20 subs instead of at the Delay Grading percent of the goal. The next sub grades all pending subs of the plan against the whole plan, and each later sub grades on arrival. Plans without a goal still wait for the percent of their desired subs.
- **Logs**: the grading log lines name the rule that applied.

Includes everything in 5.8.2.239. No database changes. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
