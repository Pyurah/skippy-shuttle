# SkippyShuttle Roadmap

Master tracking document for the SkippyShuttle Programmable Block script.

## Current status

- **Version:** 0.13.6
- **Phase:** 1 (Core shuttle) + LCD UI + orientation-matched docking + per-connector departure
  triggers + per-screen display views (+ per-screen padding) + base-role config hygiene — delivered,
  pending in-world validation
- **Environment:** Space Engineers in-game Programmable Block (single-file C#, no external
  build/test tooling available)

---

## Size budget & the 100,000-character limit

A PB script cannot exceed **100,000 source characters**, counting comments and whitespace.
The commented source sits at ~97 KB, but that's not the real ceiling — two structural facts
give the remaining roadmap far more runway than the raw number implies:

1. **Comments don't have to ship.** ~35 KB of the file is comments and blank lines. Stripped,
   the script that actually goes in the PB is ~63.6 KB, leaving **~36 KB of real headroom**.
   **Delivered (v0.13.1):** `tools/build-min.py` emits a comment-stripped `SkippyShuttle.min.cs`
   to paste into the PB, while `SkippyShuttle.cs` stays the fully-commented source of truth.
   Character-state aware (never strips a `//`/`/*` inside a literal); checks brace balance and
   the 100,000-char limit. *Tradeoff:* in-game compile errors then report line numbers against
   the minified file, not the source — a minor debugging cost on a script that already compiles
   clean.
2. **Station/controller work lives in a separate script.** Per Phase 2.0, the control tower is
   its own deliverable (`SkippyTower.cs`), so Phase 2 and most of Phase 3 consume **none** of
   the shuttle's budget. Only genuinely ship-side features (Phase 2c — multi-stop routes,
   per-item manifests) compete for the shuttle file's space — and they do so against the
   post-strip ~37 KB, not the pre-strip ~2.8 KB.

Net: the remaining roadmap fits. The shuttle file's practical budget is the stripped size, and
the tower's features don't touch it at all. Revisit this section if a single ship-side feature
ever threatens the stripped ceiling.

---

## Phase 1 — Core shuttle ✅ delivered

| Deliverable | Status | Notes |
|---|---|---|
| Unified ship/base script with role detection | ✅ | `role` key in Custom Data |
| PAM-style route teaching (`RECORD HOME` / `RECORD DEST`) | ✅ | Binds docked connector per end |
| Adaptive breadcrumb path recording | ✅ | Distance + turn-angle thresholds, `MAX_PATH` cap |
| Shareable route persistence (Custom Data `[route]`) | ✅ | Position + orientation; copy section to clone across fleet |
| State resume across recompile (Custom Data `[state]`) | ✅ | `RESUME` command re-arms autopilot |
| Custom trajectory cruise controller | ✅ | Gyro + thruster; velocity profile + √(2ad) braking; replaced stock autopilot (v0.5.0) |
| Orientation-matched docking controller | ✅ | Gyro align + thruster translate; any ship, any connector facing |
| Connector auto-connect / disconnect | ✅ | Connectable → `Connect()`; timeout → Faulted |
| Load/unload sorter control | ✅ | Toggles Enabled only; filters left untouched |
| Mass gate + fill-based departure | ✅ | `maxMassKg`, `departFill` |
| Four run modes (Continuous / OneTrip / OneWay) | ✅ | Config key + `MODE` command; OneWay added v0.8.0. WaitFull folded into a departure trigger (v0.9.0) |
| Per-connector departure triggers (Auto/Cargo/Timer/Manual) | ✅ | `homeTrigger`/`destTrigger`; separate from run mode; `DEPART` override (v0.9.0) |
| Ship LCD status + ETA | ✅ | ETA from remaining waypoint distance / speed |
| IGC broadcast + base board with NO-SIGNAL handling | ✅ | Pipe-delimited report, 20 s stale timeout |

## Phase 1 — validation (in progress)

These require an in-world test on the Earth→station run with Skippy:

