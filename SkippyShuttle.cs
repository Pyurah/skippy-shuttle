/*//////////////////////////////////////////////////////////////////////////////
 * SkippyShuttle - Autonomous two-connector delivery shuttle for Space Engineers
 * ------------------------------------------------------------------------------
 * A purpose-built replacement for PAM when all you need is a delivery shuttle
 * that ferries cargo between two docking points (e.g. a planet base and an
 * orbital station).
 *
 * ONE script, TWO roles - paste the SAME code into both Programmable Blocks:
 *   - The SHIP PB   (role = shuttle) flies the route and manages cargo.
 *   - The BASE PB   (role = base)    listens on the channel and renders the
 *                                    status + ETA of every shuttle to LCDs.
 * The role is read from the PB's Custom Data (see the [shuttle] section below).
 *
 * ------------------------------------------------------------------------------
 * QUICK START (ship)
 *   1. Put this script in the ship's Programmable Block. Recompile once.
 *      It writes a default config into Custom Data - edit it, recompile again.
 *   2. Manually dock the ship at its HOME connector, then run:  RECORD HOME
 *   3. Fly the ship by hand to the destination. The script records the path.
 *   4. Manually dock at the DESTINATION connector, then run:    RECORD DEST
 *      (The route is now saved to Custom Data under [route] - copy that whole
 *       section to any other identical ship to share the route.)
 *   5. Run:  START     (behaviour depends on the configured run mode)
 *
 * COMMANDS (run-argument on the ship PB)
 *   RECORD HOME   Bind the currently-docked connector as the home point and
 *                 begin recording the outbound path.
 *   RECORD DEST   Bind the currently-docked connector as the destination and
 *                 finish recording the path. Route is saved.
 *   START / GO    Begin operating (loads, flies, unloads per the run mode).
 *   STOP          Abort autopilot and return to Idle (stays docked/where it is).
 *   HOME          Fly back to the home connector and dock.
 *   DEPART        Release the shuttle from the dock NOW (manual trigger / override).
 *                 On a base PB it broadcasts the request: DEPART (all shuttles) or
 *                 DEPART <shipName> (one), so a station can send the ship on its way.
 *   MODE CONTINUOUS | ONETRIP | ONEWAY   Change the run mode live.
 *   RESUME        Continue the saved state after a recompile.
 *   CLEARROUTE    Erase the recorded route.
 *   UP / DOWN     Move the LCD menu cursor (bind to cockpit toolbar buttons).
 *   APPLY         Select the highlighted menu item / save a value being edited.
 *   BACK          Leave a submenu / cancel a value edit.
 *
 * CUSTOM DATA (auto-generated template, all keys optional except role)
 *   [shuttle]
 *   role         = shuttle            ; shuttle | base
 *   shipName     = Skippy             ; label shown on base screens
 *   channel      = SkippyShuttleNet   ; IGC broadcast channel (must match base)
 *   runMode      = CONTINUOUS         ; CONTINUOUS | ONETRIP | ONEWAY (trip cycle)
 *   homeTrigger  = Auto               ; Auto | Cargo | Timer | Manual - releases from HOME
 *   destTrigger  = Auto               ; Auto | Cargo | Timer | Manual - releases from DEST
 *   remoteName   =                    ; blank = auto-find a Remote Control
 *   loadTag      = [SHUTTLE:LOAD]      ; sorters with this tag in their name load cargo
 *   unloadTag    = [SHUTTLE:UNLOAD]    ; sorters with this tag in their name unload cargo
 *   lcdTag       = [SHUTTLE]          ; LCDs whose name contains this show status
 *   cruiseSpeed  = 100                ; [m/s] cruise speed cap
 *   dockSpeed    = 5                  ; [m/s] final-approach cap (precision mode)
 *   maxMassKg    = 0                  ; 0 = no mass gate; else stop loading here
 *   departFill   = 95                 ; [%] cargo fill that triggers departure
 *   unloadDrainSec = 30              ; [s] max time to spend unloading (Auto dest trigger)
 *   dwellSec     = 30                 ; [s] Timer-trigger dwell at a dock before departing
 *   minHydrogenPct = 10               ; [%] refuse to depart below this H2 level (0/no tanks = off)
 *   minBatteryPct  = 10               ; [%] refuse to depart below this battery charge
 *   fuelMarginPct  = 25               ; [%] headroom on the measured per-leg fuel/charge cost
 *   segMeters    = 250                ; breadcrumb spacing on straightaways
 *   turnDegrees  = 12                 ; extra breadcrumb when heading turns this much
 *   approachDist = 15                 ; [m] on-axis stand-off where docking takes over
 *   gyroRpmCap   = 0                  ; gyro rate cap [rpm]; 0 = auto (15 small grid / 5 large)
 *   brakeFrac    = 0.6                ; fraction of thrust reserved for braking/cornering (0.1-1.0)
 *   cornerLen    = 30                 ; [m] corner-rounding length + look-ahead blend distance
 *   gyroGain     = 4                  ; attitude P gain (higher = snappier turns toward heading)
 *   gyroDamp     = 3                  ; attitude damping (raise if the ship wobbles / overshoots / jiggles)
 *
 * NOTES
 *   - Coordinates are absolute world positions. This is correct for static
 *     grids (a planet base and a station never move). Do not use this to dock
 *     with a moving grid.
 *   - Docking is ORIENTATION-MATCHED and ship-agnostic. RECORD captures the full
 *     docked attitude (position + facing + the connector's mating axis), so the
 *     ship reproduces the exact pose it was recorded in - it works for a
 *     connector facing ANY direction, not just a nose-mounted one, and the same
 *     recorded/shared route flies correctly on any ship with gyros + thrusters.
 *   - A custom gyro + thruster controller flies the WHOLE route - takeoff,
 *     cruise, and the final approachDist metres into the dock. It follows the
 *     recorded breadcrumbs on a precomputed velocity profile (slow into turns,
 *     full speed on straights) and brakes smoothly into each point, so it never
 *     hands the cruise to the stock autopilot (which weaves and flies sideways).
 *     While flying it runs at 60 Hz for a steady heading (a slow loop overshoots
 *     and wobbles), turns the ship's DAMPENERS OFF, and coasts with thrust cut in
 *     space once up to speed - so a straight leg burns no fuel. Dampeners are
 *     restored whenever it stops, docks, faults, or is recompiled.
 *   - The script only toggles the tagged sorters on/off. Their whitelist/blacklist
 *     and Drain-All settings stay exactly as you configured them. Tag matching is
 *     case-insensitive and matches the tag anywhere in the block name, so you can
 *     name sorters however you like as long as the tag appears somewhere.
 *
 * Version tracked in CHANGELOG.md. Semver.
 *//////////////////////////////////////////////////////////////////////////////

const string VERSION = "0.9.0";

// ---- Roles / states --------------------------------------------------------
enum Role { Shuttle, Base }
// RunMode is the TRIP CYCLE only. Continuous/OneTrip do a full round trip (home ->
// dest -> home); OneWay runs a single leg to the OPPOSITE end and holds there, the
// next START sending it back. Which way OneWay goes is decided by which END it's
// physically parked at (pose proximity). The old WaitFull mode was folded into
// Continuous + homeTrigger = Cargo (see DepartTrigger); a config that still says
// WAITFULL loads as exactly that.
enum RunMode { Continuous, OneTrip, OneWay }

// DepartTrigger is a SEPARATE, PER-CONNECTOR setting (PAM-style): what releases the
// shuttle from a dock to the next leg. Each end has its own.
//   Auto   - leave as soon as the cargo op finishes (loaded at home / emptied at the
//            dest, with the drain safety timeout) - the original behaviour.
//   Cargo  - wait for the hold to be full at home / empty at the destination.
//   Timer  - run the sorters for dwellSec, then leave regardless of fill.
//   Manual - hold until a DEPART command arrives (ship button or station over IGC).
// Departure is additionally gated on fuel/charge (see DepartFuelOk): the shuttle
// won't leave a dock without enough hydrogen and battery to reach the next one.
enum DepartTrigger { Auto, Cargo, Timer, Manual }

// A full docked pose: where the Remote Control sat, which way the ship faced,
// and the bound connector's mating axis. Capturing all four lets the shuttle
// reproduce the exact orientation it was docked in - on ANY ship, for a
// connector facing ANY direction, not just a nose-mounted one.
struct DockPose
{
    public Vector3D Pos;      // Remote Control world position while docked
    public Vector3D Fwd;      // Remote Control world forward while docked
    public Vector3D Up;       // Remote Control world up while docked
    public Vector3D ConnFwd;  // bound connector's world forward (points into the dock)
}
enum State
{
    Idle,           // parked, waiting for a command / next cycle
    Loading,        // at home, load sorter on, filling to threshold
    UndockHome,     // released home connector, backing off
    CruiseToDest,   // controller flying the recorded path to the destination
    ApproachDest,   // precision final approach into the destination connector
    Unloading,      // at destination, unload sorter on, draining
    UndockDest,     // released destination connector, backing off
    CruiseToHome,   // controller flying the reversed path home
    ApproachHome,   // precision final approach into the home connector
    Recording,      // teaching a route (path breadcrumbs are being captured)
    Faulted         // something went wrong; needs operator attention
}

// ---- Configuration (loaded from Custom Data) -------------------------------
Role role = Role.Shuttle;
RunMode runMode = RunMode.Continuous;
DepartTrigger homeTrigger = DepartTrigger.Auto;   // what releases the shuttle from HOME
DepartTrigger destTrigger = DepartTrigger.Auto;   // what releases it from the DESTINATION
string shipName = "Skippy";
string channel = "SkippyShuttleNet";
string remoteName = "";
string loadTag = "[SHUTTLE:LOAD]";
string unloadTag = "[SHUTTLE:UNLOAD]";
string lcdTag = "[SHUTTLE]";
float cruiseSpeed = 100f;
float dockSpeed = 5f;
double maxMassKg = 0;
double departFill = 95;
double unloadDrainSec = 30;
double dwellSec = 30;             // [s] Timer-trigger dwell at a dock before departing
double minHydrogenPct = 10;       // [%] refuse to depart below this hydrogen level (ignored if no H2 tanks)
double minBatteryPct = 10;        // [%] refuse to depart below this battery charge (ignored if no batteries)
double fuelMarginPct = 25;        // [%] safety headroom added to the measured per-leg fuel/charge estimate
double segMeters = 250;
double turnDegrees = 12;
double approachDist = 15;         // [m] on-axis stand-off where cruise hands to the docking controller
float gyroRpmCap = 0f;            // gyro rate cap [rpm]; 0 = auto (15 small grid / 5 large) - PAM's gentle-rotation values
double brakeFrac = 0.6;           // fraction of the weakest-axis thrust reserved for braking/cornering (headroom for gravity + saturation)
double cornerLen = 30;            // [m] corner-rounding length; also the look-ahead blend distance
double gyroGain = 4.0;            // attitude controller P gain (rotate toward the target attitude)
double gyroDamp = 3.0;            // attitude controller damping on angular velocity; raise if the ship wobbles/overshoots/jiggles

// ---- Route data ------------------------------------------------------------
// A route is two docked poses (home + dest) plus the breadcrumb path between
// them. The pose carries orientation, so docking reproduces the exact attitude
// the connector was recorded in - works for connectors facing any direction.
DockPose homePose, destPose;
string homeConn = "", destConn = "";
List<Vector3D> path = new List<Vector3D>();   // home -> dest breadcrumbs
bool haveRoute = false;

// ---- Runtime state ---------------------------------------------------------
State state = State.Idle;
bool operating = false;          // set by START, cleared by STOP / OneTrip end
string statusMsg = "Idle";
double phaseTimer = 0;           // seconds spent in the current timed phase
bool departRequested = false;    // manual "Depart Now" latch (ship button / station IGC); consumed on departure

