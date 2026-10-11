Observatory Scheduler 5.8.2.211: project settings over the web API.

- **Project settings on create**: `project.create` takes dither every, filter switch frequency, flats handling, smart exposure order, meridian window, custom horizon, horizon offset, maximum altitude and grader. A setting left out gets the profile's most common value, or Target Scheduler's default when the profile has no projects. Projects created from the app no longer get dither every 0 and flats off.
- **Project settings on edit**: `project.edit` can change the same settings, with the ranges Target Scheduler's project editor allows.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.210: with NINA closed, replace the "Observatory Scheduler" folder in %LOCALAPPDATA%\NINA\Plugins\3.0.0\ with the one in this zip.
