# Polar's Thrust Preview

Mass, thrust-to-weight per direction, lift range and hover endurance for your ship, at its real load, heavily loaded and fully loaded. Shown in the cockpit on a key, and while building with the held block already counted. Client plugin for [Pulsar](https://github.com/SpaceGT/Pulsar).

**Multiplayer:** no server install. It only reads ships you own or share a faction with (or unowned ones) and draws on your screen.

## What it shows

A dark blue HUD panel in the vanilla style, top to bottom:

| Part | Meaning |
|---|---|
| Header | Ship name. While building, an amber strip shows the block in hand, how many copies, which way it pushes and its mass |
| Weight boxes | **Current weight** (what physics uses), **Heavy (full of ore)** (every inventory full of the densest ore it accepts), **Max (densest cargo)** (every inventory full of the densest item it accepts), in kg. Cargo volume and inventory multiplier below |
| Thrust to weight ratio | Per direction, with a bar per load. In gravity it is measured against the gravity where you are, with thruster effectiveness for the air where you are; in space against 1 g. Directions with the same numbers share a row (Fwd / Back, Up / Down, Left / Right). Up: green 1.25 and over, yellow 1 to 1.25, red under 1. Other directions: green 0.6 and over, yellow 0.3 to 0.6, red under 0.3 |
| Space capable | Yes if something pushes that way in vacuum, no if nothing does |
| Lifts in gravity | A 0g to 1.5g scale per load, with sea-level air. Green: lifts comfortably (TWR 1.25 and over). Yellow: only just. Red: can't lift. "lifts up to" gives the highest gravity it can lift. A white tick marks the gravity where you are |
| Hover time | With the heavy load at 1 g and full air: how long each fuel and the batteries last now, and with full tanks or batteries. Says so when generators cover the draw, or when it can't hover |

Directions are relative to the seat you are in. While building they are relative to the main cockpit, any other controller, or the grid if it has none.

The ship is the grid plus everything on rotors, pistons and hinges. Ships docked by connector are not counted.

## Cockpit

Sit in a cockpit, remote control or control seat and press **'** (the apostrophe key) to cycle the panel: full, collapsed, off. The collapsed view shows current weight, the Up thrust to weight ratio and the lift bars for current and heavy. The choice stays for every seat until you press it again.

## Building

Hold a block and aim at a grid you own, share a faction with, or that has no owner: the panel shows with that block already added, before you place it. Other players' ships are not shown.

- Every block adds its mass.
- Thrusters add thrust in the direction they would push, so rotate the block and watch the table change. Symmetry copies are counted too.
- Cargo containers add their volume to the heavy and max loads.

The same key while holding a block aimed at a grid cycles this panel: full, collapsed, hidden. It starts on full each time you load a world. Turn it off completely with **Show while building** in the settings.

## How the numbers are worked out

- Thruster force, power and the air rules come from the block definitions, so modded thrusters work. Effectiveness follows the game's own rule: ion thrust falls to 20% at sea level, atmospheric thrust is zero below 30% air.
- Per-block thrust and power multipliers (set by mods or scripts) are included.
- The densest item each inventory accepts comes from the loaded item definitions and the inventory's own restrictions, so a reactor only counts uranium and an ammo box only ammo. In vanilla, max is platinum ingots (21.3 kg/L) and heavy is ore (2.7 kg/L).
- The game divides cargo mass by the inventory multiplier, so on an x10 world a full ship is not ten times heavier. The plugin uses the world's block inventory multiplier, and confirms the rule the first time you sit in a ship carrying 2 t or more of cargo.
- Hover burn: all up thrusters throttled together to hold the ship with the heavy load at 1 g. Fuel use follows the game's conversion (power / (efficiency x fuel energy density)) and drops with effectiveness the same way thrust does.

## Settings

Pulsar plugin list > Polar's Thrust Preview > Settings.

| Option | Default | Range |
|---|---|---|
| Enabled | on | |
| Panel key | ' | any key, optional Ctrl / Alt / Shift. Seated: full, collapsed, off. Holding a block: full, collapsed, hidden |
| Show while building | on | |
| Inventory size | 0 | 0 to 100, 0 = world setting |
| Thrust multiplier | 1 | 0.1 to 10 |
| Text size | 0.8 | 0.5 to 1.5, scales the whole panel |
| Position X / Y | 0.01 / 0.28 | 0 to 1 |

**Inventory size** plans for another multiplier: cargo volume changes with it. Because the game divides cargo mass by the same multiplier, the mass and TWR stay the same unless a mod changed that rule.

**Panel key** is ' (apostrophe), which has no vanilla binding. Earlier defaults clashed with the game: Ctrl+J (before 1.2.3) also opened the helmet, and ] (1.2.3 to 1.2.5) is vanilla's next paint colour, next skin and next camera. Config files still on either move to ' automatically. Every letter key and [ ] ; , - . ~ have vanilla bindings, so pick another key with care.

**Thrust multiplier** is a what-if on all thrust, for servers or mods that scale it.

Saved to `%AppData%\SpaceEngineers\Storage\PolarsThrustPreview.cfg`, created with the defaults on first run. A damaged file is ignored (defaults are used) and left in place. Delete it to reset.

## Compatibility

- No Harmony patches, so nothing to clash with. Viktor Ferenczi's plugins and other HUD plugins are untouched.
- Works offline, hosting, and as a client on a dedicated server.

## Known issues

- In build mode only cargo containers add cargo volume before placing. Other blocks with inventories (connectors, tanks with bottle slots, production) count once placed.
- Tanks and batteries in your hand do not change the endurance line until placed.
- Hydrogen engines feeding from the same tanks are not counted as a drain on the hover time.
- Placing many blocks in one drag counts as one block (or one per symmetry copy).

## How it works

A session component, created and unloaded with each world by the game:

- Ten times a second it checks what to show. The numbers refresh once a second. While building they also refresh at once when the held block, its rotation, its symmetry copies, the target grid or the ship's blocks change; moving the block across the grid costs nothing.
- The block lists are scanned only when the ship changes (block added or removed, a subgrid attached or lost) or a new ship is shown. Refreshes reuse the cached lists.
- Every frame it only draws the prepared rectangles and text. The panel is laid out in pixels for a 1080p screen and scaled to your resolution and text size.
- The block in hand is public game data. The build target and gizmo (orientation, symmetry copies) are not, so they are read through getters compiled once per world. If they are missing after a game update, the held block counts for mass only and one line goes to the game log.

Any unexpected error writes one line to the game log, prefixed `[PolarsThrustPreview]`, and turns the panel off until the world is reloaded. No networking, and no file access beyond its own config.

## Bug reports

[Open an issue](https://github.com/Po11arr/PolarsThrustPreview/issues) with your Pulsar `info.log` (`%AppData%\Pulsar\Legacy\info.log`), the `[PolarsThrustPreview]` lines from the game log (`%AppData%\SpaceEngineers\SpaceEngineers_*.log`), and a blueprint of the ship if the numbers look wrong.

## Development

Same layout as Polar's PVP Lean, built on the [CometWorks client plugin template](https://github.com/CometWorks/client-plugin-template). The plugin source is in `PolarsThrustPreview`. `Core` holds the maths with no game types; `Game` reads the ship and draws. Pulsar compiles it from source; nothing is bundled.