// ---- Fuel / charge gate ----------------------------------------------------
// Adaptive per-leg estimate: how much hydrogen and charge (in % points) the last
// completed leg burned, measured each direction and persisted. A departure needs
// current level >= estimate * (1 + fuelMarginPct/100), floored by minHydrogen/Battery.
double estHydroOut = 0, estBattOut = 0;    // home -> dest consumption [% points]
double estHydroHome = 0, estBattHome = 0;  // dest -> home consumption [% points]
double legStartH2 = -1, legStartBatt = -1; // level captured at departure; -1 = not measuring
bool legOutbound = true;                   // direction of the leg currently being measured

// ---- Cruise controller state -----------------------------------------------
// The custom cruise controller flies a flight-ordered list of waypoints, each
// with a precomputed max speed (the velocity profile). A cursor tracks which
// waypoint we're flying toward; the profile is rebuilt every leg (loaded vs
// empty mass differ).
List<Vector3D> legWps = new List<Vector3D>();   // flight-ordered leg waypoints (+ final on-axis stand-off)
List<double> legVmax = new List<double>();      // parallel: max speed [m/s] permitted AT legWps[i]
int cruiseIdx = 0;                              // index of the waypoint currently flown toward
double cruiseAccel = 1.0;                       // [m/s^2] decel/lateral accel cached for this leg (mass-dependent)
double cruiseProgTimer = 0;                     // seconds since the cursor last advanced (stuck watchdog)

// ---- LCD menu (ship role) --------------------------------------------------
const int PAGE_MAIN = 0, PAGE_RECORD = 1, PAGE_SETTINGS = 2, PAGE_DEPART = 3;
int menuPage = PAGE_MAIN;
int menuIndex = 0;               // cursor position within the current page
bool editing = false;            // true while adjusting a value item
double editValue = 0;            // working value during an edit

// ---- Recording scratch -----------------------------------------------------
Vector3D lastCrumb;
Vector3D lastDir = Vector3D.Zero;

// ---- Blocks ----------------------------------------------------------------
IMyRemoteControl rc;
List<IMyShipConnector> connectors = new List<IMyShipConnector>();
List<IMyConveyorSorter> loadSorters = new List<IMyConveyorSorter>();
List<IMyConveyorSorter> unloadSorters = new List<IMyConveyorSorter>();
List<IMyCargoContainer> cargo = new List<IMyCargoContainer>();
List<IMyTextSurface> screens = new List<IMyTextSurface>();
IMyTextSurface pbSurface;                            // the PB's own screen (written, but not used to size)
List<IMyGyro> gyros = new List<IMyGyro>();          // final-approach attitude control
List<IMyThrust> thrusters = new List<IMyThrust>();  // final-approach translation control
List<IMyGasTank> h2Tanks = new List<IMyGasTank>();  // hydrogen tanks (fuel-gate reading)
List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();  // batteries (charge-gate reading)
IMyBroadcastListener listener;

// ---- Base-role state -------------------------------------------------------
Dictionary<string, ShuttleReport> fleet = new Dictionary<string, ShuttleReport>();

const double DT_FALLBACK = 1.0 / 6.0;   // seconds/tick fallback (first tick / long pause)
double dt = DT_FALLBACK;                 // real elapsed time this tick; timers use this, not a fixed rate
double sinceRender = 0;                  // s since the last LCD render + broadcast (throttle at 60 Hz)
const double APPROACH_TIMEOUT = 45;   // s to abort a stuck docking approach
const int MAX_PATH = 250;
const int WRAP_COLS = 26;             // ship LCD word-wrap width; keeps any one line from blowing out the shared font size

// ---- Docking controller tuning ---------------------------------------------
const double APPROACH_KP = 0.5;     // desired approach speed = distance * this (capped at dockSpeed)
const double VEL_GAIN = 2.0;        // how hard to correct a velocity error into thrust
const double ALIGN_TOL = 0.03;      // ~2 deg: considered fully aligned / docked-attitude reached
const double ALIGN_MOVE_TOL = 0.20; // ~12 deg: align this close before translating on-axis
const double ARRIVE_SPEED = 1.0;    // m/s below which a stand-off point counts as "reached"

// ---- Cruise controller tuning ----------------------------------------------
const double WP_ARRIVE_MIN = 8.0;         // m, floor for the speed-scaled waypoint arrive radius
const double MIN_ACCEL = 0.5;             // m/s^2, floors the profile accel so it can't blow up (near-zero thrust axis)
const double CORNER_STRAIGHT_TOL = 0.10;  // rad (~6 deg): below this deflection, no corner speed limit
const double ALIGN_SLOW_TOL = 0.5;        // attitude error at which the forward-speed factor hits its floor
const double ALIGN_MIN_FAC = 0.15;        // never fully stall forward speed while re-aiming (keeps creeping to re-align)
const double VEL_MIN_FAC = 0.30;          // floor on the sideways-velocity speed cut
const double CRUISE_STUCK_TIMEOUT = 60.0; // s without cursor progress -> Faulted
const double ALIGN_DEADBAND = 0.01;       // ~0.6 deg: below this the gyros rest instead of hunting the target
const double COAST_TOL = 0.5;             // m/s velocity error below which the ship coasts (thrust off) in space

// ============================================================================
//  Lifecycle
// ============================================================================
Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    if (string.IsNullOrWhiteSpace(Me.CustomData)) WriteConfigTemplate();
    LoadConfig();
    if (role == Role.Shuttle) BackfillConfig();   // add keys introduced by a newer version, keeping the route/state
    Discover();
    LoadRoute();
    LoadState();
    // Both roles listen on the channel: the base for status reports, the ship for
    // remote DEPART commands sent by a station PB.
    listener = IGC.RegisterBroadcastListener(channel);
    if (role == Role.Shuttle)
        ReleaseControl();   // clear any thruster/gyro overrides left by a previous compile
}

void Save()
{
    // Persist enough to resume a cycle across a recompile.
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    ini.Set("state", "state", state.ToString());
    ini.Set("state", "operating", operating);
    ini.Set("state", "phaseTimer", phaseTimer);
    ini.Set("state", "estHydroOut", estHydroOut);
    ini.Set("state", "estBattOut", estBattOut);
    ini.Set("state", "estHydroHome", estHydroHome);
    ini.Set("state", "estBattHome", estBattHome);
    Me.CustomData = ini.ToString();
}

// ============================================================================
//  Main
// ============================================================================
void Main(string argument, UpdateType source)
{
    try
    {
        // Real elapsed time this tick, so every timer stays correct as the loop rate
        // switches between 60 Hz (flying) and 6 Hz (idle). Guard the first post-compile
        // tick (0) and long single-player save/exit pauses (huge delta).
        dt = Runtime.TimeSinceLastRun.TotalSeconds;
        if (dt <= 0 || dt > 0.5) dt = DT_FALLBACK;

        if (!string.IsNullOrEmpty(argument)) HandleCommand(argument.Trim());

        if (role == Role.Base) { RunBase(); return; }

        // Ship role - re-discover cheaply if the remote vanished (regrind etc.)
        if (rc == null) { Discover(); if (rc == null) { statusMsg = "No Remote Control found"; RenderShip(); return; } }

        DrainIgc();   // accept remote DEPART commands from a station PB

        switch (state)
        {
            case State.Recording:  TickRecording();  break;
            case State.Loading:    TickLoading();     break;
            case State.UndockHome: TickUndock(true);  break;
            case State.CruiseToDest: TickCruise(true); break;
            case State.ApproachDest: TickApproach(true); break;
            case State.Unloading:  TickUnloading();   break;
            case State.UndockDest: TickUndock(false); break;
            case State.CruiseToHome: TickCruise(false); break;
            case State.ApproachHome: TickApproach(false); break;
            case State.Idle:       TickIdle();         break;
            case State.Faulted:    AbortAutopilot(); ReleaseControl(); SetSorters(loadSorters, false); SetSorters(unloadSorters, false); break;
        }

        // Fly the attitude/translation control at 60 Hz so it holds heading cleanly
        // (a 6 Hz loop overshoots and hunts); drop back to 6 Hz when parked. Applies
        // next tick, so it tracks the state the switch above just moved us into.
        Runtime.UpdateFrequency = IsFlightControlState() ? UpdateFrequency.Update1 : UpdateFrequency.Update10;

        // Rendering (MeasureStringInPixels per panel) + broadcast are the expensive
        // work; throttle them to ~6-7 Hz so 60 Hz flight stays cheap. Render at once
        // on any command tick so the menu/UI stays instant.
        sinceRender += dt;
        if (sinceRender >= 0.15 || !string.IsNullOrEmpty(argument))
        {
            RenderShip();
            Broadcast();
            sinceRender = 0;
        }
    }
    catch (Exception e)
    {
        state = State.Faulted;
        statusMsg = "ERROR: " + e.Message;
        Echo(statusMsg);
    }
}

// States where the custom controller actively drives gyros + thrusters and wants the
// fast (60 Hz) loop for smooth attitude hold. Everything else (docked, loading,
// recording) is fine at 6 Hz.
bool IsFlightControlState()
{
    switch (state)
    {
        case State.UndockHome:
        case State.CruiseToDest:
        case State.ApproachDest:
        case State.UndockDest:
        case State.CruiseToHome:
        case State.ApproachHome:
            return true;
        default:
            return false;
    }
}

// ============================================================================
//  Commands
// ============================================================================
void HandleCommand(string arg)
{
    var parts = arg.ToUpperInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 0) return;

    switch (parts[0])
    {
        case "RECORD":
            if (parts.Length > 1 && parts[1] == "HOME") RecordHome();
            else if (parts.Length > 1 && parts[1] == "DEST") RecordDest();
            else statusMsg = "Usage: RECORD HOME | RECORD DEST";
            break;

        case "START":
        case "GO":
            if (!haveRoute) { statusMsg = "No route - RECORD HOME/DEST first"; break; }
            operating = true;
            // Kick off from wherever we sensibly can. ONEWAY runs a single leg to the
            // OPPOSITE end and holds there, so its direction is decided purely by which
            // END we're physically docked at (by pose proximity, not connector name -
            // this ship docks both ends with the same connector): at home -> load and
            // head to dest; at dest -> depart straight for home (no re-unload). The
            // other modes cycle a full round trip.
            if (state == State.Idle || state == State.Faulted)
            {
                bool docked = DockedNow();
                bool atHome = AtHomeEnd();
                if (runMode == RunMode.OneWay)
                    state = docked && atHome ? State.Loading
                          : docked          ? State.UndockDest
                          : atHome          ? State.CruiseToDest
                          :                   State.CruiseToHome;
                else
                    state = docked && atHome ? State.Loading
                          : docked          ? State.Unloading
                          :                   State.CruiseToHome;
                phaseTimer = 0;          // begin a fresh load/unload dwell
                departRequested = false; // drop any stale manual-depart latch
            }
            statusMsg = "Started (" + runMode + ")";
            break;

        case "STOP":
            operating = false;
            departRequested = false;
            AbortAutopilot();
            ReleaseControl();
            SetSorters(loadSorters, false);
            SetSorters(unloadSorters, false);
            state = State.Idle;
            statusMsg = "Stopped";
            break;

        case "HOME":
            if (!haveRoute) { statusMsg = "No route - RECORD HOME/DEST first"; break; }
            AbortAutopilot();
            if (DockedNow() && AtHomeEnd())
            {
                operating = false;
                state = State.Idle;
                statusMsg = "Already home";
            }
            else
            {
                operating = true;
                state = DockedNow() ? State.UndockDest : State.CruiseToHome;
                statusMsg = "Returning home";
            }
            break;

        case "MODE":
            if (parts.Length > 1) SetMode(parts[1]);
            else statusMsg = "Mode: " + runMode;
            break;

        case "DEPART":
            // Ship: release the current dock now (manual trigger / override). Base:
            // broadcast the request to the shuttle(s) - "DEPART" for all, or
            // "DEPART <shipName>" to target one.
            if (role == Role.Base)
            {
                string who = parts.Length > 1 ? parts[1] : "*";
                IGC.SendBroadcastMessage(channel, "CMD|DEPART|" + who);
                statusMsg = "Sent DEPART to " + (who == "*" ? "all shuttles" : who);
            }
            else RequestDepart();
            break;

        case "RESUME":
            LoadState();
            statusMsg = "Resumed: " + state;
            break;

        case "CLEARROUTE":
            ClearRoute();
            statusMsg = "Route cleared";
            break;

        // ---- LCD menu navigation (bind these to cockpit toolbar buttons) ----
        case "UP":    MenuMove(-1); break;
        case "DOWN":  MenuMove(+1); break;
        case "APPLY": MenuApply();  break;
        case "BACK":  MenuBack();   break;

        default:
            statusMsg = "Unknown command: " + parts[0];
            break;
    }
}