- [ ] Record a full route and confirm `[route]` writes orientation and reloads after recompile.
- [ ] Confirm home undock → cruise → destination dock cycle completes hands-off.
- [ ] Confirm the cruise controller flies straight (no weaving/circling), turns before
      accelerating on departure, and slows through a deliberate dogleg then speeds up on the straight.
- [ ] Confirm the docking controller mates a **non-nose** connector (top/side) at the correct
      attitude, on Skippy's different-facing connectors.
- [ ] Confirm gyro response settles (does not oscillate/spin up); flip a gain sign if it does.
- [ ] Confirm the ship holds attitude and station-keeps against gravity at the planet base.
- [ ] Confirm mass gate stops loading before overweight.
- [ ] Confirm base board shows ETA and flips to NO SIGNAL past 50 km (until a relay is added).
- [ ] Tune `dockSpeed` / `approachDist` for reliable connector mating at both ends.
- [ ] Clone the `[route]` section to a differently-shaped ship and confirm it docks correctly.

## Phase 1.5 — LCD menu UI (delivered, v0.2.0)

- [x] PAM-style on-screen menu with `>` cursor on tagged LCDs + PB screen.
- [x] `UP` / `DOWN` / `APPLY` / `BACK` navigation bound to cockpit toolbar buttons.
- [x] Main / Record / Settings pages; Start/Stop, Run Mode cycle, Go Home, record actions.
- [x] In-place value editing (Cruise/Dock speed, Max Mass, Depart Fill) with clamp + persist.
- [x] Status header (state/route/cargo/mass/ETA) rendered above the menu.

## Phase 1.6 — PAM-style cruise controller ✅ delivered (v0.5.0)

Replaced the stock Remote Control autopilot on the cruise leg (source of the weaving,
circling, and sideways sliding) with the ship's own gyro + thruster controller, so the whole
route flies as smoothly as the docking approach.

- [x] Flight-ordered per-leg waypoint list built from the recorded route (drops stand-off crumbs).
- [x] Per-waypoint velocity profile with a backward braking pass (always able to stop into the next point).
- [x] √(2·a·d) braking curve from real available thrust + live mass (recomputed each leg for loaded vs empty).
- [x] Corner speed limits from deflection angle (`R = cornerLen/tan(θ/2)`, `v = √(cruiseAccel·R)`).
- [x] Misalignment speed blending (turn-first; no fast sideways drift) + plane-projection waypoint advance.
- [x] Gyro RPM cap (`gyroRpmCap`, auto 15/5) shared with docking; stuck-watchdog → Faulted after 60 s.
- [x] Live tuning via Custom Data: `gyroRpmCap`, `brakeFrac`, `cornerLen` (no recompile).
- [ ] Field-tune `brakeFrac` / `cornerLen` / `gyroRpmCap` per hull on the Earth→station run.

## Phase 1.7 — smooth attitude + fuel economy ✅ delivered (v0.7.0)

Fixed the continuous attitude wobble/overcorrection (same failure PAM had in space) and the
fuel drain it caused. Root cause was the 6 Hz control loop, not the gains — so no `gyroDamp`
value could settle it.

- [x] Flight-control law runs at **60 Hz** (`Update1`) while flying, 6 Hz when docked/idle.
- [x] Timers use **real elapsed time** (`Runtime.TimeSinceLastRun`), correct at either rate.
- [x] LCD render + base broadcast throttled to ~6–7 Hz so the fast loop stays cheap.
- [x] Attitude **deadband** rests the gyros once aligned (no perpetual micro-hunt).
- [x] **Zero-fuel coast in space**: thrust cut once aligned + up to speed; controller manages
      dampeners (off flying, restored on stop/dock/fault/recompile). Gravity legs keep hover thrust.
- [x] Retuned defaults: `gyroDamp` 4→3, `gyroGain` field aligned to 4.
- [ ] Field-confirm on the Earth→station run: steady heading on a straight, and **fuel flat while coasting**.

