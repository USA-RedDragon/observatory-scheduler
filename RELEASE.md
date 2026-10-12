Observatory Scheduler 5.8.2.222: kept rates by the target's altitude.

- **Altitude**: exposures are weighed by the share of past subs the stacker kept with the target at this altitude and the Moon down, and near-worthless ones are left until the target is higher.
- **Custom horizon without a profile horizon**: falls back to the minimum altitude, at least 15°.

On start-up the plugin adds `ts_alt_keep` to its SQLite database. The home database needs the same table and the `altkeep` SymmetricDS channel (install the plugin first). Nothing changes until rows arrive. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.221.
