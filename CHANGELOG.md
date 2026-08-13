# Changelog

All notable changes to SkippyShuttle are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/); this project adheres to
[Semantic Versioning](https://semver.org/).

## [0.13.1] - 2026-08-13

### Added
- **`tools/build-min.py` deploy build.** Emits a comment-stripped `SkippyShuttle.min.cs`
  (~63.6 KB, ~36 % smaller) to paste into the Programmable Block, while `SkippyShuttle.cs`
  stays the fully-commented source of truth. Character-state aware (never strips a `//` or
  `/*` inside a string/char literal); verifies brace balance and the 100,000-char limit.

### Fixed
- **An idle PB no longer fights your dampener switch.** While parked in `Idle` (or
  `Faulted`), the ship loop called `ReleaseControl()` every tick, which re-asserted
  dampeners **on** ~6×/second — so a pilot hand-flying with the PB still running couldn't
  keep dampeners off. The controller now tracks whether *it* turned dampeners off and only
  restores those, so a parked/idle shuttle leaves your manual switch alone. A ship that just
  finished a flight still gets dampeners restored once (it can't drift), and a mid-flight
  recompile still does its one-time safety restore.

## [0.13.0] - 2026-08-13

### Added
- **Collinear route simplification.** The recorder now slides a straight-run breadcrumb
  *forward* onto the current segment instead of appending a new one, so a dead-straight leg
  collapses to just its two endpoints. Corners still keep their vertices (the tip lands well
  off the new chord, so it's preserved), so the `MAX_PATH` = 250 waypoint budget is spent on
  turns, not on spacing down a straight. New `simplifyMeters` key (default `15`) sets how far
  the path may bow from a straight chord before a waypoint is kept; `0` restores the old dense
  every-`segMeters` recording. This is the fix for the 78 km run overflowing the 250-waypoint
  cap — re-record the route to benefit.

### Changed
- The "path full" status message now reads `raise segMeters/simplifyMeters` (both levers now
  reduce waypoint count).

## [0.12.2] - 2026-08-13

### Changed
- **`menu` view drops its status header.** The `menu` view now renders only the
  interactive menu — the one-line `name state [RUN/STOP]` header is gone, since it
  duplicates the `status` view on a multi-screen cockpit. Pair a `menu` screen with a
  `status` screen for the full picture. The `full` view is unchanged (still header +
  menu).

## [0.12.1] - 2026-08-13

### Changed
- **`status` view layout.** The compact status screen now leads with a `-- Status --`
  header (matching the `-- Cargo --` block) and puts the state on its own line, with a
  blank line separating the two blocks — instead of the single inline `Status: <State>`
  row. Same data, cleaner two-block layout.

## [0.12.0] - 2026-08-13

### Added
- **`station` is now accepted as an alias for `role = base`.** Setting `role = station`
  used to fall through silently to `shuttle` (the block quietly ran as a flying shuttle
  instead of a board), because only the exact value `base` was recognized. Both `base`
  and `station` now select the board role.

### Changed
- **A base/station board writes a trimmed config.** On compile, the board role now
  keeps only the four keys it actually reads — `role`, `shipName`, `channel`, `lcdTag`
  — and drops the flight/cargo/fuel tuning keys, which a board ignores. A block first
  compiled as a shuttle and then switched to base no longer carries a wall of
  irrelevant settings in its Custom Data. Shuttle-role config is unchanged.

## [0.11.0] - 2026-08-13

### Added
- **Per-screen padding — a first-class, persistent option the auto-fit respects.** You
  can now pin the `TextPadding` (% inset per side) on any ship screen alongside its view
  and font, so a cramped cockpit surface can breathe without being reset on the next
  recompile. Two syntaxes, matching the existing view/size assignment:
  - **Name tag:** `[SHUTTLE:status:1.4:6]` — `:view:size:pad`. Pin only the pad while
    keeping auto-fit font with `[SHUTTLE:status::6]`.
  - **`[shuttle-screens]` Custom Data:** `2 = status@1.4/6` — `view@font/pad`. Use
    `status@/6` to set padding but leave the font on auto-fit.
  Padding is clamped 0–40 %. Omit it (as every pre-0.11.0 config does) and screens keep
  zero padding, exactly as before.

### Changed
- **The per-screen font auto-fit now subtracts padding from the usable area** before
  choosing a size, so padded screens still fit their content instead of overflowing. A
  screen with no padding is unaffected.

## [0.10.0] - 2026-08-13

### Added
- **Per-screen display views — split the crowded ship display across multiple screens.**
  Each screen can now show a *different* slice of the status instead of every screen
  carrying the whole header+menu. Four views:
  - **full** *(default)* — today's combined status header + interactive menu. Any screen
    not explicitly assigned stays exactly as it was, so nothing changes on upgrade.
  - **menu** — a one-line header (`name state [RUN/STOP]`) plus the interactive menu.
    Good for the cockpit screen you drive from.
  - **status** — a compact block: `Status: <State> [RUN|STOP]` / `-- Cargo --` /
    `<fill>%  <mass>t  <speed>m/s`. Fits a small screen without shrinking.
  - **trip** — route, current phase, ETA + distance while cruising, and the transient
    status line (delivered / holding at destination / holding at home / fuel-hold).
- **Two ways to assign a view to a screen:**
  - **Name tag** on a standalone LCD: `[SHUTTLE:trip]`, `[SHUTTLE:menu]`, etc. — an
    optional `:view` (and `:size`) after the base tag. A bare `[SHUTTLE]` is still `full`.
  - **`[shuttle-screens]` Custom Data section** on a cockpit / multi-surface block
    (opt-in), mapping each surface index to a view:
    ```
    [shuttle-screens]
    0 = menu
    1 = trip
    2 = status@1.4
    ```
    This is the 3-screen-cockpit case; being opt-in it never touches an unconfigured block.
- **Optional fixed font size per screen** — pin a size with `[SHUTTLE:status:1.4]` (name
  tag) or `2 = status@1.4` (Custom Data). Omit it to keep auto-fit.

### Changed
- **Each screen now sizes its font to its own content**, independently. The previous
  render applied a single shared font — the largest that fit the *most-constrained* panel —
  to every screen, so one small surface (e.g. a cockpit screen) shrank the big wall LCDs and
  every screen was forced to carry the full header+menu. That shared-font coupling is gone;
  a small `status` screen and a wide `full` LCD each pick their own best size.
- Condensed the top-of-file documentation comment to keep the script under Space
  Engineers' **100,000-character Programmable Block limit** (the view feature pushed it
  just over). The full key table and semantics live in [README.md](README.md); the
  in-script header is now a concise pointer to it. No code or behaviour change.

## [0.9.1] - 2026-08-13

### Fixed
- **"Depart Now" did nothing when the shuttle was parked idle at a dock** — it
  replied "not holding at a dock" even though it plainly was. The manual `DEPART`
  (ship button, run-arg, or station broadcast) only accepted the request while the
  ship was already mid-cycle in `Loading`/`Unloading`. Now, when it's Idle and
  docked with a route, Depart Now begins operating and dispatches the next leg
  immediately — the same direction `START` would pick (by which end it's physically
  parked at), with the manual override latched so it leaves without waiting on the
  end's trigger (the fuel/battery gate still applies). The rejection messages are
  also clearer: "already under way" while flying, "dock first" / "no route" otherwise.

## [0.9.0] - 2026-08-13

### Added
- **Per-connector departure triggers — a new setting, separate from the run mode.**
  Each end now decides for itself what releases the shuttle to the next leg, PAM-style,
  via `homeTrigger` and `destTrigger` (each `Auto` | `Cargo` | `Timer` | `Manual`):
  - **Auto** — leave as soon as the cargo op finishes (loaded at home / emptied at the
    destination, keeping the drain-timeout safety net). This is the previous behaviour,
    so an unconfigured shuttle runs exactly as before.
  - **Cargo** — wait for the hold to be genuinely full at home / empty at the destination.
  - **Timer** — run the sorters for `dwellSec`, then depart regardless of fill.
  - **Manual** — hold at the dock until a `DEPART` command arrives.
  Run mode (`CONTINUOUS` / `ONETRIP` / `ONEWAY`) now governs only the trip *cycle*.
- **`DEPART` command — manual release, locally or from the station.** On the ship it
  releases the current dock immediately (and overrides any trigger); it's also the
  Main-menu **Depart Now** item. On a base PB, `DEPART` broadcasts the request to all
  shuttles and `DEPART <shipName>` targets one, so a station can send a waiting shuttle
  on its way over IGC. The ship now registers an IGC listener for these commands
  (command messages are tagged `CMD|…` so they never collide with the status board).
- **Fuel / battery departure gate.** The shuttle won't leave a dock without enough
  hydrogen and charge to reach the next one. It measures what each leg actually costs
  (per direction, persisted across recompiles) and requires the current level to clear
  that estimate plus a margin, floored by hard minimums:
  - `minHydrogenPct` (default 10; ignored if the ship has no hydrogen tanks),
  - `minBatteryPct` (default 10; ignored if it has no batteries),
  - `fuelMarginPct` (default 25) — headroom on the measured per-leg estimate.
  When gated it holds at the dock with a "low H2/charge" status and departs once the
  level is met — it never faults.
- LCD **Depart** submenu (under Settings): cycle each end's trigger and edit
  `dwellSec` / `minHydrogenPct` / `minBatteryPct` / `fuelMarginPct` in place.

### Changed
- `RunMode` no longer includes `WaitFull`; departure conditions moved to the new
  triggers. **Back-compat:** a config that still says `runMode = WAITFULL` (or a live
  `MODE WAITFULL`) loads as `Continuous` + `homeTrigger = Cargo` — the same behaviour
  under the new model — and an explicit `homeTrigger` in Custom Data always wins.
- The Run Mode menu/`MODE` cycle is now Continuous → OneTrip → OneWay.

## [0.8.1] - 2026-08-13

### Fixed
- **OneWay ran the wrong leg when the ship docks both ends with the same connector.**
  `START` in OneWay while docked at home (and `HOME` while docked at home) sent the ship
  haywire — it undocked, flew sideways in a straight beeline to the far end holding the
  wrong (docked-at-destination) attitude, failed to seat, and eventually gave up and flew
  back. Continuous and OneTrip were unaffected. Root cause: the direction was chosen by
  connector **name** (`homeConn`/`destConn`), but a shuttle that mates both ends with the
  **same physical connector** has identical names at both ends, so the name test couldn't
  tell home from destination — and the OneWay branch happened to test the destination name
  first, so "docked at home" read as "docked at destination." The ship's end is now decided
  by **physical proximity to the two recorded docked poses** (distinct world coordinates),
  which is name-independent and correct whether the ship uses one shared connector or a
  separate connector per end. Also fixes the earlier "Go Home from home beelines" report,
  which was the same collision. Purely dispatch logic — no route/config format change.
  Assumes the two docked poses are separated by more than a ship length (true for any real
  home/station pair).

## [0.8.0] - 2026-08-13

### Added
- **`ONEWAY` run mode.** Sends the shuttle a single leg to the *opposite* end and
  holds it there instead of automatically returning. Each `START` dispatches one
  hop: docked at home it loads and flies to the station, delivers, and waits;
  docked at the station it flies straight home and waits. The direction is decided
  by **which connector the ship is docked at**, read live, so it always knows which
  end it's parked on — a `START` at the station goes home (no re-unload), a `START`
  at home goes to the station. Survives a recompile: the connector status is the
  source of truth, and a mid-flight leg is restored from saved state as before.
  Selectable via `runMode = ONEWAY`, the `MODE ONEWAY` command, or the LCD menu's
  Run Mode cycle (Continuous → OneTrip → WaitFull → OneWay).

## [0.7.0] - 2026-08-13

### Fixed
- **Attitude wobble and the fuel it burned.** The ship hunted around its heading the whole
  cruise (and jiggled vertically on docking), overshooting and overcorrecting; the v0.6.0
  damping bump only traded the wobble for a sluggish ~10-second align. Root cause was the
  control loop rate, not the gains: the flight controller ran at `Update10` (~6 Hz), so it
  commanded a rotation rate, held it 167 ms, and overshot before looking again — no `gyroDamp`
  value can win that. The flight-control law now runs at **60 Hz** (`Update1`) while actively
  flying, which holds heading cleanly; a small attitude **deadband** rests the gyros once
  aligned instead of nudging forever. This is also the mechanism behind the fuel drain — every
  degree of hunt made the controller re-trim thrust, so thrusters pulsed continuously. Removing
  the hunt, plus the coast below, means a straight cruise leg in space burns effectively no fuel.

### Added
- **Zero-fuel coasting in space.** Holding a velocity in zero-g needs zero net force, so once
  aligned and up to speed the controller cuts thrust entirely rather than micro-trimming it
  every tick. To make that a true coast (no automatic braking to fight), the controller now
  **manages the ship's dampeners** — off while flying, restored on stop/dock/fault/recompile so
  the parked or hand-flown ship always holds position and is never left adrift. Gravity legs
  (planet-base undock/approach) always thrust, keeping the existing hover compensation, which is
  why running dampeners-off there is safe.

### Changed
- Flight control runs at **60 Hz while flying**, dropping back to 6 Hz when docked/idle; LCD
  rendering and the base broadcast are throttled to ~6–7 Hz so the fast loop stays cheap.
- All timers now use **real elapsed time** (`Runtime.TimeSinceLastRun`) instead of an assumed
  fixed tick, so phase/approach/unload/watchdog timing stays correct at either loop rate.
- Default `gyroDamp` `4 → 3` and `gyroGain` field aligned to its documented `4` (was `5`). With
  the fast loop, raising `gyroDamp` no longer trades away slew speed — raise it only if a
  specific hull still hunts, lower toward `2` for snappier turns.

## [0.6.0] - 2026-08-13

### Fixed
- **New config keys now appear on upgrade without wiping the route.** Custom Data was only
  ever written when it was completely empty, so recompiling an already-configured PB never
  surfaced keys added by a newer version (they silently used defaults and couldn't be tuned).
  The script now backfills any missing `[shuttle]` keys on compile, seeding them with the
  value in effect, while preserving the recorded `[route]` and `[state]` sections.
- **Attitude oscillation (wobble on cruise, up/down jiggle on landing).** The gyro PD loop
  was underdamped for heavier / lower-gyro-authority hulls, so the ship overshot its target
  heading, overcorrected, and rang before settling — visible as a constant left-right wiggle
  through the whole cruise and a vertical jiggle during the docking approach (both use the
  same `AlignTo` controller). The default damping is now doubled (`GYRO_DAMP` 2 → `gyroDamp`
  4), which moves the loop from underdamped to well-damped across a wide range of ship
  weights. Turns are marginally less snappy but no longer oscillate.

### Added
- `gyroGain` config key (default `4`) — attitude P gain (turn snappiness), now live-tunable.
- `gyroDamp` config key (default `4`) — attitude damping. Raise it if a specific hull still
  wobbles/overshoots/jiggles; lower it if turns feel sluggish. Previously a hard-coded constant
  that required editing the script and recompiling to change.

### Changed
- The attitude gains that were the script constants `GYRO_GAIN` / `GYRO_DAMP` are now the
  Custom Data keys `gyroGain` / `gyroDamp`. Tuning per hull no longer needs a recompile — this
  is what the README already advised doing, now actually possible from Custom Data.

## [0.5.0] - 2026-08-12

### Added
- **Custom PAM-style cruise controller.** The entire cruise leg is now flown by the
  ship's own gyro + thruster controller instead of being handed to the stock Remote
  Control autopilot. It builds a flight-ordered waypoint list per leg and drives it with:
  - a **per-waypoint velocity profile** (a backward pass that guarantees the ship can
    always brake into the next point) — slow into corners, full `cruiseSpeed` on straights;
  - a **√(2·a·d) braking curve** computed from the ship's *real* available thrust and live
    mass, so it eases into every waypoint and the docking stand-off;
  - **corner speed limits** from the deflection angle at each waypoint
    (`R = cornerLen / tan(θ/2)`, `v = √(cruiseAccel·R)`);
  - **misalignment speed blending** — speed is cut while the nose is still swinging onto
    heading (turn-first) and when velocity points sideways to the path (no fast drift);
  - **plane-projection waypoint advance**, which commits to the next waypoint on a
    high-speed fly-by instead of orbiting one it never quite touched (the stock-autopilot
    circling), plus a stuck-watchdog that faults after 60 s without progress.
- `gyroRpmCap` config key (default `0` = auto: 15 RPM small grid / 5 RPM large). Caps the
  gyro angular-rate command for gentle rotation; shared with the docking controller.
- `brakeFrac` config key (default `0.6`). Fraction of the weakest thrust axis reserved for
  braking/cornering — headroom against gravity and thrust saturation. Clamped to 0.1–1.0.
- `cornerLen` config key (default `30` m). Corner-rounding length and the look-ahead blend
  distance the controller uses to ease into turns.

### Changed
- **Cruise no longer uses the stock autopilot.** This removes the weaving, circling, and
  sideways sliding between waypoints that the autopilot produced — the behaviour contrasted
  against PAM. The whole flight (undock → cruise → dock) now runs on one controller, so the
  cruise flies as smoothly as the docking approach already did.
- `AlignTo` now takes an optional angular-rate cap; the docking approach uses the same cap
  (a maximum rate never hurts precision near the target).
- ETA / remaining-distance is now summed over the controller's own waypoint list rather than
  read from stock-autopilot waypoints.

### Removed
- `collisionAvoid` config key — meaningless now that the stock autopilot is gone. The route
  is taught by flying it clear, and the controller follows it in a straight line by design.

## [0.4.0] - 2026-08-12

### Added
- `collisionAvoid` config key (default **false**). The stock autopilot's collision
  avoidance is a major source of weaving/circling during cruise; since the route was
  taught by flying it clear, straight-line following is smoother. Set it back to `true`
  if the ship clips terrain on the planetary leg.

### Changed
- **Attitude controller is now PD instead of P-only.** `AlignTo` subtracts the ship's
  actual angular velocity (damping term) from the proportional response, so the ship
  settles onto the target attitude instead of overshooting and wobbling. This smooths
  every phase that uses the custom controller — final docking and undock back-off no
  longer "spin around a bit" before behaving. New `GYRO_DAMP` tuning constant.
- **Graceful undock hand-off.** After backing clear of the dock, the ship now rotates
  in place to face the first cruise waypoint *before* the autopilot engages, instead of
  handing over while still in its docked attitude (which made the autopilot spin the
  ship and slide sideways to the first waypoint).

## [0.3.3] - 2026-08-12

### Fixed
- Ship LCDs now render at **one shared font size** across every tagged panel, and the
  text always fits. A long transient status line (e.g. `Route saved: <home> -> <dest>
  (N waypoints)` after `RECORD DEST`) no longer shrinks the whole display: text is
  word-wrapped to a fixed column budget first, so no single line can blow out the
  auto-fit. The shared size is the largest that fits the most-constrained tagged LCD.

### Changed
- The PB's own screen is written but no longer participates in sizing, so the small
  built-in surface can't shrink the wall LCDs. Header uses single spacing and the
  cargo line rounds mass/speed to whole units to stay inside the wrap width.

## [0.3.2] - 2026-08-12

### Changed
- Ship LCD now **auto-fits the font** to each panel: the largest size that fits the
  panel in both dimensions is computed per render, so the same status reads well on
  a tiny PB screen, a square LCD, or a wide panel — no more fixed size that's either
  clipped or too small.
- Condensed the ship display so a large font fits without running off the edge:
  shorter one-line header (`Skippy  Cruise >  [RUN]`), compact `Cargo/mass/speed`
  line, `Route Nwp` summary (dropped the long connector names), abbreviated menu
  labels (`Mode:`, `Cruise:`, `Dock:`, `MaxMass:`, `Fill:`, `Record Home/Dest`), and
  a terse control footer. Removed the extra blank lines.

## [0.3.1] - 2026-08-12

### Fixed
- Ship LCD no longer clips its lower menu items. Status surfaces are now set to a
  monospaced font at a smaller size (with padding) when discovered, so the full
  status header and menu fit on a standard wall LCD without being cut off.

### Changed
- Removed the redundant `== SkippyShuttle vX ==` title line from the ship display
  to reclaim vertical space; the ship name and state remain on the first line.

### Removed
- Dead `signalTimer` field (base-role staleness is tracked per shuttle via
  `ShuttleReport.Age`), clearing an unused-field compiler warning.

## [0.3.0] - 2026-08-12

### Added
- **Orientation-matched docking.** `RECORD HOME`/`RECORD DEST` now capture the full docked
  pose — Remote Control position *and* facing, plus the bound connector's mating axis — not
  just a position. The shuttle reproduces the exact attitude it was recorded in, so docking
  works for a connector facing **any** direction, not only a nose-mounted one.
- **Ship-agnostic docking controller.** A gyro attitude controller (cross-product alignment)
  plus a thruster translation controller drive the final approach. The stock autopilot still
  flies the long cruise; the controller takes over only for the last `approachDist` metres to
  align and mate. Works on any ship that has gyros and thrusters — a recorded or shared route
  flies correctly on a different hull.
- On-axis stand-off hand-off: cruise now targets a point `approachDist` metres out along the
  connector's mating axis (new `approachDist` config key, default 15 m) instead of flying the
  autopilot all the way onto the dock.
- Powered, orientation-holding undock: the ship backs straight out along the connector axis
  under thruster control before cruising, replacing the fixed settle timer.
- `ReleaseControl` safety: thruster/gyro overrides are cleared on `STOP`, on fault, while
  idle, before every cruise, and on recompile — the autopilot and pilot always regain control.

### Changed
- The shareable `[route]` section now stores each end's orientation (`homeFwd`/`homeUp`/
  `homeConnFwd` and the `dest*` equivalents) alongside position. Routes recorded by v0.1–0.2
  (position only) still load — their orientation is synthesised from the path geometry as a
  nose-first approach, matching what those versions actually supported.
- **Sorters are now found by tag, not exact name.** New `loadTag` (`[SHUTTLE:LOAD]`) and
  `unloadTag` (`[SHUTTLE:UNLOAD]`) config keys replace `loadSorter`/`unloadSorter`. Any conveyor
  sorter whose name *contains* the tag (case-insensitive, anywhere in the name) is controlled,
  and multiple sorters per role are supported — so you can name sorters anything and tag a whole
  bank at once. The old `loadSorter`/`unloadSorter` keys still load as fallback tags (a full
  name matches itself as a substring), so existing configs keep working.

## [0.2.0] - 2026-08-12

### Added
- **Interactive LCD menu** (PAM-style) for the ship role. A `>` cursor navigates a menu
  rendered on the ship's LCDs and PB screen, driven by four run-arguments you bind to
  cockpit toolbar buttons: `UP`, `DOWN`, `APPLY`, `BACK`.
- Menu pages: **Main** (Start/Stop, cycle Run Mode, Go Home, and entries into submenus),
  **Record** (Record Home / Record Dest / Clear Route), and **Settings** (edit Cruise Speed,
  Dock Speed, Max Mass in tonnes, and Depart Fill % in place).
- In-place value editing: `APPLY` on a setting enters edit mode; `UP`/`DOWN` step the value;
  `APPLY` saves it to Custom Data; `BACK` cancels. Values are range-clamped on save.
- Ship display reworked to show a live status header (state, route, cargo, mass, speed, ETA)
  above the interactive menu on one screen.

## [0.1.0] - 2026-08-12

### Added
- Initial release of SkippyShuttle: an autonomous two-connector delivery shuttle for
  Space Engineers, replacing PAM for pure ferry duty.
- Unified single-file script with `shuttle`/`base` role selection via Custom Data.
- PAM-style route teaching: `RECORD HOME` and `RECORD DEST` bind the docked connector at
  each end and capture an adaptive breadcrumb flight path (distance + turn-angle based).
- Shareable, recompile-persistent route stored in Custom Data under `[route]`; copy the
  section to clone a route across a fleet.
- Stock Remote Control autopilot flight with per-phase speed limits (cruise vs. precision
  docking), eliminating the target-speed oscillation seen in PAM at high caps.
- Automatic connector connect/disconnect with a 45 s approach timeout that faults safely
  instead of grinding on the dock.
- Cargo handling: toggles configured load/unload sorters, a mass gate (`maxMassKg`), and a
  fill-based departure threshold (`departFill`) — fixes the overweight/cram-everywhere issue.
- Three run modes — `CONTINUOUS`, `ONETRIP`, `WAITFULL` — settable via config or `MODE`.
- Ship LCD status with live ETA (remaining waypoint distance ÷ current speed).
- IGC broadcast of state/ETA/distance/cargo/mass to a base board, with a 20 s NO-SIGNAL
  timeout for shuttles that drop off the network beyond antenna range.
- `START`, `STOP`, `HOME`, `RESUME`, and `CLEARROUTE` commands.
- README with setup, command reference, run-mode semantics, and honest limitations.
- Roadmap tracking Phase 1 delivery and in-world validation checklist.
