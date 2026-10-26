# Local validation

Tested on Apple Silicon macOS against the installed Valheim 1.0.20 game.

- Created and resumed an isolated character and local world, with normal saves kept separate.
- Walked and gathered while the OS reported T3 Code as the foreground application.
- Planted carrots, turnips and onions from seeds, and planted all three vegetables for seed production. Each action consumed one required inventory item.
- Added the game's growth-health preflight after finding that ordinary placement can accept an obstructed plant. Rejected spots retained their resources. Six subsequent plants passed the growth-health check.
- Used fixture-only accelerated growth to test harvesting both vegetables and seeds. Final carried totals included three of each vegetable and nine of each seed, following consumption and harvesting.
- Transferred vegetables to storage, withdrew wood and iron ore, loaded five wood into a kiln, collected five charcoal, stored them, and withdrew four for the smelter.
- Loaded the charcoal and two iron ore, waited for normal processing, collected two iron ingots, and deposited both into storage.
- Added native game pathfinding and corrected waypoint following so an interaction stops once its destination is within reach. Inaccessible routes remain bounded and require intervention.
- Entered a claimed, sheltered, heated bed at night and woke through the normal sleep sequence. Heartbeats continued while asleep. Morning is distinguished from the following night to avoid repeated sleep requests.
- Verified that an eight-second heartbeat lease expires, releases controls, and rejects movement until explicitly rearmed.
- Verified takeover on controlled fixture damage: health 25 to 20, control released.
- Verified takeover on a local raid event with its spawn list emptied: the raid warning was reported and control released.
- Observed an actual greyling proximity alert in the earlier local test. Subsequent deterministic fixture runs disabled enemy targeting only for the isolated test character.

The public package builds its native loader and managed bridge against the local installation. Python entry points passed syntax compilation. Publication checks inspect the staged files for credential patterns and personal paths; saves, logs, player IDs, local configuration and generated binaries are excluded.

The next step is a supervised trial in the user's actual server base. Multiplayer container ownership, actual routes, storage-role mapping, and any additional crop types still require that trial. This validation does not claim unattended server reliability or prediction of raids before the game announces them.
