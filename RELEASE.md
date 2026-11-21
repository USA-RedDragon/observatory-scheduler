Observatory Scheduler 5.8.2.242: the poor-sky gate covers narrowband filters, and the rig waits when every target would score under its cut.

- **Gate**: H-a, O-III and S-II sit out like broadband when the stacker expects a sub taken now to fail its score cut. The stacker's expected score already accounts for the Moon, so it is no longer scaled by the moon keep worth.
- **All held out**: targets whose every open filter is held out yield to others; when that is every ready target, the scheduler waits 10 minutes and plans again.

Includes everything in 5.8.2.241. No database changes. Requires NINA 3.1.2.9001 or later and an astro-stacker that publishes expected scores per target. Install with install-plugin.ps1 and warm-camera.ps1 from this folder.
