# Observatory Scheduler

## Unreleased
* While web editing is on, the Target Scheduler panes in NINA are read-only but stay browsable. Every write action (edit, save, copy for paste or move, paste, add, delete, import, move, reset, grading changes and purges in Acquired Images, exposure order overrides, profile preferences and profile import) is disabled in the UI and refused in the view model. An edit open when web editing turns on is discarded. Tree selection, scrolling, tabs, exposure plans, reports, previews and acquired images still work. Before this, the detail panes were disabled as a whole, so they could not be browsed, and the tree, copy and paste, import and Acquired Images could still write.
* Target Scheduler Darks now also takes bias frames. It reads the stacker's bias backlog from the new table `ts_bias_need` and takes those frames first, at the current setpoint and the camera's minimum exposure, with the need's gain, offset, binning and readout mode. The same rules as darks apply: camera only, only at the instruction's placements, the -8° sun limit for each frame plus its download, and in Before Wait the frame must end before the wait. Each frame is recorded in the local table `ts_bias_taken`, so frames taken but not yet in the stacker's index are not taken again for `session_gap_hours`. A bias-only run does not move the cooler. A camera that cannot set its temperature now gets bias frames only.
* Target Scheduler Darks Ready also passes when no darks can be taken but bias frames are needed and the sun is below the limit. The basis names the bias combos. Results for darks are unchanged.
* Target Scheduler Flats now also takes the flats requested from the web app for one target and night. It reads the new table `ts_flat_request` and takes the flat sets for that target's light sessions of that night. It ignores the project's flats handling, flat history and the 45-day cut-off. A request is not taken again within 36 hours; the local table `ts_flat_request_taken` records the sets taken, or 0 with a note when Target Scheduler has no lights of that target on that night.
* Filter Steering now picks between narrowband and broadband filters by the moon and the measured night quality, with the same definitions as Condition Match. Broadband is favored while its Condition Match filter score (moon score times sky score) is at least 0.5, that is while a broadband sub is worth at least half of a good one; below that, narrowband is favored. With the moon down and no fresh night quality, broadband is favored. Among the favored filters that still need data, the filter furthest behind its goal is taken, as before. When no favored filter needs data, all filters compete as before, so no filter waits forever. Targets with only one kind of filter are unchanged. The TS debug log states the glare, the night quality and the favored kind. Before this, steering used only the moon, so a target picked by Condition Match for its narrowband filters in a poor sky shot mostly broadband: on 2026-10-11 (night quality 0.45) Sharpless 216 would have shot about 80 of its 140 minutes in broadband with Condition Match at 50.
* Condition Match now credits a target that has both kinds of filters only for the kind Filter Steering favors under the current conditions, and for its other filters only when none of the favored kind is open. Before this, such a target got the narrowband score in a good sky (0.9 at night quality 0.8) and then shot broadband (worth 0.8). Targets with one kind of filter score as before.
* The web preview uses the same selection. The night quality expires after 3 hours, so a preview of tomorrow night treats it as unknown and favors broadband unless the moon glare is above 0.5.
* Mosaic panels now prefer, for each filter, the moon conditions their sibling panels shot that filter in, so the panels of a mosaic match at the seams. The plugin reads every kept sub (not rejected) of active mosaic projects from `acquiredimage`, with its exposure length from the image metadata, and works out the moon glare at the time of each sub (the same glare as Condition Match: illumination times moon altitude times nearness). Glare goes in four buckets: dark (below 0.1), low (0.1 to 0.35), mid (0.35 to 0.65) and bright. For a panel and filter, the match is the share of the other panels' hours in the current bucket, with half credit for the next bucket, against their most used bucket. Filter Steering multiplies each filter's value by 0.25 + 0.75 times the match, and Condition Match does the same for each filter score, so a filter is pushed toward the conditions the other panels used and a panel whose open filters do not match tonight scores lower. With under 1 hour of that filter on the other panels there is no preference. A mismatched filter is still shot when nothing better is open, so no filter waits forever. Single targets are unchanged. The TS debug log lists the match per filter.
* The web preview uses the same matching: subs the preview simulates are added to the history with the glare at their simulated time, and the preview reloads the history before it runs.
* New switch rule Even Panels (opt-in, weight 0 is off, any weight above 0 is on, like Filter Steering). With Even Panels on and Panel Deficit above 0, Panel Deficit scores each unfinished panel by its share of this season's imaging hours against a fair share in proportion to its unfinished filter plans: 1 with no hours yet, 0.5 at its fair share and 0 at twice its fair share or more. The season starts when the Sun last passed the mosaic's mean right ascension, the same half year the web Seasons tab uses. Hours are raw hours of kept subs, from the same history as the panel matching. With Even Panels off, Panel Deficit is unchanged (weakest panel first). A panel past its goal still scores 0 while the mosaic is not finished.
* New remote API endpoint `GET /os/v1/horizon` returns the active profile's custom horizon sampled every degree of azimuth (`altitudes`, 360 values, from NINA's horizon interpolation), or `custom: false` when the profile has none, so the web app can work out dark hours with the same horizon the planner uses.
* Sub length is now chosen by the system. The stacker publishes a recommended length per target, filter, gain and moon glare bucket in the new table `ts_sub_length` (home to the observatory over SymmetricDS); the plugin creates the table when it is missing. When the planner selects an exposure, it works out the moon glare for the target at the exposure's planned start (the same glare as Condition Match) and puts it in a bucket: dark below 0.05, low below 0.25, mid below 0.5, else bright. It then looks up the target's guid, the exposure template's filter name and gain, and the bucket. A row with an exposure above 0 sets the length (source `system`). Otherwise the exposure plan's own length is used when it is set (source `plan`), else the template's default exposure (source `template`), as before. Without the table or without a matching row, every length is as before.
* The live planner, the previous-target continuation, the web preview and the NINA plan previewer all go through the same choice, so the end of imaging check, the minimum and bonus time checks and the plan end times use the chosen length. A sync client chooses its own length the same way, with its own template's filter and gain at the time the server's exposure starts, and still repeats its exposure to fill the server's exposure.
* The chosen length, its source and its bucket are in the TS log (the plan line and "adding take exposure"), the NINA plan previewer, the Imaging dockable, the sync client display and the Target Scheduler publisher message headers (`ExposureLengthSource`, `ExposureLengthBucket`). The status `exposure` has new fields `length_source` and `length_bucket`, and each web preview block has `length_source` and `length_bucket`. Preview blocks with a system length are split when the bucket changes; blocks with a plan or template length are not, and their `length_bucket` is null when the block spans more than one bucket.
* Delayed grading of a goal-driven plan now estimates the subs still needed with the length of the image being graded (from its metadata) instead of the plan or template length.
* An exposure plan with a length of 0 now uses the template's default exposure. Before this, only -1 meant "use the template", so 0 gave 0 s exposures.

