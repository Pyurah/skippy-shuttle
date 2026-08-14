const string VERSION = "0.14.1";
enum Role { Shuttle, Base }
enum RunMode { Continuous, OneTrip, OneWay }
enum DepartTrigger { Auto, Cargo, Timer, Manual }
struct DockPose
{
    public Vector3D Pos;
    public Vector3D Fwd;
    public Vector3D Up;
    public Vector3D ConnFwd;
}
enum State
{
    Idle,
    Loading,
    UndockHome,
    CruiseToDest,
    ApproachDest,
    Unloading,
    UndockDest,
    CruiseToHome,
    ApproachHome,
    Recording,
    Faulted
}
Role role = Role.Shuttle;
RunMode runMode = RunMode.Continuous;
DepartTrigger homeTrigger = DepartTrigger.Auto;
DepartTrigger destTrigger = DepartTrigger.Auto;
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
double dwellSec = 30;
double minHydrogenPct = 10;
double minBatteryPct = 10;
double fuelMarginPct = 25;
double segMeters = 250;
double turnDegrees = 12;
double simplifyMeters = 15;
double approachDist = 15;
float gyroRpmCap = 0f;
double brakeFrac = 0.6;
double cornerLen = 30;
double gyroGain = 4.0;
double gyroDamp = 3.0;
string cruiseAttitude = "auto";
DockPose homePose, destPose;
string homeConn = "", destConn = "";
List<Vector3D> path = new List<Vector3D>();
bool haveRoute = false;
State state = State.Idle;
bool operating = false;
string statusMsg = "Idle";
double phaseTimer = 0;
bool departRequested = false;
double estHydroOut = 0, estBattOut = 0;
double estHydroHome = 0, estBattHome = 0;
double legStartH2 = -1, legStartBatt = -1;
bool legOutbound = true;
List<Vector3D> legWps = new List<Vector3D>();
List<double> legVmax = new List<double>();
int cruiseIdx = 0;
double cruiseAccel = 1.0;
double cruiseProgTimer = 0;
double cruiseBestDist = double.MaxValue;
bool cruiseFlyLevel = false;
bool gyroResting = false;
const string VIEW_FULL = "full", VIEW_MENU = "menu", VIEW_STATUS = "status", VIEW_TRIP = "trip";
const int PAGE_MAIN = 0, PAGE_RECORD = 1, PAGE_SETTINGS = 2, PAGE_DEPART = 3;
int menuPage = PAGE_MAIN;
int menuIndex = 0;
bool editing = false;
double editValue = 0;
Vector3D lastCrumb;
Vector3D lastDir = Vector3D.Zero;
IMyRemoteControl rc;
List<IMyShipConnector> connectors = new List<IMyShipConnector>();
List<IMyConveyorSorter> loadSorters = new List<IMyConveyorSorter>();
List<IMyConveyorSorter> unloadSorters = new List<IMyConveyorSorter>();
List<IMyCargoContainer> cargo = new List<IMyCargoContainer>();
struct ScreenTarget { public IMyTextSurface Surface; public string View; public float FixedSize; public float Pad; }
List<ScreenTarget> shipScreens = new List<ScreenTarget>();
IMyTextSurface pbSurface;
List<IMyGyro> gyros = new List<IMyGyro>();
List<IMyThrust> thrusters = new List<IMyThrust>();
List<IMyGasTank> h2Tanks = new List<IMyGasTank>();
List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
IMyBroadcastListener listener;
Dictionary<string, ShuttleReport> fleet = new Dictionary<string, ShuttleReport>();
const double DT_FALLBACK = 1.0 / 6.0;
double dt = DT_FALLBACK;
double sinceRender = 0;
const double APPROACH_TIMEOUT = 45;
const int MAX_PATH = 250;
const int WRAP_COLS = 26;
const double APPROACH_KP = 0.5;
const double VEL_GAIN = 2.0;
const double ALIGN_TOL = 0.03;
const double ALIGN_MOVE_TOL = 0.20;
const double ARRIVE_SPEED = 1.0;
const double WP_ARRIVE_MIN = 8.0;
const double MIN_ACCEL = 0.5;
const double CORNER_STRAIGHT_TOL = 0.10;
const double ALIGN_SLOW_TOL = 0.5;
const double ALIGN_MIN_FAC = 0.15;
const double VEL_MIN_FAC = 0.30;
const double CRUISE_STUCK_TIMEOUT = 60.0;
const double ALIGN_DEADBAND = 0.01;
const double GYRO_REST_ATT = 0.02;
const double GYRO_REST_RATE = 0.02;
const double COAST_HOLD_ENTER = 0.05;
const double COAST_HOLD_WAKE = 0.10;
const double COAST_TOL = 0.5;
const double CRUISE_COAST_BAND = 5.0;
const double VEL_DEADBAND = 0.4;
Program()
{
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    if (string.IsNullOrWhiteSpace(Me.CustomData)) WriteConfigTemplate();
    LoadConfig();
    if (role == Role.Shuttle) BackfillConfig();
    else TrimBaseConfig();
    Discover();
    LoadRoute();
    LoadState();
    listener = IGC.RegisterBroadcastListener(channel);
    if (role == Role.Shuttle)
    {
        dampenersOwned = true;
        ReleaseControl();
    }
}
void Save()
{
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
void Main(string argument, UpdateType source)
{
    try
    {
        dt = Runtime.TimeSinceLastRun.TotalSeconds;
        if (dt <= 0 || dt > 0.5) dt = DT_FALLBACK;
        if (!string.IsNullOrEmpty(argument)) HandleCommand(argument.Trim());
        if (role == Role.Base) { RunBase(); return; }
        if (rc == null) { Discover(); if (rc == null) { statusMsg = "No Remote Control found"; RenderShip(); return; } }
        DrainIgc();
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
        Runtime.UpdateFrequency = IsFlightControlState() ? UpdateFrequency.Update1 : UpdateFrequency.Update10;
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
                phaseTimer = 0;
                departRequested = false;
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
        case "WAITFULL":
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
    if (path.Count == 0 || Vector3D.Distance(path[path.Count - 1], destPose.Pos) > 5)
        AddCrumb(rc.GetPosition());
    haveRoute = true;
    state = State.Idle;
    SaveRoute();
    statusMsg = "Route saved: " + homeConn + " -> " + destConn + " (" + path.Count + " waypoints)";
}
DockPose CapturePose(IMyShipConnector c)
{
    return new DockPose
    {
        Pos     = rc.GetPosition(),
        Fwd     = rc.WorldMatrix.Forward,
        Up      = rc.WorldMatrix.Up,
        ConnFwd = c.WorldMatrix.Forward
    };
}
void TickRecording()
{
    Vector3D p = rc.GetPosition();
    double moved = Vector3D.Distance(p, lastCrumb);
    if (moved < 20) return;
    Vector3D dir = Vector3D.Normalize(p - lastCrumb);
    double turn = lastDir == Vector3D.Zero ? 0
                : Math.Acos(MathHelper.Clamp(dir.Dot(lastDir), -1, 1)) * 180.0 / Math.PI;
    if (moved >= segMeters || (moved >= 30 && turn >= turnDegrees))
        AddCrumb(p);
}
void AddCrumb(Vector3D p)
{
    if (simplifyMeters > 0 && path.Count >= 2)
    {
        Vector3D a = path[path.Count - 2];
        Vector3D chord = p - a;
        double chordLen = chord.Length();
        if (chordLen > 1e-3)
        {
            Vector3D u = chord / chordLen;
            Vector3D at = path[path.Count - 1] - a;
            double proj = at.Dot(u);
            double perp = (at - proj * u).Length();
            if (perp <= simplifyMeters && proj >= 0 && proj <= chordLen)
            {
                path[path.Count - 1] = p;
                lastCrumb = p;
                lastDir = u;
                return;
            }
        }
    }
    if (path.Count >= MAX_PATH) { statusMsg = "Path full (" + MAX_PATH + " wp) - raise segMeters/simplifyMeters"; return; }
    if (path.Count > 0) lastDir = Vector3D.Normalize(p - lastCrumb);
    path.Add(p);
    lastCrumb = p;
}
void TickIdle()
{
    AbortAutopilot();
    ReleaseControl();
    if (!operating) return;
    phaseTimer = 0;
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
    bool cargoReady = fill <= 1.0;
    SetSorters(unloadSorters, !cargoReady);
    if (DepartureAllowed(false, cargoReady))
    {
        SetSorters(unloadSorters, false);
        if (runMode == RunMode.OneWay)
        {
            departRequested = false;
            phaseTimer = 0;
            operating = false;
            state = State.Idle;
            statusMsg = "Delivered - holding at destination";
            return;
        }
        string why;
        if (!DepartFuelOk(false, out why)) { statusMsg = why; return; }
        departRequested = false;
        BeginLegMeasure(false);
        phaseTimer = 0;
        state = State.UndockDest;
        return;
    }
    statusMsg = DepartStatus(false, fill);
}
bool DepartureAllowed(bool atHome, bool cargoReady)
{
    if (departRequested) return true;
    DepartTrigger trig = atHome ? homeTrigger : destTrigger;
    switch (trig)
    {
        case DepartTrigger.Manual: return false;
        case DepartTrigger.Timer:  return phaseTimer >= dwellSec;
        case DepartTrigger.Cargo:  return cargoReady;
        default:
            return cargoReady || (!atHome && phaseTimer >= unloadDrainSec);
    }
}
string DepartStatus(bool atHome, double fill)
{
    string act = (atHome ? "Loading " : "Unloading ") + fill.ToString("0") + "%";
    DepartTrigger trig = atHome ? homeTrigger : destTrigger;
    if (trig == DepartTrigger.Manual) return act + " - waiting DEPART";
    if (trig == DepartTrigger.Timer)  return act + " - dwell " + phaseTimer.ToString("0") + "/" + dwellSec.ToString("0") + "s";
    return act;
}
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
    Vector3D standoff = ApproachPoint(p);
    bool clear = Vector3D.Distance(rc.GetPosition(), standoff) < 3.0;
    Vector3D faceFwd = p.Fwd;
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
    statusMsg = (toDest ? "Cruising to destination" : "Cruising home") + "  ETA " + FormatEta();
    if (done)
    {
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
Vector3D ApproachPoint(DockPose p) { return p.Pos - p.ConnFwd * approachDist; }
void ArmCruise(bool toDest)
{
    BuildLeg(toDest);
    if (legWps.Count == 0)
    {
        state = State.Faulted;
        statusMsg = "Cruise: empty path - re-record route";
        return;
    }
    BuildVelocityProfile();
    cruiseIdx = 0;
    cruiseProgTimer = 0;
    cruiseBestDist = double.MaxValue;
    cruiseArmed = true;
    cruiseArmedToDest = toDest;
    statusMsg = toDest ? "Cruising to destination" : "Cruising home";
}
void BuildLeg(bool toDest)
{
    legWps.Clear();
    DockPose from = toDest ? homePose : destPose;
    DockPose to   = toDest ? destPose : homePose;
    double skip = approachDist + 3;
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
    legWps.Add(ApproachPoint(to));
}
void BuildVelocityProfile()
{
    int n = legWps.Count;
    legVmax.Clear();
    for (int i = 0; i < n; i++) legVmax.Add(cruiseSpeed);
    if (n == 0) return;
    double[] cap; MatrixD toLocal;
    AxisThrust(out cap, out toLocal);
    double minAxis = cap[0];
    for (int i = 1; i < 6; i++) minAxis = Math.Min(minAxis, cap[i]);
    double mass = rc.CalculateShipMass().PhysicalMass;
    cruiseAccel = Math.Max(MIN_ACCEL, brakeFrac * minAxis / Math.Max(mass, 1.0));
    for (int i = 1; i < n - 1; i++)
    {
        Vector3D inDir = legWps[i] - legWps[i - 1];
        Vector3D outDir = legWps[i + 1] - legWps[i];
        if (inDir.LengthSquared() < 1e-6 || outDir.LengthSquared() < 1e-6) continue;
        inDir = Vector3D.Normalize(inDir);
        outDir = Vector3D.Normalize(outDir);
        double theta = Math.Acos(MathHelper.Clamp(inDir.Dot(outDir), -1, 1));
        if (theta < CORNER_STRAIGHT_TOL) continue;
        double R = cornerLen / Math.Max(Math.Tan(theta * 0.5), 1e-3);
        double corner = Math.Sqrt(cruiseAccel * R);
        legVmax[i] = Math.Min(legVmax[i], Math.Min(corner, cruiseSpeed));
    }
    legVmax[n - 1] = ARRIVE_SPEED;
    for (int i = n - 2; i >= 0; i--)
    {
        double segLen = Vector3D.Distance(legWps[i], legWps[i + 1]);
        double reachable = Math.Sqrt(legVmax[i + 1] * legVmax[i + 1] + 2.0 * cruiseAccel * segLen);
        legVmax[i] = Math.Min(legVmax[i], Math.Min(reachable, cruiseSpeed));
    }
}
double WpArriveRadius()
{
    return Math.Max(WP_ARRIVE_MIN, rc.GetShipSpeed() * dt * 2.0);
}
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
        if (arrived || passed) { cruiseIdx++; cruiseProgTimer = 0; cruiseBestDist = double.MaxValue; }
        else break;
    }
}
bool RunCruiseControl()
{
    SetDampeners(false);
    Vector3D pos = rc.GetPosition();
    Vector3D vel = rc.GetShipVelocities().LinearVelocity;
    Vector3D grav = rc.GetNaturalGravity();
    double mass = rc.CalculateShipMass().PhysicalMass;
    AdvanceCursor(pos);
    Vector3D target = legWps[cruiseIdx];
    Vector3D toWp = target - pos;
    double dist = toWp.Length();
    Vector3D pathDir = dist > 1e-3 ? toWp / dist : rc.WorldMatrix.Forward;
    if (dist < cruiseBestDist - 1.0) { cruiseBestDist = dist; cruiseProgTimer = 0; }
    if (cruiseIdx < legWps.Count - 1 && dist < cornerLen)
    {
        Vector3D nextSeg = legWps[cruiseIdx + 1] - target;
        if (nextSeg.LengthSquared() > 1e-6)
        {
            Vector3D nextDir = Vector3D.Normalize(nextSeg);
            double b = 1.0 - dist / cornerLen;
            Vector3D blended = Vector3D.Lerp(pathDir, nextDir, b);
            if (blended.LengthSquared() > 1e-6) pathDir = Vector3D.Normalize(blended);
        }
    }
    double vmax = legVmax[cruiseIdx];
    double vBrake = Math.Sqrt(vmax * vmax + 2.0 * cruiseAccel * dist);
    double speed = Math.Min(cruiseSpeed, vBrake);
    Vector3D fwdTarget, upTarget;
    bool inGrav = grav.LengthSquared() > 1e-3;
    if (inGrav && UseLevelFlight())
    {
        Vector3D upWorld = Vector3D.Normalize(-grav);
        Vector3D horiz = pathDir - pathDir.Dot(upWorld) * upWorld;
        fwdTarget = horiz.LengthSquared() > 1e-6 ? Vector3D.Normalize(horiz) : rc.WorldMatrix.Forward;
        upTarget = upWorld;
    }
    else if (inGrav)
    {
        Vector3D up = -grav;
        Vector3D perp = up - up.Dot(pathDir) * pathDir;
        fwdTarget = pathDir;
        upTarget = perp.LengthSquared() > 1e-6 ? Vector3D.Normalize(perp) : rc.WorldMatrix.Up;
    }
    else { fwdTarget = pathDir; upTarget = rc.WorldMatrix.Up; }
    double align = AlignTo(fwdTarget, upTarget, true);
    double headErr = rc.WorldMatrix.Forward.Cross(fwdTarget).Length();
    double alignFac = Clamp(1.0 - headErr / ALIGN_SLOW_TOL, ALIGN_MIN_FAC, 1.0);
    double vmag = vel.Length();
    double velFac = vmag < 1.0 ? 1.0 : Clamp((vel / vmag).Dot(pathDir), VEL_MIN_FAC, 1.0);
    speed *= alignFac * velFac;
    Vector3D desiredVel = pathDir * speed;
    Vector3D dv = desiredVel - vel;
    bool inSpace = grav.LengthSquared() < 1e-3;
    if (inSpace && align < ALIGN_MOVE_TOL && dv.Length() < COAST_TOL)
        ZeroThrusters();
    else
    {
        double along = dv.Dot(pathDir);
        if (along < 0.0 && along > -CRUISE_COAST_BAND) dv -= along * pathDir;
        if (dv.Length() < VEL_DEADBAND) dv = Vector3D.Zero;
        ApplyForce(dv * mass * VEL_GAIN - grav * mass);
    }
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
        c.Connect();
    if (rc.IsAutoPilotEnabled) AbortAutopilot();
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
bool FlyToPose(Vector3D pos, Vector3D fwd, Vector3D up, double arriveDist)
{
    SetDampeners(false);
    double align = AlignTo(fwd, up);
    Vector3D toTarget = pos - rc.GetPosition();
    double dist = toTarget.Length();
    Vector3D grav = rc.GetNaturalGravity();
    double mass = rc.CalculateShipMass().PhysicalMass;
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
double AlignTo(Vector3D targetFwd, Vector3D targetUp) => AlignTo(targetFwd, targetUp, GyroCapRad(), false);
double AlignTo(Vector3D targetFwd, Vector3D targetUp, bool coastHold) => AlignTo(targetFwd, targetUp, GyroCapRad(), coastHold);
double AlignTo(Vector3D targetFwd, Vector3D targetUp, double maxRad, bool coastHold)
{
    Vector3D fwd = rc.WorldMatrix.Forward, up = rc.WorldMatrix.Up;
    Vector3D fErr = fwd.Cross(targetFwd);
    if (fwd.Dot(targetFwd) < 0.0)
    {
        double l = fErr.Length();
        fErr = l > 1e-6 ? fErr / l : Vector3D.Normalize(up);
    }
    Vector3D uErr = up.Cross(targetUp);
    if (up.Dot(targetUp) < 0.0)
    {
        double l = uErr.Length();
        uErr = l > 1e-6 ? uErr / l : Vector3D.Normalize(fwd);
    }
    Vector3D err = fErr + uErr;
    double attErr = fErr.Length() + uErr.Length();
    Vector3D angVel = rc.GetShipVelocities().AngularVelocity;
    if (coastHold)
    {
        bool stay = gyroResting ? attErr < COAST_HOLD_WAKE
                                : (attErr < COAST_HOLD_ENTER && angVel.Length() < GYRO_REST_RATE * 2.0);
        if (stay) { gyroResting = true; HoldGyrosInert(); return attErr; }
        gyroResting = false;
    }
    else
    {
        gyroResting = false;
        if (attErr < GYRO_REST_ATT && angVel.Length() < GYRO_REST_RATE) { HoldGyrosInert(); return attErr; }
    }
    if (err.Length() < ALIGN_DEADBAND) err = Vector3D.Zero;
    Vector3D cmd = err * gyroGain - angVel * gyroDamp;
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
    return attErr;
}
void HoldGyrosInert()
{
    foreach (var g in gyros)
        if (g != null && g.IsWorking) { g.GyroOverride = true; g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f; }
}
double GyroCapRad()
{
    double rpm = gyroRpmCap > 0 ? gyroRpmCap
               : (Me.CubeGrid.GridSizeEnum == MyCubeSize.Small ? 15.0 : 5.0);
    return rpm * 2.0 * Math.PI / 60.0;
}
void ApplyForce(Vector3D worldForce)
{
    if (rc == null) return;
    if (!IsFinite(worldForce)) { ZeroThrusters(); return; }
    double[] cap; MatrixD toLocal;
    AxisThrust(out cap, out toLocal);
    Vector3D lf = Vector3D.TransformNormal(worldForce, toLocal);
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
static bool IsFinite(Vector3D v)
{
    return !double.IsNaN(v.X) && !double.IsNaN(v.Y) && !double.IsNaN(v.Z) &&
           !double.IsInfinity(v.X) && !double.IsInfinity(v.Y) && !double.IsInfinity(v.Z);
}
void AxisThrust(out double[] cap, out MatrixD toLocal)
{
    toLocal = MatrixD.Transpose(rc.WorldMatrix);
    cap = new double[6];
    foreach (var t in thrusters)
        if (t != null && t.IsWorking) cap[ThrustKey(t, toLocal)] += t.MaxEffectiveThrust;
}
int ThrustKey(IMyThrust t, MatrixD toLocal)
{
    Vector3D lp = Vector3D.TransformNormal(t.WorldMatrix.Backward, toLocal);
    double ax = Math.Abs(lp.X), ay = Math.Abs(lp.Y), az = Math.Abs(lp.Z);
    if (ax >= ay && ax >= az) return lp.X >= 0 ? 0 : 1;
    if (ay >= az)             return lp.Y >= 0 ? 2 : 3;
    return lp.Z >= 0 ? 4 : 5;
}
bool UseLevelFlight()
{
    if (cruiseAttitude == "level") return true;
    if (cruiseAttitude == "nose") return false;
    double[] cap; MatrixD toLocal;
    AxisThrust(out cap, out toLocal);
    double up = cap[2], fwd = cap[5];
    if (!cruiseFlyLevel && up > fwd * 1.1) cruiseFlyLevel = true;
    else if (cruiseFlyLevel && up < fwd * 0.9) cruiseFlyLevel = false;
    return cruiseFlyLevel;
}
void ReleaseControl()
{
    foreach (var t in thrusters) if (t != null) t.ThrustOverride = 0f;
    foreach (var g in gyros)
        if (g != null) { g.GyroOverride = false; g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f; }
    SetDampeners(true);
}
void ZeroThrusters()
{
    foreach (var t in thrusters) if (t != null) t.ThrustOverride = 0f;
}
bool dampenersOwned = false;
void SetDampeners(bool on)
{
    if (rc == null) return;
    if (on)
    {
        if (!dampenersOwned) return;
        rc.DampenersOverride = true;
        dampenersOwned = false;
    }
    else
    {
        rc.DampenersOverride = false;
        dampenersOwned = true;
    }
}
void OnDocked(bool atDest)
{
    FinishLegMeasure();
    if (atDest)
    {
        state = State.Unloading;
        phaseTimer = 0;
    }
    else
    {
        if (runMode == RunMode.OneTrip) { operating = false; state = State.Idle; statusMsg = "Trip complete"; }
        else if (runMode == RunMode.OneWay) { operating = false; state = State.Idle; statusMsg = "Holding at home"; }
        else { state = State.Loading; phaseTimer = 0; }
    }
}
void Discover()
{
    connectors.Clear(); cargo.Clear(); shipScreens.Clear();
    var grid = Me.CubeGrid;
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
    var tanks = new List<IMyGasTank>();
    GridTerminalSystem.GetBlocksOfType(tanks, b => b.CubeGrid == grid);
    h2Tanks.Clear();
    foreach (var t in tanks)
        if (t.BlockDefinition.SubtypeName.IndexOf("Hydrogen", StringComparison.OrdinalIgnoreCase) >= 0)
            h2Tanks.Add(t);
    var sorters = new List<IMyConveyorSorter>();
    GridTerminalSystem.GetBlocksOfType(sorters, b => b.CubeGrid == grid);
    loadSorters.Clear(); unloadSorters.Clear();
    foreach (var s in sorters)
    {
        if (HasTag(s.CustomName, loadTag)) loadSorters.Add(s);
        if (HasTag(s.CustomName, unloadTag)) unloadSorters.Add(s);
    }
    var panels = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(panels, b => b.CubeGrid == grid && HasTag(b.CustomName, TagOpener()));
    foreach (var p in panels)
    {
        string view; float size, pad;
        ParseScreenTag(p.CustomName, out view, out size, out pad);
        AddScreen(p, view, size, pad);
    }
    var providers = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType(providers, b => b.CubeGrid == grid
        && b is IMyTextSurfaceProvider
        && b.CustomData.IndexOf("shuttle-screens", StringComparison.OrdinalIgnoreCase) >= 0);
    bool pbConfigured = false;
    foreach (var b in providers)
    {
        var prov = b as IMyTextSurfaceProvider;
        var ini = new MyIni();
        if (!ini.TryParse(b.CustomData) || !ini.ContainsSection("shuttle-screens")) continue;
        var keys = new List<MyIniKey>();
        ini.GetKeys("shuttle-screens", keys);
        foreach (var k in keys)
        {
            int idx;
            if (!int.TryParse(k.Name.Trim(), out idx) || idx < 0 || idx >= prov.SurfaceCount) continue;
            string view; float size, pad;
            ParseViewSpec(ini.Get(k).ToString(""), out view, out size, out pad);
            AddScreen(prov.GetSurface(idx), view, size, pad);
        }
        if (b == Me) pbConfigured = true;
    }
    pbSurface = Me.GetSurface(0);
    if (!pbConfigured) { PrepSurface(pbSurface); AddScreen(pbSurface, VIEW_FULL, 0f, 0f); }
}
string TagOpener()
{
    return lcdTag.EndsWith("]") ? lcdTag.Substring(0, lcdTag.Length - 1) : lcdTag;
}
void AddScreen(IMyTextSurface s, string view, float size, float pad)
{
    if (s == null) return;
    for (int i = 0; i < shipScreens.Count; i++) if (shipScreens[i].Surface == s) return;
    PrepSurface(s);
    shipScreens.Add(new ScreenTarget { Surface = s, View = view, FixedSize = size, Pad = pad });
}
void ParseScreenTag(string name, out string view, out float size, out float pad)
{
    view = VIEW_FULL; size = 0f; pad = 0f;
    string opener = TagOpener();
    int i = name.IndexOf(opener, StringComparison.OrdinalIgnoreCase);
    if (i < 0) return;
    int start = i + opener.Length;
    int end = name.IndexOf(']', start);
    string inner = end > start ? name.Substring(start, end - start) : name.Substring(start);
    var parts = inner.Split(':');
    if (parts.Length >= 2 && parts[1].Trim().Length > 0) view = NormalizeView(parts[1]);
    if (parts.Length >= 3) { float f; if (float.TryParse(parts[2].Trim(), out f) && f > 0) size = f; }
    if (parts.Length >= 4) { float f; if (float.TryParse(parts[3].Trim(), out f) && f >= 0) pad = f; }
}
void ParseViewSpec(string spec, out string view, out float size, out float pad)
{
    view = VIEW_FULL; size = 0f; pad = 0f;
    if (string.IsNullOrEmpty(spec)) return;
    var pp = spec.Split('/');
    if (pp.Length >= 2) { float f; if (float.TryParse(pp[1].Trim(), out f) && f >= 0) pad = f; }
    var parts = pp[0].Split('@');
    view = NormalizeView(parts[0]);
    if (parts.Length >= 2) { float f; if (float.TryParse(parts[1].Trim(), out f) && f > 0) size = f; }
}
string NormalizeView(string v)
{
    switch (v.Trim().ToLowerInvariant())
    {
        case VIEW_MENU:   return VIEW_MENU;
        case VIEW_STATUS: return VIEW_STATUS;
        case VIEW_TRIP:   return VIEW_TRIP;
        default:          return VIEW_FULL;
    }
}
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
bool DockedNow() { return ConnectedConnector() != null; }
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
void DrainIgc()
{
    if (listener == null) return;
    while (listener.HasPendingMessage)
    {
        var m = listener.AcceptMessage();
        var s = m.Data as string;
        if (string.IsNullOrEmpty(s) || !s.StartsWith("CMD|")) continue;
        var f = s.Split('|');
        if (f.Length >= 2 && f[1] == "DEPART")
        {
            string who = f.Length >= 3 ? f[2] : "*";
            if (who == "*" || who.Equals(shipName, StringComparison.OrdinalIgnoreCase))
                RequestDepart();
        }
    }
}
void RequestDepart()
{
    if (state == State.Loading || state == State.Unloading)
    {
        departRequested = true;
        statusMsg = "Depart requested";
        return;
    }
    if (state == State.Idle && haveRoute && DockedNow())
    {
        operating = true;
        bool atHome = AtHomeEnd();
        if (runMode == RunMode.OneWay)
            state = atHome ? State.Loading : State.UndockDest;
        else
            state = atHome ? State.Loading : State.Unloading;
        phaseTimer = 0;
        departRequested = true;
        statusMsg = "Departing now";
        return;
    }
    if (operating) statusMsg = "DEPART: already under way";
    else statusMsg = haveRoute ? "DEPART: dock first" : "DEPART: no route";
}
double HydrogenPct()
{
    double cur = 0, cap = 0;
    foreach (var t in h2Tanks)
        if (t != null && t.IsWorking) { cap += t.Capacity; cur += t.FilledRatio * t.Capacity; }
    return cap <= 0 ? -1 : cur / cap * 100.0;
}
double BatteryPct()
{
    double cur = 0, cap = 0;
    foreach (var b in batteries)
        if (b != null && b.IsWorking) { cap += b.MaxStoredPower; cur += b.CurrentStoredPower; }
    return cap <= 0 ? -1 : cur / cap * 100.0;
}
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
void BeginLegMeasure(bool outbound)
{
    legOutbound = outbound;
    legStartH2 = HydrogenPct();
    legStartBatt = BatteryPct();
}
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
string FormatEta()
{
    double dist = RemainingDistance();
    double spd = rc != null ? rc.GetShipSpeed() : 0;
    if (spd < 1) return "--:--";
    int sec = (int)(dist / spd);
    return (sec / 60).ToString("00") + ":" + (sec % 60).ToString("00");
}
double RemainingDistance()
{
    if (rc == null || !cruiseArmed || legWps.Count == 0) return 0;
    if (cruiseIdx >= legWps.Count) return 0;
    double d = Vector3D.Distance(rc.GetPosition(), legWps[cruiseIdx]);
    for (int i = cruiseIdx; i < legWps.Count - 1; i++)
        d += Vector3D.Distance(legWps[i], legWps[i + 1]);
    return d;
}
int MenuCount()
{
    switch (menuPage)
    {
        case PAGE_MAIN:     return 6;
        case PAGE_RECORD:   return 4;
        case PAGE_SETTINGS: return 6;
        case PAGE_DEPART:   return 7;
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
            case 2: BeginEdit(maxMassKg / 1000.0); break;
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
            case 0: return 5;
            case 1: return 0.5;
            case 2: return 1;
            case 3: return 5;
        }
    if (menuPage == PAGE_DEPART) return 5;
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
void RenderShip()
{
    var cache = new Dictionary<string, string>();
    foreach (var t in shipScreens)
    {
        string text;
        if (!cache.TryGetValue(t.View, out text))
        {
            text = WrapText(BuildView(t.View), WRAP_COLS);
            cache[t.View] = text;
        }
        SizeAndWrite(t, text);
    }
    Echo(WrapText(BuildView(VIEW_FULL), WRAP_COLS));
}
string BuildView(string view)
{
    switch (view)
    {
        case VIEW_MENU:   return BuildMenu();
        case VIEW_STATUS: return BuildStatus();
        case VIEW_TRIP:   return BuildTrip();
        default:          return BuildHeader() + BuildMenu();
    }
}
string BuildHeaderLine()
{
    return shipName + " " + ShipState() + (operating ? " [RUN]" : " [STOP]");
}
string BuildHeader()
{
    var sb = new StringBuilder();
    sb.Append(BuildHeaderLine()).Append('\n');
    sb.Append("Cargo ").Append(CargoFillPct().ToString("0")).Append("% ")
      .Append((ShipMassKg() / 1000.0).ToString("0")).Append("t ")
      .Append((rc != null ? rc.GetShipSpeed() : 0).ToString("0")).Append("m/s\n");
    sb.Append(haveRoute ? ("Route " + path.Count + "wp") : "Route: none").Append('\n');
    if (state == State.CruiseToDest || state == State.CruiseToHome)
        sb.Append("ETA ").Append(FormatEta()).Append(' ')
          .Append((RemainingDistance() / 1000.0).ToString("0.0")).Append("km\n");
    sb.Append(statusMsg).Append('\n');
    return sb.ToString();
}
string BuildMenu()
{
    var sb = new StringBuilder();
    sb.Append("-- ").Append(PageName()).Append(" --\n");
    var labels = MenuLabels();
    for (int i = 0; i < labels.Count; i++)
        sb.Append(i == menuIndex ? "> " : "  ").Append(labels[i]).Append('\n');
    sb.Append(editing ? "UP/DN +/-  APPLY save" : "UP/DN  APPLY  BACK");
    return sb.ToString();
}
string BuildStatus()
{
    var sb = new StringBuilder();
    sb.Append("-- Status --\n");
    sb.Append(ShipState()).Append(operating ? " [RUN]" : " [STOP]").Append('\n');
    sb.Append('\n');
    sb.Append("-- Cargo --\n");
    sb.Append(CargoFillPct().ToString("0")).Append("%  ")
      .Append((ShipMassKg() / 1000.0).ToString("0")).Append("t  ")
      .Append((rc != null ? rc.GetShipSpeed() : 0).ToString("0")).Append("m/s");
    return sb.ToString();
}
string BuildTrip()
{
    var sb = new StringBuilder();
    sb.Append("-- Trip --\n");
    sb.Append(haveRoute ? ("Route " + path.Count + "wp") : "Route: none").Append('\n');
    sb.Append("Phase: ").Append(ShipState()).Append('\n');
    if (state == State.CruiseToDest || state == State.CruiseToHome)
        sb.Append("ETA ").Append(FormatEta()).Append("  ")
          .Append((RemainingDistance() / 1000.0).ToString("0.0")).Append("km\n");
    sb.Append(statusMsg);
    return sb.ToString();
}
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
void SizeAndWrite(ScreenTarget t, string text)
{
    var s = t.Surface;
    if (s == null) return;
    float pad = (float)Clamp(t.Pad, 0, 40);
    s.TextPadding = pad;
    if (t.FixedSize > 0)
    {
        s.FontSize = t.FixedSize;
    }
    else
    {
        var m = s.MeasureStringInPixels(new StringBuilder(text), s.Font, 1f);
        if (m.X >= 1 && m.Y >= 1)
        {
            float padScale = Math.Max(0.1f, 1f - 2f * pad / 100f);
            Vector2 area = s.SurfaceSize * padScale;
            float fit = Math.Min(area.X / m.X, area.Y / m.Y) * 0.95f;
            s.FontSize = (float)Clamp(fit, 0.4, 3.0);
        }
    }
    s.WriteText(text);
}
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
void Broadcast()
{
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
class ShuttleReport
{
    public string Name, State;
    public int EtaSec, DistM, Fill;
    public double MassT;
    public bool Running;
    public double Age;
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
void WriteConfigTemplate()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    WriteShuttleSection(ini);
    Me.CustomData = ini.ToString();
}
void BackfillConfig()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    WriteShuttleSection(ini);
    Me.CustomData = ini.ToString();
}
void TrimBaseConfig()
{
    var ini = new MyIni();
    ini.TryParse(Me.CustomData);
    ini.DeleteSection("shuttle");
    WriteBaseSection(ini);
    Me.CustomData = ini.ToString();
}
void WriteBaseSection(MyIni ini)
{
    ini.Set("shuttle", "role", "base");
    ini.Set("shuttle", "shipName", shipName);
    ini.Set("shuttle", "channel", channel);
    ini.Set("shuttle", "lcdTag", lcdTag);
}
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
    ini.Set("shuttle", "simplifyMeters", simplifyMeters);
    ini.Set("shuttle", "approachDist", approachDist);
    ini.Set("shuttle", "gyroRpmCap", gyroRpmCap);
    ini.Set("shuttle", "brakeFrac", brakeFrac);
    ini.Set("shuttle", "cornerLen", cornerLen);
    ini.Set("shuttle", "gyroGain", gyroGain);
    ini.Set("shuttle", "gyroDamp", gyroDamp);
    ini.Set("shuttle", "cruiseAttitude", cruiseAttitude);
}
void LoadConfig()
{
    var ini = new MyIni();
    if (!ini.TryParse(Me.CustomData)) return;
    string roleStr = ini.Get("shuttle", "role").ToString("shuttle").Trim().ToLowerInvariant();
    role = (roleStr == "base" || roleStr == "station") ? Role.Base : Role.Shuttle;
    shipName = ini.Get("shuttle", "shipName").ToString(shipName);
    channel = ini.Get("shuttle", "channel").ToString(channel);
    string modeStr = ini.Get("shuttle", "runMode").ToString("CONTINUOUS").Trim().ToUpperInvariant();
    string defHome = "Auto";
    if (modeStr == "WAITFULL") { runMode = RunMode.Continuous; defHome = "Cargo"; }
    else SetModeSilent(modeStr);
    homeTrigger = TrigFromString(ini.Get("shuttle", "homeTrigger").ToString(defHome));
    destTrigger = TrigFromString(ini.Get("shuttle", "destTrigger").ToString("Auto"));
    remoteName = ini.Get("shuttle", "remoteName").ToString("");
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
    simplifyMeters = ini.Get("shuttle", "simplifyMeters").ToDouble(simplifyMeters);
    approachDist = ini.Get("shuttle", "approachDist").ToDouble(approachDist);
    gyroRpmCap = (float)ini.Get("shuttle", "gyroRpmCap").ToDouble(gyroRpmCap);
    brakeFrac = Clamp(ini.Get("shuttle", "brakeFrac").ToDouble(brakeFrac), 0.1, 1.0);
    cornerLen = Math.Max(1.0, ini.Get("shuttle", "cornerLen").ToDouble(cornerLen));
    gyroGain = Math.Max(0.1, ini.Get("shuttle", "gyroGain").ToDouble(gyroGain));
    gyroDamp = Math.Max(0.0, ini.Get("shuttle", "gyroDamp").ToDouble(gyroDamp));
    string attStr = ini.Get("shuttle", "cruiseAttitude").ToString(cruiseAttitude).Trim().ToLowerInvariant();
    cruiseAttitude = (attStr == "level" || attStr == "nose") ? attStr : "auto";
}
void SetModeSilent(string m)
{
    switch (m.Trim().ToUpperInvariant())
    {
        case "ONETRIP":  runMode = RunMode.OneTrip; break;
        case "ONEWAY":   runMode = RunMode.OneWay; break;
        default:         runMode = RunMode.Continuous; break;
    }
}
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
    bool haveHP = LoadPos(ini, "homePos", "homeDock", out homePose.Pos);
    bool haveDP = LoadPos(ini, "destPos", "destDock", out destPose.Pos);
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
bool LoadPos(MyIni ini, string primary, string legacy, out Vector3D pos)
{
    if (TryVec(ini.Get("route", primary).ToString(""), out pos)) return true;
    return TryVec(ini.Get("route", legacy).ToString(""), out pos);
}
void SynthesizePoses()
{
    if (path.Count >= 2)
    {
        Vector3D outDir = Vector3D.Normalize(path[1] - path[0]);
        Vector3D inDir  = Vector3D.Normalize(path[path.Count - 1] - path[path.Count - 2]);
        homePose.Fwd = outDir; homePose.ConnFwd = -outDir;
        destPose.Fwd = inDir;  destPose.ConnFwd = inDir;
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
    cruiseArmed = false;
}
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