## Phase 1.8 — one-way ferry mode ✅ delivered (v0.8.0)

Added `ONEWAY` for "take this over and stay put." Runs a single leg to the opposite
end and holds there; the next `START` sends it back. Direction is derived from the
docked connector (live), so it always knows which end it's sitting at across restarts.

- [x] `OneWay` added to `RunMode` (enum, `MODE` command, config load/save, LCD cycle).
- [x] `START` dispatch is direction-aware in OneWay: at dest → depart home (no re-unload);
      at home → load + fly to dest; mid-route → continue outbound.
- [x] Holds (stops, `operating=false`) after delivering at the station **and** after
      arriving home, instead of auto-cycling.
- [x] **Direction is decided by physical proximity to the recorded docked poses, not the
      connector name (v0.8.1)** — a shuttle that docks both ends with the *same* connector
      no longer mis-reads which end it's parked at (was sending OneWay the wrong way).
- [ ] Field-confirm: dispatch home→station holds at the station; a second `START` returns
      home and holds; mode survives a recompile mid-hold.

## Phase 1.9 — per-connector departure triggers ✅ delivered (v0.9.0)

Split the departure *condition* out of `RunMode` into its own per-end setting, PAM-style, plus a
manual `DEPART` (local + remote) and a fuel/battery gate.

- [x] `DepartTrigger { Auto, Cargo, Timer, Manual }`; `homeTrigger` / `destTrigger` fields + config keys.
- [x] `DepartureAllowed(atHome, cargoReady)` gates Loading→UndockHome and Unloading→UndockDest
      (dwell via `phaseTimer`, `departRequested` override) — no new enum states.
- [x] `DEPART [shipName]` run-arg + Main-menu "Depart Now"; ship registers an IGC listener and
      drains `CMD|DEPART|…` messages; base role broadcasts `DEPART` to the channel.
- [x] Fuel/battery gate: `minHydrogenPct`/`minBatteryPct` floors + adaptive per-direction estimate
      (measured, persisted in `[state]`, required with `fuelMarginPct` margin). Hold + status, no fault.
- [x] Discover `IMyGasTank` (hydrogen only, by subtype) + `IMyBatteryBlock`; skip a check if none.
- [x] LCD **Depart** page (cycle triggers; edit dwell/floors/margin); config load/save/backfill.
- [x] **Back-compat:** `runMode = WAITFULL` (config or `MODE`) loads as `Continuous` + `homeTrigger = Cargo`.
- [ ] Field-confirm: each end honors its trigger; Manual holds until `DEPART` (ship + base both release);
      Timer dwells; Cargo waits full/empty; Auto unchanged; fuel gate holds then departs when met.

## Phase 1.10 — per-screen display views ✅ delivered (v0.10.0)

The ship display was too crowded and resized badly: `RenderShip()` wrote one combined
header+menu blob to every screen, and a single shared font (largest that fit the *most-
constrained* panel) dragged the big wall LCDs down to a tiny screen's size. Split the
information across screens, each sized to its own content.

- [x] Four views — `full` (default, unchanged), `menu`, `status` (compact cargo block),
      `trip` (route/phase/ETA + transient status line). `RenderShip()` refactored into
      composable text builders (`BuildHeader`/`BuildMenu`/`BuildView`).
- [x] Per-screen view assignment: name tag `[SHUTTLE:view]` on standalone LCDs (bare
      `[SHUTTLE]` = `full`, back-compat), and an opt-in `[shuttle-screens]` Custom Data
      section (`index = view@size`) on cockpit / multi-surface providers — the 3-screen case.
- [x] **Each screen sizes its own font independently** (`SizeAndWrite`), replacing the
      shared most-constrained-panel font — the fix for both the shrink and the clutter.
- [x] Optional fixed size per screen (`[SHUTTLE:status:1.4]` / `2 = status@1.4`); omit for auto-fit.
- [ ] Field-confirm: existing `[SHUTTLE]` LCD + PB screen still show full view; cockpit
      `0=menu / 1=trip / 2=status` splits correctly; small screen no longer shrinks the wall LCD.

