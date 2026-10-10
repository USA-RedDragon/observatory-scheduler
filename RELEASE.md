Observatory Scheduler 5.8.2.201: the web status follows the real camera exposure.

- The status `exposure` block (`started_at`, `ends_at`, `number`) now comes from the moment the plugin's TakeExposure opens the shutter, not from the plan pick. A slew, centering, autofocus, guiding settle or dither between the pick and the shutter no longer makes the times early.
- While the camera is not exposing, `exposure` is absent and the new `activity` field says what is happening: `slewing`, `centering`, `focusing`, `guiding`, `meridian_flip`, `switching_filter`, `starting_exposure`, `exposing`, `downloading`, `saving`, `dithering`, `waiting`, `preparing` or `busy`. `activity_detail` names the running NINA instruction or trigger and `activity_since` says when the activity began.
- A pending command's `applies_at` is the real end of the open exposure. With no exposure open it is null and the message says when the command applies: after the next exposure ends (during slew, focus and so on), once the current exposure is downloaded and saved, or before the next exposure starts. Commands still apply at the plan boundary after the exposure, as before.
- `exposure.number` counts real exposures on the current target.

No database or settings changes. The zip has the same files as 5.8.2.200. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.200: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
