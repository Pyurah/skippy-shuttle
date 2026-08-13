# SkippyShuttle Roadmap

Master tracking document for the SkippyShuttle Programmable Block script.

## Current status

- **Version:** 0.9.0
- **Phase:** 1 (Core shuttle) + LCD UI + orientation-matched docking + per-connector departure
  triggers — delivered, pending in-world validation
- **Environment:** Space Engineers in-game Programmable Block (single-file C#, no external
  build/test tooling available)

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