void SetMode(string m)
{
    switch (m)
    {
        case "CONTINUOUS": runMode = RunMode.Continuous; break;
        case "ONETRIP":    runMode = RunMode.OneTrip;    break;
        case "ONEWAY":     runMode = RunMode.OneWay;     break;
        case "WAITFULL":   // legacy: fold into Continuous + a Cargo home trigger
            runMode = RunMode.Continuous;
            homeTrigger = DepartTrigger.Cargo;
            SaveCfg("runMode", "CONTINUOUS");
            SaveCfg("homeTrigger", "Cargo");
            statusMsg = "WaitFull -> Continuous + Home trigger = Cargo";
            return;
        default: statusMsg = "Mode must be CONTINUOUS|ONETRIP|ONEWAY"; return;
    }
    var ini = new MyIni(); ini.TryParse(Me.CustomData);
    ini.Set("shuttle", "runMode", m);
    Me.CustomData = ini.ToString();
    statusMsg = "Mode = " + runMode;
}

// ============================================================================
//  Route recording
// ============================================================================
void RecordHome()
{
    var c = ConnectedConnector();
    if (c == null) { statusMsg = "RECORD HOME: dock at the home connector first"; return; }
    homePose = CapturePose(c);
    homeConn = c.CustomName;
    path.Clear();
    lastCrumb = homePose.Pos;
    lastDir = Vector3D.Zero;
    state = State.Recording;
    operating = false;
    statusMsg = "Recording from HOME (" + homeConn + "). Fly to destination.";
}

void RecordDest()
{
    if (state != State.Recording) { statusMsg = "RECORD DEST: run RECORD HOME first"; return; }
    var c = ConnectedConnector();
    if (c == null) { statusMsg = "RECORD DEST: dock at the destination connector first"; return; }
    destPose = CapturePose(c);
    destConn = c.CustomName;
    // Ensure the final approach point is captured.
    if (path.Count == 0 || Vector3D.Distance(path[path.Count - 1], destPose.Pos) > 5)
        AddCrumb(rc.GetPosition());
    haveRoute = true;
    state = State.Idle;
    SaveRoute();
    statusMsg = "Route saved: " + homeConn + " -> " + destConn + " (" + path.Count + " waypoints)";
}

// Snapshot the exact docked attitude so it can be reproduced later, on any ship.
DockPose CapturePose(IMyShipConnector c)
{
    return new DockPose
    {
        Pos     = rc.GetPosition(),
        Fwd     = rc.WorldMatrix.Forward,
        Up      = rc.WorldMatrix.Up,
        ConnFwd = c.WorldMatrix.Forward   // points out of the connector face, into the dock
    };
}

void TickRecording()
{
    Vector3D p = rc.GetPosition();
    double moved = Vector3D.Distance(p, lastCrumb);
    if (moved < 20) return;                      // ignore jitter while parked

    Vector3D dir = Vector3D.Normalize(p - lastCrumb);
    double turn = lastDir == Vector3D.Zero ? 0
                : Math.Acos(MathHelper.Clamp(dir.Dot(lastDir), -1, 1)) * 180.0 / Math.PI;

    if (moved >= segMeters || (moved >= 30 && turn >= turnDegrees))
        AddCrumb(p);
}

void AddCrumb(Vector3D p)
{
    if (path.Count >= MAX_PATH) { statusMsg = "Path full (" + MAX_PATH + " wp) - increase segMeters"; return; }
    if (path.Count > 0) lastDir = Vector3D.Normalize(p - lastCrumb);
    path.Add(p);
    lastCrumb = p;
}

// ============================================================================
//  Flight state machine (ship)
// ============================================================================
void TickIdle()
{
    AbortAutopilot();
    ReleaseControl();
    if (!operating) return;
    phaseTimer = 0;   // fresh load/unload dwell when we pick a dock phase back up
    if (DockedNow()) state = AtHomeEnd() ? State.Loading : State.Unloading;
    else state = State.CruiseToHome;
}

void TickLoading()
{
    SetSorters(unloadSorters, false);
    phaseTimer += dt;
    double mass = ShipMassKg();
    double fill = CargoFillPct();

    bool massGate = maxMassKg > 0 && mass >= maxMassKg * 0.98;
    bool cargoReady = fill >= departFill || massGate;

    // Keep loading until the hold is full (or the mass gate trips); then stop the
    // sorters even while we're still waiting on a Manual/Timer trigger, so a shuttle
    // that dwells or waits for a button never keeps cramming past full/overweight.
    SetSorters(loadSorters, !cargoReady);

    if (DepartureAllowed(true, cargoReady))
    {
        string why;
        if (!DepartFuelOk(true, out why)) { SetSorters(loadSorters, false); statusMsg = why; return; }
        SetSorters(loadSorters, false);
        departRequested = false;
        BeginLegMeasure(true);
        statusMsg = "Loaded (" + fill.ToString("0") + "%, " + (mass / 1000.0).ToString("0.0") + "t) - departing";
        state = State.UndockHome;
        phaseTimer = 0;
        return;
    }

    statusMsg = DepartStatus(true, fill);
}

void TickUnloading()
{
    SetSorters(loadSorters, false);
    phaseTimer += dt;
    double fill = CargoFillPct();

    // Auto keeps its original drain-timeout safety net; the explicit Cargo trigger
    // waits for a truly empty hold. Both stop the sorters once empty.
    bool cargoReady = fill <= 1.0;
    SetSorters(unloadSorters, !cargoReady);

    if (DepartureAllowed(false, cargoReady))
    {
        SetSorters(unloadSorters, false);

        if (runMode == RunMode.OneWay)   // delivered - hold at the destination, don't return
        {
            departRequested = false;
            phaseTimer = 0;
            operating = false;
            state = State.Idle;
            statusMsg = "Delivered - holding at destination";
            return;
        }

        // Round trip: don't leave for home without the fuel to get there.
        string why;
        if (!DepartFuelOk(false, out why)) { statusMsg = why; return; }
        departRequested = false;
        BeginLegMeasure(false);
        phaseTimer = 0;
        state = State.UndockDest;   // return leg; OneTrip stops after docking home
        return;
    }

    statusMsg = DepartStatus(false, fill);
}

// Should the shuttle leave this dock now? A manual DEPART overrides any trigger;
// otherwise the end's configured trigger decides. `atHome` selects the trigger and
// `cargoReady` is that end's cargo condition (full at home / empty at the dest).
bool DepartureAllowed(bool atHome, bool cargoReady)
{
    if (departRequested) return true;   // manual "Depart Now" (ship button or station)
    DepartTrigger trig = atHome ? homeTrigger : destTrigger;
    switch (trig)
    {
        case DepartTrigger.Manual: return false;                    // wait for DEPART
        case DepartTrigger.Timer:  return phaseTimer >= dwellSec;    // dwell, then go
        case DepartTrigger.Cargo:  return cargoReady;               // full / empty
        default:                                                    // Auto: cargo-ready, with the drain safety net at the dest
            return cargoReady || (!atHome && phaseTimer >= unloadDrainSec);
    }
}

// The holding status line while waiting on a trigger.
string DepartStatus(bool atHome, double fill)
{
    string act = (atHome ? "Loading " : "Unloading ") + fill.ToString("0") + "%";
    DepartTrigger trig = atHome ? homeTrigger : destTrigger;
    if (trig == DepartTrigger.Manual) return act + " - waiting DEPART";
    if (trig == DepartTrigger.Timer)  return act + " - dwell " + phaseTimer.ToString("0") + "/" + dwellSec.ToString("0") + "s";
    return act;
}

// heading == true  => currently at HOME, undocking to go to DEST
// heading == false => currently at DEST, undocking to go HOME
void TickUndock(bool fromHome)
{
    var c = GetConnector(fromHome ? homeConn : destConn);
    DockPose p = fromHome ? homePose : destPose;

    if (c != null && c.Status == MyShipConnectorStatus.Connected)
    {
        c.Disconnect();
        phaseTimer = 0;
        statusMsg = "Undocking";
        return;
    }

    // Two-step powered departure so the cruise autopilot starts already pointed
    // the right way instead of spinning-and-sliding when it engages:
    //   1. Back straight out to the stand-off, holding the recorded docked attitude.
    //   2. Once clear of the dock, rotate in place to face the first cruise waypoint.
    Vector3D standoff = ApproachPoint(p);
    bool clear = Vector3D.Distance(rc.GetPosition(), standoff) < 3.0;   // far enough off to rotate safely

    Vector3D faceFwd = p.Fwd;   // hold docked facing until clear of the dock
    if (clear)
    {
        Vector3D toTarget = FirstCruiseTarget(fromHome) - standoff;
        if (toTarget.LengthSquared() > 1) faceFwd = Vector3D.Normalize(toTarget);
    }

    bool ready = FlyToPose(standoff, faceFwd, p.Up, 1.0) && clear;
    phaseTimer += dt;
    statusMsg = clear ? "Aligning for cruise"
                      : (fromHome ? "Clearing home dock" : "Clearing station dock");

    if (ready || phaseTimer >= APPROACH_TIMEOUT)
    {
        ReleaseControl();
        phaseTimer = 0;
        state = fromHome ? State.CruiseToDest : State.CruiseToHome;
    }
}

// The first point the cruise controller will actually fly to. Used to pre-aim
// the ship during undock so it engages cruise already facing its heading.
// BuildLeg always appends the final stand-off, so legWps[0] is never empty.
Vector3D FirstCruiseTarget(bool toDest)
{
    BuildLeg(toDest);
    return legWps[0];
}

void TickCruise(bool toDest)
{
    if (!CruiseArmed(toDest)) { ArmCruise(toDest); return; }

    cruiseProgTimer += dt;
    bool done = RunCruiseControl();
    statusMsg = (toDest ? "Cruising to destination" : "Cruising home")
              + "  ETA " + FormatEta();

    if (done)
    {
        // Reached the on-axis stand-off -> hand to the docking controller.
        cruiseArmed = false;
        ReleaseControl();
        state = toDest ? State.ApproachDest : State.ApproachHome;
        phaseTimer = 0;
        return;
    }
    if (cruiseProgTimer >= CRUISE_STUCK_TIMEOUT)
    {
        cruiseArmed = false;
        ReleaseControl();
        state = State.Faulted;
        statusMsg = "Cruise stuck - check thrust/gyro/geometry";
    }
}

bool cruiseArmedToDest = false;
bool cruiseArmed = false;
bool CruiseArmed(bool toDest) { return cruiseArmed && cruiseArmedToDest == toDest; }

// The stand-off point sits on the connector's mating axis, approachDist metres
// clear of the dock. ConnFwd points into the dock, so we back off along -ConnFwd.
Vector3D ApproachPoint(DockPose p) { return p.Pos - p.ConnFwd * approachDist; }