## 5.8.2.213 - 2026-10-11
* New web command `exposure.abort` stops the running exposure at once. It cancels only the camera capture: NINA aborts the camera exposure, the partial frame is discarded and not saved, the sequence keeps running and the scheduler re-plans. The payload names the exposure by `plan_id` and `started_at`; when another exposure is running, it is left alone. A second abort of the same exposure returns "An abort of this exposure is already in progress." The command applies immediately and is never queued.
* An aborted exposure does not advance the filter cadence or count toward the next dither, so the aborted filter is taken next. The cadence still advances when the shutter opens, so the web preview during an exposure is unchanged, and it is put back and saved to the database when the abort ends the capture. A dither that ran before the aborted frame still counts. Filter switch, override order, repeat-until-done, smart exposure rotation and filter steering are covered.
* An aborted exposure does not count in the status exposure number, and the status does not show it as saving.
* The status now keeps `exposure` (with `plan_id` and `started_at`) while the camera downloads, until the frame is saved or aborted, so a web abort is still possible during the download. `activity` is still `downloading`.
* New status fields on `exposure`: `abort_blocked` gives the reason the exposure cannot be aborted from the web, and `sync_clients` is true when sync clients take the same exposure. On a sync client, exposures are started by the sync server, so `abort_blocked` is set and `exposure.abort` is rejected with that reason.
* On a sync server, an abort stops only the server camera. The server then waits for the sync clients to finish their own exposures before it continues, as it does after a normal exposure. The sync protocol cannot cancel a client exposure, and skipping the wait would make the clients' late completion count for the next exposure.
* The After Each Exposure instructions still run after an aborted exposure, because they run when the plan ends and not per saved frame.
* What-if previews can now override rule weights. An override with entity `ruleweight`, the rule name as its field and a weight from 0 to 100 applies to one project, or to every project with id 0; a project's own override wins over the one for every project. A rule the project has no weight for is added. Weights outside 0 to 100, NaN or without a rule name are ignored. The preview result lists the override kinds it accepts in `override_kinds`. Before this, a what-if preview could only override project fields.

## 5.8.2.212 - 2026-10-11
* The status exposure number now counts each target's exposures for the whole night. The count is kept per target and resets at local noon. Before this, the number went back to 1 every time the planner re-planned, even when it picked the same target again, so it showed the position inside the current plan and not the target's count for the night.
* The exposure number now continues after a NINA or plugin restart. The first exposure of a target after a restart starts from the number of that target's acquired images since local noon, read from the scheduler database. Before this, the count started again at 1.
* Tonight's web preview, when no start time is given, now starts when the running exposure ends and counts that exposure as acquired. The target that is imaging now starts as the current target, so it keeps the Target Switch Penalty score the live planner gives it. A new `live` assumption states which case applied: nothing imaging, the live target not among the simulated projects, a running exposure, or no exposure running (the rest of the current step is not simulated). Before this, the preview started at the current time with no current target, so its first pick could differ from what the live planner does next.
* `project.edit` and `project.batchedit` now reject a change that puts the maximum altitude at or below the minimum altitude, unless the maximum is 0 (off). The check uses the stored value for the side the edit leaves out. Nothing is applied when the check fails.
* Edits now accept a null `before` value for a setting the project has no stored value for. Before this, such an edit was rejected because null could not be converted to the column type, so a setting that was never set could not be changed over the web API.

## 5.8.2.211 - 2026-10-11
* `project.create` now takes the project's scheduling settings: `ditherevery`, `filterswitchfrequency`, `flatshandling`, `smartexposureorder`, `meridianwindow`, `usecustomhorizon`, `horizonoffset`, `maximumaltitude` and `enablegrader`. A setting the payload leaves out gets the most common value among the profile's projects (a tie goes to the value of the newest project), or Target Scheduler's own new-project default when the profile has no projects; the command result lists the filled values and their basis. Before this, every project created over the web API got dither every 0, filter switch frequency 0 and flats off, so it never dithered and took no automatic flats; the 10 projects created from the app on 2026-10-10 have these values while the profile's other projects mostly dither every 2 with filter switch 1 and flats every day.
* `project.edit` can now change the same settings, so those projects can be fixed remotely. Ranges follow Target Scheduler's project editor: dither every and filter switch frequency 0 to 999, flats handling 0 (off) to 99 days, 100 (target completion) or 200 (use with immediate), meridian window 0 to 720 minutes, horizon offset 0° to 90°, maximum altitude 0° (off) to 90°; smart exposure order, custom horizon and grader are true or false.

