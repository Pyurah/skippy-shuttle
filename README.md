# SkippyShuttle

An autonomous two-connector **delivery shuttle** script for Space Engineers, built as a
purpose-made replacement for PAM when all you need is a ship that ferries cargo between
two docking points (for example, a planet base and an orbital station).

- **One file, two roles.** Paste the same script into the ship PB *and* the base PB. The
  role is chosen in Custom Data (`role = shuttle` or `role = base`).
- **PAM-style route teaching.** Dock, `RECORD HOME`, fly the route by hand, dock, `RECORD DEST`.
  The route is saved as copy-pasteable text so you can share it across a fleet.
- **Orientation-matched, ship-agnostic docking.** Recording captures the full docked pose —
  position, facing, and the connector's mating axis — so the shuttle reproduces the exact
  attitude it was recorded in. Works for connectors facing **any** direction (nose, top,
  bottom, side), on any ship with gyros and thrusters.
- **Custom PAM-style flight.** A single gyro + thruster controller flies the whole route —
  undock, cruise, and dock — on a precomputed velocity profile with a √(2·a·d) braking curve.
  It turns to face each waypoint, accelerates on straights, slows through corners, and eases
  into the dock. No stock autopilot, so no weaving or circling between waypoints. While flying
  it runs at **60 Hz** so the heading stays rock-steady (a slow loop overshoots and wobbles),
  and it **coasts in space** — dampeners off, thrust cut once up to speed — so a straight cruise
  leg burns effectively **no fuel**.
- **Cargo-aware.** Toggles your load/unload sorters and enforces a mass gate so the ship
  never departs overweight.
- **Live status + ETA.** Ship LCDs show state/ETA; the shuttle broadcasts to base screens.

---

## Install

1. Paste [`SkippyShuttle.cs`](SkippyShuttle.cs) into the ship's Programmable Block and recompile.
   On first run it writes a config template into the PB's **Custom Data**.
2. Edit the Custom Data (see below), then recompile again.
3. Repeat for the base/station PB, but set `role = base`.

## Custom Data (`[shuttle]` section)

| Key | Default | Meaning |
|---|---|---|
| `role` | `shuttle` | `shuttle` (flies) or `base` (renders the board) |
| `shipName` | `Skippy` | Label shown on the base board |
| `channel` | `SkippyShuttleNet` | IGC channel — **must match** on ship and base |
| `runMode` | `CONTINUOUS` | `CONTINUOUS`, `ONETRIP`, `WAITFULL`, or `ONEWAY` |
| `remoteName` | *(blank)* | Blank = auto-find a Remote Control on the grid |
| `loadTag` | `[SHUTTLE:LOAD]` | Sorters whose name **contains** this tag load cargo at home |
| `unloadTag` | `[SHUTTLE:UNLOAD]` | Sorters whose name **contains** this tag unload at the destination |
| `lcdTag` | `[SHUTTLE]` | LCDs whose name contains this tag show status |
| `cruiseSpeed` | `100` | Cruise speed cap (m/s); the controller stays at or below this |
| `dockSpeed` | `5` | Final-approach speed cap (m/s, controller) |
| `maxMassKg` | `0` | `0` = no gate; otherwise stop loading near this mass |
| `departFill` | `95` | Cargo fill % that triggers departure |
| `unloadDrainSec` | `30` | Max seconds spent unloading before leaving |
| `segMeters` | `250` | Breadcrumb spacing on straight runs |
| `turnDegrees` | `12` | Extra breadcrumb when heading turns this much |
| `approachDist` | `15` | On-axis stand-off (m) where cruise hands off to the docking controller |
| `gyroRpmCap` | `0` | Gyro rate cap (RPM) for gentle rotation. `0` = auto (15 small grid / 5 large) |
| `brakeFrac` | `0.6` | Fraction of the weakest thrust axis used for braking/cornering (headroom for gravity + saturation). Lower = brakes earlier/gentler. Clamped 0.1–1.0 |
| `cornerLen` | `30` | Corner-rounding length (m); also the look-ahead blend distance into turns. Larger = wider, faster corners |
| `gyroGain` | `4` | Attitude P gain — how hard the gyros rotate toward the target heading. Higher = snappier turns |
| `gyroDamp` | `3` | Attitude damping. Raise it if a hull still wobbles/overshoots/jiggles onto heading; lower toward `2` for snappier turns |

The recorded route lives in a separate `[route]` section that the script writes for you.
**To clone a route to another identical ship, copy that whole `[route]` section into its PB.**

### Tagging sorters

The script finds its cargo sorters by tag, not by exact name. Any conveyor sorter whose name
**contains** `loadTag` is switched on while loading; any that contains `unloadTag` is switched
on while unloading. Matching is case-insensitive and the tag can appear anywhere in the name,
so `[SHUTTLE:LOAD] Bottom Feeder` and `Ore intake [shuttle:load]` are both picked up. You can
tag several sorters for the same role. Set both tags to whatever suits your fleet (e.g.
`[SKIPPY:LOAD]`). The script only toggles the sorters on and off — your filters and Drain-All
settings are left untouched.

## Teaching a route