void ArmCruise(bool toDest)
{
    BuildLeg(toDest);
    if (legWps.Count == 0)   // defensive: BuildLeg always appends the stand-off, so this shouldn't happen
    {
        state = State.Faulted;
        statusMsg = "Cruise: empty path - re-record route";
        return;
    }
    BuildVelocityProfile();
    cruiseIdx = 0;
    cruiseProgTimer = 0;
    cruiseArmed = true;
    cruiseArmedToDest = toDest;
    statusMsg = toDest ? "Cruising to destination" : "Cruising home";
}

// Build the flight-ordered waypoint list for a leg: the recorded crumbs (forward
// for the outbound leg, reversed for the return) minus any that sit inside either
// dock's stand-off radius, then the on-axis stand-off point as the final target.
void BuildLeg(bool toDest)
{
    legWps.Clear();
    DockPose from = toDest ? homePose : destPose;
    DockPose to   = toDest ? destPose : homePose;
    double skip = approachDist + 3;   // drop crumbs that sit inside either stand-off

    if (toDest)
    {
        for (int i = 0; i < path.Count; i++)
            if (Vector3D.Distance(path[i], from.Pos) > skip && Vector3D.Distance(path[i], to.Pos) > skip)
                legWps.Add(path[i]);
    }
    else
    {
        for (int i = path.Count - 1; i >= 0; i--)
            if (Vector3D.Distance(path[i], from.Pos) > skip && Vector3D.Distance(path[i], to.Pos) > skip)
                legWps.Add(path[i]);
    }

    legWps.Add(ApproachPoint(to));   // final target: on-axis stand-off (NOT the dock itself)
}

// Precompute a max speed for each leg waypoint (PAM-style velocity profile): slow
// into sharp corners, full speed on straights, and always able to brake to the
// next point. Recomputed every arm because loaded vs empty mass differ.
void BuildVelocityProfile()
{
    int n = legWps.Count;
    legVmax.Clear();
    for (int i = 0; i < n; i++) legVmax.Add(cruiseSpeed);
    if (n == 0) return;

    // Conservative isotropic accel = weakest thrust axis / mass, with headroom.
    // The leg turns, so no single direction is right; the weakest axis is safe.
    double[] cap; MatrixD toLocal;
    AxisThrust(out cap, out toLocal);
    double minAxis = cap[0];
    for (int i = 1; i < 6; i++) minAxis = Math.Min(minAxis, cap[i]);
    double mass = rc.CalculateShipMass().PhysicalMass;
    cruiseAccel = Math.Max(MIN_ACCEL, brakeFrac * minAxis / Math.Max(mass, 1.0));

    // Corner speed from the deflection angle at each interior waypoint. Round the
    // corner within cornerLen metres -> arc radius R = L/tan(theta/2), and the
    // centripetal limit vCorner = sqrt(aLat * R).
    for (int i = 1; i < n - 1; i++)
    {
        Vector3D inDir = legWps[i] - legWps[i - 1];
        Vector3D outDir = legWps[i + 1] - legWps[i];
        if (inDir.LengthSquared() < 1e-6 || outDir.LengthSquared() < 1e-6) continue;
        inDir = Vector3D.Normalize(inDir);
        outDir = Vector3D.Normalize(outDir);
        double theta = Math.Acos(MathHelper.Clamp(inDir.Dot(outDir), -1, 1));
        if (theta < CORNER_STRAIGHT_TOL) continue;   // ~straight, keep full cruise
        double R = cornerLen / Math.Max(Math.Tan(theta * 0.5), 1e-3);
        double corner = Math.Sqrt(cruiseAccel * R);
        legVmax[i] = Math.Min(legVmax[i], Math.Min(corner, cruiseSpeed));
    }

    // Final target: arrive slow so the docking controller takes over cleanly.
    legVmax[n - 1] = ARRIVE_SPEED;

    // Backward pass: guarantee we can always decelerate into each point's limit.
    for (int i = n - 2; i >= 0; i--)
    {
        double segLen = Vector3D.Distance(legWps[i], legWps[i + 1]);
        double reachable = Math.Sqrt(legVmax[i + 1] * legVmax[i + 1] + 2.0 * cruiseAccel * segLen);
        legVmax[i] = Math.Min(legVmax[i], Math.Min(reachable, cruiseSpeed));
    }
}

// Speed-scaled radius at which a waypoint counts as reached: at least WP_ARRIVE_MIN,
// but a couple of ticks of travel at high speed so a fast fly-by doesn't stall.
double WpArriveRadius()
{
    return Math.Max(WP_ARRIVE_MIN, rc.GetShipSpeed() * dt * 2.0);
}

// Advance the waypoint cursor: step to the next point when we're within the arrive
// radius OR our projection along the leg has passed the waypoint plane. The plane
// test commits to the next point on a high-speed fly-by, which stops the ship
// orbiting a waypoint it never quite touched (the stock-autopilot circling).
void AdvanceCursor(Vector3D pos)
{
    while (cruiseIdx < legWps.Count - 1)
    {
        Vector3D cur = legWps[cruiseIdx];
        Vector3D next = legWps[cruiseIdx + 1];
        bool arrived = Vector3D.Distance(pos, cur) < WpArriveRadius();
        Vector3D seg = next - cur;
        bool passed = seg.LengthSquared() > 1e-6 &&
                      (pos - cur).Dot(Vector3D.Normalize(seg)) > 0;
        if (arrived || passed) { cruiseIdx++; cruiseProgTimer = 0; }
        else break;
    }
}

// Per-tick cruise control law. Faces the ship along the path, picks a desired
// speed from the velocity profile + a sqrt(2*a*d) braking curve, scales it down
// when mis-aimed or drifting sideways, and drives it with the shared thrust/gyro
// primitives (same force law as FlyToPose). Returns true at the stand-off.
bool RunCruiseControl()
{
    SetDampeners(false);   // controller owns thrust all leg; off = coast in space, no auto-braking to fight

    Vector3D pos = rc.GetPosition();
    Vector3D vel = rc.GetShipVelocities().LinearVelocity;
    Vector3D grav = rc.GetNaturalGravity();
    double mass = rc.CalculateShipMass().PhysicalMass;

    AdvanceCursor(pos);

    Vector3D target = legWps[cruiseIdx];
    Vector3D toWp = target - pos;
    double dist = toWp.Length();
    Vector3D pathDir = dist > 1e-3 ? toWp / dist : rc.WorldMatrix.Forward;

    // Ease toward the next segment's direction as we near a corner.
    if (cruiseIdx < legWps.Count - 1 && dist < cornerLen)
    {
        Vector3D nextSeg = legWps[cruiseIdx + 1] - target;
        if (nextSeg.LengthSquared() > 1e-6)
        {
            Vector3D nextDir = Vector3D.Normalize(nextSeg);
            double b = 1.0 - dist / cornerLen;   // 0 far from the vertex, 1 at it
            Vector3D blended = Vector3D.Lerp(pathDir, nextDir, b);
            if (blended.LengthSquared() > 1e-6) pathDir = Vector3D.Normalize(blended);
        }
    }

    // Braking curve toward this waypoint's profiled speed, capped at cruise.
    double vmax = legVmax[cruiseIdx];
    double vBrake = Math.Sqrt(vmax * vmax + 2.0 * cruiseAccel * dist);
    double speed = Math.Min(cruiseSpeed, vBrake);

    // Attitude: face travel; hold up = anti-gravity (planet) or current up (space, no roll).
    Vector3D upTarget = grav.LengthSquared() > 1e-3 ? Vector3D.Normalize(-grav) : rc.WorldMatrix.Up;
    double align = AlignTo(pathDir, upTarget);

    // Turn before accelerating; don't fly fast sideways. Both factors are floored
    // so the ship can still creep and re-align rather than dead-stall.
    double alignFac = Clamp(1.0 - align / ALIGN_SLOW_TOL, ALIGN_MIN_FAC, 1.0);
    double vmag = vel.Length();
    double velFac = vmag < 1.0 ? 1.0 : Clamp((vel / vmag).Dot(pathDir), VEL_MIN_FAC, 1.0);
    speed *= alignFac * velFac;

    Vector3D desiredVel = pathDir * speed;
    Vector3D dv = desiredVel - vel;

    // Coast in space once we're aligned and already at the target velocity: holding a
    // velocity in zero-g needs zero net force, so cut thrust entirely rather than
    // micro-trimming it every tick (the continuous pulsing is what drains fuel). In
    // gravity we always thrust - ApplyForce keeps its -grav*mass hover compensation,
    // which is exactly why running with dampeners off is safe on the planetary leg.
    bool inSpace = grav.LengthSquared() < 1e-3;
    if (inSpace && align < ALIGN_MOVE_TOL && dv.Length() < COAST_TOL)
        ZeroThrusters();
    else
        ApplyForce(dv * mass * VEL_GAIN - grav * mass);   // identical law to FlyToPose

    bool atEnd = cruiseIdx == legWps.Count - 1;
    return atEnd && dist < WpArriveRadius() && vmag < ARRIVE_SPEED;
}

void TickApproach(bool toDest)
{
    cruiseArmed = false;
    var c = GetConnector(toDest ? destConn : homeConn);
    DockPose p = toDest ? destPose : homePose;

    if (c != null && c.Status == MyShipConnectorStatus.Connected)
    {
        AbortAutopilot();
        ReleaseControl();
        c.Connect();
        phaseTimer = 0;
        OnDocked(toDest);
        return;
    }
    if (c != null && c.Status == MyShipConnectorStatus.Connectable)
        c.Connect();   // magnet range reached; keep steering until Connected confirms next tick

    if (rc.IsAutoPilotEnabled) AbortAutopilot();

    // Orientation-matched final approach: hold the recorded attitude while
    // translating straight down the connector axis into the dock.
    FlyToPose(p.Pos, p.Fwd, p.Up, 0.3);

    phaseTimer += dt;
    statusMsg = (toDest ? "Docking at destination" : "Docking at home")
              + " (" + Vector3D.Distance(rc.GetPosition(), p.Pos).ToString("0") + "m)";

    if (phaseTimer >= APPROACH_TIMEOUT)
    {
        AbortAutopilot();
        ReleaseControl();
        state = State.Faulted;
        statusMsg = "Docking timed out - check approach geometry";
    }
}

// ============================================================================
//  Docking controller (orientation-matched, works on any ship / any connector)
// ============================================================================
// Aligns the ship to a target attitude with the gyros and drives the Remote
// Control to a target position with the thrusters. Returns true once the ship
// has reached the point, matched the attitude, and slowed below ARRIVE_SPEED.
bool FlyToPose(Vector3D pos, Vector3D fwd, Vector3D up, double arriveDist)
{
    SetDampeners(false);   // controller drives translation; ApplyForce handles hover + stopping
    double align = AlignTo(fwd, up);

    Vector3D toTarget = pos - rc.GetPosition();
    double dist = toTarget.Length();
    Vector3D grav = rc.GetNaturalGravity();
    double mass = rc.CalculateShipMass().PhysicalMass;

    // Only translate once roughly aligned, so we never thrust off-axis into the dock.
    Vector3D desiredVel = Vector3D.Zero;
    if (align < ALIGN_MOVE_TOL && dist > 0.05)
    {
        double speedCap = Math.Min((double)dockSpeed, dist * APPROACH_KP);
        desiredVel = toTarget / dist * speedCap;
    }

    Vector3D vel = rc.GetShipVelocities().LinearVelocity;
    Vector3D force = (desiredVel - vel) * mass * VEL_GAIN - grav * mass;
    ApplyForce(force);

    return dist <= arriveDist && align < ALIGN_TOL && vel.Length() < ARRIVE_SPEED;
}