## 5.8.2.210 - 2026-10-10
* Delayed grading of a goal-driven exposure plan now waits on the goal instead of the plan's desired count. The trigger is the goal's share of the subs it needs: stack_subs against min_subs while collecting, against sub_limit while unmeasurable, local subs against min_subs while pending, and stack_subs against stack_subs plus the subs the measured hours still need. A goal that is reached or done grades all pending subs. Plans without a goal still use the desired count. Before this, a collecting plan stopped at 8 subs while grading waited for 80% of its desired count (for example 120 of 150 for Lowers Nebula H-a), so its subs stayed pending and HFR or star-count rejects reached the stack; 68 goal-driven plans held 983 pending subs. The TS log line now states the basis, and the grader's population rule over the web API says when grading follows the goal.
* The web preview now adds each sub it plans to the goal's stack_subs for that target and filter, so a collecting plan closes at min_subs and an unmeasurable one at sub_limit during the simulated night, as the planner sees it once the stacker publishes the new subs. Before this, the preview only raised the local acquired count, which the goal ignores when the stacker publishes stack_subs, so a target that reached its goal mid-night kept being picked. Measured goals are unchanged: the preview cannot predict a new measurement.

## 5.8.2.209 - 2026-10-10
* New sequence instruction **Target Scheduler Darks Ready**. It uses no camera and fails with the reason in the TS log unless all of these hold: a combo in `ts_dark_need` still needs darks; the sun is below -8°; at least one needed dark fits before the sun reaches -8° (its exposure, the last measured save time and the cooling time from ambient: the measured cooling rate, or one hour while it is not measured); the weather device is connected and reports a temperature; and a needed set temperature is covered by cooler evidence. A cooler failure that rules out a set temperature takes precedence over a hold, matching Target Scheduler Darks. A hold covers a set temperature when it was at the same or a warmer ambient and at least as far below it. A cooler failure at the same or a colder ambient rules out that set temperature and colder ones. When no evidence covers a needed set temperature, one learning attempt per night (noon to noon, local time) is allowed. It is used up when Target Scheduler Darks records the first hold or failure that night, so a power-up that never reaches the camera does not use it.
* **Target Scheduler Darks** records a hold after each dark that stays within 2 °C of its set temperature (ambient, set temperature and the peak cooler power during the dark) and keeps cooler failures with a known ambient in `observatory-scheduler-darks.json` across restarts (the newest 200 of each). A new hold removes failures it contradicts, and a new failure removes holds it contradicts. Kept failures expire after 30 days, so a one-off failure does not block a set temperature for good. Failures without an ambient still last only for the current run, and a camera that disconnects during a setpoint move is no longer counted as a cooler failure. A set temperature above the ambient is skipped without a failure record, because the cooler cannot heat the sensor.
* *While Unsafe* placement takes the backlog warmest set temperature first, then by the stacker's priority, so a learning attempt starts with the easiest set temperature.

## 5.8.2.208 - 2026-10-10
* **Target Scheduler Darks** never counts a setpoint move as instant when the cooler's rate in that direction has not been measured. If a deadline applies (the end of the wait for *Before Wait*, or the sun reaching -8° within an hour of the dark), it skips that combo and logs why. With no deadline near (for example *While Unsafe* in the middle of the night) it still makes the move, which measures the rate. Every move now also stops at the latest start that lets the dark end before the sun reaches -8°. On 2026-10-10 at 06:59 the *End Of Sequence* item cooled from -14 °C to -20 °C for a 120 s dark as if the move took no time; it took 251 s and the sun limit ended the item with no darks. The measured rates (41.8 s/°C cooling, 25.9 s/°C warming) now reject that dark.
* The planner no longer plans imaging past the end of imaging that the sequence enforces. It reads each enabled Sun Altitude loop condition on the Target Scheduler container and its parent containers (with NINA's comparator, offset and refraction-corrected sun altitude), finds when the loop ends, and clips every target's twilight window to that time. A target, or the previous target's next exposure, is not selected when the exposure cannot end before it, and an exposure is not started when it can no longer end before it (the rest of that plan is then skipped). If a NINA Meridian Flip trigger encloses the container and a flip is due before the exposure (by NINA's trigger rules, including at once between the minimum and maximum minutes after the meridian), the flip's wait to pass the meridian and its measured slew, recenter and settle time count too. The flip time is measured from NINA's flip start and end events, minus NINA's wait to pass the meridian (longest of the last 5, in `observatory-scheduler-flips.json`); until one is measured, a flip due within an hour of the end of imaging is not started. On 2026-10-10 the planner allowed Lowers Nebula until 07:11 (the exposure templates' twilight) while the sequence's 'Imaging' loop ends when the sun is above -14° (06:35:19), so a meridian flip and a new 600 s O-III exposure started at 06:34 and were cancelled.
* With no Sun Altitude loop condition around the container, planning is unchanged. The TS log states the end of imaging, its basis and the flip time used whenever they change.