> Cockpit only this round. The base/station board (`RunBase()`) is unchanged; a station
> "marquee" arrival-time view is deferred (the `trip` view is the groundwork for it).

## Phase 1.11 — per-screen padding ✅ delivered (v0.11.0)

Manual `TextPadding` set in the terminal both got reset on the next recompile (`PrepSurface`
rewrites it) and broke the auto-fit (text overflowed, since the fit math didn't know about the
inset). Made padding a first-class, persistent per-screen option that the auto-fit respects.

- [x] `Pad` added to `ScreenTarget`; threaded through both `Discover()` loops, the PB fallback,
      and `AddScreen`.
- [x] Assignment syntax extended: name tag `[SHUTTLE:view:size:pad]` (`ParseScreenTag`) and
      `[shuttle-screens]` value `view@font/pad` (`ParseViewSpec`); both back-compatible (omit → 0).
- [x] `SizeAndWrite` sets `TextPadding` (clamped 0–40 %) and **subtracts padding from the usable
      area** before auto-fitting, so padded text still fits its surface.
- [ ] Field-confirm: a padded cockpit screen shows the inset and keeps a readable auto-fit font;
      padding survives a recompile; unpadded screens are unchanged.

## Phase 1.12 — base-role config hygiene ✅ delivered (v0.12.0)

`role = station` silently fell through to the shuttle role (only the exact value `base` was
matched), so a board set up with the natural word quietly ran as a flying shuttle. And a block
switched from shuttle to base kept the full wall of flight/cargo keys it never reads.

- [x] `station` accepted as an alias for `base` in `LoadConfig` (both select the board role).
- [x] `TrimBaseConfig` / `WriteBaseSection`: the base role rewrites `[shuttle]` with only the four
      keys it uses (`role`, `shipName`, `channel`, `lcdTag`) and normalizes the role to `base`.
- [ ] Field-confirm: `role = station` renders the board; a shuttle-then-base block sheds its extra
      keys on the next recompile.

## Phase 2.0 — split the station controller into its own script (structural decision)

Phase 2 turns the passive base board into an active **control tower** (pad registry, clearance
protocol, multi-ship scheduling, per-pad UI). That is several KB of code a shuttle never
executes, and the shuttle file has only ~2.8 KB of headroom under the **100,000-character PB
limit**. So Phase 2 is where the single-file design ends: the controller gets its **own
deliverable** rather than growing the shared file past the ceiling.

**Two deliverables after the split:**

- `SkippyShuttle.cs` — the ship (flight, cargo, docking, ship LCD views). Unchanged in spirit.
- `SkippyTower.cs` *(new)* — the station controller. The `controller` role is its only role;
  the per-view **station "marquee"** (the deferred item from Phase 1.10) belongs here, where
  there's room to do it properly instead of bolting views onto `RunBase()`.

**The load-bearing constraint — a shared IGC contract, not shared code.** The two scripts share
almost no logic; what they *must* keep compatible is the wire protocol:

- same IGC channel (`SkippyShuttleNet`) and the pipe-delimited report/command grammar, so an
  existing shuttle keeps talking to a new tower and vice versa;
- the tower is a **superset** listener + new `PAD|…` / `CLEAR|…` messages the shuttle learns to
  answer — additive, so old shuttles degrade gracefully rather than breaking.

Config idioms (`MyIni`, `[shuttle]`/`[route]` sections) stay shared by copy-paste convention,
not by shared source.

**Decision on the current base/station code (2026-08-13):** *keep it in the shuttle file for
now.* It's small, self-contained (`RunBase()` + role parse + `TrimBaseConfig`/`WriteBaseSection`),
working, and in active use as a fleet board — and it is **not** what threatens the char limit.
Stripping it ahead of a tower that isn't scheduled would be a pure regression. Whether the
lightweight board **survives as a low-effort option** in the shuttle file or is **superseded** by
the tower is deferred to the split itself — that call needs to see how much the tower actually
does, so it's made at Phase 2, not blind now.