// PD cross-product attitude controller. The P term rotates toward the target
// attitude; the D term (actual angular velocity) damps the rotation so the ship
// settles cleanly instead of overshooting and wobbling. Gyro Pitch/Yaw/Roll are
// angular-velocity setpoints, so the command is desiredRate = Kp*err - Kd*angVel,
// clamped to a gentle max rate (PAM-style) that never hurts docking precision.
// Returns an error metric (~sin of the misalignment angle); near zero when
// forward AND up both match the target.
double AlignTo(Vector3D targetFwd, Vector3D targetUp) => AlignTo(targetFwd, targetUp, GyroCapRad());

double AlignTo(Vector3D targetFwd, Vector3D targetUp, double maxRad)
{
    Vector3D fErr = rc.WorldMatrix.Forward.Cross(targetFwd);
    Vector3D uErr = rc.WorldMatrix.Up.Cross(targetUp);
    Vector3D err = fErr + uErr;   // combined world-space rotation axis * angle

    // Inside the deadband, stop chasing the target: drop the P term so the command is
    // pure damping (-angVel*gyroDamp), which nulls any residual spin and then holds
    // still. This kills the constant micro-hunt around a heading that otherwise never
    // rests (and keeps re-trimming thrust, burning fuel).
    if (err.Length() < ALIGN_DEADBAND) err = Vector3D.Zero;

    Vector3D angVel = rc.GetShipVelocities().AngularVelocity;   // world rad/s
    Vector3D cmd = err * gyroGain - angVel * gyroDamp;

    // Cap the commanded angular rate so rotation stays gentle (rad/s, frame-independent).
    double m = cmd.Length();
    if (m > maxRad && m > 1e-6) cmd *= maxRad / m;

    foreach (var g in gyros)
    {
        if (g == null || !g.IsWorking) continue;
        Vector3D local = Vector3D.TransformNormal(cmd, MatrixD.Transpose(g.WorldMatrix));
        g.GyroOverride = true;
        g.Pitch = (float)(-local.X);
        g.Yaw   = (float)(-local.Y);
        g.Roll  = (float)(-local.Z);
    }
    return fErr.Length() + uErr.Length();
}

// Gyro angular-rate cap in rad/s. gyroRpmCap>0 uses that; otherwise PAM's gentle
// defaults by grid size (15 rpm small / 5 rpm large).
double GyroCapRad()
{
    double rpm = gyroRpmCap > 0 ? gyroRpmCap
               : (Me.CubeGrid.GridSizeEnum == MyCubeSize.Small ? 15.0 : 5.0);
    return rpm * 2.0 * Math.PI / 60.0;
}

// Distribute a desired world-space force across the thrusters. Each thruster
// pushes the ship along its own Backward axis; we bucket them into the six
// ship-local directions and split each axis's demand proportionally to thrust.
void ApplyForce(Vector3D worldForce)
{
    if (rc == null) return;
    double[] cap; MatrixD toLocal;
    AxisThrust(out cap, out toLocal);
    Vector3D lf = Vector3D.TransformNormal(worldForce, toLocal);

    // need[0..5] = demand along +X,-X,+Y,-Y,+Z,-Z (local), all >= 0
    double[] need = new double[6];
    need[0] = Math.Max(0, lf.X); need[1] = Math.Max(0, -lf.X);
    need[2] = Math.Max(0, lf.Y); need[3] = Math.Max(0, -lf.Y);
    need[4] = Math.Max(0, lf.Z); need[5] = Math.Max(0, -lf.Z);

    foreach (var t in thrusters)
    {
        if (t == null || !t.IsWorking) continue;
        int k = ThrustKey(t, toLocal);
        if (cap[k] <= 1e-3 || need[k] <= 1e-3) { t.ThrustOverride = 0f; continue; }
        double share = need[k] * (t.MaxEffectiveThrust / cap[k]);
        t.ThrustOverride = (float)Math.Min(share, t.MaxEffectiveThrust);
    }
}

// Sum each working thruster's MaxEffectiveThrust into its ship-local axis bucket
// (+X,-X,+Y,-Y,+Z,-Z). Shared by ApplyForce (allocation) and the velocity
// profile (available acceleration).
void AxisThrust(out double[] cap, out MatrixD toLocal)
{
    toLocal = MatrixD.Transpose(rc.WorldMatrix);
    cap = new double[6];
    foreach (var t in thrusters)
        if (t != null && t.IsWorking) cap[ThrustKey(t, toLocal)] += t.MaxEffectiveThrust;
}

// Which of the six ship-local directions this thruster pushes the ship.
int ThrustKey(IMyThrust t, MatrixD toLocal)
{
    Vector3D lp = Vector3D.TransformNormal(t.WorldMatrix.Backward, toLocal);
    double ax = Math.Abs(lp.X), ay = Math.Abs(lp.Y), az = Math.Abs(lp.Z);
    if (ax >= ay && ax >= az) return lp.X >= 0 ? 0 : 1;
    if (ay >= az)             return lp.Y >= 0 ? 2 : 3;
    return lp.Z >= 0 ? 4 : 5;
}

// Zero every thruster/gyro override so the autopilot (or the pilot) has control.
// Also restores dampeners, which the flight controller turns off so it can coast
// in space without the game braking the cruise. Runs on done/fault/stop/idle and on
// recompile, so the ship is never left adrift with dampeners disabled.
void ReleaseControl()
{
    foreach (var t in thrusters) if (t != null) t.ThrustOverride = 0f;
    foreach (var g in gyros)
        if (g != null) { g.GyroOverride = false; g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f; }
    SetDampeners(true);
}

// Zero only the thruster overrides (gyros keep holding attitude). With dampeners off,
// this is a true coast: the ship keeps its velocity and burns no fuel.
void ZeroThrusters()
{
    foreach (var t in thrusters) if (t != null) t.ThrustOverride = 0f;
}

// The controller owns the dampeners while flying: OFF so coasting in space costs no
// fuel and there's no automatic braking to fight; restored ON when control is released.
void SetDampeners(bool on)
{
    if (rc != null) rc.DampenersOverride = on;
}

void OnDocked(bool atDest)
{
    FinishLegMeasure();   // learn what the leg just flown actually cost in fuel/charge
    if (atDest)
    {
        state = State.Unloading;
        phaseTimer = 0;
    }
    else
    {
        // Home again. OneTrip and OneWay stop and hold here; Continuous loads and
        // sets out again (subject to the home departure trigger).
        if (runMode == RunMode.OneTrip) { operating = false; state = State.Idle; statusMsg = "Trip complete"; }
        else if (runMode == RunMode.OneWay) { operating = false; state = State.Idle; statusMsg = "Holding at home"; }
        else { state = State.Loading; phaseTimer = 0; }
    }
}

// ============================================================================
//  Helpers - blocks & sensors
// ============================================================================
void Discover()
{
    connectors.Clear(); cargo.Clear(); screens.Clear();
    var grid = Me.CubeGrid;

    // Remote Control
    if (!string.IsNullOrEmpty(remoteName))
        rc = GridTerminalSystem.GetBlockWithName(remoteName) as IMyRemoteControl;
    if (rc == null)
    {
        var rcs = new List<IMyRemoteControl>();
        GridTerminalSystem.GetBlocksOfType(rcs, b => b.CubeGrid == grid);
        rc = rcs.Count > 0 ? rcs[0] : null;
    }

    GridTerminalSystem.GetBlocksOfType(connectors, b => b.CubeGrid == grid);
    GridTerminalSystem.GetBlocksOfType(cargo, b => b.CubeGrid == grid);
    GridTerminalSystem.GetBlocksOfType(gyros, b => b.CubeGrid == grid);
    GridTerminalSystem.GetBlocksOfType(thrusters, b => b.CubeGrid == grid);
    GridTerminalSystem.GetBlocksOfType(batteries, b => b.CubeGrid == grid);

    // Gas tanks whose subtype names them a Hydrogen tank feed the fuel gate; oxygen
    // tanks are ignored. Ships with no hydrogen tanks just skip the hydrogen check.
    var tanks = new List<IMyGasTank>();
    GridTerminalSystem.GetBlocksOfType(tanks, b => b.CubeGrid == grid);
    h2Tanks.Clear();
    foreach (var t in tanks)
        if (t.BlockDefinition.SubtypeName.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0)
            h2Tanks.Add(t);

    // Sorters are found by tag: any conveyor sorter whose name contains the
    // load/unload tag (case-insensitive). Name them anything - only the tag
    // has to appear somewhere in the name. Multiple per role is fine.
    var sorters = new List<IMyConveyorSorter>();
    GridTerminalSystem.GetBlocksOfType(sorters, b => b.CubeGrid == grid);
    loadSorters.Clear(); unloadSorters.Clear();
    foreach (var s in sorters)
    {
        if (HasTag(s.CustomName, loadTag)) loadSorters.Add(s);
        if (HasTag(s.CustomName, unloadTag)) unloadSorters.Add(s);
    }

    // Status surfaces: any text panel whose name contains the tag, plus the PB.
    // A small font keeps the full status header + menu on screen without clipping.
    var panels = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(panels, b => b.CustomName.Contains(lcdTag));
    foreach (var p in panels) { PrepSurface(p); screens.Add(p); }
    pbSurface = Me.GetSurface(0);
    PrepSurface(pbSurface);
}

// Configure a surface for monospaced, left-aligned status text. The size is set
// per-render by WriteShipScreens (one shared size across all panels).
void PrepSurface(IMyTextSurface s)
{
    s.ContentType = ContentType.TEXT_AND_IMAGE;
    s.Font = "Monospace";
    s.Alignment = TextAlignment.LEFT;
    s.TextPadding = 0f;
}

IMyShipConnector ConnectedConnector()
{
    foreach (var c in connectors) if (c.Status == MyShipConnectorStatus.Connected) return c;
    return null;
}

IMyShipConnector GetConnector(string name)
{
    foreach (var c in connectors) if (c.CustomName == name) return c;
    return null;
}

// Am I physically connected to ANY connector right now? Name-independent, so it
// works even when the ship docks both ends with the same physical connector.
bool DockedNow() { return ConnectedConnector() != null; }

// Which recorded end is the ship physically at? Decided by proximity to the two
// recorded docked poses (distinct world coordinates ~78 km apart), NOT by the
// connector name - a shuttle that docks both ends with the SAME connector has
// homeConn == destConn, so a name match can't tell the ends apart. Assumes the
// two docked poses are separated by more than a ship length (true for any real
// home/station pair). Only meaningful when haveRoute.
bool AtHomeEnd()
{
    Vector3D p = rc.GetPosition();
    return Vector3D.DistanceSquared(p, homePose.Pos) <= Vector3D.DistanceSquared(p, destPose.Pos);
}

void SetSorters(List<IMyConveyorSorter> list, bool on)
{
    foreach (var s in list)
        if (s != null && s.Enabled != on) s.Enabled = on;
}

// Case-insensitive "does this block name contain the tag" test.
bool HasTag(string name, string tag)
{
    return !string.IsNullOrEmpty(tag) &&
           name.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0;
}

double ShipMassKg() { return rc != null ? rc.CalculateShipMass().PhysicalMass : 0; }

double CargoFillPct()
{
    double cur = 0, max = 0;
    foreach (var c in cargo)
    {
        var inv = c.GetInventory();
        cur += (double)inv.CurrentVolume;
        max += (double)inv.MaxVolume;
    }
    return max <= 0 ? 0 : cur / max * 100.0;
}

// ---- Remote / manual departure --------------------------------------------
// Accept DEPART commands broadcast by a station PB. Command messages are tagged
// "CMD|..." so they're never confused with the pipe-delimited status reports the
// ship itself broadcasts on the same channel (which the base consumes).
void DrainIgc()
{
    if (listener == null) return;
    while (listener.HasPendingMessage)
    {
        var m = listener.AcceptMessage();
        var s = m.Data as string;
        if (string.IsNullOrEmpty(s) || !s.StartsWith("CMD|")) continue;   // ignore status broadcasts
        var f = s.Split('|');
        if (f.Length >= 2 && f[1] == "DEPART")
        {
            string who = f.Length >= 3 ? f[2] : "*";
            if (who == "*" || who.Equals(shipName, StringComparison.OrdinalIgnoreCase))
                RequestDepart();
        }
    }
}

