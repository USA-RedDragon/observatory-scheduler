Observatory Scheduler 5.8.2.203: the grader's HFR limit over the web API, and real reasons in the web preview.

- **Grader limit** (new `GET /os/v1/grader`): for each exposure plan of a target, the HFR limit the grader would apply to the next sub, from the grader's own population, sample standard deviation, sigma factor, Accept Improvement and auto-accept level. The stacker shows it on the Now page instead of its own estimate.
- **Left out tonight**: each target's reason now comes from the night's planner passes, not the pass after dawn. Targets that lost on score name the winner and both totals.
- **Gives way because**: the real state of the target at the re-plan, and the real reason the simulation stopped, instead of "end of the night".
- **Preview assumptions**: the preview now states what it does not simulate (weather, grading rejections, overhead, web actions).

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.202: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
