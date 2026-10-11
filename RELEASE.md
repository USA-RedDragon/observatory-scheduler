Observatory Scheduler 5.8.2.213: abort the running exposure from the web and rule weight what-ifs.

- **Abort now**: the new `exposure.abort` web command cancels the running camera capture through NINA and discards the partial frame. The sequence keeps running and the scheduler re-plans. The aborted exposure does not advance the filter cadence, the dither count or the exposure number, so its filter is taken next.
- **Abort during download**: the status keeps the running exposure until the frame is saved or aborted, so it can still be aborted while the camera downloads.
- **Sync**: `abort_blocked` and `sync_clients` on the status exposure. A sync client rejects a web abort with the reason; a sync server aborts only its own camera and waits for the clients as after a normal exposure.
- **Rule weight what-ifs**: preview overrides can set rule weights for one project or for every project.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.212: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