// Latch a manual departure. Only meaningful while holding at a dock; the latch is
// consumed when the shuttle actually leaves (and cleared by STOP / START).
void RequestDepart()
{
    if (state == State.Loading || state == State.Unloading)
    { departRequested = true; statusMsg = "Depart requested"; }
    else statusMsg = "DEPART: not holding at a dock";
}

// ---- Fuel / charge gate ----------------------------------------------------
// Current hydrogen fill across working H2 tanks, as a %. -1 when the ship has no
// hydrogen tanks, so the hydrogen gate simply doesn't apply (battery/ion ships).
double HydrogenPct()
{
    double cur = 0, cap = 0;
    foreach (var t in h2Tanks)
        if (t != null && t.IsWorking) { cap += t.Capacity; cur += t.FilledRatio * t.Capacity; }
    return cap <= 0 ? -1 : cur / cap * 100.0;
}

// Current battery charge across working batteries, as a %. -1 when there are none.
double BatteryPct()
{
    double cur = 0, cap = 0;
    foreach (var b in batteries)
        if (b != null && b.IsWorking) { cap += b.MaxStoredPower; cur += b.CurrentStoredPower; }
    return cap <= 0 ? -1 : cur / cap * 100.0;
}

// May the shuttle depart on fuel grounds? Requires each resource (that the ship
// actually has) to sit at or above the greater of its configured floor and the
// last measured consumption for this leg direction plus the safety margin.
bool DepartFuelOk(bool outbound, out string msg)
{
    double h2 = HydrogenPct();
    double batt = BatteryPct();
    double m = 1.0 + fuelMarginPct / 100.0;

    double needH2 = minHydrogenPct;
    double estH2 = outbound ? estHydroOut : estHydroHome;
    if (estH2 > 0) needH2 = Math.Max(needH2, estH2 * m);

    double needBatt = minBatteryPct;
    double estB = outbound ? estBattOut : estBattHome;
    if (estB > 0) needBatt = Math.Max(needBatt, estB * m);

    if (h2 >= 0 && h2 < needH2)
    { msg = "Hold: H2 " + h2.ToString("0") + "% < " + needH2.ToString("0") + "% to depart"; return false; }
    if (batt >= 0 && batt < needBatt)
    { msg = "Hold: Batt " + batt.ToString("0") + "% < " + needBatt.ToString("0") + "% to depart"; return false; }
    msg = "";
    return true;
}

// Snapshot fuel/charge at the start of a leg so its consumption can be measured on
// arrival. `outbound` = home->dest; else dest->home.
void BeginLegMeasure(bool outbound)
{
    legOutbound = outbound;
    legStartH2 = HydrogenPct();
    legStartBatt = BatteryPct();
}

// On arrival, record how much this leg burned (per direction) and persist it, so
// the fuel gate learns the real cost of the run. Skipped if no leg was in progress
// (e.g. a recompile mid-flight lost the start snapshot) - the prior estimate holds.
void FinishLegMeasure()
{
    if (legStartH2 < 0 && legStartBatt < 0) return;
    double h2 = HydrogenPct(), batt = BatteryPct();
    if (legOutbound)
    {
        if (legStartH2 >= 0 && h2 >= 0) estHydroOut = Math.Max(0, legStartH2 - h2);
        if (legStartBatt >= 0 && batt >= 0) estBattOut = Math.Max(0, legStartBatt - batt);
    }
    else
    {
        if (legStartH2 >= 0 && h2 >= 0) estHydroHome = Math.Max(0, legStartH2 - h2);
        if (legStartBatt >= 0 && batt >= 0) estBattHome = Math.Max(0, legStartBatt - batt);
    }
    legStartH2 = -1; legStartBatt = -1;
    SaveEstimates();
}

void AbortAutopilot()
{
    if (rc == null) return;
    rc.SetAutoPilotEnabled(false);
    rc.ClearWaypoints();
    cruiseArmed = false;
}

// ============================================================================
//  ETA
// ============================================================================
string FormatEta()
{
    double dist = RemainingDistance();
    double spd = rc != null ? rc.GetShipSpeed() : 0;
    if (spd < 1) return "--:--";
    int sec = (int)(dist / spd);
    return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
}

// Remaining distance along the current leg: ship -> current waypoint, then each
// remaining leg segment through to the final stand-off. Drives the ETA and the
// base-board distance readout.
double RemainingDistance()
{
    if (rc == null || !cruiseArmed || legWps.Count == 0) return 0;
    if (cruiseIdx >= legWps.Count) return 0;
    double d = Vector3D.Distance(rc.GetPosition(), legWps[cruiseIdx]);
    for (int i = cruiseIdx; i < legWps.Count - 1; i++)
        d += Vector3D.Distance(legWps[i], legWps[i + 1]);
    return d;
}

// ============================================================================
//  LCD menu (ship role)
// ============================================================================
int MenuCount()
{
    switch (menuPage)
    {
        case PAGE_MAIN:     return 6;   // Start/Stop, Run Mode, Depart Now, Go Home, Record, Settings
        case PAGE_RECORD:   return 4;   // Home, Dest, Clear, Back
        case PAGE_SETTINGS: return 6;   // Cruise, Dock, MaxMass, DepartFill, Depart page, Back
        case PAGE_DEPART:   return 7;   // Home trig, Dest trig, Dwell, MinH2, MinBatt, Margin, Back
        default:            return 1;
    }
}

void MenuMove(int dir)
{
    if (editing) { AdjustEdit(dir); return; }
    int n = MenuCount();
    menuIndex = ((menuIndex + dir) % n + n) % n;
}

void MenuApply()
{
    if (editing) { CommitEdit(); editing = false; return; }

    if (menuPage == PAGE_MAIN)
    {
        switch (menuIndex)
        {
            case 0: HandleCommand(operating ? "STOP" : "START"); break;
            case 1: CycleMode(); break;
            case 2: HandleCommand("DEPART"); break;
            case 3: HandleCommand("HOME"); break;
            case 4: menuPage = PAGE_RECORD; menuIndex = 0; break;
            case 5: menuPage = PAGE_SETTINGS; menuIndex = 0; break;
        }
    }
    else if (menuPage == PAGE_RECORD)
    {
        switch (menuIndex)
        {
            case 0: RecordHome(); break;
            case 1: RecordDest(); break;
            case 2: ClearRoute(); statusMsg = "Route cleared"; break;
            case 3: menuPage = PAGE_MAIN; menuIndex = 4; break;
        }
    }
    else if (menuPage == PAGE_SETTINGS)
    {
        switch (menuIndex)
        {
            case 0: BeginEdit(cruiseSpeed); break;
            case 1: BeginEdit(dockSpeed); break;
            case 2: BeginEdit(maxMassKg / 1000.0); break;   // edit in tonnes
            case 3: BeginEdit(departFill); break;
            case 4: menuPage = PAGE_DEPART; menuIndex = 0; break;
            case 5: menuPage = PAGE_MAIN; menuIndex = 5; break;
        }
    }
    else if (menuPage == PAGE_DEPART)
    {
        switch (menuIndex)
        {
            case 0: CycleTrigger(true); break;
            case 1: CycleTrigger(false); break;
            case 2: BeginEdit(dwellSec); break;
            case 3: BeginEdit(minHydrogenPct); break;
            case 4: BeginEdit(minBatteryPct); break;
            case 5: BeginEdit(fuelMarginPct); break;
            case 6: menuPage = PAGE_SETTINGS; menuIndex = 4; break;
        }
    }
}

void MenuBack()
{
    if (editing) { editing = false; statusMsg = "Edit cancelled"; return; }
    if (menuPage == PAGE_DEPART) { menuPage = PAGE_SETTINGS; menuIndex = 4; }
    else if (menuPage != PAGE_MAIN) { menuPage = PAGE_MAIN; menuIndex = 0; }
}

void CycleMode()
{
    runMode = runMode == RunMode.Continuous ? RunMode.OneTrip
            : runMode == RunMode.OneTrip ? RunMode.OneWay
            : RunMode.Continuous;
    string s = runMode == RunMode.OneTrip ? "ONETRIP"
             : runMode == RunMode.OneWay ? "ONEWAY" : "CONTINUOUS";
    SaveCfg("runMode", s);
    statusMsg = "Mode = " + runMode;
}

// Cycle a per-connector departure trigger (APPLY on the Depart page), persisting it.
void CycleTrigger(bool home)
{
    DepartTrigger t = home ? homeTrigger : destTrigger;
    t = t == DepartTrigger.Auto ? DepartTrigger.Cargo
      : t == DepartTrigger.Cargo ? DepartTrigger.Timer
      : t == DepartTrigger.Timer ? DepartTrigger.Manual
      : DepartTrigger.Auto;
    if (home) homeTrigger = t; else destTrigger = t;
    SaveCfg(home ? "homeTrigger" : "destTrigger", t.ToString());
    statusMsg = (home ? "Home" : "Dest") + " trigger = " + t;
}

void BeginEdit(double v) { editing = true; editValue = v; }

double EditStep()
{
    if (menuPage == PAGE_SETTINGS)
        switch (menuIndex)
        {
            case 0: return 5;      // cruise m/s
            case 1: return 0.5;    // dock m/s
            case 2: return 1;      // max mass tonnes
            case 3: return 5;      // depart fill %
        }
    if (menuPage == PAGE_DEPART) return 5;   // dwell s / min H2 % / min batt % / margin %
    return 1;
}

void AdjustEdit(int dir) { editValue = Math.Round(editValue + dir * EditStep(), 2); }

void CommitEdit()
{
    if (menuPage == PAGE_SETTINGS)
        switch (menuIndex)
        {
            case 0: cruiseSpeed = (float)Clamp(editValue, 5, 1000); SaveCfg("cruiseSpeed", cruiseSpeed); break;
            case 1: dockSpeed   = (float)Clamp(editValue, 0.5, 20); SaveCfg("dockSpeed", dockSpeed); break;
            case 2: maxMassKg   = Clamp(editValue, 0, 100000) * 1000.0; SaveCfg("maxMassKg", maxMassKg); break;
            case 3: departFill  = Clamp(editValue, 0, 100); SaveCfg("departFill", departFill); break;
        }
    else if (menuPage == PAGE_DEPART)
        switch (menuIndex)
        {
            case 2: dwellSec       = Clamp(editValue, 0, 3600); SaveCfg("dwellSec", dwellSec); break;
            case 3: minHydrogenPct = Clamp(editValue, 0, 100); SaveCfg("minHydrogenPct", minHydrogenPct); break;
            case 4: minBatteryPct  = Clamp(editValue, 0, 100); SaveCfg("minBatteryPct", minBatteryPct); break;
            case 5: fuelMarginPct  = Clamp(editValue, 0, 200); SaveCfg("fuelMarginPct", fuelMarginPct); break;
        }
    statusMsg = "Saved";
}

double Clamp(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

void SaveCfg(string key, object val)
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    ini.Set("shuttle", key, val.ToString());
    Me.CustomData = ini.ToString();
}

