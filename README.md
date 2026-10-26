# Valheim Agent

An experimental local bridge that lets a controller perform farming and workshop chores in Valheim on Apple Silicon, while another application stays in front. Commands run on Unity's main thread and use game movement, planting validation, inventory transfers, machine inputs, and bed interactions.

## Build

Requires the installed macOS game, a .NET SDK, and Xcode Command Line Tools. Game assemblies are read from the installation and are not redistributed.

```sh
export VALHEIM_DIR="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
# Set DOTNET_ROOT if dotnet is not on PATH.
python3 build.py --native
```

The bundled native loader source is pinned UnityDoorstop, with a small main-queue callback wrapper. Its LGPL license and revision are included under `third_party/UnityDoorstop` and `runtime`.

## Local test

Close Valheim first. Run `python3 launch-test.py`, then `python3 control.py start` after the title screen loads. Test saves live in `saves/`, separately from normal saves. Wait for the character to finish loading and stabilize, then run `python3 control.py arm`.

The fixture launcher disables enemy targeting for the isolated test character. Fixture spawning, accelerated growth, time changes, and test damage are guarded to the isolated local world and cannot execute in server mode. Normal chore actions consume resources and use normal processing times.

## Supervised server trial

Close the local test. Run `python3 launch-server.py`, choose your normal character, and join your server normally. This launcher uses normal game saves and starts the bridge **disarmed**. It changes no persistent Steam launch options and requires no server-side mod.

The controller must explicitly arm with the joined world's exact name. Control is bound to that player and connection. The trial still needs verification against the actual server's layout and multiplayer ownership behavior.

Commands are local JSON files in `control/`; there is no network listener or stored server password. Example:

```sh
python3 control.py status
python3 control.py arm '{"world":"Your joined world name"}'
python3 control.py move '{"x":1,"z":0,"seconds":0.5}'
python3 control.py release
```

A movement command lasts at most three seconds. An active controller must renew its heartbeat within eight seconds. Lost heartbeat, disconnect, or a world change releases controls. Nearby raids, alerted attacking creatures, and health loss latch a takeover stop. The mod displays an in-game warning, requests a macOS notification, and plays a local alert sound. Press **F8** in the game to take over immediately. Notification delivery depends on macOS settings; sound and in-game warning do not require Accessibility permission. A raid can only be reported once the game exposes it; this does not predict future raids.

## Chores

`agent.py` provides bounded navigation, harvesting, planting, storage transfers, charcoal production, iron smelting, output collection, and sleep interruptions. Its configuration maps surveyed chest/machine IDs to roles. Keep configuration local; instance IDs can change between sessions.

Farm configuration example:

```json
{"ingredients": 12345}
```

Smelting configuration example:

```json
{"wood":1,"coal":2,"ore":3,"ingots":4,"kiln":5,"smelter":6}
```

Run `python3 agent.py farm --world 'World name' --config local-config.json`, or use `smelt` instead of `farm`.

The farm runner harvests carrots, turnips, onions, barley and flax. It plants carried carrot/turnip/onion seeds, reserves at least half of each carried vegetable for seed production, and replants half of barley/flax. Surplus vegetables go to the configured ingredients chest. It checks growth space before consuming seeds and retains leftovers when the cultivated plot is full.

The workshop runner prioritizes iron ore and scrap, then processes other compatible ores from the mapped ore chest. It takes wood from its mapped chest, feeds the charcoal kiln, stores charcoal, fuels the smelter, adds iron ore, and stores finished iron in the mapped ingot chest. It waits for normal processing. Routes use the game's pathfinding with short movement steps; blocked routes stop for human intervention. Buildings may require surveyed waypoints in a real base. Night interrupts navigation for the current claimed bed; refusal to sleep or waiting on other players stops for human attention.

This is a supervised prototype, not an unattended survival bot. Takeover warnings restore manual controls but do not make the character invulnerable. Keep the game within reach during a trial.

## Privacy

Game saves, player/world data, credentials, local configuration, runtime logs, generated binaries, and captured test telemetry are excluded from this repository. Build against your own installed game. The bridge does not provide chat, credential, or account access.

See [VALIDATION.md](VALIDATION.md) for the completed local checks and remaining server trial scope. Guard checks can be repeated with `python3 tests/live_checks.py heartbeat` (or `damage` / `raid`) in the isolated fixture world.