## Phase 2 — Air-traffic control & connector discovery (recommended next)

Enables stations to publish landing pads and coordinate arrivals. The docking controller
below already reproduces an arbitrary connector attitude, so a broadcast pad only needs to
carry the pad's world pose for the ship to fly it — no per-ship geometry assumptions.

- [ ] New `controller` role: a station PB declares connectors as shuttle pads.
- [ ] Controller advertises pads over IGC: `{station, pad, worldPos, fwd, up, connFwd, free}`.
- [ ] Ship collects in-range pad ads into a live list (staleness timeout).
- [ ] Menu **Select Destination** page lists in-range pads; APPLY sets the active destination.
- [ ] Clearance protocol: ship requests a pad; controller grants a free one or replies HOLD;
      ship enters a `Holding` state until cleared.
- [ ] Ship builds an approach pose from the broadcast pad and docks with the existing controller.
- [ ] Controller tracks pad occupancy so two shuttles never target one pad.

> Range note: base↔station is 78 km (> 50 km antenna limit), so the live pad list only shows
> the far station once a relay bridges the gap. Taught routes remain range-independent.

## Phase 2b — Orientation-matched docking ✅ delivered (v0.3.0)

Delivered into the core (was conditional). Docking is no longer nose-first-only.

- [x] Thrust + gyro final-approach controller matching an arbitrary connector's orientation.
- [x] Records the full docked pose (RC position + facing + connector mating axis) per end.
- [x] Cruise hands off at an on-axis stand-off (`approachDist`) computed from the connector axis.
- [x] Powered, orientation-holding back-off on undock (thruster nudge) replacing the settle delay.
- [x] Falls back to Faulted on timeout like the autopilot path; `ReleaseControl` safety on stop/fault/idle.
- [x] Field-tune gains per hull (in-world); expose gains in Custom Data. `gyroGain`/`gyroDamp`
      exposed as live Custom Data keys in v0.6.0 (fixed attitude oscillation on cruise + landing).

## Phase 2c — flight robustness (planned)

- [x] Low-battery / low-hydrogen guard: refuse departure until the level covers the next leg
      (adaptive per-direction estimate + hard floors, delivered v0.9.0). *Divert-home-if-stranded
      is not implemented — the shuttle holds at the dock rather than launching under-fuelled.*
- [x] **Collinear route simplification (v0.13.0)** — the recorder slides straight-run
      breadcrumbs forward instead of appending, so a straight leg collapses to its two
      endpoints and the `MAX_PATH` = 250 budget is spent on turns. New `simplifyMeters` key
      (default 15 m; 0 = off). Fixes the 78 km run overflowing the waypoint cap. *Field-confirm:
      re-record HOME→DEST and check the straight cruise uses only a handful of waypoints while
      the dock approaches keep full detail.*
- [x] **Progress-based cruise watchdog (v0.13.2)** — collinear simplification made a straight
      leg a single waypoint tens of km away, which false-tripped the old "60 s without a
      waypoint advance" stuck-watchdog and faulted the ship mid-cruise (restoring dampeners).
      The watchdog now resets on *closing distance* to the current waypoint, so a long straight
      flown correctly never faults while a genuine stall still does. *Field-confirm: a full
      simplified straight leg completes without a "Cruise stuck" fault.*
- [x] **Gyro rest deadband (v0.13.3)** — the attitude loop fed noisy `AngularVelocity` back
      through the damping term every frame, so strong gyros micro-jittered while holding a
      heading (even at `gyroRpmCap = 2`). Added `GYRO_REST_ATT` / `GYRO_REST_RATE`: on heading
      and not rotating → gyros held inert, re-engaging only on a real disturbance. *Field-confirm:
      no gyro chatter while coasting a straight; heading still holds.*