// Builds the labels for the current page; substitutes the working value while editing.
List<string> MenuLabels()
{
    var l = new List<string>();
    if (menuPage == PAGE_MAIN)
    {
        l.Add(operating ? "Stop" : "Start");
        l.Add("Mode: " + runMode);
        l.Add("Depart Now");
        l.Add("Go Home");
        l.Add("Record >>");
        l.Add("Settings >>");
    }
    else if (menuPage == PAGE_RECORD)
    {
        l.Add("Record Home");
        l.Add("Record Dest");
        l.Add("Clear Route");
        l.Add("<< Back");
    }
    else if (menuPage == PAGE_SETTINGS)
    {
        l.Add("Cruise: " + FmtSetting(0, cruiseSpeed) + " m/s");
        l.Add("Dock: " + FmtSetting(1, dockSpeed) + " m/s");
        l.Add("MaxMass: " + FmtSetting(2, maxMassKg / 1000.0) + "t" + (maxMassKg <= 0 ? " off" : ""));
        l.Add("Fill: " + FmtSetting(3, departFill) + " %");
        l.Add("Depart >>");
        l.Add("<< Back");
    }
    else if (menuPage == PAGE_DEPART)
    {
        l.Add("Home trig: " + homeTrigger);
        l.Add("Dest trig: " + destTrigger);
        l.Add("Dwell: " + FmtSetting(2, dwellSec) + " s");
        l.Add("Min H2: " + FmtSetting(3, minHydrogenPct) + " %");
        l.Add("Min Bat: " + FmtSetting(4, minBatteryPct) + " %");
        l.Add("Margin: " + FmtSetting(5, fuelMarginPct) + " %");
        l.Add("<< Back");
    }
    return l;
}

string FmtSetting(int idx, double current)
{
    bool active = editing && menuIndex == idx;
    double v = active ? editValue : current;
    string s = v.ToString("0.##");
    return active ? "[" + s + "]" : s;
}

string PageName()
{
    return menuPage == PAGE_RECORD ? "RECORD"
         : menuPage == PAGE_SETTINGS ? "SETTINGS"
         : menuPage == PAGE_DEPART ? "DEPART" : "MAIN";
}

// ============================================================================
//  Displays (ship) - status header + interactive menu
// ============================================================================
void RenderShip()
{
    var sb = new StringBuilder();
    // Header: ship + short state + run flag on one compact line.
    sb.Append(shipName).Append(' ').Append(ShipState())
      .Append(operating ? " [RUN]" : " [STOP]").Append('\n');
    sb.Append("Cargo ").Append(CargoFillPct().ToString("0")).Append("% ")
      .Append((ShipMassKg() / 1000.0).ToString("0")).Append("t ")
      .Append((rc != null ? rc.GetShipSpeed() : 0).ToString("0")).Append("m/s\n");
    sb.Append(haveRoute ? ("Route " + path.Count + "wp") : "Route: none").Append('\n');
    if (state == State.CruiseToDest || state == State.CruiseToHome)
        sb.Append("ETA ").Append(FormatEta()).Append(' ')
          .Append((RemainingDistance() / 1000.0).ToString("0.0")).Append("km\n");
    sb.Append(statusMsg).Append('\n');

    // Interactive menu
    sb.Append("-- ").Append(PageName()).Append(" --\n");
    var labels = MenuLabels();
    for (int i = 0; i < labels.Count; i++)
        sb.Append(i == menuIndex ? "> " : "  ").Append(labels[i]).Append('\n');
    sb.Append(editing ? "UP/DN +/-  APPLY save" : "UP/DN  APPLY  BACK");

    string text = WrapText(sb.ToString(), WRAP_COLS);
    WriteShipScreens(text);
    Echo(text);
}

// Short, single-word state label for the compact header.
string ShipState()
{
    switch (state)
    {
        case State.Loading:      return "Loading";
        case State.UndockHome:   return "Undock";
        case State.CruiseToDest: return "Cruise >";
        case State.ApproachDest: return "Docking >";
        case State.Unloading:    return "Unloading";
        case State.UndockDest:   return "Undock";
        case State.CruiseToHome: return "Cruise <";
        case State.ApproachHome: return "Docking <";
        case State.Recording:    return "Recording";
        case State.Faulted:      return "FAULT";
        default:                 return "Idle";
    }
}

// Render the same text on every ship screen at ONE shared font size. The size
// is the largest that still fits the most-constrained tagged LCD, so all the
// status panels read identically and nothing clips. The PB's own screen is
// written too but never drives the size (it is tiny and only a fallback), so it
// can't shrink the wall LCDs. Word-wrap keeps any single line inside the budget.
void WriteShipScreens(string text)
{
    var probe = new StringBuilder(text);
    // Panels the operator actually reads size the font; fall back to the PB
    // screen only when no LCDs are tagged.
    List<IMyTextSurface> sizing = screens.Count > 0
        ? screens
        : new List<IMyTextSurface> { pbSurface };

    float size = 0f; bool have = false;
    foreach (var s in sizing)
    {
        if (s == null) continue;
        var m = s.MeasureStringInPixels(probe, s.Font, 1f);
        if (m.X < 1 || m.Y < 1) continue;
        Vector2 area = s.SurfaceSize;
        float fit = Math.Min(area.X / m.X, area.Y / m.Y) * 0.95f;
        if (!have || fit < size) { size = fit; have = true; }   // most-constrained wins
    }
    if (have) size = (float)Clamp(size, 0.4, 3.0);

    foreach (var s in screens) { if (s == null) continue; if (have) s.FontSize = size; s.WriteText(text); }
    if (pbSurface != null) { if (have) pbSurface.FontSize = size; pbSurface.WriteText(text); }
}

// Word-wrap to a fixed column count. Monospace => columns == characters, so this
// guarantees no single (possibly long) status line dictates the shared font.
string WrapText(string text, int cols)
{
    var outSb = new StringBuilder();
    foreach (var line in text.Split('\n'))
    {
        if (line.Length <= cols) { outSb.Append(line).Append('\n'); continue; }
        int col = 0;
        foreach (var raw in line.Split(' '))
        {
            string w = raw;
            // Hard-break a single word longer than the budget (e.g. a long name).
            while (w.Length > cols)
            {
                if (col > 0) { outSb.Append('\n'); col = 0; }
                outSb.Append(w.Substring(0, cols)).Append('\n');
                w = w.Substring(cols);
            }
            if (col == 0) { outSb.Append(w); col = w.Length; }
            else if (col + 1 + w.Length <= cols) { outSb.Append(' ').Append(w); col += 1 + w.Length; }
            else { outSb.Append('\n').Append(w); col = w.Length; }
        }
        outSb.Append('\n');
    }
    if (outSb.Length > 0 && outSb[outSb.Length - 1] == '\n') outSb.Length--;
    return outSb.ToString();
}

// ============================================================================
//  Broadcast (ship -> base)
// ============================================================================
void Broadcast()
{
    // Pipe-delimited: name|state|etaSec|distM|fill|massT|running
    double distM = 0; int etaSec = -1;
    if (state == State.CruiseToDest || state == State.CruiseToHome)
    {
        distM = RemainingDistance();
        double spd = rc.GetShipSpeed();
        if (spd >= 1) etaSec = (int)(distM / spd);
    }
    string msg = string.Join("|", new[]
    {
        shipName,
        state.ToString(),
        etaSec.ToString(),
        ((int)distM).ToString(),
        CargoFillPct().ToString("0"),
        (ShipMassKg() / 1000.0).ToString("0.0"),
        operating ? "1" : "0"
    });
    IGC.SendBroadcastMessage(channel, msg);
}

// ============================================================================
//  Base role - listen & render
// ============================================================================
class ShuttleReport
{
    public string Name, State;
    public int EtaSec, DistM, Fill;
    public double MassT;
    public bool Running;
    public double Age;   // seconds since last update
}

void RunBase()
{
    if (listener == null) listener = IGC.RegisterBroadcastListener(channel);

    while (listener.HasPendingMessage)
    {
        var m = listener.AcceptMessage();
        var s = m.Data as string;
        if (s == null) continue;
        var f = s.Split('|');
        if (f.Length < 7) continue;
        var r = new ShuttleReport
        {
            Name = f[0],
            State = f[1],
            EtaSec = ParseInt(f[2], -1),
            DistM = ParseInt(f[3], 0),
            Fill = ParseInt(f[4], 0),
            MassT = ParseDouble(f[5], 0),
            Running = f[6] == "1",
            Age = 0
        };
        fleet[r.Name] = r;
    }

    foreach (var r in fleet.Values) r.Age += dt;

    var sb = new StringBuilder();
    sb.Append("== Shuttle Board v").Append(VERSION).Append(" ==\n\n");
    if (fleet.Count == 0) sb.Append("Waiting for shuttle signal...\n");
    foreach (var r in fleet.Values)
    {
        if (r.Age > 20) { sb.Append(r.Name).Append(": NO SIGNAL (").Append((int)r.Age).Append("s)\n\n"); continue; }
        sb.Append(r.Name).Append(": ").Append(PrettyState(r.State)).Append('\n');
        if (r.EtaSec >= 0)
            sb.Append("   ETA ").Append((r.EtaSec / 60).ToString("00")).Append(':').Append((r.EtaSec % 60).ToString("00"))
              .Append("   ").Append((r.DistM / 1000.0).ToString("0.0")).Append(" km\n");
        sb.Append("   Cargo ").Append(r.Fill).Append("%   ").Append(r.MassT.ToString("0.0")).Append("t\n\n");
    }

    var text = sb.ToString();
    Echo(text);
    var panels = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(panels, b => b.CustomName.Contains(lcdTag));
    foreach (var p in panels) { p.ContentType = ContentType.TEXT_AND_IMAGE; p.WriteText(text); }
    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
    Me.GetSurface(0).WriteText(text);
}

string PrettyState(string s)
{
    switch (s)
    {
        case "Loading":      return "Loading at home";
        case "CruiseToDest": return "En route to station";
        case "ApproachDest": return "Docking at station";
        case "Unloading":    return "Unloading at station";
        case "CruiseToHome": return "Returning home";
        case "ApproachHome": return "Docking at home";
        case "Idle":         return "Idle";
        case "Faulted":      return "FAULT - needs attention";
        default:             return s;
    }
}

// ============================================================================
//  Persistence & config
// ============================================================================
void WriteConfigTemplate()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);   // keep any existing [route]/[state] if present
    WriteShuttleSection(ini);
    Me.CustomData = ini.ToString();
}

// Ensure every known [shuttle] key exists in Custom Data, seeding any that a
// newer script version added with the value currently in effect (the loaded
// value, or the default if the key was absent). Runs on compile so upgrading the
// script surfaces its new tuning keys WITHOUT wiping the recorded [route]/[state].
void BackfillConfig()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    WriteShuttleSection(ini);
    Me.CustomData = ini.ToString();
}

// Write the full [shuttle] key set from the current field values into an ini,
// leaving all other sections untouched. Shared by the first-run template and the
// on-compile backfill.
void WriteShuttleSection(MyIni ini)
{
    string modeStr = runMode == RunMode.OneTrip ? "ONETRIP"
                   : runMode == RunMode.OneWay ? "ONEWAY" : "CONTINUOUS";
    ini.Set("shuttle", "role", role == Role.Base ? "base" : "shuttle");
    ini.Set("shuttle", "shipName", shipName);
    ini.Set("shuttle", "channel", channel);
    ini.Set("shuttle", "runMode", modeStr);
    ini.Set("shuttle", "homeTrigger", homeTrigger.ToString());
    ini.Set("shuttle", "destTrigger", destTrigger.ToString());
    ini.Set("shuttle", "remoteName", remoteName);
    ini.Set("shuttle", "loadTag", loadTag);
    ini.Set("shuttle", "unloadTag", unloadTag);
    ini.Set("shuttle", "lcdTag", lcdTag);
    ini.Set("shuttle", "cruiseSpeed", cruiseSpeed);
    ini.Set("shuttle", "dockSpeed", dockSpeed);
    ini.Set("shuttle", "maxMassKg", maxMassKg);
    ini.Set("shuttle", "departFill", departFill);
    ini.Set("shuttle", "unloadDrainSec", unloadDrainSec);
    ini.Set("shuttle", "dwellSec", dwellSec);
    ini.Set("shuttle", "minHydrogenPct", minHydrogenPct);
    ini.Set("shuttle", "minBatteryPct", minBatteryPct);
    ini.Set("shuttle", "fuelMarginPct", fuelMarginPct);
    ini.Set("shuttle", "segMeters", segMeters);
    ini.Set("shuttle", "turnDegrees", turnDegrees);
    ini.Set("shuttle", "approachDist", approachDist);
    ini.Set("shuttle", "gyroRpmCap", gyroRpmCap);
    ini.Set("shuttle", "brakeFrac", brakeFrac);
    ini.Set("shuttle", "cornerLen", cornerLen);
    ini.Set("shuttle", "gyroGain", gyroGain);
    ini.Set("shuttle", "gyroDamp", gyroDamp);
}

