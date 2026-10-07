using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BBI.Unity.Game;
using BBI.Unity.Game.UI.Hab;
using Cinemachine;
using Doozy.Engine.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace FlatHab
{
    internal sealed class HabMenu : MonoBehaviour
    {
        private enum LocationKind { Viewpoint, Screen, Poster }

        private sealed class MenuEntry
        {
            public string Label;
            public string Hint;
            public bool Enabled;
            public Action Run;
        }

        private sealed class PendingStep
        {
            public Hab3DLocationNavigation Target;
            public Action Run;
            public string Description;
            public float Deadline;
            public float ArrivedAt = -1f;
        }

        private const string TerminalCamera = "HabScreen_Virtual_Camera";
        private const string WorkstationCamera = "Workstation_Screen_Virtual_Camera";
        private const string CommsCamera = "Comms_Virtual_Camera";
        private const float MenuAppearDelay = 0.2f;
        private const float ArrivalSettleTime = 0.15f;
        private const float PendingTimeout = 5f;

        private static readonly AccessTools.FieldRef<Hab3DController, List<Hab3DLocationNavigation>> sLocationsRef =
            AccessTools.FieldRefAccess<Hab3DController, List<Hab3DLocationNavigation>>("m_Hab3DLocations");
        private static readonly AccessTools.FieldRef<Hab3DController, List<GameObject>> sStartShiftObjectsRef =
            AccessTools.FieldRefAccess<Hab3DController, List<GameObject>>("m_GlobalStartShiftObjects");
        private static readonly AccessTools.FieldRef<Hab3DController, CinemachineVirtualCamera> sWakeUpCameraRef =
            AccessTools.FieldRefAccess<Hab3DController, CinemachineVirtualCamera>("m_WakeUpStartCamera");
        private static readonly AccessTools.FieldRef<Hab3DController, bool> sInNavigableScreenRef =
            AccessTools.FieldRefAccess<Hab3DController, bool>("mIsInNavigableScreen");
        private static readonly AccessTools.FieldRef<Hab3DController, NavigationOption> sGoToRef =
            AccessTools.FieldRefAccess<Hab3DController, NavigationOption>("mGoTo");
        private static readonly AccessTools.FieldRef<Hab3DController, HabAfterShiftAsset> sAfterShiftDataRef =
            AccessTools.FieldRefAccess<Hab3DController, HabAfterShiftAsset>("mCurrentAfterShiftData");
        private static readonly AccessTools.FieldRef<Hab3DController, bool> sInAfterShiftHabRef =
            AccessTools.FieldRefAccess<Hab3DController, bool>("mIsInAfterShiftHab");
        private static readonly AccessTools.FieldRef<Hab3DController, bool> sAfterShiftFinishedRef =
            AccessTools.FieldRefAccess<Hab3DController, bool>("mHasFinsishedAfterShiftSequence");
        private static readonly AccessTools.FieldRef<Hab3DLocationNavigation, List<NavigationOption>> sOptionsRef =
            AccessTools.FieldRefAccess<Hab3DLocationNavigation, List<NavigationOption>>("m_NavigationOptions");
        private static readonly AccessTools.FieldRef<Hab3DLocationNavigation, NavigationOption> sQueuedOptionRef =
            AccessTools.FieldRefAccess<Hab3DLocationNavigation, NavigationOption>("mQueuedNavigationOption");

        private static readonly LynxControls.DeadZoneDefinition sDeadZone =
            new LynxControls.DeadZoneDefinition(LynxControls.DeadZoneType.IndependentAxes, 0.4f);

        private static HabMenu sInstance;

        private Hab3DController mHab;
        private readonly List<Hab3DLocationNavigation> mLocations = new List<Hab3DLocationNavigation>();
        private readonly Dictionary<Hab3DLocationNavigation, LocationKind> mKinds = new Dictionary<Hab3DLocationNavigation, LocationKind>();
        private readonly Dictionary<Hab3DLocationNavigation, CanvasGroup> mHideGroups = new Dictionary<Hab3DLocationNavigation, CanvasGroup>();
        private Canvas mNavCanvas;
        private CanvasGroup mNavCanvasGroup;
        private GameObject mClearedSelection;

        private bool mFlatMode;
        private bool mUiHidden;
        private bool mContextValid;
        private float mContextSince;
        private float mNoMenuSince;
        private Hab3DLocationNavigation mMenuLocation;
        private bool mMenuVisible;
        private bool mHintVisible;
        private string mLastContextReason;
        private readonly List<MenuEntry> mEntries = new List<MenuEntry>();
        private int mSelected;
        private int mQueuedActivation = -1;
        private PendingStep mPending;
        private int mLastNavDir;
        private float mNavRepeatAt;
        private Vector3 mLastMousePos;
        private bool mMouseMoved;

        private CinemachineBrain mBrain;
        private CinemachineBlendDefinition mOriginalBlend;
        private CinemachineBlenderSettings mOriginalCustomBlends;

        private bool mStylesReady;
        private float mStyleScale;
        private Texture2D mTexDim, mTexPanel, mTexEntry, mTexEntrySelected, mTexAccent;
        private GUIStyle mTitleStyle, mSubtitleStyle, mLabelStyle, mLabelDisabledStyle, mHintStyle, mNumberStyle, mFooterStyle, mSmallHintStyle;

        private void Awake()
        {
            sInstance = this;
            mFlatMode = FlatHabPlugin.StartInFlatMode.Value;
            FlatHabPlugin.InstantCameraCuts.SettingChanged += (_, __) => ApplyBlendSettings();
        }

        internal static void RegisterController(Hab3DController controller)
        {
            if (sInstance != null)
            {
                sInstance.Attach(controller);
            }
        }

        internal static bool ShouldSuppressLocationInput(Hab3DLocationNavigation location)
        {
            var menu = sInstance;
            if (menu == null || !menu.mFlatMode || menu.mHab == null)
            {
                return false;
            }
            if (!menu.mKinds.TryGetValue(location, out var kind) || kind != LocationKind.Viewpoint)
            {
                return false;
            }
            // Let the game finish navigations it already queued.
            return sQueuedOptionRef(location) == null;
        }

        private void Attach(Hab3DController controller)
        {
            mHab = controller;
            mLocations.Clear();
            mKinds.Clear();
            mHideGroups.Clear();
            mPending = null;
            mUiHidden = false;
            mContextValid = false;
            mBrain = null;

            var screenCameras = new HashSet<GameObject>();
            foreach (var location in sLocationsRef(controller) ?? new List<Hab3DLocationNavigation>())
            {
                if (location == null)
                {
                    continue;
                }
                mLocations.Add(location);
                foreach (var option in Options(location))
                {
                    var target = CamGo(option.CameraToGoTo);
                    if (option.GoesToNavigableScreen && target != null)
                    {
                        screenCameras.Add(target);
                    }
                }
            }

            foreach (var location in mLocations)
            {
                LocationKind kind;
                if (location.GetComponent<Hab3DPosterLocation>() != null)
                {
                    kind = LocationKind.Poster;
                }
                else if (screenCameras.Contains(CamGo(location.LocationVirtualCamera)))
                {
                    kind = LocationKind.Screen;
                }
                else
                {
                    kind = LocationKind.Viewpoint;
                }
                mKinds[location] = kind;
            }

            mNavCanvas = mLocations.Count > 0 && mLocations[0].transform.parent != null
                ? mLocations[0].transform.parent.GetComponentInParent<Canvas>()
                : null;
            mNavCanvasGroup = mNavCanvas != null ? mNavCanvas.GetComponent<CanvasGroup>() : null;

            ApplyBlendSettings();
            FlatHabPlugin.Log.LogInfo($"Hab detected: {mLocations.Count} locations, {mKinds.Count(k => k.Value == LocationKind.Viewpoint)} viewpoints. Flat menu {(mFlatMode ? "on" : "off")}.");
            if (FlatHabPlugin.VerboseLogging.Value)
            {
                DumpLayout();
            }
        }

        private void ApplyBlendSettings()
        {
            if (mHab == null)
            {
                return;
            }
            var brain = mHab.CinemachineBrain;
            if (brain == null)
            {
                return;
            }
            if (mBrain != brain)
            {
                mBrain = brain;
                mOriginalBlend = brain.m_DefaultBlend;
                mOriginalCustomBlends = brain.m_CustomBlends;
            }
            if (FlatHabPlugin.InstantCameraCuts.Value)
            {
                brain.m_DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);
                brain.m_CustomBlends = null;
            }
            else
            {
                brain.m_DefaultBlend = mOriginalBlend;
                brain.m_CustomBlends = mOriginalCustomBlends;
            }
        }

        private void Update()
        {
            mMouseMoved = (Input.mousePosition - mLastMousePos).sqrMagnitude > 0.5f;
            mLastMousePos = Input.mousePosition;

            if (mHab == null)
            {
                mMenuVisible = false;
                mHintVisible = false;
                mPending = null;
                return;
            }

            if (FlatHabPlugin.ToggleKey.Value.IsDown() && GameSession.CurrentGameState == GameSession.GameState.Hab)
            {
                mFlatMode = !mFlatMode;
                mContextValid = false;
                FlatHabPlugin.Log.LogInfo($"Flat hab menu {(mFlatMode ? "enabled" : "disabled (3D view)")}.");
            }

            if (mUiHidden != mFlatMode)
            {
                SetLocationUiHidden(mFlatMode);
            }
            if (mFlatMode)
            {
                ClearHiddenSelection();
            }

            ProcessPending();

            var current = ActiveLocation();
            var reason = mFlatMode ? MenuBlockReason(current) : "3D mode";
            if (reason != mLastContextReason)
            {
                VerboseLog($"Menu state: {reason ?? "visible"} (location: {(current != null ? current.name : "none")})");
                mLastContextReason = reason;
            }

            bool context = reason == null;
            if (context && (!mContextValid || current != mMenuLocation))
            {
                mContextValid = true;
                mContextSince = Time.unscaledTime;
                mMenuLocation = current;
                mSelected = 0;
            }
            else if (!context)
            {
                mContextValid = false;
                mMenuLocation = null;
            }

            mMenuVisible = context && Time.unscaledTime - mContextSince >= MenuAppearDelay;
            if (mMenuVisible)
            {
                mNoMenuSince = Time.unscaledTime;
                BuildEntries(current);
                HandleMenuInput();
            }

            mHintVisible = !mMenuVisible
                && GameSession.CurrentGameState == GameSession.GameState.Hab
                && !sInNavigableScreenRef(mHab)
                && !mHab.IsHabPaused
                && Time.unscaledTime - mNoMenuSince > 1.5f;
        }

        private string MenuBlockReason(Hab3DLocationNavigation current)
        {
            if (GameSession.CurrentGameState != GameSession.GameState.Hab)
            {
                return "not in hab state";
            }
            if (!mHab.isActiveAndEnabled)
            {
                return "hab controller inactive";
            }
            if (mPending != null)
            {
                return "navigating (multi-step)";
            }
            if (sGoToRef(mHab) != null)
            {
                return "navigating";
            }
            if (sInNavigableScreenRef(mHab))
            {
                return "in a hab screen";
            }
            if (mHab.IsHabPaused)
            {
                return "paused";
            }
            if (mHab.CinemachineBrain == null || !mHab.CinemachineBrain.enabled)
            {
                return "camera brain disabled";
            }
            if (mNavCanvas != null && (!mNavCanvas.gameObject.activeInHierarchy || !mNavCanvas.enabled))
            {
                return "3D navigation UI hidden by game";
            }
            if (mNavCanvasGroup != null && mNavCanvasGroup.alpha < 0.01f)
            {
                return "3D navigation UI faded out by game";
            }
            if (current == null)
            {
                return "no active location";
            }
            if (Kind(current) != LocationKind.Viewpoint)
            {
                return $"location is a {Kind(current)}";
            }
            if (IsStoryPlaying())
            {
                return "story scene playing";
            }
            if (mHab.GoToSleepBlock && !IsWakeLocation(current))
            {
                return "going to sleep";
            }
            return null;
        }

        private void SetLocationUiHidden(bool hidden)
        {
            foreach (var location in mLocations)
            {
                if (location == null || Kind(location) != LocationKind.Viewpoint)
                {
                    continue;
                }
                if (!mHideGroups.TryGetValue(location, out var group) || group == null)
                {
                    group = location.gameObject.AddComponent<CanvasGroup>();
                    mHideGroups[location] = group;
                }
                group.alpha = hidden ? 0f : 1f;
                group.blocksRaycasts = !hidden;
            }
            mUiHidden = hidden;

            if (!hidden && mClearedSelection != null)
            {
                var eventSystem = EventSystem.current;
                if (eventSystem != null && eventSystem.currentSelectedGameObject == null && mClearedSelection.activeInHierarchy)
                {
                    eventSystem.SetSelectedGameObject(mClearedSelection);
                }
                mClearedSelection = null;
            }
        }

        // Hold-to-start buttons react to Confirm while selected, even when invisible.
        private void ClearHiddenSelection()
        {
            var eventSystem = EventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected == null)
            {
                return;
            }
            var owner = OwningLocation(selected.transform);
            if (owner != null && Kind(owner) == LocationKind.Viewpoint)
            {
                mClearedSelection = selected;
                eventSystem.SetSelectedGameObject(null);
            }
        }

        private void BuildEntries(Hab3DLocationNavigation current)
        {
            mEntries.Clear();

            if (IsWakeLocation(current))
            {
                foreach (var option in Options(current))
                {
                    var opt = option;
                    bool ok = IsUsable(opt);
                    mEntries.Add(new MenuEntry
                    {
                        Label = "Wake up",
                        Hint = ok ? "Check your morning messages" : "Not available yet",
                        Enabled = ok,
                        Run = () => current.Navigate(opt),
                    });
                }
                AddLookAround();
                return;
            }

            if (IsStoryPending())
            {
                var comms = FindViewpointByCamera(CommsCamera);
                bool ok = comms != null && CanReach(current, comms);
                mEntries.Add(new MenuEntry
                {
                    Label = "Answer the comms terminal",
                    Hint = ok ? "Someone is trying to reach you" : "Not reachable right now",
                    Enabled = ok,
                    Run = () => GoToLocationThen(comms, () => mHab.TryStartAfterShiftSequence(), "comms story"),
                });
            }

            AddStartShift(current);
            AddScreen(current, TerminalCamera, "Employee Terminal", "The computer at your desk");
            AddScreen(current, WorkstationCamera, "Workstation", "Equipment and upgrades");
            AddOtherScreens(current);
            AddPosters(current);
            AddLookAround();

            if (mSelected >= mEntries.Count || !mEntries[mSelected].Enabled)
            {
                mSelected = Math.Max(0, mEntries.FindIndex(e => e.Enabled));
            }
        }

        private void AddStartShift(Hab3DLocationNavigation current)
        {
            UIButton here = null;
            UIButton elsewhere = null;
            Hab3DLocationNavigation elsewhereLocation = null;
            foreach (var go in sStartShiftObjectsRef(mHab) ?? new List<GameObject>())
            {
                if (go == null || !go.activeSelf)
                {
                    continue;
                }
                var button = go.GetComponent<UIButton>();
                if (button == null)
                {
                    continue;
                }
                var owner = OwningLocation(go.transform);
                if (owner == current)
                {
                    here = button;
                }
                else if (elsewhere == null && owner != null && CanReach(current, owner))
                {
                    elsewhere = button;
                    elsewhereLocation = owner;
                }
            }

            if (here == null && elsewhere == null)
            {
                if (!IsStoryPending())
                {
                    mEntries.Add(new MenuEntry { Label = "Start Shift", Hint = "Not available right now", Enabled = false });
                }
                return;
            }

            var button2 = here ?? elsewhere;
            bool enabled = button2.Interactable && button2.OnLongClick.Enabled;
            mEntries.Add(new MenuEntry
            {
                Label = "Start Shift",
                Hint = enabled ? "Choose your next ship" : "Not available yet",
                Enabled = enabled,
                Run = here != null
                    ? (Action)(() => here.ExecuteLongClick())
                    : () => GoToLocationThen(elsewhereLocation, () => elsewhere.ExecuteLongClick(), "start shift"),
            });
        }

        private void AddScreen(Hab3DLocationNavigation current, string cameraName, string label, string hint)
        {
            var routes = FindOptions(o => o.GoesToNavigableScreen && CamName(o.CameraToGoTo) == cameraName).ToList();
            if (routes.Count == 0)
            {
                return;
            }
            var usable = routes.Where(r => IsUsable(r.option) && CanReach(current, r.location))
                .OrderBy(r => r.location == current ? 0 : 1)
                .FirstOrDefault();
            if (usable.location == null)
            {
                mEntries.Add(new MenuEntry { Label = label, Hint = "Not available right now", Enabled = false });
                return;
            }
            mEntries.Add(new MenuEntry
            {
                Label = label,
                Hint = hint,
                Enabled = true,
                Run = () => GoToLocationThen(usable.location, () => usable.location.Navigate(usable.option), label),
            });
        }

        // Mirrors any other screen the game currently offers (e.g. the end-game exit), so progression is never hidden.
        private void AddOtherScreens(Hab3DLocationNavigation current)
        {
            var seen = new HashSet<string>();
            foreach (var (location, option) in FindOptions(o => o.GoesToNavigableScreen && o.GoTo != null))
            {
                if (IsWakeLocation(location))
                {
                    continue;
                }
                var camName = CamName(option.CameraToGoTo);
                if (camName == TerminalCamera || camName == WorkstationCamera)
                {
                    continue;
                }
                if (option.ClickableButton == null || !ActiveWithin(option.ClickableButton.transform, location.transform))
                {
                    continue;
                }
                if (!IsUsable(option) || !CanReach(current, location))
                {
                    continue;
                }
                var key = camName + "|" + option.GoTo.GameEvent;
                if (!seen.Add(key))
                {
                    continue;
                }
                var loc = location;
                var opt = option;
                bool endGame = option.GoTo.GameEvent == "GoToEndGame";
                mEntries.Add(new MenuEntry
                {
                    Label = endGame ? "Leave the Hab" : ButtonText(option.ClickableButton) ?? option.GoTo.GameEvent,
                    Hint = endGame ? "This starts the ending" : "",
                    Enabled = true,
                    Run = () => GoToLocationThen(loc, () => loc.Navigate(opt), "other screen"),
                });
            }
        }

        private void AddPosters(Hab3DLocationNavigation current)
        {
            foreach (var poster in mLocations.Where(l => Kind(l) == LocationKind.Poster))
            {
                var posterCam = CamGo(poster.LocationVirtualCamera);
                var route = FindOptions(o => !o.GoesToNavigableScreen && CamGo(o.CameraToGoTo) == posterCam)
                    .Where(r => IsUsable(r.option) && CanReach(current, r.location))
                    .OrderBy(r => r.location == current ? 0 : 1)
                    .FirstOrDefault();
                if (route.location == null)
                {
                    continue;
                }
                var name = poster.name.Replace("Posters", "").Replace("_UI", "").Trim('_', ' ');
                if (name == "Hab")
                {
                    name = "Suit area";
                }
                mEntries.Add(new MenuEntry
                {
                    Label = $"Posters: {name}",
                    Hint = "Decorate your hab",
                    Enabled = true,
                    Run = () => GoToLocationThen(route.location, () => route.location.Navigate(route.option), "posters"),
                });
            }
        }

        private void AddLookAround()
        {
            mEntries.Add(new MenuEntry
            {
                Label = "Look around (3D view)",
                Hint = $"Press {FlatHabPlugin.ToggleKey.Value} to come back to this menu",
                Enabled = true,
                Run = () =>
                {
                    mFlatMode = false;
                    FlatHabPlugin.Log.LogInfo("Flat hab menu disabled (3D view).");
                },
            });
        }

        private void GoToLocationThen(Hab3DLocationNavigation target, Action run, string description)
        {
            var current = ActiveLocation();
            if (current == target)
            {
                run();
                return;
            }
            if (!TryFindRoute(current, target, out var routeLocation, out var routeOption))
            {
                FlatHabPlugin.Log.LogWarning($"No route from {current?.name ?? "nowhere"} to {target.name} ({description}).");
                return;
            }

            VerboseLog($"Routing {current?.name ?? "nowhere"} -> {target.name} via {routeLocation.name} for '{description}'.");
            if (current != null && current != routeLocation)
            {
                current.gameObject.SetActive(false);
            }
            routeLocation.Navigate(routeOption);

            if (sGoToRef(mHab) == null)
            {
                FlatHabPlugin.Log.LogWarning($"Navigation to {target.name} did not start; restoring {current?.name}.");
                if (current != null)
                {
                    current.gameObject.SetActive(true);
                }
                return;
            }

            mPending = new PendingStep
            {
                Target = target,
                Run = run,
                Description = description,
                Deadline = Time.unscaledTime + PendingTimeout,
            };
        }

        private void ProcessPending()
        {
            if (mPending == null)
            {
                return;
            }
            if (Time.unscaledTime > mPending.Deadline)
            {
                FlatHabPlugin.Log.LogWarning($"Gave up waiting to reach {mPending.Target.name} for '{mPending.Description}'.");
                mPending = null;
                return;
            }
            if (sGoToRef(mHab) != null || !mPending.Target.gameObject.activeSelf)
            {
                mPending.ArrivedAt = -1f;
                return;
            }
            if (mPending.ArrivedAt < 0f)
            {
                mPending.ArrivedAt = Time.unscaledTime;
                return;
            }
            if (Time.unscaledTime - mPending.ArrivedAt < ArrivalSettleTime)
            {
                return;
            }

            var step = mPending;
            mPending = null;
            VerboseLog($"Arrived at {step.Target.name}; running '{step.Description}'.");
            try
            {
                step.Run();
            }
            catch (Exception ex)
            {
                FlatHabPlugin.Log.LogError($"'{step.Description}' failed: {ex}");
            }
        }

        private bool TryFindRoute(Hab3DLocationNavigation current, Hab3DLocationNavigation target,
            out Hab3DLocationNavigation routeLocation, out NavigationOption routeOption)
        {
            var targetCam = CamGo(target.LocationVirtualCamera);
            var route = FindOptions(o => !o.GoesToNavigableScreen && targetCam != null && CamGo(o.CameraToGoTo) == targetCam)
                .Where(r => Kind(r.location) == LocationKind.Viewpoint && IsUsable(r.option))
                .OrderBy(r => r.location == current ? 0 : 1)
                .FirstOrDefault();
            routeLocation = route.location;
            routeOption = route.option;
            return routeLocation != null;
        }

        private bool CanReach(Hab3DLocationNavigation current, Hab3DLocationNavigation target) =>
            target != null && (current == target || TryFindRoute(current, target, out _, out _));

        private void HandleMenuInput()
        {
            if (mQueuedActivation >= 0)
            {
                var index = mQueuedActivation;
                mQueuedActivation = -1;
                Activate(index);
                return;
            }

            for (int i = 0; i < Math.Min(9, mEntries.Count); i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                {
                    Activate(i);
                    return;
                }
            }

            int dir = ReadNavDirection();
            if (dir != 0 && mEntries.Count > 0)
            {
                for (int step = 0; step < mEntries.Count; step++)
                {
                    mSelected = (mSelected + dir + mEntries.Count) % mEntries.Count;
                    if (mEntries[mSelected].Enabled)
                    {
                        break;
                    }
                }
            }

            if (ConfirmPressed())
            {
                Activate(mSelected);
            }
        }

        private int ReadNavDirection()
        {
            int dir = 0;
            try
            {
                var lynx = LynxControls.Instance;
                if (lynx != null)
                {
                    var v = LynxControls.GetDeadzoneVector(
                        lynx.GetTwoAxisInputControlVector(FrontEndActions.FrontEndActionSet.NavigateComposite), sDeadZone);
                    if (v.y > 0.5f) dir = -1;
                    else if (v.y < -0.5f) dir = 1;
                }
            }
            catch (Exception)
            {
                // Fall back to raw keys below.
            }
            if (dir == 0)
            {
                if (Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.W)) dir = -1;
                else if (Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.S)) dir = 1;
            }

            if (dir == 0)
            {
                mLastNavDir = 0;
                return 0;
            }
            if (dir != mLastNavDir)
            {
                mLastNavDir = dir;
                mNavRepeatAt = Time.unscaledTime + 0.4f;
                return dir;
            }
            if (Time.unscaledTime >= mNavRepeatAt)
            {
                mNavRepeatAt = Time.unscaledTime + 0.12f;
                return dir;
            }
            return 0;
        }

        private static bool ConfirmPressed()
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                return true;
            }
            try
            {
                return LynxControls.Instance != null && LynxControls.Instance.GetInputWasPressed(FrontEndActions.FrontEndActionSet.Confirm);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void Activate(int index)
        {
            if (index < 0 || index >= mEntries.Count || !mEntries[index].Enabled || mEntries[index].Run == null)
            {
                return;
            }
            var entry = mEntries[index];
            VerboseLog($"Menu: '{entry.Label}' selected.");
            mMenuVisible = false;
            mContextValid = false;
            try
            {
                entry.Run();
            }
            catch (Exception ex)
            {
                FlatHabPlugin.Log.LogError($"'{entry.Label}' failed: {ex}");
            }
            try
            {
                LynxControls.Instance?.ClearInputState(FrontEndActions.FrontEndActionSet.Confirm);
            }
            catch (Exception)
            {
                // Not critical.
            }
        }

        private Hab3DLocationNavigation ActiveLocation()
        {
            Hab3DLocationNavigation first = null;
            var liveCam = mHab != null && mHab.CinemachineBrain != null ? CamGo(mHab.CinemachineBrain.ActiveVirtualCamera) : null;
            foreach (var location in mLocations)
            {
                if (location == null || !location.gameObject.activeSelf)
                {
                    continue;
                }
                if (liveCam != null && CamGo(location.LocationVirtualCamera) == liveCam)
                {
                    return location;
                }
                if (first == null)
                {
                    first = location;
                }
            }
            return first;
        }

        private IEnumerable<(Hab3DLocationNavigation location, NavigationOption option)> FindOptions(Func<NavigationOption, bool> predicate)
        {
            foreach (var location in mLocations)
            {
                if (location == null || Kind(location) != LocationKind.Viewpoint)
                {
                    continue;
                }
                foreach (var option in Options(location))
                {
                    if (option != null && predicate(option))
                    {
                        yield return (location, option);
                    }
                }
            }
        }

        private Hab3DLocationNavigation FindViewpointByCamera(string cameraName) =>
            mLocations.FirstOrDefault(l => l != null && Kind(l) == LocationKind.Viewpoint && CamName(l.LocationVirtualCamera) == cameraName);

        private bool IsUsable(NavigationOption option) =>
            option.CameraToGoTo != null && !mHab.IsInputBlocked(option) && !mHab.IsCameraBlocked(option);

        private bool IsWakeLocation(Hab3DLocationNavigation location)
        {
            var wake = sWakeUpCameraRef(mHab);
            return location != null && wake != null && CamGo(location.LocationVirtualCamera) == wake.gameObject;
        }

        private bool IsStoryPending() =>
            sAfterShiftDataRef(mHab) != null && !sAfterShiftFinishedRef(mHab) && !sInAfterShiftHabRef(mHab);

        private bool IsStoryPlaying() =>
            sAfterShiftDataRef(mHab) != null && sInAfterShiftHabRef(mHab);

        private LocationKind Kind(Hab3DLocationNavigation location) =>
            location != null && mKinds.TryGetValue(location, out var kind) ? kind : LocationKind.Screen;

        private static List<NavigationOption> Options(Hab3DLocationNavigation location) =>
            sOptionsRef(location) ?? new List<NavigationOption>();

        private static Hab3DLocationNavigation OwningLocation(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                var location = t.GetComponent<Hab3DLocationNavigation>();
                if (location != null)
                {
                    return location;
                }
            }
            return null;
        }

        private static bool ActiveWithin(Transform t, Transform root)
        {
            for (; t != null && t != root; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    return false;
                }
            }
            return t == root;
        }

        private static GameObject CamGo(ICinemachineCamera cam)
        {
            var unityObject = cam as UnityEngine.Object;
            return unityObject == null ? null : cam.VirtualCameraGameObject;
        }

        private static string CamName(ICinemachineCamera cam) => CamGo(cam)?.name;

        private static string ButtonText(UIButton button)
        {
            var texts = button.GetComponentsInChildren<TMP_Text>(true);
            var named = texts.FirstOrDefault(t => t.name == "Name" && !string.IsNullOrWhiteSpace(t.text));
            var text = (named ?? texts.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t.text)))?.text;
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static void VerboseLog(string message)
        {
            if (FlatHabPlugin.VerboseLogging.Value)
            {
                FlatHabPlugin.Log.LogInfo(message);
            }
        }

        private void DumpLayout()
        {
            var sb = new StringBuilder("Hab layout:");
            foreach (var location in mLocations)
            {
                sb.Append($"\n  {location.name} [{Kind(location)}] cam={CamName(location.LocationVirtualCamera)} active={location.gameObject.activeSelf}");
                var options = Options(location);
                for (int i = 0; i < options.Count; i++)
                {
                    var o = options[i];
                    sb.Append($"\n    [{i}] -> {CamName(o.CameraToGoTo)} screen={o.GoesToNavigableScreen} key={o.ButtonOption} goTo='{o.GoTo?.GameEvent}' blockConds={o.BlockingConditions?.Count ?? 0}");
                }
            }
            FlatHabPlugin.Log.LogInfo(sb.ToString());
        }

        private void OnGUI()
        {
            if (!mMenuVisible && !mHintVisible)
            {
                return;
            }
            GUI.depth = -1000;
            float scale = Screen.height / 1080f * FlatHabPlugin.MenuScale.Value;
            EnsureStyles(scale);

            if (!mMenuVisible)
            {
                var hint = mFlatMode
                    ? $"{FlatHabPlugin.ToggleKey.Value}: 3D view"
                    : $"{FlatHabPlugin.ToggleKey.Value}: hab menu";
                var size = mSmallHintStyle.CalcSize(new GUIContent(hint));
                GUI.Label(new Rect(Screen.width - size.x - 24 * scale, 20 * scale, size.x, size.y), hint, mSmallHintStyle);
                return;
            }

            var dim = new Color(0f, 0f, 0f, FlatHabPlugin.BackgroundOpacity.Value);
            var oldColor = GUI.color;
            GUI.color = dim;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), mTexDim);
            GUI.color = oldColor;

            float pad = 28 * scale;
            float width = 640 * scale;
            float entryHeight = 76 * scale;
            float gap = 8 * scale;
            float headerHeight = 110 * scale;
            float footerHeight = 54 * scale;
            float height = headerHeight + mEntries.Count * (entryHeight + gap) + footerHeight;
            var panel = new Rect((Screen.width - width) / 2f, (Screen.height - height) / 2f, width, height);

            GUI.DrawTexture(panel, mTexPanel);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 4 * scale), mTexAccent);
            GUI.Label(new Rect(panel.x + pad, panel.y + 20 * scale, width - 2 * pad, 48 * scale), "HAB", mTitleStyle);
            GUI.Label(new Rect(panel.x + pad, panel.y + 66 * scale, width - 2 * pad, 28 * scale), "What would you like to do?", mSubtitleStyle);

            float y = panel.y + headerHeight;
            for (int i = 0; i < mEntries.Count; i++)
            {
                var entry = mEntries[i];
                var rect = new Rect(panel.x + pad, y, width - 2 * pad, entryHeight);
                bool hover = rect.Contains(Event.current.mousePosition);
                if (hover && mMouseMoved && entry.Enabled && Event.current.type == EventType.Repaint)
                {
                    mSelected = i;
                }
                bool selected = i == mSelected && entry.Enabled;

                GUI.DrawTexture(rect, selected ? mTexEntrySelected : mTexEntry);
                if (selected)
                {
                    GUI.DrawTexture(new Rect(rect.x, rect.y, 6 * scale, rect.height), mTexAccent);
                }
                GUI.Label(new Rect(rect.x + 18 * scale, rect.y, 40 * scale, rect.height), i < 9 ? (i + 1).ToString() : "", mNumberStyle);
                GUI.Label(new Rect(rect.x + 64 * scale, rect.y + 10 * scale, rect.width - 80 * scale, 34 * scale), entry.Label,
                    entry.Enabled ? mLabelStyle : mLabelDisabledStyle);
                GUI.Label(new Rect(rect.x + 64 * scale, rect.y + 44 * scale, rect.width - 80 * scale, 24 * scale), entry.Hint ?? "", mHintStyle);

                if (GUI.Button(rect, GUIContent.none, GUIStyle.none) && entry.Enabled)
                {
                    mSelected = i;
                    mQueuedActivation = i;
                }
                y += entryHeight + gap;
            }

            GUI.Label(new Rect(panel.x + pad, panel.yMax - footerHeight + 8 * scale, width - 2 * pad, footerHeight - 16 * scale),
                $"Click, number keys, or arrows + Enter  •  {FlatHabPlugin.ToggleKey.Value}: 3D view  •  Esc: pause", mFooterStyle);
        }

        private void EnsureStyles(float scale)
        {
            if (mStylesReady && Mathf.Approximately(scale, mStyleScale))
            {
                return;
            }
            mStyleScale = scale;
            if (!mStylesReady)
            {
                mTexDim = MakeTexture(Color.white);
                mTexPanel = MakeTexture(new Color(0.055f, 0.07f, 0.085f, 0.94f));
                mTexEntry = MakeTexture(new Color(1f, 1f, 1f, 0.05f));
                mTexEntrySelected = MakeTexture(new Color(1f, 0.55f, 0.12f, 0.22f));
                mTexAccent = MakeTexture(new Color(1f, 0.55f, 0.12f, 1f));
            }

            var white = new Color(0.93f, 0.95f, 0.97f);
            var dimText = new Color(0.62f, 0.66f, 0.7f);
            mTitleStyle = Text(Mathf.RoundToInt(40 * scale), FontStyle.Bold, new Color(1f, 0.6f, 0.2f), TextAnchor.UpperLeft);
            mSubtitleStyle = Text(Mathf.RoundToInt(18 * scale), FontStyle.Normal, dimText, TextAnchor.UpperLeft);
            mLabelStyle = Text(Mathf.RoundToInt(26 * scale), FontStyle.Bold, white, TextAnchor.MiddleLeft);
            mLabelDisabledStyle = Text(Mathf.RoundToInt(26 * scale), FontStyle.Bold, new Color(0.45f, 0.48f, 0.52f), TextAnchor.MiddleLeft);
            mHintStyle = Text(Mathf.RoundToInt(16 * scale), FontStyle.Normal, dimText, TextAnchor.UpperLeft);
            mNumberStyle = Text(Mathf.RoundToInt(24 * scale), FontStyle.Bold, new Color(1f, 0.6f, 0.2f), TextAnchor.MiddleCenter);
            mFooterStyle = Text(Mathf.RoundToInt(14 * scale), FontStyle.Normal, dimText, TextAnchor.MiddleCenter);
            mFooterStyle.wordWrap = true;
            mSmallHintStyle = Text(Mathf.RoundToInt(16 * scale), FontStyle.Bold, new Color(1f, 0.6f, 0.2f, 0.85f), TextAnchor.MiddleRight);
            mStylesReady = true;
        }

        private static GUIStyle Text(int size, FontStyle fontStyle, Color color, TextAnchor anchor)
        {
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Math.Max(8, size),
                fontStyle = fontStyle,
                alignment = anchor,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
            style.normal.textColor = color;
            return style;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }
    }
}
