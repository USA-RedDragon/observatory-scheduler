Observatory Scheduler 5.8.2.215: darks only with the cover closed, autofocus history and live autofocus points.

- **Darks safety**: Target Scheduler Darks takes no dark or bias frame unless the flat device reports its cover Closed.
- **Autofocus history**: every autofocus run is stored in `ts_autofocus_run`, and the last one is in the remote status.
- **Live autofocus points**: while focusing, the remote status carries each measured point (NINA 3.1.2.9001 with Hocus Focus 3.0.0.21 only; with a kill switch).

On start-up the plugin adds `ts_autofocus_run` to its SQLite database. The home database needs `ts_autofocus_run` and its SymmetricDS trigger to receive the history. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.214.
