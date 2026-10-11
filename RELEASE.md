Observatory Scheduler 5.8.2.212: exposure counts per night and a live start for tonight's preview.

- **Exposure number per night**: the status exposure number counts each target's exposures for the whole night (reset at local noon) and no longer resets on every re-plan. After a restart it continues from the target's acquired images since local noon.
- **Preview starts from the live state**: tonight's preview starts when the running exposure ends, counts that exposure, and treats the target imaging now as the current target. A `live` assumption explains the starting state.
- **Altitude check on edits**: project edits that put the maximum altitude at or below the minimum (other than 0, off) are rejected.
- **Null before values**: edits accept a null `before` value for settings the project has no value for.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.211: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
