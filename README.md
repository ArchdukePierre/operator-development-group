# ODG Operations

The Operator Development Group mod pack for OPERATOR, and the launcher that installs it and keeps it up to date.

## Install

1. Download **ODG-Ops.zip** from the [latest release](https://github.com/ArchdukePierre/operator-development-group/releases/latest).
2. Unzip it anywhere. You get a folder called `ODG Ops`.
3. Run **ODG Ops.exe**.

That is the whole install. The launcher finds your copy of OPERATOR, sets up the mod loader if it is not there yet, downloads the pack, and starts the game. Leave the folder where you put it and use the same exe every time, because that is also how you get updates.

You can run it two ways. On its own it starts the game for you. If you would rather keep launching from Steam, set the launch options for OPERATOR to:

```
"C:\path\to\ODG Ops\ODG Ops.exe" %command%
```

## What you get

Every mod has its own switch in the launcher, so you can turn any of them off before you play.

| Mod | What it does |
| --- | --- |
| ODG Map Loader | Custom maps and the co-op operations that run on them, under DUKE'S OPS |
| M200 Intervention | A bolt-action .408 rifle with its own parts, ammunition and rack slot |
| Barrett M107A1 | A .50 anti-materiel rifle, built as a new weapon rather than a reskin |
| VKS | A suppressed rifle standing as a takeable prop |
| Fireteam Roles | Assaulter, Breacher, Sniper, Medic and JTAC, plus a downed state your team can pull you out of |
| Airborne Infiltration | Jump in from altitude instead of walking on |
| New Slots | New clothing, patches and camo added alongside the stock ones instead of replacing them |
| Loadout Presets | Save and recall named loadouts from the cabinet |
| Quartermaster | A workbench for building and comparing weapon setups |
| Third Person | A third person camera |
| Operator Compass | A heading tape across the top of the screen |
| Spectator Name | Names the operator you are watching |
| No Fatigue | Removes the stamina drain from carried weight |
| Host Pin | Keeps the host's settings from drifting between missions |
| Infil Markers | Puts insertion points where the mission wants them |
| DLSS 5 | Optional upscaler setup, off unless you run the installer in the pack |
| ODG Diagnostics | Records what the game was doing when something breaks, for bug reports |

## Missions

The operations board carries Farmstead, Little Rocket Man, Trenches, Killhouse and the Rig. Map data downloads once each, the first time you launch after a release that carries it, so the first run is a long one.

## Requirements

- Windows 10 or 11
- OPERATOR on Steam
- Roughly 4 GB free on the drive the game is on

## When something breaks

The pack ships ODG Diagnostics, which keeps a record of what the game was doing. After a crash, open the launcher and go to the reports page. It tells you exactly which file to send and where it is. Send that file with your report and it will be a much shorter conversation.

Report problems on the [issues page](https://github.com/ArchdukePierre/operator-development-group/issues).

## Credits

Built by Archduke and the Operator Development Group.

Some art in the pack comes from work released under [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/) and is used under that licence:

- "Osama Bin Laden" by [Abdullah5007](https://sketchfab.com/Abdullah5007), CC BY 4.0

Credit files travel inside the pack next to the assets they cover. Look for `CREDIT.txt` and `PROVENANCE.txt` under `Mods/MapLoader_Assets`.

## Note

This is the public release line. It updates when a public release is published here, and it is separate from the internal builds the team runs.
