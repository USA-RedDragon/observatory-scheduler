Observatory Scheduler 5.8.2.101: release 1 (5.8.2.100) plus one fix.

- The plugin's options page (Target Management, Scheduler Preview, Reporting, Acquired Images) shows again. Release 1 renamed the plugin to "Observatory Scheduler" but kept the options template key "Target Scheduler_Options", and NINA looks the template up by plugin name, so the page fell back to an empty template.

No database schema change. Requires NINA 3.1.2.9001 or later. Install exactly like 5.8.2.100: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