## 5.8.2.207 - 2026-10-10
* **Target Scheduler Darks** takes darks only while the sun is below -8°, in every placement. It does not start a dark (with its setpoint move, exposure and download) that would end with the sun at or above -8°, and it stops when the sun is there. The sun's altitude is NINA's, at the profile's observer location; without a location it takes no darks. *End Of Sequence* after the dawn flats now ends at once instead of running the backlog in daylight.
* Basis for -8°, from the darks in `astro/DARK` with the stacker's light-leak spread (leak limit about 0.6 ADU): on 2026-09-29 at dawn the last clean dark ended at -0.67° (0.33 ADU) and the next one, -0.67° to +1.45°, leaked (0.71 ADU, then 1.76, 3.11, 4.14). On 2025-02-05 at dusk the dark from -3.61° to -5.66° leaked (5.51 ADU) and the one from -5.66° to -7.73° was at the limit (0.59 ADU). -8° is below every altitude at which any of these darks gained signal. The TS log states the limit and this basis each time the instruction runs.

## 5.8.2.206 - 2026-10-10
* New sequence instruction **Target Scheduler Darks**. It reads `ts_dark_need`, which the stacker publishes home to the observatory over SymmetricDS: one row per dark combo (exposure, gain, camera offset, binning, readout mode, set temperature) with `priority` (by lights blocked, then lights on a scaled dark), `frames_needed` (the master-dark builder's minimum), `frames_have`, `frames_rejected` and `newest_dark_at`. It takes the remaining darks combo by combo. Darks taken but not yet ingested by the stacker are kept in a local `ts_dark_taken` ledger, so they are not taken twice.
* The instruction only uses the camera: never the flat device, its light or a switch.
* Placement *Before Wait* gets the wait's end from the Target Scheduler container. It starts a dark only when the setpoint move there and back (at the cooler rates it measured itself), the exposure, NINA's last download time and the last measured AfterWait time all fit before the wait ends. It stops on a web re-plan or pause, and a re-plan or pause requested during Before Wait now also ends the planned wait at once.
* Placement *While Unsafe* stops when the safety monitor reports safe: a dark already exposing finishes, a setpoint move in progress is stopped.
* Placement *End Of Sequence* runs until the backlog is done; Stop after (min) sets an optional limit for any placement.
* Temperature gate: setpoint moves use NINA's CoolCamera (which reports a cooler that cannot reach the target), the sensor must be within NINA's at-target tolerance (2 °C) of the setpoint, and a combo whose temperature the cooler cannot reach or hold is skipped until it is cooler outside (weather station) or the cooler needs less power at the imaging setpoint. The sequence's setpoint and readout mode are restored before the instruction returns.
* The AfterWait instructions are now timed, for the Before Wait budget.

## 5.8.2.205 - 2026-10-10
* Seasonal Runway no longer scores a goal-driven filter as needing no time when the stacker has not measured it. When `hours_needed` is missing or negative (the stacker writes -1 when it cannot compute it), the filter needs its remaining desired subs times the exposure length, the count Target Scheduler uses without goals. It is never less than what the 5.8.2.204 completion rules still need: the subs left to `min_subs` for a `collecting` or pending goal, or at least one sub for any goal that is not complete. An `unmeasurable` goal needs the subs left to its `sub_limit`, where 5.8.2.204 stops it. Measured `hours_needed` and done goals score as before.
* A missing or negative `hours_needed` now stays unknown in the plugin instead of reading as a number.
* The TS log records each goal-driven exposure plan's completion decision when it changes: the target, the filter, the old and new state (`measured`, `collecting`, `unmeasurable` or `pending`, and `open` or `closed`), `stack_subs`, `min_subs`, `sub_limit`, `done`, `plateau`, `hours_needed` (measured value or unknown), the subs the rules still need and the stacker's reason. Changes are logged at Info. The first decision for each plan after NINA starts is logged at Debug. The web preview's simulated passes are not logged.

## 5.8.2.204 - 2026-10-10
* Goal-driven exposure plans (a `ts_goal` row for the target and filter) no longer stop on their desired count. The goal decides: done when the goal is met, or at the plateau when `plateau_stop` is on. The desired count stays in the database untouched and still feeds delayed grading.
* Before the stacker has a usable measurement, a goal-driven plan stays open until the stacker has the minimum stack it needs to measure. `ts_goal_progress` gains `state` (`measured`, `collecting` or `unmeasurable`), `reason`, `stack_subs`, `min_subs` and `sub_limit`, all written by the stacker. `collecting` plans close at `stack_subs >= min_subs` and wait for the measurement. `unmeasurable` plans (for example a depth goal with no plate solution or too few Gaia stars) close at `stack_subs >= sub_limit`.
* A goal row with no progress row yet is pending. It closes when the plan's acquired count reaches the stacker's published `min_subs`. While the stacker publishes no readiness at all, such plans keep the count behaviour of 5.8.2.203.
* Plans without a goal row keep Target Scheduler's count behaviour exactly.

## 5.8.2.203 - 2026-10-10
* New `GET /os/v1/grader?target_id=N` (current target when omitted): for each exposure plan, the HFR limit the grader would apply to the next sub. It uses the grader's own population rule (subs of that exposure plan in any grading status with the same profile, exposure, gain, offset, binning and ROI; the newest `MaxGradingSampleSize` including the new sub, or all of them with delayed grading), the sample standard deviation, the sigma factor, Accept Improvement and the HFR auto-accept level. Each plan reports `mean`, `sd`, `samples`, `matching`, `upper`, `reject_above`, `reject_below`, `population_rule` and a `state` (`limit`, `too_few_samples`, `no_images`, `invalid_samples`, `hfr_grading_off` or `project_grading_off`).
* Web preview `left_out`: each target's reason now comes from the planner passes during the night, not from the pass after dawn. A target that was ever eligible reports its closest score loss with the winner and both totals; otherwise the reason that held longest, with `at` and `until`. `reasons` lists every reason with its time span. A reason seen only at the last pass is flagged `after_night`.
* Web preview blocks: "gives way because" is the previous target's state at the re-plan (its rejection reason, "X scored higher at the re-plan (a vs b)", or when it is ready again). It no longer guesses from the target's window or reports "end of the night" for every last block. The preview adds `stop_reason`, `error` (when the simulation fails) and `assumptions` (no weather, every simulated sub accepted, no overhead, coarser visibility sampling, web actions not simulated, 20 hours or 5000 plans).

## 5.8.2.202 - 2026-10-09
* Condition Match rule: scores a target on how well tonight suits it. Its altitude now against its own peak (culmination, capped by the project's maximum altitude), times the best of its open filters, each scored for the moon (illumination × how close and how high the moon is; narrowband keeps 75% under the worst glare, broadband loses it all) and for the sky quality the stacker measured on the last three hours of subs (unknown counts as perfect; poor skies hurt broadband fully and narrowband half as much).
* Seasonal Runway rule: the hours a target's goals still need (goal `hours_needed`, or remaining exposures for plans without a goal) against 30% of the usable hours left in its season. 1.0 when it won't finish in season at that pace, falling linearly to 0.
* Season Priority rule: the boost the stacker publishes for mosaic panels under the weakest-first season plan (balancing on), 1.0 for the panel most behind with the fewest months of window left.
* Filter Steering, a per-project switch stored as a `Filter Steering` rule weight (any weight above 0 is on): in a target or panel with goals, the planner shoots the open filter with the largest goal deficit and best gain per hour, keeping broadband out of a bright moon, instead of the exposure plan order. It keeps the current filter when another is within 2%, and falls back to the normal order without goals.
* Rarity's season scan now counts a night only when the target is inside the project's maximum altitude and meridian window and at least one of its filters passes moon avoidance.
* The new rules and the switch are added to every project at weight 0, so nothing changes until a weight is raised.
* `ts_goal_progress` gains `night_quality`, `night_quality_at` and `season_boost`, added on start-up when missing.

## 5.8.2.201 - 2026-10-09
* The web status `exposure` block now comes from the real camera exposure: `started_at` is when the TakeExposure instruction opens the shutter and `ends_at` is that plus the exposure time. Before, both came from the plan pick, so a slew, autofocus or guiding settle made them minutes early.
* Between exposures `exposure` is absent and the new `activity` field says what the rig is doing (`slewing`, `centering`, `focusing`, `guiding`, `meridian_flip`, `switching_filter`, `starting_exposure`, `exposing`, `downloading`, `saving`, `dithering`, `waiting`, `preparing` or `busy`), with `activity_detail` naming the NINA instruction or trigger and `activity_since` when it began.
* Pending commands report `applies_at` only while an exposure is open, as its real end. Otherwise `applies_at` is null and the message says when they apply ("after the next exposure ends", "once the current exposure is downloaded and saved" or "before the next exposure starts"). Pending results are re-sent when the shutter opens and when the capture ends.
* `exposure.number` counts real exposures on the current target.

## 5.8.2.200 - 2026-10-09
* Web control: optional HTTP API on port 8189 with a bearer token, plus a durable command queue (`ts_command`, `ts_command_result`). Commands apply when the current exposure ends, in the order they were made.
* Web editing switch that makes NINA's Target Management editor read-only while the web UI edits.
* Per-filter SNR or depth goals (`ts_goal`, `ts_goal_progress`); targets with goals finish on the goal instead of the image count.
* Novelty, Rarity and Panel Deficit scoring rules, added to existing projects at weight 0. Rarity reads a background season cache (`ts_target_season`).
* TS project, target, exposure plan and panel FITS keywords on lights.
* Includes the 5.8.2.101 options page fix.

## 5.8.2.101 - 2026-10-09
* The plugin options page shows again.

## 5.8.2.100 - 2026-10-09
* Renamed to Observatory Scheduler with its own NINA plugin identifier, based on upstream 5.8.2.0.

# Target Scheduler

## 5.8.3.1 - 2025-12-28
* Fixed issue with flats mishandling ROI.

## 5.8.3.0 - 2025-12-01
* The first 'Switch filter' is now shown in the TS sequence progress display even if same filter was used by last exposure of previous target.
* Fixed issue where targets could have no selectable exposure plan (related to exposure plan disable).

## 5.8.2.0 - 2025-10-29
* New profile preference to disable target completion reset at the profile level.
* Fixes for TS failure when it can't find a suitable exposure.
* Fix for override exposure orders and disabled exposure plans.
* Fixed SQLite dll load error on startup.
* Added a warning to the custom trigger sequence drop area.
* Added guids to most tables to support future api.
* Fix for threading problem in scheduler progress vm add.

## 5.8.1.0 (beta) - 2025-08-07
* Fixed issue with layout during exposure template editing.
* Added twilight offset and dither settings to the exposure template profile table.

## 5.8.0.0 (beta) - 2025-08-04
* Added support for exposure rejection for humidity.
* Added support for an offset in minutes from twilight acceptable times.
* Added ability to disable an exposure plan.

## 5.7.3.0 - 2025-07-28
* Fixed issue with reporting - was assuming that FWHM and Eccentricity are available via HocusFocus.

## 5.7.2.0 - 2025-07-24
* Fixed issue with Center After Drift and updating when same target.

## 5.7.1.0 - 2025-06-28
* Fixed issue with target move and how associated acquired image rows are updated.

## 5.7.0.0 - 2025-06-24
* Added ability to move a target from one project to another.
* Added display of the thumbnail image to the acquired image detail view.
* Added ability to manually change the grading status on acquired images.

## 5.6.5.0 - 2025-06-20
* Fixed issue where TS Sync Condition was not resetting properly.

## 5.6.4.0 - 2025-06-02
* Fixed issue with missing active/inactive icons on project and target panels.

## 5.6.3.0 - 2025-05-24
* Fixed issue with MF pause handling and project minimum time check.

## 5.6.2.0 - 2025-05-19
* Fixed project/target name truncation in new panel for Imaging tab.
* Fixed issue where previous target was allowed to continue even though next exposure was not suitable for current twilight level.

## 5.6.0.0 - 2025-05-05
* Added a TS dockable panel for the NINA Imaging tab.
* Added pause/resume buttons to TS instruction and dockable panel.

## 5.5.1.0 - 2025-05-02
* Fixed issue with meridian window and out-of-range target transit time.
* Fixed issue with targets not continuing after a meridian flip pause.

## 5.5.0.0 - 2025-04-26
* Added Dither Every setting to Exposure Templates, if set it will override the project value.
* Fixed issue with main panel height - was cutting off scoring rule weights list.
* Added better cancel/interrupt handling when taking flats.

## 5.4.0.1 - 2025-04-23
* Bug fix for new meridian pause handling for bad target transit times.

## 5.4.0.0 - 2025-04-22
* Added new scoring rule: Meridian Flip Penalty.
* Added special handling for users that need a pause before a meridian flip.
* Added support for exposure repeating for smart exposure when multiple have the same avoidance score.
* Relaxed profile import: user has option to continue if importing database is newer than the export.
* Fixed issue where flats could miss file name pattern substitutions.

## 5.3.0.0 - 2025-04-13
* Added ability to export all profile data (projects, targets, etc) to a zip file for later import.
* Fixed issue in Reporting where any NaN FWHM or Eccentricity values caused the corresponding range display to show 'n/a'. 
* Preview view details output was horked at TS Info log level.

## 5.2.0.1 - 2025-04-04
* Fixed a problem where filter cadence is not cleared during some target edits.

## 5.2.0.0 - 2025-04-02
Official release of version 5.

## 5.1.9.0 (unreleased beta) - 2025-03-XX
* Reduced tolerance for 'equal' moon avoidance scores (0.05 -> 0.01).
* Targets in Acquired Images dropdown are now sorted by name.

## 5.1.8.0 (beta) - 2025-03-22
* Added ability to manually grade all pending exposure plans for a target.
* Targets in Reporting dropdown are now sorted by name.
* Added Exposure Template name to Acquired Images row detail view.
* Added database busy timeout to avoid locked errors.
* Other misc fixes.

## 5.1.7.0 (beta) - 2025-03-18
* Smart exposure selector will now select the exposure with lowest percent complete when multiple plans have equal highest avoidance scores.
* Fixed issue with custom event containers finding the current target.
* Added additional cancellation check in TS Container.

## 5.1.6.0 (beta) - 2025-03-11
* Added target acquisition summary report to Reporting display.
* Allow the current target to continue if it can use remaining visibility time if less than project minimum.
* Above should also help with TS Condition checks stopping the container if that same timing applies.
* Fixed issue where targets could forget dither state.
* Fixed issues with last flat image missing TS image file pattern variable substitutions.
* Fixed issue where TS Background Condition was postponing rechecks too far in the future.

## 5.1.5.0 (beta) - 2025-03-07
* Added explicit display of regular or provisional percent complete on exposure plans.
* Fixed bug with grading (was impacting all previous delayed grading runs).

## 5.1.4.0 (beta) - 2025-03-03
* Revised target/exposure plan percent complete calculation to handle delayed grading.

## 5.1.3.0 (beta) - 2025-02-26
* Added 'After Target Complete' custom event container.
* Fixed bug where sync client was ignoring exposure length on exp template of same name.
* Sync client can now take multiple exposures per server exposure.
* Location in sequence of a Center After Drift trigger is relaxed, can now be in any container above TS container.
* Preview now shows end times for targets.
* Added new published message for target complete (developers only).
* Added details to the message published when starting a planned wait: the next target and the number of seconds until the wait ends (developers only).

## 5.1.2.0 (beta) - 2025-02-20
* Added 'After Each Exposure' custom event container.
* Added ability to set the number of flats to take in the TS flats instructions.

## 5.1.1.0 (beta) - 2025-02-18
* Reformulated the moon avoidance score.
* Scheduler Preview now includes an end time.

## 5.1.0.0 (beta) - 2025-02-17
* Planner will now attempt to continue with a selected target for the minimum time.
* Fixed display and threading problems with Scheduler Preview.
* Added profile preference to set the TS log level (independent of NINA log level).
* Fixed issue with moon avoidance scoring.
* Improved performance of scheduler preview by decreasing visibility sampling rate.

## 5.0.2.0 (beta) - 2025-02-12
* Fixed Telescopius CSV format change.
* Recheck project minimum time after a meridian window clip.
* Fixed bug with new slew disable capability.

## 5.0.1.0 (beta) - 2025-02-11
* Added profile preference to enable/disable slew/center for new targets.
* Fixed problem where (now slower) plan preview locks the main UI thread.
* Fixed serious problem with planner time handling.

## 5.0.0.0 (beta) - 2025-02-09
* Major release

## 4.9.1.0 - 2025-01-02
* Fix for entering fractional seconds in target coordinates.  It will behave like core NINA: the fractional seconds will be stored and used but will be rounded for display.

## 4.9.0.1 - 2024-12-21
* Patch to prevent TS Target coordinates from getting cleared in certain scenarios (typically with CenterAfterDrift handling and Powerups usage)

## 4.9.0.0 - 2024-10-24
* Added flag to trigger avoidance if moon altitude is above the relax maximum altitude - regardless of actual separation or moon phase
* Adjusted moon avoidance evaluation time for not yet visible targets
* Hopefully fixed issue with taking superfluous flats
* Raised the max for Exposure Plan Desired and Accepted counts to 99999

## 4.8.0.0 - 2024-09-12
* Fixed a bug where the planner could return a plan with no exposures, will now abort container and warn instead
* Fixed a bug where needed flats were being improperly culled
* Added test support for inter-plugin messaging: TS will now message when a wait starts and when a target plan starts

## 4.7.6.2 - 2024-09-07
* Don't abort a flat exposure if setting flat panel brightness throws an error, just warn
* Changed the time at which moon avoidance is evaluated, now halfway through target's minimum time
* Removed rotation as one of the comparison criteria for flats

## 4.7.5.0 - 2024-09-01
* Added ability to change scheduler preview start time to now.

## 4.7.4.0 - 2024-08-13
* Bug fix for immediate flats on sync client
* Bug fix for event container race condition.

## 4.7.3.0 - 2024-08-09
* Bug fix for event containers not waiting for completion before continuing.

## 4.7.1.0 - 2024-08-07
* Bug fix for event container naming.

## 4.7.0.0 - 2024-08-06
* Added support for custom event containers in the Target Scheduler Sync Container instruction.
* Added support for running TS Flats instruction in a sync client sequence.

## 4.6.0.0 - 2024-07-30
* Added ability to reset target completion at the profile, project, and target levels.
* Added support for TSPROJECTNAME path variable.
* TS Flats instruction no longer displays misleading progress when idle.
* Fixed bug with caching and project/target horizon changes.

## 4.5.1.0 - 2024-07-05
* Fixed bug with smart plan window and concurrent or future potential targets

## 4.5.0.0 - 2024-06-19
* Relaxed matching criteria for trained flats, will now match if gain or offset is not equal
* Added additional logging for flat panel operations

## 4.4.0.0 - 2024-06-08
* Added ability to progressively relax classic moon avoidance when the moon is near or below the horizon
* Fixed (hopefully) crash when making some TS database changes after other NINA operations
* Fixed bug with nighttime only exposures and high latitudes near summer solstice
* Fixed bug logging training flat details which broke taking some flats

## 4.3.8.0 - 2024-05-09
* Fixed problem impacting sequences used for sync clients.

## 4.3.7.0 - 2024-05-05
* Raised timeouts/deadlines for sync operations

## 4.3.6.0 - 2024-04-17
* Added Target Scheduler Background Condition
* TS Container UI reworked to be more like a standard container and with better scrolling behavior (thanks Stefan)
* Fixed problem with override exposure order not being copied on paste operations and bulk import
* Fixed bug where internal filter name is unknown for OSC users
* Fixed bug (hopefully) where sync client was failing to process images and update the database

## 4.3.5.0 - 2024-03-08
* Fixed problem with CSV import due to NINA package updates

## 4.3.4.0 - 2024-02-23
*## 4.3.5.0 - 2024-03-08
* Fixed problem with CSV import due to NINA package updates

 Added toggle in Projects navigation to color projects/targets by whether they are active or not
* Added toggle in Projects navigation to show/hide projects/targets by whether they are active or not
* Added copy/paste/reset for Project Scoring Rule Weights

## 4.3.3.0 - 2024-02-15
* Refactored target and exposure planning percent complete handling

## 4.3.2.1 - 2024-02-12
* Fixed exposure completion reversion caused by previous percent complete rule fix

## 4.3.2.0 - 2024-02-06
* Fixed bug in percent complete scoring rule for completed exposure plans

## 4.3.1.0 - 2024-02-02
* Another tweak to TS Condition to ensure loop remains completed
* Fixed bug where target from Framing Wizard would appear to replace target in TS target management panel
* Code clean up

## 4.3.0.0 - 2024-01-26
* Fixed issue where TS Condition wasn't working when called in outer containers
* Increased timeout for sync client registration
* Added validation of TS Container triggers and custom event containers
* Stopped cloning of TS Container triggers into plan sub-container (now run normally)
* Added additional logging of sequence item lifecycle events

## 4.2.0.0 - 2023-12-28
* Added ability to bulk load targets from CSV files

## 4.1.2.2 - 2023-12-21
* Fixed bug in readout mode handling
* Fixed bug with Percent Complete and Mosaic Complete scoring rules if image grading is off

## 4.1.2.0 - 2023-12-18
* Fixed bug in smart plan window - was skipping projects incorrectly
* Fixed another bug with determining target completed
* You can now choose to delete acquired image records when deleting the associated target
* If running as a sync client, TS Condition will now use the server's data for the targets remain or projects remain checks

## 4.1.1.3 - 2023-12-15
* Fixed bug in TS Flats with project flats cadence > 1
* Fixed bug with determining target completeness with exposure throttling
* Fixed missing TS version in TS log

## 4.1.1.1 - 2023-12-14
* Fixed bug in TS Condition - check wasn't running the first time through
* Immediate flats wasn't handling Repeat Flat Set off correctly
* Immediate flats instruction will now open a flip-flat cover when done
* Updated for latest NINA 3 beta libraries

## 4.1.0.8 - 2023-12-12
* Added support for taking automated flats
* Optimized the condition check in Target Scheduler condition
* Target Scheduler Container instruction has a new custom event container: _After Each Target_
* Added a 'need flats' check to Target Scheduler condition

## 4.0.5.1 - 2023-11-26
* Improved handling when TS is canceled/interrupted which means it behaves better in safety scenarios and with Powerups safety controls.

## 4.0.5.0 - 2023-11-17
* Added image grading on FWHM and Eccentricity (requires Hocus Focus plugin)
* Added option to move rejected images to a 'rejected' directory
* Added ability to purge acquired image records by date or date/target
* Added CSV output for acquired image records
* Added better support for the Center After Drift trigger (see release notes)
* Added smarter determination of plan stop times
* Added ability to delete all target exposure plans
* The rule weight list is now sorted when displayed
* Added target rotation and ROI to the set of data saved for acquired images.  A future release will use these values when selecting 'like' images for grading.
* Fixed issue where target rotation wasn't being sent to Framing Wizard
* Added experimental support for synchronization across multiple instances of NINA
* All sequencer instructions moved to new category "Target Scheduler"

## 3.3.3.1 - 2023-10-11
* Fixed bug with exposure planner.

## 3.3.3.0 - 2023-09-19
* Fixed edge case bug with custom horizons.

## 3.3.2.0 - 2023-09-07
* Fixed problem with override exposure ordering. Unfortunately, any existing override order had to be cleared (automatically) for this fix.  You'll have to manually redo any that you had already created.

## 3.3.1.0 - 2023-08-22
* Added ability to override exposure ordering.
* Added Mosaic Completion scoring rule
* Fixed bug with rotation not being set when importing from a saved Sequence Target.
* Fixed bug related to non-existent custom horizon

## 3.2.1.0 - 2023-08-09
* Fixed bug preventing target ROI from being applied properly.

## 3.2.0.0 - 2023-08-07
* Changed the behavior of project minimum altitude: now can be used with or without a custom horizon.  If used with, then the horizon at each azimuth is the greater of (custom horizon + horizon offset) or project minimum altitude.
* Added ability to copy/paste exposure plans.
* Added fixed date range options to Acquired Images viewer and improved performance.
* Added ability to select images in the Acquired Images table by filter used.
* Fixed issue with scheduler preview: wasn't picking up dynamic changes to target database.
* Added 5/10/20 minute options to project minimum time.
* Will automatically unpark the scope if parked before a target slew.
* Fixed the annoying bug related to editing Exposure Templates on Target Exposure Plans.
* Images in the acquired images table will now show 'not graded' as the Reject Reason if grading was disabled when the image completed.
* Now skips useless Target Scheduler Condition checks.

## 3.1.2.0 - 2023-07-20
* The execution of the Before/After Target containers was changed to mean run only for new or changed targets. 
* Added a 'View Details' button to the Scheduler Preview to show details of the planning process.  The same information is available in the TS log for actual runs via the sequencer.

## 3.1.0.0 - 2023-07-13
Limited release only.
* Added support for inserting arbitrary instructions to run at various points during scheduler operation: before/after each wait period and before/after each target plan.
* Removed Conditions and Instructions drop areas in the Target Scheduler Container (not used and just confusing).
* Improved the display of running instructions in Target Scheduler Container

## 3.0.0.0 - 2023-07-XX
Limited release only.
* Ported to NINA 3
* Target rotation values will be auto-converted to NINA 3 counter-clockwise notation

## 0.8.0.0 - 2023-06-12
* Revised dithering approach (see release notes)
* Now does a center with rotation even if target rotation is zero

## 0.7.1.1 - 2023-05-30
* Fixed problem with missing parent for internal container

## 0.7.1.0 - 2023-05-25
* Added support for meridian window restriction
* Added default exposure time to Exposure Templates
* Added airmass to acquired image data detail display
* Added option to park the mount while waiting for next target
* Added option to throttle exposure planning when not grading
* Added option to accept all improvements in image grading
* Added loop condition to support outer loops for safety or multi-night
* Fixed problem with ROI exposure capture
* Fixed problem with including rejected exposure plans

## 0.6.0.0 - 2023-04-27
* Added validation to detect when Loop Conditions or Instructions are added to the TS container

## 0.5.0.0 - 2023-04-26
* Added support for importing mosaic panels from Framing Assistant

## 0.4.1.0 - 2023-04-25
* Added support for managing profile preferences
* Added image grader reject reason to acquired image data

## 0.4.0.0 - 2023-04-24
* First cut at image grader

## 0.3.0.0 - 2023-04-21
* Removed start and end date fields from projects
* Created a custom log for the plugin
* Added support for database migration scripts
* Fixed bug with plan end time

## 0.2.0.1 - 2023-04-20
* Increased the timeout in the image save watcher for DB updates
* Fixed problem saving a sequence as a sequence template

## 0.2.0.0 - 2023-04-02
* Major refactoring of the plugin sequence containers.
* Added Setting Soonest scoring rule.  Although the database schema hasn't changed, any projects created prior to this release will not be able to use this rule.

