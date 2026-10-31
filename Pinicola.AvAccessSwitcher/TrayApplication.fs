namespace Pinicola.AvAccessSwitcher

open System
open System.Drawing
open System.IO
open System.Reflection
open System.Threading
open System.Windows.Forms
open Microsoft.Toolkit.Uwp.Notifications
open Serilog

type DockState =
    | Unplugged
    | SwitchedAway
    | Active

type AppState = {
    LastDockState: DockState option
    AutoExtendOnReconnect: bool
    KeepScreenAwake: bool
    CursorIdleTracker: CursorActivity.IdleTrackerState
}

type AppMsg =
    | CheckStatus of userTriggered: bool
    | SetAutoExtend of enabled: bool
    | SetKeepScreenAwake of enabled: bool
    | ForceLaptop
    | ForceExtend
    | StopAgent

type AppDependencies = {
    IsKvmConnected: unit -> bool
    IsExternalDisplayConnected: unit -> bool
    SetTopology: NativeDisplay.DisplayTopology -> bool
    UpdateUi: DockState -> unit
    ShowToast: string -> string -> unit
    GetTimestamp: unit -> TimeSpan
    GetCursorPosition: unit -> int * int
    KeepScreenAwake: unit -> int * int
    SaveSettings: UserSettings -> unit
}

module TrayStateLogic =

    let createInitialState (settings: UserSettings) : AppState = {
        LastDockState = None
        AutoExtendOnReconnect = settings.AutoExtendOnReconnect
        KeepScreenAwake = settings.KeepScreenAwake
        CursorIdleTracker = CursorActivity.initialTrackerState
    }

    let initialState = createInitialState AppSettings.defaultSettings

    /// Resolves the current dock state from KVM USB presence and external display availability.
    let resolveDockState (isKvmConnected: bool) (isExternalDisplayConnected: bool) : DockState =
        match isExternalDisplayConnected, isKvmConnected with
        | false, false -> DockState.Unplugged
        | false, true ->
            Log.Debug(
                "KVM USB is connected, but no external display target is available yet. Waiting for display before transitioning to Active."
            )

            DockState.Unplugged
        | true, false -> DockState.SwitchedAway
        | true, true -> DockState.Active

    let private updateCursorIdle (deps: AppDependencies) (state: AppState) : AppState =
        if not state.KeepScreenAwake then
            state
        else
            let now = deps.GetTimestamp()
            let currentPos = deps.GetCursorPosition()

            let nextTracker, shouldTrigger =
                CursorActivity.evaluateCursorIdle CursorActivity.IdleThreshold now currentPos state.CursorIdleTracker

            if shouldTrigger then
                let restoredPos = deps.KeepScreenAwake()
                let afterActivity = deps.GetTimestamp()

                {
                    state with
                        CursorIdleTracker = {
                            CursorActivity.LastCursorPos = Some restoredPos
                            CursorActivity.LastMoveTimestamp = afterActivity
                        }
                }
            else
                { state with CursorIdleTracker = nextTracker }

    let step (deps: AppDependencies) (state: AppState) (msg: AppMsg) : AppState option =
        match msg with
        | StopAgent ->
            Log.Information("StateAgent received StopAgent signal.")
            None

        | SetAutoExtend enabled ->
            Log.Information("User toggled AutoExtendOnReconnect to {Enabled}.", enabled)
            let nextState = { state with AutoExtendOnReconnect = enabled }

            deps.SaveSettings {
                AutoExtendOnReconnect = nextState.AutoExtendOnReconnect
                KeepScreenAwake = nextState.KeepScreenAwake
            }

            Some nextState

        | SetKeepScreenAwake enabled ->
            Log.Information("User toggled KeepScreenAwake to {Enabled}.", enabled)

            let tracker: CursorActivity.IdleTrackerState =
                if enabled then
                    {
                        LastCursorPos = Some(deps.GetCursorPosition())
                        LastMoveTimestamp = deps.GetTimestamp()
                    }
                else
                    CursorActivity.initialTrackerState

            let nextState = {
                state with
                    KeepScreenAwake = enabled
                    CursorIdleTracker = tracker
            }

            deps.SaveSettings {
                AutoExtendOnReconnect = nextState.AutoExtendOnReconnect
                KeepScreenAwake = nextState.KeepScreenAwake
            }

            Some nextState

        | ForceLaptop ->
            Log.Information("User requested manual switch: Force Show Only on 1.")
            let success = deps.SetTopology NativeDisplay.DisplayTopology.ShowOnlyInternal

            if success then
                deps.ShowToast "Display Switcher" "Manually set display to Show only on 1."

            Some state

        | ForceExtend ->
            Log.Information("User requested manual switch: Force Extend Displays.")
            let success = deps.SetTopology NativeDisplay.DisplayTopology.Extend

            if success then
                deps.ShowToast "Display Switcher" "Manually set display to Extend displays."

            Some state

        | CheckStatus userTriggered ->
            let stateAfterIdleCheck = updateCursorIdle deps state
            let isKvmConnected = deps.IsKvmConnected()
            let isExternalDisplayConnected = deps.IsExternalDisplayConnected()
            let currentDockState = resolveDockState isKvmConnected isExternalDisplayConnected

            match stateAfterIdleCheck.LastDockState with
            | None ->
                Log.Information(
                    "Initial status check: DockState = {DockState} (KvmConnected = {IsKvmConnected}, ExternalDisplay = {IsExternalDisplayConnected}).",
                    currentDockState,
                    isKvmConnected,
                    isExternalDisplayConnected
                )

                deps.UpdateUi currentDockState

                let statusMsg =
                    match currentDockState with
                    | DockState.Active -> "Started monitoring KVM (Display is ACTIVE on this PC)."
                    | DockState.SwitchedAway -> "Started monitoring KVM (Display is SWITCHED AWAY)."
                    | DockState.Unplugged -> "Started monitoring KVM (Dock is UNPLUGGED)."

                deps.ShowToast "AV Access Switcher Started" statusMsg

                Some { stateAfterIdleCheck with LastDockState = Some currentDockState }

            | Some prevDockState when prevDockState <> currentDockState || userTriggered ->
                if prevDockState <> currentDockState then
                    Log.Information("KVM State Transition: {PrevState} -> {NewState}.", prevDockState, currentDockState)
                else
                    Log.Information("Manual status refresh: DockState = {DockState}.", currentDockState)

                deps.UpdateUi currentDockState

                match currentDockState with
                | DockState.Unplugged ->
                    deps.ShowToast "AV Access Dock Unplugged" "USB-C cable unplugged. Display is on internal screen."

                | DockState.SwitchedAway ->
                    let applied = deps.SetTopology NativeDisplay.DisplayTopology.ShowOnlyInternal

                    let toastMsg =
                        if applied then
                            "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen)."
                        else
                            "KVM switched to other PC. (Failed to set topology)."

                    deps.ShowToast "AV Access KVM Switched Away" toastMsg

                | DockState.Active ->
                    if stateAfterIdleCheck.AutoExtendOnReconnect then
                        let applied = deps.SetTopology NativeDisplay.DisplayTopology.Extend

                        let toastMsg =
                            if applied then
                                "KVM reconnected. Display restored to Extend displays."
                            else
                                "KVM reconnected. (Failed to set topology)."

                        deps.ShowToast "AV Access KVM Reconnected" toastMsg
                    else
                        deps.ShowToast "AV Access KVM Reconnected" "KVM is back online on this PC."

                Some { stateAfterIdleCheck with LastDockState = Some currentDockState }

            | Some _ -> Some stateAfterIdleCheck

