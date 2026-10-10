Observatory Scheduler 5.8.2.207: Target Scheduler Darks never takes darks in daylight.

- **Sun limit**: in every placement, the instruction starts a dark only if it ends (setpoint move, exposure and download included) with the sun below -8°, and it stops when the sun reaches -8°. It uses NINA's sun altitude at the profile's observer location; with no location set it takes no darks. Placed at the end of the sequence after the dawn flats, it now ends at once.
- **Basis**: darks on 2026-09-29 leaked from the one that started at sun -0.67° (0.71 to 4.14 ADU against a limit of about 0.6), and darks on 2025-02-05 leaked at dusk down to -5.66° and were at the limit to -7.73°. -8° is below all of them. The TS log states the limit and its basis.
- The other yield rules, the temperature gate and the camera-only rule are unchanged.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.206: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