1. Manually dock the ship at its **home** connector.
2. Run `RECORD HOME`. Fly straight out ~50 m, then continue to the destination by hand.
3. Manually dock at the **destination** connector.
4. Run `RECORD DEST`. The route is saved.

## Commands (run-argument on the ship PB)

| Command | Effect |
|---|---|
| `RECORD HOME` | Bind the docked connector as home; start recording the path |
| `RECORD DEST` | Bind the docked connector as destination; finish + save the route |
| `START` / `GO` | Begin operating per the run mode |
| `STOP` | Abort the flight, turn sorters off, return to Idle |
| `HOME` | Fly back to the home connector and dock |
| `MODE CONTINUOUS\|ONETRIP\|WAITFULL\|ONEWAY` | Change the run mode live |
| `RESUME` | Continue the saved state after a recompile |
| `CLEARROUTE` | Erase the recorded route |
| `UP` / `DOWN` | Move the LCD menu cursor (or change a value while editing) |
| `APPLY` | Select the highlighted item / save the value being edited |
| `BACK` | Leave a submenu / cancel an edit |

### LCD menu (bind to cockpit buttons)

Every command above is still usable as a run-argument, but day to day you drive the shuttle
from the **on-screen menu**. Bind four cockpit toolbar buttons to run the PB with the
arguments `UP`, `DOWN`, `APPLY`, and `BACK`. The ship's tagged LCDs (and the PB's own screen)
show a status header with a `>` cursor menu beneath it:

- **Main:** Start/Stop, Run Mode (APPLY cycles it), Go Home, and entries into the submenus.
- **Record:** Record Home connector, Record Dest connector, Clear Route.
- **Settings:** Cruise Speed, Dock Speed, Max Mass (tonnes), Depart Fill % — `APPLY` to edit,
  `UP`/`DOWN` to change, `APPLY` to save, `BACK` to cancel. Every saved value is written back
  to Custom Data, so it survives recompiles.

### Run modes

- **CONTINUOUS** — loops forever: load → fly → unload → return → repeat, until `STOP`.
- **ONETRIP** — one round trip on `START`/`GO`, then waits.
- **WAITFULL** — like continuous, but only departs once cargo reaches `departFill`%.
- **ONEWAY** — one leg per `START`, then **holds at the far end** instead of returning.
  Docked at home, it loads, flies to the station, unloads, and waits there. The next
  `START` flies it straight back home and waits again. It decides which way to go from
  **which end it's physically parked at** (by proximity to the two recorded docked poses),
  so it works even on a ship that mates both ends with the **same connector**, and always
  knows whether it's sitting at home or at the station — you never have to tell it. Good
  for "take this load over and stay put until I send you back."

## Base board

Set a base PB to `role = base` and the same `channel`. Tag base LCDs with `[SHUTTLE]`
(or your `lcdTag`). The board shows each shuttle's state, ETA, distance, cargo % and mass,
and flags **NO SIGNAL** if a shuttle drops off the network (e.g. beyond antenna range).

> Antenna range is 50 km; your run is 78 km. Place one relay antenna near the midpoint if
> you want an unbroken board — the shuttle still flies fine without signal; only the board
> blanks while out of range.

## Limitations (honest)

- Uses **absolute world coordinates**. Correct for static grids (base + station). Do not use
  it to dock with a grid that moves.
- Docking requires the ship to have **gyros and thrusters** with authority on every axis
  (including against gravity at a planet base). The controller reproduces the recorded docked
  attitude and drives straight down the connector axis; the connector magnet completes the
  mate. If it fails to seat, the approach times out after 45 s and the shuttle **faults**
  rather than grinding on the dock — widen `approachDist`, lower `dockSpeed`, or check that
  the recorded connector axis is clear of obstacles.
- Control gains are tuned conservatively but every ship is different. The attitude gains
  `gyroGain` (turn snappiness) and `gyroDamp` (wobble damping) are **live-tunable in Custom
  Data**. The flight controller now runs at 60 Hz while flying, so the heading holds steady and
  raising `gyroDamp` no longer makes turns sluggish — raise it only if a specific hull still
  hunts onto heading, or lower toward `2` for snappier turns. `VEL_GAIN` and `APPROACH_KP`
  remain script constants. Cruise behaviour is likewise tunable from Custom Data:
  `brakeFrac` (how early/gently it brakes), `cornerLen` (corner tightness), and `gyroRpmCap`
  (max rotation rate — lower it to calm big-angle turn overshoot).
- **The script controls your dampeners while flying.** To coast without fuel in space it turns
  dampeners **off** during undock/cruise/dock and restores them **on** when it stops, docks,
  faults, or is recompiled — so a parked or hand-flown ship always holds position. Gravity legs
  keep thrusting (hover compensation), so the ship never sags at the planet base. If you take
  manual control mid-flight, re-enable dampeners yourself (the ship's Z / dampener toggle).
- The controller obeys the world's speed cap (100 m/s by default). Setting `cruiseSpeed` above
  the world cap won't make the ship go faster — the game clamps it.
- Routes recorded by v0.1–0.2 stored position only; they still load, with orientation
  synthesised as a nose-first approach. Re-record them to capture true orientation.
- This is an in-game PB script; it cannot be unit-tested outside Space Engineers. Validation
  is in-world (see [roadmap.md](roadmap.md)).
