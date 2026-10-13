Observatory Scheduler 5.8.2.226: the running target keeps its turn unless a challenger scores more than 1% higher.

- **Tie band**: at a re-plan, a challenger within 1% of the running target's score no longer replaces it, as long as the running target is still ready to image. The live planner and the Tonight simulation use the same rule.
- **Plain-words reason**: the Now page, the Tonight pick table and timeline, and Left out tonight say when and why the running target kept its turn. The log records each hold.
- **NINA 3.2**: the plugin builds for NINA 3.1 and NINA 3.2.

No database changes. Requires NINA 3.1.2.9001 or later. Install like 5.8.2.225. The web shows the hold after the astro-stacker and astro-processing updates that carry score_note and held.
