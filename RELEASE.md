Observatory Scheduler 5.8.2.241: pending subs of goal-driven plans with 20 or more subs grade at plugin start.

- **Grading sweep**: once, about 45 seconds after the plugin starts, each matching group of a goal plan with 20 or more subs and a pending backlog is queued for grading, and all its pending subs grade. Plans without a goal still follow the Delay Grading percent.
- **Log**: one line gives the plans checked, groups queued and pending subs covered.

Includes everything in 5.8.2.240. No database changes. Requires NINA 3.1.2.9001 or later. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