type TrayApplicationContext() as this =
    inherit ApplicationContext()

    let syncContext = SynchronizationContext.Current

    // Load embedded custom application icon from assembly resources
    let appIcon =
        try
            let asm = Assembly.GetExecutingAssembly()

            using
                (asm.GetManifestResourceStream("Pinicola.AvAccessSwitcher.icon.ico"))
                (fun stream ->
                    if stream <> null then
                        new Icon(stream)
                    else
                        SystemIcons.Application
                )
        with ex ->
            Log.Warning(ex, "Could not load embedded icon.ico resource. Falling back to default system icon.")
            SystemIcons.Application

    // System Tray Icon & Context Menu Controls
    let notifyIcon = new NotifyIcon()
    let contextMenu = new ContextMenuStrip()

    let statusMenuItem = new ToolStripMenuItem("Status: Initializing...")
    let autoRestoreMenuItem = new ToolStripMenuItem("Auto-Extend on Switch Back")
    let keepAwakeMenuItem = new ToolStripMenuItem("Keep screen awake")
    let checkNowMenuItem = new ToolStripMenuItem("Check Status Now")
    let forceLaptopMenuItem = new ToolStripMenuItem("Force: Show Only on 1")
    let forceExtendMenuItem = new ToolStripMenuItem("Force: Extend Displays")
    let exitMenuItem = new ToolStripMenuItem("Exit")

    let checkTimer = new Timer()

    // Helper: Display rich Windows Toast Notifications with custom logo icon in corner
    let showToast (title: string) (message: string) =
        try
            let iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png")
            let builder = ToastContentBuilder().AddText(title).AddText(message)

            if File.Exists(iconPath) then
                let iconUri = Uri(iconPath)
                builder.AddAppLogoOverride(iconUri, ToastGenericAppLogoCrop.None) |> ignore

            builder.Show()
        with ex ->
            Log.Error(ex, "Failed to show Windows Toast notification. Falling back to BalloonTip.")
            notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.None)

    // Helper: Safely post UI updates to the WinForms SynchronizationContext
    let postToUi (action: unit -> unit) =
        match syncContext with
        | null -> action ()
        | ctx -> ctx.Post((fun _ -> action ()), null)

    // Dynamic status icon generator overlaying status badge onto custom app icon
    let createStatusIcon (isActive: bool) : Icon =
        try
            let bitmap = new Bitmap(appIcon.ToBitmap(), 32, 32)

            using
                (Graphics.FromImage(bitmap))
                (fun g ->
                    g.SmoothingMode <- System.Drawing.Drawing2D.SmoothingMode.AntiAlias
                    let statusColor = if isActive then Color.LimeGreen else Color.Gray
                    using (new SolidBrush(statusColor)) (fun brush -> g.FillEllipse(brush, 18, 18, 12, 12))
                    using (new Pen(Color.White, 1.5f)) (fun pen -> g.DrawEllipse(pen, 18, 18, 12, 12))
                )

            Icon.FromHandle(bitmap.GetHicon())
        with _ ->
            appIcon

    let updateUiControls (dockState: DockState) =
        match dockState with
        | DockState.Active ->
            statusMenuItem.Text <- "Status: KVM Active (Extended)"
            notifyIcon.Text <- "AV Access Switcher: Active (Extended)"
        | DockState.SwitchedAway ->
            statusMenuItem.Text <- "Status: KVM Switched Away (Laptop Only)"
            notifyIcon.Text <- "AV Access Switcher: Switched Away (Laptop Only)"
        | DockState.Unplugged ->
            statusMenuItem.Text <- "Status: Dock Unplugged (Laptop Only)"
            notifyIcon.Text <- "AV Access Switcher: Dock Unplugged (Laptop Only)"

        try
            notifyIcon.Icon <- createStatusIcon (dockState = DockState.Active)
        with _ ->
            ()

    let initialSettings = AppSettings.load ()

    let appDeps = {
        IsKvmConnected = KvmDetector.isKvmConnected
        IsExternalDisplayConnected = NativeDisplay.isExternalDisplayConnected
        SetTopology = NativeDisplay.setTopology
        UpdateUi = fun dockState -> postToUi (fun () -> updateUiControls dockState)
        ShowToast = fun title message -> postToUi (fun () -> showToast title message)
        GetTimestamp = fun () -> TimeSpan.FromMilliseconds(float Environment.TickCount64)
        GetCursorPosition = CursorActivity.defaultDeps.GetCursorPosition
        KeepScreenAwake = CursorActivity.keepScreenAwake
        SaveSettings = AppSettings.save
    }

    // Pure functional MailboxProcessor managing state without mutable variables
    let stateAgent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: AppState) =
                async {
                    let! msg = inbox.Receive()

                    match TrayStateLogic.step appDeps state msg with
                    | None -> return ()
                    | Some nextState -> return! loop nextState
                }

            loop (TrayStateLogic.createInitialState initialSettings)
        )

    do
        statusMenuItem.Enabled <- false

        autoRestoreMenuItem.CheckOnClick <- true
        autoRestoreMenuItem.Checked <- initialSettings.AutoExtendOnReconnect
        autoRestoreMenuItem.CheckedChanged.Add(fun _ -> stateAgent.Post(SetAutoExtend autoRestoreMenuItem.Checked))

        keepAwakeMenuItem.CheckOnClick <- true
        keepAwakeMenuItem.Checked <- initialSettings.KeepScreenAwake
        keepAwakeMenuItem.CheckedChanged.Add(fun _ -> stateAgent.Post(SetKeepScreenAwake keepAwakeMenuItem.Checked))

        checkNowMenuItem.Click.Add(fun _ -> stateAgent.Post(CheckStatus true))
        forceLaptopMenuItem.Click.Add(fun _ -> stateAgent.Post(ForceLaptop))
        forceExtendMenuItem.Click.Add(fun _ -> stateAgent.Post(ForceExtend))
        exitMenuItem.Click.Add(fun _ -> this.ExitApp())

        contextMenu.Items.Add(statusMenuItem) |> ignore
        contextMenu.Items.Add(new ToolStripSeparator()) |> ignore
        contextMenu.Items.Add(autoRestoreMenuItem) |> ignore
        contextMenu.Items.Add(keepAwakeMenuItem) |> ignore
        contextMenu.Items.Add(new ToolStripSeparator()) |> ignore
        contextMenu.Items.Add(forceLaptopMenuItem) |> ignore
        contextMenu.Items.Add(forceExtendMenuItem) |> ignore
        contextMenu.Items.Add(checkNowMenuItem) |> ignore
        contextMenu.Items.Add(new ToolStripSeparator()) |> ignore
        contextMenu.Items.Add(exitMenuItem) |> ignore

        notifyIcon.ContextMenuStrip <- contextMenu
        notifyIcon.Text <- "Pinicola AV Access Switcher"
        notifyIcon.Icon <- appIcon
        notifyIcon.Visible <- true

        checkTimer.Interval <- 2000
        checkTimer.Tick.Add(fun _ -> stateAgent.Post(CheckStatus false))
        checkTimer.Start()

        stateAgent.Post(CheckStatus false)

    member private this.ExitApp() =
        Log.Information("Exiting application context...")
        checkTimer.Stop()
        stateAgent.Post(StopAgent)
        notifyIcon.Visible <- false
        notifyIcon.Dispose()
        Log.CloseAndFlush()
        this.ExitThread()