void LoadConfig()
{
    var ini = new MyIni();
    if (!ini.TryParse(Me.CustomData)) return;
    role = ini.Get("shuttle", "role").ToString("shuttle").Trim().ToLower() == "base" ? Role.Base : Role.Shuttle;
    shipName = ini.Get("shuttle", "shipName").ToString(shipName);
    channel = ini.Get("shuttle", "channel").ToString(channel);
    // Run mode. Legacy WAITFULL folds into Continuous + a Cargo home trigger, but
    // only supplies that default if no explicit homeTrigger key is present, so a new
    // config's homeTrigger always wins.
    string modeStr = ini.Get("shuttle", "runMode").ToString("CONTINUOUS").Trim().ToUpperInvariant();
    string defHome = "Auto";
    if (modeStr == "WAITFULL") { runMode = RunMode.Continuous; defHome = "Cargo"; }
    else SetModeSilent(modeStr);
    homeTrigger = TrigFromString(ini.Get("shuttle", "homeTrigger").ToString(defHome));
    destTrigger = TrigFromString(ini.Get("shuttle", "destTrigger").ToString("Auto"));
    remoteName = ini.Get("shuttle", "remoteName").ToString("");
    // Sorter tags; fall back to the legacy exact-name keys (a full name still
    // matches as a substring tag), else the defaults.
    loadTag = ini.Get("shuttle", "loadTag").ToString(ini.Get("shuttle", "loadSorter").ToString(loadTag));
    unloadTag = ini.Get("shuttle", "unloadTag").ToString(ini.Get("shuttle", "unloadSorter").ToString(unloadTag));
    lcdTag = ini.Get("shuttle", "lcdTag").ToString(lcdTag);
    cruiseSpeed = (float)ini.Get("shuttle", "cruiseSpeed").ToDouble(cruiseSpeed);
    dockSpeed = (float)ini.Get("shuttle", "dockSpeed").ToDouble(dockSpeed);
    maxMassKg = ini.Get("shuttle", "maxMassKg").ToDouble(maxMassKg);
    departFill = ini.Get("shuttle", "departFill").ToDouble(departFill);
    unloadDrainSec = ini.Get("shuttle", "unloadDrainSec").ToDouble(unloadDrainSec);
    dwellSec = ini.Get("shuttle", "dwellSec").ToDouble(dwellSec);
    minHydrogenPct = Clamp(ini.Get("shuttle", "minHydrogenPct").ToDouble(minHydrogenPct), 0, 100);
    minBatteryPct = Clamp(ini.Get("shuttle", "minBatteryPct").ToDouble(minBatteryPct), 0, 100);
    fuelMarginPct = Math.Max(0, ini.Get("shuttle", "fuelMarginPct").ToDouble(fuelMarginPct));
    segMeters = ini.Get("shuttle", "segMeters").ToDouble(segMeters);
    turnDegrees = ini.Get("shuttle", "turnDegrees").ToDouble(turnDegrees);
    approachDist = ini.Get("shuttle", "approachDist").ToDouble(approachDist);
    gyroRpmCap = (float)ini.Get("shuttle", "gyroRpmCap").ToDouble(gyroRpmCap);
    brakeFrac = Clamp(ini.Get("shuttle", "brakeFrac").ToDouble(brakeFrac), 0.1, 1.0);
    cornerLen = Math.Max(1.0, ini.Get("shuttle", "cornerLen").ToDouble(cornerLen));
    gyroGain = Math.Max(0.1, ini.Get("shuttle", "gyroGain").ToDouble(gyroGain));
    gyroDamp = Math.Max(0.0, ini.Get("shuttle", "gyroDamp").ToDouble(gyroDamp));
}

void SetModeSilent(string m)
{
    switch (m.Trim().ToUpperInvariant())
    {
        case "ONETRIP":  runMode = RunMode.OneTrip; break;
        case "ONEWAY":   runMode = RunMode.OneWay; break;
        default:         runMode = RunMode.Continuous; break;   // incl. legacy WAITFULL (home trigger set by LoadConfig)
    }
}

// Parse a DepartTrigger name (case-insensitive), defaulting to Auto.
DepartTrigger TrigFromString(string s)
{
    switch (s.Trim().ToUpperInvariant())
    {
        case "CARGO":  return DepartTrigger.Cargo;
        case "TIMER":  return DepartTrigger.Timer;
        case "MANUAL": return DepartTrigger.Manual;
        default:       return DepartTrigger.Auto;
    }
}

void SaveRoute()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    ini.Set("route", "homeConn", homeConn);
    ini.Set("route", "destConn", destConn);
    // Full docked pose (position + orientation + connector axis) at each end.
    ini.Set("route", "homePos", Vec(homePose.Pos));
    ini.Set("route", "homeFwd", Vec(homePose.Fwd));
    ini.Set("route", "homeUp", Vec(homePose.Up));
    ini.Set("route", "homeConnFwd", Vec(homePose.ConnFwd));
    ini.Set("route", "destPos", Vec(destPose.Pos));
    ini.Set("route", "destFwd", Vec(destPose.Fwd));
    ini.Set("route", "destUp", Vec(destPose.Up));
    ini.Set("route", "destConnFwd", Vec(destPose.ConnFwd));
    var sb = new StringBuilder();
    for (int i = 0; i < path.Count; i++) { if (i > 0) sb.Append(';'); sb.Append(Vec(path[i])); }
    ini.Set("route", "path", sb.ToString());
    Me.CustomData = ini.ToString();
}

void LoadRoute()
{
    var ini = new MyIni();
    if (!ini.TryParse(Me.CustomData)) return;
    if (!ini.ContainsSection("route")) return;
    homeConn = ini.Get("route", "homeConn").ToString("");
    destConn = ini.Get("route", "destConn").ToString("");

    // Position: prefer new keys, fall back to the legacy homeDock/destDock keys.
    bool haveHP = LoadPos(ini, "homePos", "homeDock", out homePose.Pos);
    bool haveDP = LoadPos(ini, "destPos", "destDock", out destPose.Pos);

    // Orientation: present in v0.3.0+ routes. Older routes get poses synthesised
    // from the flight-path geometry (nose-first, which is all they supported).
    bool haveOri = TryVec(ini.Get("route", "homeFwd").ToString(""), out homePose.Fwd)
                 & TryVec(ini.Get("route", "homeUp").ToString(""), out homePose.Up)
                 & TryVec(ini.Get("route", "homeConnFwd").ToString(""), out homePose.ConnFwd)
                 & TryVec(ini.Get("route", "destFwd").ToString(""), out destPose.Fwd)
                 & TryVec(ini.Get("route", "destUp").ToString(""), out destPose.Up)
                 & TryVec(ini.Get("route", "destConnFwd").ToString(""), out destPose.ConnFwd);

    path.Clear();
    var raw = ini.Get("route", "path").ToString("");
    if (!string.IsNullOrEmpty(raw))
        foreach (var token in raw.Split(';'))
        {
            Vector3D v;
            if (TryVec(token, out v)) path.Add(v);
        }

    if (!haveOri) SynthesizePoses();
    haveRoute = haveHP && haveDP && path.Count > 0 && homeConn != "" && destConn != "";
}

// Read a position, preferring the primary key and falling back to a legacy one.
bool LoadPos(MyIni ini, string primary, string legacy, out Vector3D pos)
{
    if (TryVec(ini.Get("route", primary).ToString(""), out pos)) return true;
    return TryVec(ini.Get("route", legacy).ToString(""), out pos);
}

// Derive orientation for a legacy route (position-only) from its path geometry.
// Assumes the ship left home and arrived at the destination nose-first.
void SynthesizePoses()
{
    if (path.Count >= 2)
    {
        Vector3D outDir = Vector3D.Normalize(path[1] - path[0]);                          // departing home
        Vector3D inDir  = Vector3D.Normalize(path[path.Count - 1] - path[path.Count - 2]); // arriving dest
        homePose.Fwd = outDir; homePose.ConnFwd = -outDir;   // stand-off is out along departure dir
        destPose.Fwd = inDir;  destPose.ConnFwd = inDir;     // stand-off is behind along arrival dir
    }
    else
    {
        homePose.Fwd = rc != null ? rc.WorldMatrix.Forward : Vector3D.Forward;
        destPose.Fwd = homePose.Fwd;
        homePose.ConnFwd = homePose.Fwd;
        destPose.ConnFwd = destPose.Fwd;
    }
    homePose.Up = UpAt(homePose.Pos);
    destPose.Up = UpAt(destPose.Pos);
}

// Up = away from gravity where we are now (best available for a legacy route);
// falls back to the ship's current up in zero-g.
Vector3D UpAt(Vector3D pos)
{
    Vector3D g = rc != null ? rc.GetNaturalGravity() : Vector3D.Zero;
    return g.LengthSquared() > 1e-3 ? Vector3D.Normalize(-g)
         : rc != null ? rc.WorldMatrix.Up : Vector3D.Up;
}

void ClearRoute()
{
    haveRoute = false; path.Clear(); homeConn = ""; destConn = "";
    homePose = new DockPose(); destPose = new DockPose();
    var ini = new MyIni(); ini.TryParse(Me.CustomData);
    ini.DeleteSection("route");
    Me.CustomData = ini.ToString();
}

void LoadState()
{
    var ini = new MyIni();
    if (!ini.TryParse(Me.CustomData) || !ini.ContainsSection("state")) return;
    Enum.TryParse(ini.Get("state", "state").ToString("Idle"), out state);
    operating = ini.Get("state", "operating").ToBoolean(false);
    phaseTimer = ini.Get("state", "phaseTimer").ToDouble(0);
    estHydroOut = ini.Get("state", "estHydroOut").ToDouble(0);
    estBattOut = ini.Get("state", "estBattOut").ToDouble(0);
    estHydroHome = ini.Get("state", "estHydroHome").ToDouble(0);
    estBattHome = ini.Get("state", "estBattHome").ToDouble(0);
    cruiseArmed = false;  // always re-arm autopilot after a recompile
}

// Persist just the learned per-leg fuel/charge estimates (called on arrival), so a
// recompile doesn't forget what the run costs.
void SaveEstimates()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    ini.Set("state", "estHydroOut", estHydroOut);
    ini.Set("state", "estBattOut", estBattOut);
    ini.Set("state", "estHydroHome", estHydroHome);
    ini.Set("state", "estBattHome", estBattHome);
    Me.CustomData = ini.ToString();
}

// ---- Vector <-> string -----------------------------------------------------
string Vec(Vector3D v)
{
    return v.X.ToString("R") + ":" + v.Y.ToString("R") + ":" + v.Z.ToString("R");
}
bool TryVec(string s, out Vector3D v)
{
    v = Vector3D.Zero;
    if (string.IsNullOrEmpty(s)) return false;
    var p = s.Split(':');
    if (p.Length != 3) return false;
    double x, y, z;
    if (!double.TryParse(p[0], out x) || !double.TryParse(p[1], out y) || !double.TryParse(p[2], out z)) return false;
    v = new Vector3D(x, y, z);
    return true;
}

int ParseInt(string s, int def) { int r; return int.TryParse(s, out r) ? r : def; }
double ParseDouble(string s, double def) { double r; return double.TryParse(s, out r) ? r : def; }
