// Footbridge Builder. Original work by TevBenji, 2026-11. GPL-3.0.
//
// The tool: hover a road, see the bridge, click to pin it, adjust it in the panel, build it from there.
//  * Hover: a net raycast finds the road; the world round it is copied (SiteSnapshot) and planned on a worker thread, the
//    latest request winning.
//  * Ghost: the plan goes out as real temporary nets. One Clear frame wipes the old ghost, the next frame emits the new one,
//    and the game draws it with its pillars, its errors and its price, as for its own tools.
//  * Pin: a left-click holds the bridge where it is, so the pointer can go to the panel. Every setting then re-plans it there.
//  * Build: only the panel's button; a fresh snapshot is compared first (something may have changed); then Apply, then the crossing upgrades as a
//    second Apply, then a check of what the game actually built: pieces by their end points, feet by the sidewalk's side
//    connection, crossings by the node's crosswalk flag, cost by the city's money.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Colossal.Mathematics;
using Colossal.Serialization.Entities;
using FootbridgeBuilder.Build;
using FootbridgeBuilder.Core;
using Game;
using Game.City;
using Game.Common;
using Game.Input;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.InputSystem;
using UnityEngine.Scripting;

namespace FootbridgeBuilder.Tool
{
    public sealed partial class FootbridgeToolSystem : ToolBaseSystem
    {
        private const float AreaSeconds = 3f;
        private const float AreaReach = 80f;
        private const int SettleFrames = 3, MostSettleFrames = 60, VerifyFrames = 30;
        private const float TurnStep = 5f, MostTurn = 30f;
        // Pieces whose ends are raised more than this are outlined where they stand; the rest are laid on the ground.
        private const float ElevatedOutline = 0.5f;
        // The panel's green (#35be4f) when the bridge can be built here, red when it can't.
        private static readonly UnityEngine.Color Fits = new UnityEngine.Color(0.208f, 0.745f, 0.31f, 1f);
        private static readonly UnityEngine.Color DoesNotFit = new UnityEngine.Color(0.9f, 0.22f, 0.18f, 1f);

        private TerrainSystem terrain;
        private CitySystem city;
        private Game.Rendering.OverlayRenderSystem overlay;
        private EntityQuery temporaryEdges, temporaries, refused, replaced, builtPaths, icons, joinedRoads;
        private List<PathKind> paths = new List<PathKind>();
        private List<DeckBridge> bridges = new List<DeckBridge>();
        // The prefabs the ghost on show is built from: the build check matches built edges of these.
        private readonly HashSet<Entity> ghostPrefabs = new HashSet<Entity>();
        private SiteArea area;
        private bool areaStale = true;
        private float nextErrorLog;

        // Planning: the latest request wins.
        private string requestedKey = "", suppressedKey = "";
        // The request the plan on show was made for: a build waits until it is the latest one.
        private string plannedKey = "";
        private float requestedAt;
        private Task<BridgePlan> planTask;
        private CancellationTokenSource planCancel;
        private (string Key, HoverSnapshot Snapshot, PathKind Path, float3 Hit) taskInput;
        private BridgePlan plan;
        private HoverSnapshot planSnapshot;
        private PathKind planPath;
        private float3 planHit;
        private float turn;
        // The road and point of the latest request, which is where a pinned bridge stays.
        private Entity requestedEdge;
        private float3 requestedPoint;

        // A left-click pins the bridge. Only the panel's Build button builds it, so a click meant to open the settings can't
        // spend the city's money. Clicks are latched because input waits on the ghost's own frames; a left-click keeps where it
        // landed, since the pointer is on its way to the panel by the time it's handled.
        private bool pinned, leftClicked, rightClicked, clickedRoad;
        private Entity clickedEdge;
        private float3 clickedPoint;
        private readonly RightClickGesture rightClick = new RightClickGesture();

        // The ghost on show.
        private readonly List<Entity> ghost = new List<Entity>();
        private bool clearPending, closing;
        private BridgePlan emitPending;
        private int emittedFrame = -1, lastCount = -1, stableSince;
        private bool settled;

        // The build.
        private enum Step { None, Applied, CrossingsEmitted, CrossingsApplied }
        private Step step;
        private int stepFrame;
        private long moneyBefore;
        private int priced;
        private readonly List<Entity> upgrades = new List<Entity>();
        private string piecesReport = "";
        private bool piecesMissing;
        // Where the last bridge was built: its report stays on the panel until the pointer moves on from there.
        private float2? reportAt;
        private const float ReportHold = 8f;
        // Another tool took over before a build finished: said above every plan until the next build starts.
        private string interrupted = "";

        private string[] WithNotice(IEnumerable<string> messages) =>
            (interrupted.Length > 0 ? new[] { interrupted }.Concat(messages) : messages).ToArray();

        public ToolState State { get; } = new ToolState();
        public override string toolID => "Footbridge Builder";
        public override PrefabBase GetPrefab() => null;
        public override bool TrySetPrefab(PrefabBase prefab) => false;
        internal bool IsActive => m_ToolSystem != null && m_ToolSystem.activeTool == this;

        // For the local test runner (Dev build only): a hover and a click without the mouse.
        internal bool DevHovering { get; set; }
        internal bool DevEscape { get; set; }
        internal Entity DevEdge { get; set; }
        internal float3 DevPoint { get; set; }
        /// <summary>A build asked for from the panel's button (or the test runner), handled on the next frame.</summary>
        internal bool BuildRequested { get; set; }
        internal bool GhostSettled => settled && plan != null;
        /// <summary>Counts every ghost the game has priced, so a watcher can wait for the next one.</summary>
        internal int GhostVersion { get; private set; }
        /// <summary>Counts every plan the tool has taken on, so a watcher can wait for one made after its own hover.</summary>
        internal int PlanVersion { get; private set; }
        // What the last build's check found.
        internal int LastPlanned { get; private set; }
        internal int LastBuilt { get; private set; }
        internal int LastFeet { get; private set; }
        internal int LastJoined { get; private set; }
        internal int LastCrossingsWanted { get; private set; }
        internal int LastCrossingsRemoved { get; private set; }
        internal long LastCharged { get; private set; }
        internal BridgePlan CurrentPlan => plan;
        internal HoverSnapshot CurrentSnapshot => planSnapshot;
        internal PathKind CurrentPath => planPath;

