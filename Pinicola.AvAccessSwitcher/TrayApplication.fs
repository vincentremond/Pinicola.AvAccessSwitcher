namespace Pinicola.AvAccessSwitcher

open System
open System.Drawing
open System.IO
open System.Reflection
open System.Threading
open System.Windows.Forms
open Microsoft.Toolkit.Uwp.Notifications
open Serilog

type AppState = {
    LastKvmState: bool option
    AutoExtendOnReconnect: bool
}

type AppMsg =
    | CheckStatus of userTriggered: bool
    | SetAutoExtend of enabled: bool
    | ForceLaptop
    | ForceExtend
    | StopAgent

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
                    let statusColor = if isActive then Color.LimeGreen else Color.Orange
                    using (new SolidBrush(statusColor)) (fun brush -> g.FillEllipse(brush, 18, 18, 12, 12))
                    using (new Pen(Color.White, 1.5f)) (fun pen -> g.DrawEllipse(pen, 18, 18, 12, 12))
                )

            Icon.FromHandle(bitmap.GetHicon())
        with _ ->
            appIcon

    let updateUiControls (isConnected: bool) =
        if isConnected then
            statusMenuItem.Text <- "Status: KVM Active (Extended)"
            notifyIcon.Text <- "AV Access Switcher: Active (Extended)"
        else
            statusMenuItem.Text <- "Status: KVM Switched Away (Laptop Only)"
            notifyIcon.Text <- "AV Access Switcher: Switched Away (Laptop Only)"

        try
            notifyIcon.Icon <- createStatusIcon (isConnected)
        with _ ->
            ()

    // Pure functional MailboxProcessor managing state without mutable variables
    let stateAgent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (state: AppState) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | StopAgent ->
                        Log.Information("StateAgent received StopAgent signal.")
                        return ()

                    | SetAutoExtend enabled ->
                        Log.Information("User toggled AutoExtendOnReconnect to {Enabled}.", enabled)
                        return! loop { state with AutoExtendOnReconnect = enabled }

                    | ForceLaptop ->
                        Log.Information("User requested manual switch: Force Show Only on 1.")

                        let success =
                            NativeDisplay.setTopology NativeDisplay.DisplayTopology.ShowOnlyInternal

                        postToUi (fun () ->
                            if success then
                                showToast "Display Switcher" "Manually set display to Show only on 1."
                        )

                        return! loop state

                    | ForceExtend ->
                        Log.Information("User requested manual switch: Force Extend Displays.")
                        let success = NativeDisplay.setTopology NativeDisplay.DisplayTopology.Extend

                        postToUi (fun () ->
                            if success then
                                showToast "Display Switcher" "Manually set display to Extend displays."
                        )

                        return! loop state

                    | CheckStatus userTriggered ->
                        let isConnected = KvmDetector.isKvmConnected ()

                        match state.LastKvmState with
                        | None ->
                            Log.Information("Initial status check: KVM IsConnected = {IsConnected}.", isConnected)

                            postToUi (fun () ->
                                updateUiControls isConnected

                                let statusMsg =
                                    if isConnected then
                                        "Started monitoring KVM (Display is ACTIVE on this PC)."
                                    else
                                        "Started monitoring KVM (Display is SWITCHED AWAY)."

                                showToast "AV Access Switcher Started" statusMsg
                            )

                            return! loop { state with LastKvmState = Some isConnected }

                        | Some prevConnected when prevConnected <> isConnected || userTriggered ->
                            if prevConnected <> isConnected then
                                Log.Information(
                                    "KVM State Transition: {PrevState} -> {NewState}.",
                                    (if prevConnected then "Active" else "SwitchedAway"),
                                    (if isConnected then "Active" else "SwitchedAway")
                                )
                            else
                                Log.Information("Manual status refresh: IsConnected = {IsConnected}.", isConnected)

                            postToUi (fun () -> updateUiControls isConnected)

                            if not isConnected then
                                let applied =
                                    NativeDisplay.setTopology NativeDisplay.DisplayTopology.ShowOnlyInternal

                                postToUi (fun () ->
                                    let msg =
                                        if applied then
                                            "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen)."
                                        else
                                            "KVM switched to other PC. (Failed to set topology)."

                                    showToast "AV Access KVM Switched Away" msg
                                )
                            else if state.AutoExtendOnReconnect then
                                let applied = NativeDisplay.setTopology NativeDisplay.DisplayTopology.Extend

                                postToUi (fun () ->
                                    let msg =
                                        if applied then
                                            "KVM reconnected. Display restored to Extend displays."
                                        else
                                            "KVM reconnected. (Failed to set topology)."

                                    showToast "AV Access KVM Reconnected" msg
                                )
                            else
                                postToUi (fun () ->
                                    showToast "AV Access KVM Reconnected" "KVM is back online on this PC."
                                )

                            return! loop { state with LastKvmState = Some isConnected }

                        | _ -> return! loop state
                }

            let initialState = {
                LastKvmState = None
                AutoExtendOnReconnect = true
            }

            loop initialState
        )

    do
        statusMenuItem.Enabled <- false

        autoRestoreMenuItem.CheckOnClick <- true
        autoRestoreMenuItem.Checked <- true
        autoRestoreMenuItem.CheckedChanged.Add(fun _ -> stateAgent.Post(SetAutoExtend autoRestoreMenuItem.Checked))

        checkNowMenuItem.Click.Add(fun _ -> stateAgent.Post(CheckStatus true))
        forceLaptopMenuItem.Click.Add(fun _ -> stateAgent.Post(ForceLaptop))
        forceExtendMenuItem.Click.Add(fun _ -> stateAgent.Post(ForceExtend))
        exitMenuItem.Click.Add(fun _ -> this.ExitApp())

        contextMenu.Items.Add(statusMenuItem) |> ignore
        contextMenu.Items.Add(new ToolStripSeparator()) |> ignore
        contextMenu.Items.Add(autoRestoreMenuItem) |> ignore
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
