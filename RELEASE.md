Observatory Scheduler 5.8.2.202: planner rules for tonight's conditions and the season, and filter steering by goal.

- **Condition Match** (new rule, weight 0): favours targets that suit tonight. Altitude against the target's own peak, times its best open filter, each scored for the moon (narrowband tolerates a bright, close moon; broadband doesn't) and for the sky quality the stacker measured on the last three hours of subs.
- **Seasonal Runway** (new rule, weight 0): the hours a target's goals still need against 30% of the usable hours left in its season. Targets that will run out of season before finishing score up to 1.0.
- **Season Priority** (new rule, weight 0): follows the stacker's season plan for mosaics using weakest-first balancing, boosting the panels to push this season.
- **Filter Steering** (per-project switch, off): with goals, each target and panel shoots the filter furthest behind its goal with the best gain per hour, keeping broadband out of a bright moon. Without goals the exposure order is unchanged. It is the `Filter Steering` rule weight: above 0 is on.
- **Rarity** now counts a night only when the target is inside the project's maximum altitude and meridian window and at least one of its filters passes moon avoidance.

Nothing changes until a weight is raised or steering is switched on. On start-up the plugin adds `night_quality`, `night_quality_at` and `season_boost` to `ts_goal_progress`; the home database needs the same columns before the stacker publishes them. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.201: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