        [Preserve]
        protected override void OnCreate()
        {
            try
            {
                base.OnCreate();
                terrain = World.GetOrCreateSystemManaged<TerrainSystem>();
                city = World.GetOrCreateSystemManaged<CitySystem>();
                overlay = World.GetOrCreateSystemManaged<Game.Rendering.OverlayRenderSystem>();
                temporaryEdges = GetEntityQuery(ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<Temp>());
                temporaries = GetEntityQuery(ComponentType.ReadOnly<Temp>());
                refused = GetEntityQuery(ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Error>());
                replaced = GetEntityQuery(ComponentType.ReadOnly<Game.Buildings.Building>(), ComponentType.ReadOnly<Warning>(), ComponentType.Exclude<Temp>());
                icons = GetEntityQuery(ComponentType.ReadOnly<Game.Notifications.Icon>(), ComponentType.ReadOnly<PrefabRef>());
                joinedRoads = GetEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Road>(), ComponentType.ReadOnly<Curve>(), ComponentType.ReadOnly<ConnectedNode>() },
                    None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
                });
                builtPaths = GetEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<Edge>(), ComponentType.ReadOnly<PrefabRef>() },
                    None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>() },
                });
            }
            catch (Exception exception)
            {
                Fail(exception, "The footbridge tool could not be created.");
                Enabled = false;
            }
        }

        public void Toggle()
        {
            if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
            if (IsActive) Close(); else Open();
        }

        public void Open()
        {
            try
            {
                if (State.Stage == ToolStage.Building) return;
                paths = PathCatalog.List(EntityManager, World.GetOrCreateSystemManaged<PrefabSystem>());
                bridges = PathCatalog.Bridges(EntityManager, World.GetOrCreateSystemManaged<PrefabSystem>());
                State.PathNames = paths.Select(path => path.Name).ToArray();
                State.PathLabels = paths.Select(path => path.Label).ToArray();
                areaStale = true;
                requestedKey = suppressedKey = "";
                reportAt = null;
                m_ToolSystem.activeTool = this;
                State.Stage = ToolStage.Hovering;
                State.Summary = paths.Count == 0 ? "No footpath that can be raised is available yet." : "";
                State.Messages = WithNotice(Array.Empty<string>());
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not open."); }
        }

        public void Close()
        {
            try
            {
                if (State.Stage == ToolStage.Building) { Message("Still building. Wait until it is done."); return; }
                // The ghost goes first: only the active tool can clear its temporaries.
                if (IsActive && ghost.Count > 0) { closing = true; return; }
                State.Stage = ToolStage.Closed;
                if (IsActive) m_ToolSystem.activeTool = m_DefaultToolSystem;
                Touch();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not close."); }
        }

        public void Build()
        {
            if (pinned) BuildRequested = true;
        }

        public void SettingsChanged()
        {
            requestedKey = suppressedKey = "";
            reportAt = null;
            Touch();
        }

        public void SetTurn(float degrees)
        {
            turn = math.clamp(math.round(degrees / TurnStep) * TurnStep, -MostTurn, MostTurn);
            State.Turn = turn;
            SettingsChanged();
        }

        [Preserve]
        protected override void OnStartRunning()
        {
            try
            {
                base.OnStartRunning();
                EnableActions(true);
                if (State.Stage == ToolStage.Closed) { State.Stage = ToolStage.Hovering; Touch(); }
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not start."); }
        }

        [Preserve]
        protected override void OnStopRunning()
        {
            try
            {
                // The game hands over to another tool whenever the player picks one, even mid-build: what stops here is said.
                if (State.Stage == ToolStage.Building)
                {
                    interrupted = step == Step.Applied
                        ? "Another tool was picked while the last bridge was building: it wasn't checked, and the street crossings were left as they are."
                        : step == Step.CrossingsEmitted
                            ? "Another tool was picked while the last bridge was building: the street crossings were left as they are."
                            : "Another tool was picked while the last bridge was building, before its check finished.";
                    Mod.Logger.Warn("Footbridge Builder: " + interrupted);
                }
                applyMode = ApplyMode.None;
                EnableActions(false);
                planCancel?.Cancel();
                planTask = null;
                ClearPin();
                NetDefinitions.Destroy(EntityManager, ghost);
                NetDefinitions.Destroy(EntityManager, upgrades);
                plan = null;
                settled = closing = clearPending = false;
                emitPending = null;
                step = Step.None;
                State.Stage = ToolStage.Closed;
                Touch();
                base.OnStopRunning();
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not stop cleanly."); }
        }

        public override void InitializeRaycast()
        {
            try
            {
                base.InitializeRaycast();
                m_ToolRaycastSystem.typeMask = TypeMask.Net;
                m_ToolRaycastSystem.netLayerMask = Layer.Road;
            }
            catch (Exception exception) { LogUpdateError(exception, "Footbridge Builder: setting up the raycast failed"); }
        }

        [Preserve]
        protected override JobHandle OnUpdate(JobHandle inputDependencies)
        {
            try
            {
                applyMode = ApplyMode.None;
                AdoptFinishedPlan();
                if (!IsActive) return inputDependencies;
                int frame = UnityEngine.Time.frameCount;
                bool handled = false;
                DevTick(frame, ref handled);
                if (handled) return inputDependencies;
                DrawOutline();
                if (State.Stage == ToolStage.Building) { BuildTick(frame); return inputDependencies; }
                if (closing)
                {
                    NetDefinitions.Destroy(EntityManager, ghost);
                    applyMode = ApplyMode.Clear;
                    closing = false;
                    plan = null;
                    State.Stage = ToolStage.Closed;
                    m_ToolSystem.activeTool = m_DefaultToolSystem;
                    Touch();
                    return inputDependencies;
                }
                var input = InputManager.instance;
                bool typing = input == null || input.hasInputFieldFocus;
                if (!typing) LatchClicks(input);
                if (GhostTick(frame) || typing) return inputDependencies;
                HandleInput(input, frame);
            }
            catch (Exception exception) { LogUpdateError(exception, "Footbridge Builder: tool update failed"); }
            return inputDependencies;
        }

        private void LatchClicks(InputManager input)
        {
            var mouse = Mouse.current;
            bool overWorld = input.controlOverWorld;
            if (overWorld && ((applyAction != null && applyAction.WasPressedThisFrame()) || (mouse != null && mouse.leftButton.wasPressedThisFrame)))
            {
                leftClicked = true;
                clickedRoad = RoadUnderPointer(overWorld, out clickedEdge, out clickedPoint);
            }
            if (mouse == null) return;
            // Dragging with the right button turns the camera: only a quick click counts.
            rightClicked |= rightClick.Update(overWorld && mouse.rightButton.wasPressedThisFrame, mouse.rightButton.isPressed, mouse.delta.ReadValue(),
                overWorld, UnityEngine.Time.realtimeSinceStartup);
        }

        private void HandleInput(InputManager input, int frame)
        {
            bool click = leftClicked, unpin = rightClicked;
            leftClicked = rightClicked = false;
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            bool overWorld = input.controlOverWorld;
            // The game's cancel action also fires on the right button, which belongs to the camera.
            bool rightButton = mouse != null && (mouse.rightButton.isPressed || mouse.rightButton.wasPressedThisFrame);
            bool escape = (cancelAction != null && cancelAction.WasPressedThisFrame() && !rightButton) || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) || DevEscape;
            DevEscape = false;
            bool onRoad = RoadUnderPointer(overWorld, out Entity edge, out float3 point);
            if (escape)
            {
                if (pinned)
                {
                    // Nothing stays on show, even with the pointer on the road, so the next Esc closes the tool.
                    ClearPin();
                    suppressedKey = onRoad ? KeyFor(edge, point) : requestedKey;
                    DropPlan();
                    return;
                }
                // Esc drops the bridge on show; again, it closes the tool.
                if (plan != null || ghost.Count > 0) { suppressedKey = requestedKey; DropPlan(); return; }
                Close();
                return;
            }
            if (keyboard != null && keyboard.leftBracketKey.wasPressedThisFrame) { SetTurn(turn - TurnStep); return; }
            if (keyboard != null && keyboard.rightBracketKey.wasPressedThisFrame) { SetTurn(turn + TurnStep); return; }
            if (unpin && pinned) { ClearPin(); return; }
            if (pinned)
            {
                // The road went (bulldozed, or upgraded into a new one): there is nothing left to hold on to.
                if (!SiteSnapshot.IsGroundRoad(EntityManager, requestedEdge, out _)) { ClearPin(); requestedKey = ""; DropPlan(); return; }
                // A new design or setting: plan again where the bridge stands.
                string pinnedKey = KeyFor(requestedEdge, requestedPoint);
                if (pinnedKey != requestedKey) RequestPlan(requestedEdge, requestedPoint, pinnedKey);
            }
            // A setting changed (from the panel, with the pointer off the road): plan again where the bridge was.
            else if (requestedKey.Length == 0 && plan != null && planSnapshot != null && !DevHovering
                && SiteSnapshot.IsGroundRoad(EntityManager, planSnapshot.Edge, out _))
                RequestPlan(planSnapshot.Edge, planHit, KeyFor(planSnapshot.Edge, planHit));
            // The panel's button: the pointer is over the panel then, not the road, so this comes before the hover.
            if (BuildRequested)
            {
                BuildRequested = false;
                if (pinned) TryBuild(frame);
                return;
            }

            // Another road or another spot: the pin moves there. A click never builds, even on the pinned bridge.
            if (click && clickedRoad) { PinAt(clickedEdge, clickedPoint); return; }
            if (pinned) return;
            if (!onRoad)
            {
                if (plan != null && !DevHovering && overWorld) { requestedKey = ""; DropPlan(); }
                return;
            }

            // The pointer is still where a bridge was just built: its report stays until it moves on.
            if (reportAt.HasValue && !DevHovering)
            {
                if (math.distance(point.xz, reportAt.Value) < ReportHold) return;
                reportAt = null;
            }
            string key = KeyFor(edge, point);
            if (key != requestedKey && key != suppressedKey && UnityEngine.Time.realtimeSinceStartup - requestedAt > 0.12f)
                RequestPlan(edge, point, key);
        }

        private bool RoadUnderPointer(bool overWorld, out Entity edge, out float3 point)
        {
            edge = Entity.Null;
            point = default;
            if (DevHovering) { edge = DevEdge; point = DevPoint; }
            else
            {
                if (!overWorld || !GetRaycastResult(out edge, out RaycastHit hit) || !math.all(math.isfinite(hit.m_HitPosition))) return false;
                point = hit.m_HitPosition;
            }
            if (EntityManager.HasComponent<Temp>(edge)) edge = EntityManager.GetComponentData<Temp>(edge).m_Original;
            // Over the middle of a junction the raycast can name the junction's node: plan from one of its roads at the same
            // point, which puts the ring round that junction.
            if (EntityManager.HasComponent<Node>(edge) && !EntityManager.HasComponent<Edge>(edge)) edge = RoadAt(edge);
            return SiteSnapshot.IsGroundRoad(EntityManager, edge, out _);
        }

        private void PinAt(Entity edge, float3 point)
        {
            pinned = State.Pinned = true;
            suppressedKey = "";
            reportAt = null;
            string key = KeyFor(edge, point);
            // The same key with nothing on show: Esc dropped this bridge, and the click asks for it back.
            if (key != requestedKey || (plan == null && planTask == null)) RequestPlan(edge, point, key);
            Touch();
        }

        private void ClearPin()
        {
            pinned = State.Pinned = false;
            leftClicked = rightClicked = BuildRequested = false;
            rightClick.Reset();
            Touch();
        }

        /// <summary>A road on the ground meeting at the node, or Null.</summary>
        private Entity RoadAt(Entity node)
        {
            if (!EntityManager.HasBuffer<ConnectedEdge>(node)) return Entity.Null;
            foreach (var connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))
                if (SiteSnapshot.IsGroundRoad(EntityManager, connected.m_Edge, out _)) return connected.m_Edge;
            return Entity.Null;
        }

        private string KeyFor(Entity edge, float3 point) =>
            $"{edge.Index}:{edge.Version}:{(int)math.round(point.x / 2f)}:{(int)math.round(point.z / 2f)}:{turn}:{SettingsKey()}";

        private void TryBuild(int frame)
        {
            if (plan == null) return;
            // A newer bridge is still being planned (a move, a turn or a setting): the build waits for that one.
            if (planTask != null || plannedKey != requestedKey)
            {
                Message("Updating the bridge to the new settings. Press Build again in a moment.");
                return;
            }
            if (!plan.Buildable) { Message(plan.Messages.Count > 0 ? plan.Messages[0] : "No bridge fits here."); return; }
            if (!settled) return;
            if (State.Refused > 0) { Message("The game refuses part of this bridge here (shown red). Move along the road or turn it."); return; }
            StartBuild(frame);
        }

        /// <summary>The local test runner's own work (Dev build only; empty otherwise).</summary>
        partial void DevTick(int frame, ref bool handled);

        private string SettingsKey()
        {
            var settings = Mod.Settings;
            return settings == null ? "" : $"{settings.Design}|{settings.PathName}|{settings.RemoveCrossings}|{settings.CrossingReach}|{settings.ExtraClearance}";
        }

        // ── planning ──

        private SiteArea Area(float2 centre, bool fresh)
        {
            if (fresh || areaStale || area == null || math.distance(area.Centre, centre) > AreaReach
                || UnityEngine.Time.realtimeSinceStartup - area.Taken > AreaSeconds)
            {
                area = new SiteArea(EntityManager, World, centre);
                areaStale = false;
            }
            return area;
        }

        private void RequestPlan(Entity edge, float3 point, string key)
        {
            requestedKey = key;
            requestedEdge = edge;
            requestedPoint = point;
            requestedAt = UnityEngine.Time.realtimeSinceStartup;
            var settings = Mod.Settings;
            var path = PathCatalog.Find(paths, settings?.PathName ?? PathCatalog.DefaultName);
            // No new plan here: the bridge from before goes too, or it would stay green and buildable under the old settings.
            void Refuse(string reason)
            {
                if (plan != null || ghost.Count > 0) DropPlan();
                State.Summary = reason;
                Touch();
            }
            if (path == null) { Refuse("No footpath that can be raised is available."); return; }
            if (path.Locked) { Refuse("Footpaths aren't unlocked in this city yet."); return; }
            var snapshot = SiteSnapshot.Capture(EntityManager, Area(point.xz, fresh: false), edge, point, path, out string problem);
            if (snapshot == null) { Refuse(problem); return; }
            var request = Request(snapshot, path, point);
            planCancel?.Cancel();
            planCancel = new CancellationTokenSource();
            var cancel = planCancel.Token;
            taskInput = (key, snapshot, path, point);
            planTask = Task.Run(() => FootbridgePlanner.Plan(request, cancel), cancel);
        }

        private BridgeRequest Request(HoverSnapshot snapshot, PathKind path, float3 point)
        {
            var settings = Mod.Settings;
            Enum.TryParse(settings?.Design ?? "Auto", out Design design);
            // Wider than the path can span: one of the game's pedestrian bridges carries the deck instead.
            var spec = path.Spec;
            if (bridges.Count > 0)
                spec = new PathSpec
                {
                    Name = spec.Name, Width = spec.Width, MaxSlope = spec.MaxSlope, NodeOffset = spec.NodeOffset,
                    MaxEdgeLength = spec.MaxEdgeLength, MaxElevation = spec.MaxElevation, MaxSpan = math.max(path.Span, bridges[bridges.Count - 1].Longest),
                    HeightRange = spec.HeightRange,
                };
            return new BridgeRequest
            {
                Road = snapshot.Road,
                RoadWidth = snapshot.RoadWidth,
                RoadHasSidewalks = snapshot.Sidewalks,
                RoadPillarBands = snapshot.PillarBands,
                Cursor = point.xz,
                TurnDegrees = turn,
                Clearance = snapshot.Clearance,
                ExtraClearance = math.clamp(settings?.ExtraClearance ?? 0.5f, 0.5f, 3f),
                Design = design,
                CrossingControl = settings == null || settings.RemoveCrossings ? CrossingControl.RemoveInReach : CrossingControl.Off,
                CrossingReach = math.clamp(settings?.CrossingReach ?? 60, 20, 150),
                Path = spec,
                Ground = snapshot.Ground,
                Obstacles = snapshot.Obstacles,
                Crossings = snapshot.Crossings,
                Beside = snapshot.Beside,
                Junction = snapshot.Junction,
            };
        }

        private void AdoptFinishedPlan()
        {
            if (planTask == null || !planTask.IsCompleted) return;
            var task = planTask;
            planTask = null;
            if (task.IsCanceled) return;
            if (task.IsFaulted) { Fail(task.Exception?.GetBaseException(), "The bridge could not be planned here."); return; }
            if (taskInput.Key != requestedKey) return;
            plan = task.Result;
            plannedKey = taskInput.Key;
            PlanVersion++;
            if (!plan.Buildable && plan.Blockers.Count > 0)
                Mod.Logger.Info("Footbridge Builder: nothing fits here. " + string.Join(" | ", plan.Messages) + " Blocked by: " + string.Join(" | ", plan.Blockers.Select(Named)));
            planSnapshot = taskInput.Snapshot;
            planPath = taskInput.Path;
            planHit = taskInput.Hit;
            State.Stage = ToolStage.Planned;
            State.Buildable = plan.Buildable;
            State.DesignName = plan.DesignName;
            State.DeckHeight = plan.DeckHeight;
            State.UseLabel = plan.UseLabel;
            State.UseShare = plan.UseShare;
            State.ExtraWalk = plan.ExtraWalk;
            State.CompetingMarked = plan.CompetingMarked;
            State.CompetingDistance = plan.CompetingDistance;
            State.CrossingsRemoved = plan.CrossingsToRemove.Count(id => CrossingFor(plan, planSnapshot, id) is CrossingNode node && node.Marked);
            State.Cost = -1;
            State.Refused = State.BuildingsReplaced = 0;
            State.Summary = plan.Buildable ? plan.DesignName : "No bridge fits here.";
            State.Messages = WithNotice(plan.Messages);
            State.Progress = 0f;
            Touch();
            // The old ghost goes in one frame and the new one comes in the next.
            clearPending = true;
            emitPending = plan.Buildable ? plan : null;
        }

        // A blocker line ("Net 4295193185 (surface ...") with the prefab's name after the id, so a player's log says what was in the way.
        private string Named(string blocker)
        {
            try
            {
                var parts = blocker.Split(' ');
                if (parts.Length < 2 || !long.TryParse(parts[1], out long id) || id == 0) return blocker;
                var entity = SiteArea.Unpack(id);
                if (!EntityManager.Exists(entity) || !EntityManager.HasComponent<PrefabRef>(entity)) return blocker;
                string name = World.GetOrCreateSystemManaged<PrefabSystem>().GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(entity))?.name ?? "?";
                return parts[0] + " " + parts[1] + " '" + name + "'" + blocker.Substring(parts[0].Length + parts[1].Length + 1);
            }
            catch (Exception) { return blocker; }
        }

        private void DropPlan()
        {
            planCancel?.Cancel();
            planTask = null;
            plan = null;
            clearPending = true;
            emitPending = null;
            State.Stage = ToolStage.Hovering;
            State.Buildable = false;
            State.DesignName = "";
            State.Summary = "";
            State.Messages = WithNotice(Array.Empty<string>());
            Touch();
        }

        // ── the ghost ──

        /// <summary>
        /// An outline round the bridge on show, every frame: green where it can be built, red where it can't. Where nothing
        /// fits there is no ghost (it would mark buildings to go and show a price), only the red outline of the bridge as it
        /// would stand, so the player sees what they are placing while looking for room.
        /// </summary>
        private void DrawOutline()
        {
            try
            {
                if (plan == null || plan.Pieces.Count == 0 || State.Stage != ToolStage.Planned) return;
                // A new ghost is green only once the game has had its say on it (a few frames), so a refused one never flashes green.
                if (plan.Buildable && !settled) return;
                bool fits = plan.Buildable && State.Refused == 0;
                var colour = fits ? Fits : DoesNotFit;
                var fill = new UnityEngine.Color(colour.r, colour.g, colour.b, pinned ? 0.45f : 0.25f);
                // A pinned bridge has a heavier outline, so it reads as held in place.
                float line = pinned ? 1.2f : 0.6f;
                // Clear of the game's own blue outline round the ghost, so the green (or red) reads as a ring round it.
                float half = (planPath?.Spec.Width ?? 3f) * 0.5f + 0.9f;
                var buffer = overlay.GetBuffer(out JobHandle dependencies);
                dependencies.Complete();
                foreach (var piece in plan.Pieces)
                {
                    var curve = new Bezier4x3(plan.Position(piece.Start), piece.B, piece.C, plan.Position(piece.End));
                    bool raised = plan.Nodes[piece.Start].Elevation > ElevatedOutline || plan.Nodes[piece.End].Elevation > ElevatedOutline;
                    var style = raised ? (Game.Rendering.OverlayRenderSystem.StyleFlags)0 : Game.Rendering.OverlayRenderSystem.StyleFlags.Projected;
                    // A light fill: on the ground under a ghost (its footprint), or where the path would stand when there is none.
                    buffer.DrawCurve(fill, fill, 0f, plan.Buildable ? Game.Rendering.OverlayRenderSystem.StyleFlags.Projected : style, curve, half * 2f, float2.zero);
                    // Both edges as short lines, so the outline runs on unbroken past the joins between pieces.
                    int steps = math.max(2, (int)math.ceil(MathUtils.Length(curve) / 1.5f));
                    float3 lastLeft = default, lastRight = default;
                    for (int step = 0; step <= steps; step++)
                    {
                        float t = step / (float)steps;
                        float3 at = MathUtils.Position(curve, t), tangent = MathUtils.Tangent(curve, t);
                        float3 side = math.normalizesafe(new float3(-tangent.z, 0f, tangent.x)) * half;
                        if (step > 0)
                        {
                            Edge(buffer, colour, style, lastLeft, at + side, line);
                            Edge(buffer, colour, style, lastRight, at - side, line);
                        }
                        lastLeft = at + side;
                        lastRight = at - side;
                    }
                    // Across each ramp's foot, closing the outline there.
                    if (plan.Nodes[piece.End].GroundEnd) Edge(buffer, colour, style, lastLeft, lastRight, line);
                }
            }
            catch (Exception exception) { LogUpdateError(exception, "Footbridge Builder: drawing the outline failed"); }
        }

        private static void Edge(Game.Rendering.OverlayRenderSystem.Buffer buffer, UnityEngine.Color colour,
            Game.Rendering.OverlayRenderSystem.StyleFlags style, float3 from, float3 to, float width) =>
            buffer.DrawLine(colour, colour, 0f, style, new Line3.Segment(from, to), width, new float2(1f, 1f));

        /// <summary>True when this frame belongs to the ghost (a Clear or an emission), so input waits.</summary>
        private bool GhostTick(int frame)
        {
            if (clearPending)
            {
                NetDefinitions.Destroy(EntityManager, ghost);
                applyMode = ApplyMode.Clear;
                clearPending = false;
                settled = false;
                emittedFrame = -1;
                return true;
            }
            if (emitPending != null)
            {
                // Room in the terrain's road cull list before our nets come (TerrainCullGuard: the CullForCascades crash).
                TerrainCullGuard.Reserve(terrain, emitPending.Pieces.Count);
                var bridge = emitPending;
                ghostPrefabs.Clear();
                ghost.AddRange(NetDefinitions.Emit(EntityManager, bridge, piece =>
                {
                    var prefab = PrefabFor(bridge, piece);
                    ghostPrefabs.Add(prefab);
                    return prefab;
                }, (uint)requestedKey.GetHashCode()));
                emitPending = null;
                emittedFrame = frame;
                lastCount = -1;
                return true;
            }
            if (emittedFrame < 0 || settled) return false;
            if (!Settle(frame, emittedFrame)) return false;
            settled = true;
            ReadGhost();
            return false;
        }

        /// <summary>
        /// The path itself, or for a span longer than the path can carry, the game's pedestrian bridge that reaches it (the
        /// shortest). Each span of a ring or a pair of roads gets its own.
        /// </summary>
        private Entity PrefabFor(BridgePlan bridge, PlannedPiece piece)
        {
            Entity chosen = planPath.Prefab;
            float span = math.distance(bridge.Position(piece.Start).xz, bridge.Position(piece.End).xz);
            if (piece.Deck && span > planPath.Span - 0.5f)
                foreach (var candidate in bridges)
                    if (span >= candidate.Shortest && span <= candidate.Longest - 0.5f) { chosen = candidate.Prefab; break; }
            DevPrefab(piece, ref chosen);
            return chosen;
        }

        /// <summary>The local test runner's choice of deck prefab for a probe (Dev build only; empty otherwise).</summary>
        partial void DevPrefab(PlannedPiece piece, ref Entity prefab);

        // The game has turned the definitions into temporaries once their number stops changing.
        private bool Settle(int frame, int since)
        {
            int count = temporaryEdges.CalculateEntityCount();
            if (count != lastCount) { lastCount = count; stableSince = frame; }
            return (frame - since >= 2 && count > 0 && frame - stableSince >= SettleFrames) || frame - since >= MostSettleFrames;
        }

        /// <summary>The game's own verdict on the ghost: its price, its refusals and the buildings it would replace.</summary>
        private void ReadGhost()
        {
            long cost = 0;
            using (var temps = temporaries.ToComponentDataArray<Temp>(Allocator.Temp))
                foreach (var temp in temps)
                    if ((temp.m_Flags & TempFlags.Cancel) == 0) cost += temp.m_Cost;
            State.Cost = (int)math.min(cost, int.MaxValue);
            State.Refused = refused.CalculateEntityCount();
            State.BuildingsReplaced = replaced.CalculateEntityCount();
            var messages = new List<string>(plan?.Messages ?? new List<string>());
            RefusedBecause = State.Refused > 0 ? ErrorNames() : "";
            if (State.Refused > 0)
                messages.Insert(0, "The game refuses part of this bridge here (shown red)" + (RefusedBecause.Length > 0 ? ": " + RefusedBecause + "." : "."));
            State.Messages = WithNotice(messages);
            GhostVersion++;
            Touch();
        }

        /// <summary>The game's own names for the errors on the ghost (on its warning icons), for the log and the panel.</summary>
        internal string RefusedBecause { get; private set; } = "";

        private string ErrorNames()
        {
            var names = new SortedSet<string>();
            using (var found = icons.ToEntityArray(Allocator.Temp))
                foreach (var icon in found)
                {
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(icon).m_Prefab;
                    if (!EntityManager.HasComponent<ToolErrorData>(prefab)) continue;
                    Entity owner = EntityManager.HasComponent<Owner>(icon) ? EntityManager.GetComponentData<Owner>(icon).m_Owner : Entity.Null;
                    bool ours = EntityManager.HasComponent<Temp>(icon)
                        || (owner != Entity.Null && EntityManager.Exists(owner) && EntityManager.HasComponent<Temp>(owner));
                    if (ours) names.Add(EntityManager.GetComponentData<ToolErrorData>(prefab).m_Error.ToString());
                }
            return string.Join(", ", names);
        }

        // ── building ──

        private void StartBuild(int frame)
        {
            // Something may have been built or bulldozed since the plan was made: compare a fresh snapshot first.
            var fresh = SiteSnapshot.Capture(EntityManager, Area(planHit.xz, fresh: true), planSnapshot.Edge, planHit, planPath, out _);
            if (fresh == null || fresh.Fingerprint != planSnapshot.Fingerprint)
            {
                requestedKey = "";
                Message("Something changed here. Check the new bridge and press Build again.");
                return;
            }
            // A new build's own report replaces the note about the one before.
            interrupted = "";
            moneyBefore = Money();
            priced = math.max(0, State.Cost);
            applyMode = ApplyMode.Apply;
            step = Step.Applied;
            stepFrame = frame;
            State.Stage = ToolStage.Building;
            State.Progress = 0.3f;
            State.Summary = "Building…";
            Touch();
            Mod.Logger.Info($"Footbridge Builder: building '{plan.DesignName}', {plan.Pieces.Count} pieces, deck {plan.DeckHeight:F2} m, priced {priced}, {State.BuildingsReplaced} buildings replaced.");
        }

        private void BuildTick(int frame)
        {
            switch (step)
            {
                case Step.Applied:
                    if (frame - stepFrame == 2) NetDefinitions.Destroy(EntityManager, ghost);
                    if (frame - stepFrame < VerifyFrames) return;
                    piecesReport = VerifyPieces(out piecesMissing);
                    // An unfinished bridge leaves the street crossings as they are: cims still need a way across.
                    if (piecesMissing) { Finish("The street crossings were left as they are."); return; }
                    EmitCrossings();
                    if (upgrades.Count == 0) { Finish(""); return; }
                    step = Step.CrossingsEmitted;
                    stepFrame = frame;
                    lastCount = -1;
                    State.Progress = 0.7f;
                    Touch();
                    return;
                case Step.CrossingsEmitted:
                    if (!Settle(frame, stepFrame)) return;
                    applyMode = ApplyMode.Apply;
                    step = Step.CrossingsApplied;
                    stepFrame = frame;
                    return;
                case Step.CrossingsApplied:
                    if (frame - stepFrame == 2) NetDefinitions.Destroy(EntityManager, upgrades);
                    if (frame - stepFrame < VerifyFrames) return;
                    Finish(VerifyCrossings());
                    return;
            }
        }

        /// <summary>
        /// A crossing the plan removes: on the bridged road, or on the road beside when the bridge goes over both. A node on both
        /// roads is then one crossing with both roads' lines and edge ends.
        /// </summary>
        private static CrossingNode CrossingFor(BridgePlan bridge, HoverSnapshot snapshot, long id)
        {
            snapshot.CrossingNodes.TryGetValue(id, out var own);
            CrossingNode beside = null;
            if (bridge.OverBothRoads) snapshot.BesideCrossingNodes.TryGetValue(id, out beside);
            if (own == null || beside == null) return own ?? beside;
            var both = new CrossingNode { Node = own.Node, Position = own.Position, Marked = own.Marked || beside.Marked, RoadWidth = math.max(own.RoadWidth, beside.RoadWidth) };
            both.Lines.AddRange(own.Lines);
            both.Lines.AddRange(beside.Lines);
            both.Ends.AddRange(own.Ends);
            both.Ends.AddRange(beside.Ends);
            return both;
        }

        private void EmitCrossings()
        {
            upgrades.Clear();
            if (plan.CrossingsToRemove.Count == 0) return;
            // Each road edge gets one definition carrying every end that loses its crosswalk.
            var ends = new Dictionary<Entity, (bool Start, bool End)>();
            foreach (long id in plan.CrossingsToRemove)
            {
                if (!(CrossingFor(plan, planSnapshot, id) is CrossingNode crossing) || !crossing.Marked) continue;
                foreach (var (edge, atStart) in Fresh(crossing).Ends)
                {
                    ends.TryGetValue(edge, out var both);
                    ends[edge] = atStart ? (true, both.End) : (both.Start, true);
                }
            }
            // The upgrades are temporary road edges too: room in the terrain's road cull list first (TerrainCullGuard).
            if (ends.Count > 0) TerrainCullGuard.Reserve(terrain, ends.Count);
            foreach (var pair in ends)
            {
                var definition = NetDefinitions.RemoveCrosswalk(EntityManager, pair.Key, pair.Value.Start, pair.Value.End);
                if (definition.HasValue) upgrades.Add(definition.Value);
            }
        }

        /// <summary>Matches every planned piece to a built edge by its two end points, and every foot to a sidewalk join.</summary>
        private string VerifyPieces(out bool missing)
        {
            var built = new List<(float3 Start, float3 End, Entity StartNode, Entity EndNode)>();
            using (var edges = builtPaths.ToEntityArray(Allocator.Temp))
                foreach (var edge in edges)
                {
                    var prefab = EntityManager.GetComponentData<PrefabRef>(edge).m_Prefab;
                    if (prefab != planPath.Prefab && !ghostPrefabs.Contains(prefab)) continue;
                    var ends = EntityManager.GetComponentData<Edge>(edge);
                    if (!EntityManager.HasComponent<Node>(ends.m_Start) || !EntityManager.HasComponent<Node>(ends.m_End)) continue;
                    float3 a = EntityManager.GetComponentData<Node>(ends.m_Start).m_Position, b = EntityManager.GetComponentData<Node>(ends.m_End).m_Position;
                    if (math.distance(a.xz, planHit.xz) > 400f) continue;
                    built.Add((a, b, ends.m_Start, ends.m_End));
                }
            bool Near(float3 a, float3 b) => math.distance(a.xz, b.xz) < 1f && math.abs(a.y - b.y) < 1.5f;
            int matched = 0;
            foreach (var piece in plan.Pieces)
            {
                float3 a = plan.Position(piece.Start), b = plan.Position(piece.End);
                if (Linked(built, a, b, Near)) { matched++; continue; }
                // Which piece, and the nearest built node to each end, so a missing one can be traced.
                float nearestA = built.Count == 0 ? -1f : built.Min(edge => math.min(math.distance(edge.Start, a), math.distance(edge.End, a)));
                float nearestB = built.Count == 0 ? -1f : built.Min(edge => math.min(math.distance(edge.Start, b), math.distance(edge.End, b)));
                Mod.Logger.Warn($"Footbridge Builder: a planned piece wasn't found built: ({a.x:F1}, {a.y:F1}, {a.z:F1}) to ({b.x:F1}, {b.y:F1}, {b.z:F1}), "
                    + $"{(piece.Deck ? "the deck" : plan.Nodes[piece.End].GroundEnd || plan.Nodes[piece.Start].GroundEnd ? "a foot" : "a ramp")}; nearest built nodes {nearestA:F2} m and {nearestB:F2} m away; "
                    + EdgesAt(built.Where(edge => Near(edge.Start, a)).Select(edge => edge.StartNode).Concat(built.Where(edge => Near(edge.End, a)).Select(edge => edge.EndNode)).FirstOrDefault()));
            }
            int feet = 0, joined = 0;
            foreach (var node in plan.Nodes.Where(node => node.GroundEnd))
            {
                feet++;
                Entity foot = Entity.Null;
                foreach (var edge in built)
                {
                    if (Near(edge.Start, node.Position)) foot = edge.StartNode;
                    else if (Near(edge.End, node.Position)) foot = edge.EndNode;
                }
                if (foot != Entity.Null && JoinedRoad(foot, node.Position) != Entity.Null) joined++;
            }
            missing = matched < plan.Pieces.Count;
            LastPlanned = plan.Pieces.Count;
            LastBuilt = matched;
            LastFeet = feet;
            LastJoined = joined;
            LastCrossingsWanted = LastCrossingsRemoved = 0;
            string report = matched == plan.Pieces.Count ? $"All {matched} pieces built." : $"Only {matched} of {plan.Pieces.Count} pieces were built.";
            if (planSnapshot.Sidewalks)
                report += joined == feet ? $" All {feet} ramp feet joined the sidewalk." : $" {joined} of {feet} ramp feet joined the sidewalk.";
            Mod.Logger.Info($"Footbridge Builder: {report} ({built.Count} path edges nearby)");
            return report;
        }

        // From the node at one end, along built path edges that stay on the piece's line, to the node at the other end.
        private static bool Linked(List<(float3 Start, float3 End, Entity StartNode, Entity EndNode)> built, float3 a, float3 b, Func<float3, float3, bool> near)
        {
            float3 at = a;
            var visited = new HashSet<float3>();
            for (int step = 0; step < 4; step++)
            {
                visited.Add(at);
                float3? next = null;
                foreach (var edge in built)
                {
                    float3 other;
                    if (near(edge.Start, at)) other = edge.End;
                    else if (near(edge.End, at)) other = edge.Start;
                    else continue;
                    if (near(other, b)) return true;
                    // A node the game added sits between the two ends.
                    bool between = !visited.Contains(other) && math.distance(other.xz, b.xz) < math.distance(at.xz, b.xz)
                        && Distance(other.xz, a.xz, b.xz) < 2f;
                    if (between) next = other;
                }
                if (!next.HasValue) return false;
                at = next.Value;
            }
            return false;
        }

        private static float Distance(float2 point, float2 from, float2 to)
        {
            float2 line = to - from;
            float t = math.saturate(math.dot(point - from, line) / math.max(1e-6f, math.lengthsq(line)));
            return math.distance(point, from + line * t);
        }

        // What meets at a node, for tracing a piece the game built differently from the plan.
        private string EdgesAt(Entity node)
        {
            if (node == Entity.Null || !EntityManager.HasBuffer<ConnectedEdge>(node)) return "no node there";
            var prefabs = World.GetOrCreateSystemManaged<PrefabSystem>();
            var parts = new List<string>();
            foreach (var connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                var ends = EntityManager.GetComponentData<Edge>(connected.m_Edge);
                Entity other = ends.m_Start == node ? ends.m_End : ends.m_Start;
                string name;
                try { name = prefabs.GetPrefab<PrefabBase>(EntityManager.GetComponentData<PrefabRef>(connected.m_Edge).m_Prefab)?.name ?? "?"; } catch (Exception) { name = "?"; }
                float3 at = EntityManager.HasComponent<Node>(other) ? EntityManager.GetComponentData<Node>(other).m_Position : float3.zero;
                parts.Add($"{name} to ({at.x:F1}, {at.y:F1}, {at.z:F1})");
            }
            return "edges there: " + string.Join("; ", parts);
        }

        /// <summary>
        /// A crossing's node and our road's edge ends at it, as they are now: the bridge's feet joining the sidewalk renews the
        /// road's nodes and edges, so the snapshot's entities are gone by the time the crossings are removed.
        /// </summary>
        private (Entity Node, List<(Entity Edge, bool AtStart)> Ends) Fresh(CrossingNode crossing)
        {
            var ends = new List<(Entity Edge, bool AtStart)>();
            Entity node = Entity.Null;
            float best = 1f;
            using (var query = EntityManager.CreateEntityQuery(ComponentType.ReadOnly<Node>(), ComponentType.ReadOnly<ConnectedEdge>(),
                ComponentType.Exclude<Temp>(), ComponentType.Exclude<Deleted>()))
            using (var nodes = query.ToEntityArray(Allocator.Temp))
                foreach (var candidate in nodes)
                {
                    float distance = math.distance(EntityManager.GetComponentData<Node>(candidate).m_Position.xz, crossing.Position.xz);
                    if (distance < best) { best = distance; node = candidate; }
                }
            if (node == Entity.Null) return (node, ends);
            foreach (var connected in EntityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                var edge = connected.m_Edge;
                if (!EntityManager.HasComponent<Road>(edge) || EntityManager.HasComponent<Temp>(edge) || !EntityManager.HasComponent<Curve>(edge)) continue;
                var edgeEnds = EntityManager.GetComponentData<Edge>(edge);
                // The junction a ring goes round: every road there loses its crosswalk.
                if (!crossing.AllLegs)
                {
                    // Our roads' legs, not the side street's: the edge's middle lies on a bridged road's line, and so does its
                    // far node. A side street whose first piece is short has its middle near our line too, but never its far node.
                    float3 middle = MathUtils.Position(EntityManager.GetComponentData<Curve>(edge).m_Bezier, 0.5f);
                    if (DistanceToLines(crossing, middle.xz) > crossing.RoadWidth * 0.5f) continue;
                    Entity far = edgeEnds.m_Start == node ? edgeEnds.m_End : edgeEnds.m_Start;
                    if (!EntityManager.HasComponent<Node>(far) || DistanceToLines(crossing, EntityManager.GetComponentData<Node>(far).m_Position.xz) > 1f) continue;
                }
                ends.Add((edge, edgeEnds.m_Start == node));
            }
            return (node, ends);
        }

        private static float DistanceToLines(CrossingNode crossing, float2 point)
        {
            float best = float.MaxValue;
            foreach (var line in crossing.Lines)
                for (int index = 0; index + 1 < line.Length; index++) best = math.min(best, Distance(point, line[index].xz, line[index + 1].xz));
            return best;
        }

        /// <summary>The road a foot joined: any road near it whose side connections list the foot's node (the road may be a new entity by now).</summary>
        internal Entity JoinedRoad(Entity foot, float3 position)
        {
            using (var roads = joinedRoads.ToEntityArray(Allocator.Temp))
                foreach (var road in roads)
                {
                    if (MathUtils.Distance(EntityManager.GetComponentData<Curve>(road).m_Bezier.xz, position.xz, out _) > 40f) continue;
                    foreach (var connected in EntityManager.GetBuffer<ConnectedNode>(road, true))
                        if (connected.m_Node == foot) return road;
                }
            return Entity.Null;
        }

        private string VerifyCrossings()
        {
            int wanted = 0, removed = 0;
            foreach (long id in plan.CrossingsToRemove)
            {
                if (!(CrossingFor(plan, planSnapshot, id) is CrossingNode crossing) || !crossing.Marked) continue;
                wanted++;
                var (node, fresh) = Fresh(crossing);
                if (node != Entity.Null && SiteSnapshot.MarkedCrossings(EntityManager, node, fresh, crossing.RoadWidth) == 0) removed++;
            }
            LastCrossingsWanted = wanted;
            LastCrossingsRemoved = removed;
            if (wanted == 0) return "";
            return removed == wanted ? (wanted == 1 ? "1 crossing removed." : $"{wanted} crossings removed.") : $"{removed} of {wanted} crossings removed.";
        }

        private void Finish(string crossings)
        {
            // The game's own price for the ghost is what Apply charged; the money's change over the build mixes in a second of
            // the city's income and spending, so it's only the fallback.
            long spent = moneyBefore - Money();
            int charged = priced > 0 ? priced : spent > 0 ? (int)math.min(spent, int.MaxValue) : 0;
            LastCharged = spent;
            State.Stage = piecesMissing ? ToolStage.Failed : ToolStage.Built;
            State.Summary = piecesMissing ? "Built, with pieces missing." : "Built.";
            var report = new List<string> { piecesReport };
            if (crossings.Length > 0) report.Add(crossings);
            if (charged > 0) report.Add($"Cost {charged:N0}.");
            State.Messages = new[] { string.Join(" ", report.Where(line => line.Length > 0)) };
            State.Progress = 1f;
            step = Step.None;
            plan = null;
            settled = false;
            ClearPin();
            // The ghost became the bridge: nothing is left to settle, so the report isn't overwritten by a read of no ghost.
            emittedFrame = -1;
            reportAt = planHit.xz;
            areaStale = true;
            requestedKey = "";
            suppressedKey = "";
            Mod.Logger.Info("Footbridge Builder: " + State.Summary + " " + State.Messages[0]);
            Touch();
        }

        private long Money()
        {
            try
            {
                Entity cityEntity = city.City;
                return cityEntity != Entity.Null && EntityManager.HasComponent<PlayerMoney>(cityEntity) ? EntityManager.GetComponentData<PlayerMoney>(cityEntity).money : 0;
            }
            catch (Exception) { return 0; }
        }

        [Preserve]
        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            try
            {
                // Everything the tool holds belongs to the old city.
                planCancel?.Cancel();
                planTask = null;
                plan = null;
                area = null;
                ghost.Clear();
                upgrades.Clear();
                step = Step.None;
                ClearPin();
                interrupted = "";
                reportAt = null;
                State.Stage = ToolStage.Closed;
                State.Summary = "";
                State.Messages = new string[0];
                State.Progress = 0f;
                Touch();
                base.OnGamePreload(purpose, mode);
            }
            catch (Exception exception) { Fail(exception, "The footbridge tool could not reset for the next city."); }
        }

        private void EnableActions(bool enabled)
        {
            applyAction.shouldBeEnabled = enabled;
            secondaryApplyAction.shouldBeEnabled = enabled;
            cancelAction.shouldBeEnabled = enabled;
        }

        private void Message(string message)
        {
            if (State.Messages.Contains(message)) return;
            State.Messages = new[] { message }.Concat(State.Messages).ToArray();
            Touch();
        }

        private void Fail(Exception exception, string message)
        {
            Mod.Logger.Error(exception, "Footbridge Builder: " + message);
            State.Stage = ToolStage.Failed;
            State.Summary = message;
            Message(message);
            Touch();
        }

        private void LogUpdateError(Exception exception, string message)
        {
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (now < nextErrorLog) return;
            nextErrorLog = now + 5f;
            Mod.Logger.Error(exception, message);
        }

        private void Touch() => State.Version++;
    }
}
