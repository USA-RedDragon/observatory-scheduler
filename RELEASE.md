Observatory Scheduler 5.8.2.219: filters instead of exposure templates.

- **Managed templates**: one system template per filter ("<filter> (auto)"): broadband gain 0 with a -15 deg Sun gate, narrowband gain 100 from nautical twilight, and a 20 deg Moon floor. Plans can name just their filter.
- **Migration**: new commands `templates.migrate` and `templates.revert` (with dry run) move every plan onto the managed template of its filter and back; old templates are retired, not deleted, and plan counts and history are unchanged.

On start-up the plugin adds `ts_template_role` and `ts_template_migration` to its SQLite database. The home database needs the same tables and the `templates` SymmetricDS channel (install the plugin first). Nothing changes until `templates.migrate` runs. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.218.
