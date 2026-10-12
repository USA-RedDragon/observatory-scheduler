Observatory Scheduler 5.8.2.220: bright-Moon kept rates.

- **Kept rates**: exposures are weighed by the share of past subs the stacker kept under a Moon like this one, and near-worthless ones are left; the scheduler then takes other filters or targets, or waits for the Moon to set.
- **Flats in waits**: Target Scheduler Flats in Before Wait runs only when it fits and the cover is confirmed closed; the light is always turned off after a failed set. Darks refuse while the panel light is on.

On start-up the plugin adds `ts_moon_keep` to its SQLite database. The home database needs the same table and the `moonkeep` SymmetricDS channel (install the plugin first). Nothing changes until rows arrive. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.219.