- [x] **Heading-only cruise throttle (v0.13.3)** — the forward-speed governor throttled on the
      combined heading+roll attitude error, so crossing into gravity (where "up" flips to
      anti-gravity, adding a standing roll error until level) capped cruise to ~30 m/s. Now
      throttles on heading error only. *(Necessary but not sufficient — see v0.13.4.)*
- [x] **Orthogonal cruise up-target (v0.13.4)** — the real cause of the ~30 m/s gravity cap. The
      attitude controller held Forward = pathDir *and* Up = −gravity, which are only jointly
      satisfiable when orthogonal; on a climbing/descending leg the gyro settled on a standing
      ~45° heading compromise (measured in-world: `aF0.15 hd45`), which the heading throttle
      floored to 0.15 → ~30 m/s. `upTarget` is now the component of anti-gravity perpendicular to
      the flight direction (Gram-Schmidt), so the gyro hits the heading exactly and the throttle
      opens to full. *Field-confirm: full cruise speed on the base→station climb, belly still
      level.*
- [x] **Cruise cap anti-chatter (v0.13.5)** — the velocity P-controller reverse-thrust at a hard
      speed cap (over → brake, under → accelerate at 60 Hz) caused the shaking/throttle pulsing
      felt while holding cruise. Now coasts through a small along-track overshoot
      (`CRUISE_COAST_BAND = 5 m/s`) instead of braking it; cross-track correction, gravity hover,
      and real corner/arrival braking are unaffected. *Field-confirm: steady throttle at the cap,
      no pulsing.*
- [x] **Non-finite thrust guard (v0.13.5)** — `ApplyForce` refuses to write a `NaN`/`Infinity`
      `ThrustOverride` (cuts thrust that tick), closing the one script-side vector that could
      destabilise/crash a server. Defensive: no known upstream produces one, but the old
      per-thruster skip guard is `false` for `NaN`.
- [x] **Cruise vertical-jitter deadband (v0.13.6)** — after the along-track coast fixed the
      speed-cap pulsing, a vertical up/down shake remained in low gravity. The cruise vertical
      force is hover (`−grav·mass`) plus a velocity correction; at `g ≈ 0.05` the hover bias is
      so small that a ~0.25 m/s velocity error flips the net vertical force sign and swaps
      thruster banks at 60 Hz. Added `VEL_DEADBAND = 0.4 m/s`: sub-threshold velocity error is
      left uncorrected (hover always kept), so the ship rides through the noise; path position
      still self-corrects via the target-pointing desired velocity. *Field-confirm: steady
      vertical hold during high-altitude cruise.*
- [ ] Multi-stop routes (more than two connectors).
- [ ] Per-item load manifests (fill specific items to target amounts).

## Phase 3 — fleet (planned)

- [ ] Base-side dispatch: assign routes to named ships over IGC.
- [ ] Collision-slot scheduling so multiple shuttles don't converge on one connector.
- [ ] Base board sorting/priority and per-ship alert thresholds.

## Known gaps / risks

- Both cruise and docking use a custom gyro + thruster controller; geometry- and mass-sensitive.
  The flight loop runs at 60 Hz so the heading holds steady, and attitude gains (`gyroGain`/
  `gyroDamp`) stay live-tunable from Custom Data if a hull still hunts; fault-on-timeout (cruise
  watchdog + docking timeout) prevents damage. `brakeFrac`/`cornerLen`/`gyroRpmCap` still want
  in-world tuning per hull.
- The controller turns dampeners OFF while flying (to coast fuel-free in space) and restores
  them on stop/dock/fault/recompile. If the PB is disabled mid-flight, re-enable dampeners
  manually — the ship would otherwise drift until then. Gravity legs keep hover thrust, so no
  sag at the planet base.
- The controller obeys the world speed cap (100 m/s default); `cruiseSpeed` above it is clamped.
- Absolute coordinates assume static grids only.
- No automated test harness is possible for PB scripts; all validation is in-world.
