namespace Pinicola.AvAccessSwitcher

open System.Drawing
open System.Threading
open System.Windows.Forms

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

    // Helper: Safely post UI updates to the WinForms SynchronizationContext
    let postToUi (action: unit -> unit) =
        match syncContext with
        | null -> action()
        | ctx -> ctx.Post((fun _ -> action()), null)

    // Helper: Dynamic status icon generator (Monitor with colored status dot)
    let createStatusIcon (isActive: bool) : Icon =
        let bitmap = new Bitmap(32, 32)
        using (Graphics.FromImage(bitmap)) (fun g ->
            g.Clear(Color.Transparent)
            using (new Pen(Color.White, 2.0f)) (fun pen ->
                g.DrawRectangle(pen, 4, 4, 24, 16)
                g.DrawLine(pen, 16, 20, 16, 26)
                g.DrawLine(pen, 10, 26, 22, 26)
            )
            let statusColor = if isActive then Color.LimeGreen else Color.Orange
            using (new SolidBrush(statusColor)) (fun brush ->
                g.FillEllipse(brush, 18, 10, 8, 8)
            )
        )
        Icon.FromHandle(bitmap.GetHicon())

    let updateUiControls (isConnected: bool) =
        if isConnected then
            statusMenuItem.Text <- "Status: KVM Active (Extended)"
            notifyIcon.Text <- "AV Access Switcher: Active (Extended)"
        else
            statusMenuItem.Text <- "Status: KVM Switched Away (Laptop Only)"
            notifyIcon.Text <- "AV Access Switcher: Switched Away (Laptop Only)"

        try
            notifyIcon.Icon <- createStatusIcon(isConnected)
        with _ -> ()

    // Pure functional MailboxProcessor managing state without mutable variables
    let stateAgent = MailboxProcessor.Start(fun inbox ->
        let rec loop (state: AppState) = async {
            let! msg = inbox.Receive()
            match msg with
            | StopAgent -> 
                return ()

            | SetAutoExtend enabled ->
                return! loop { state with AutoExtendOnReconnect = enabled }

            | ForceLaptop ->
                let success = NativeDisplay.setTopology NativeDisplay.DisplayTopology.ShowOnlyInternal
                postToUi (fun () ->
                    if success then
                        notifyIcon.ShowBalloonTip(2000, "Display Switcher", "Manually set display to Show only on 1.", ToolTipIcon.Info)
                )
                return! loop state

            | ForceExtend ->
                let success = NativeDisplay.setTopology NativeDisplay.DisplayTopology.Extend
                postToUi (fun () ->
                    if success then
                        notifyIcon.ShowBalloonTip(2000, "Display Switcher", "Manually set display to Extend displays.", ToolTipIcon.Info)
                )
                return! loop state

            | CheckStatus userTriggered ->
                let isConnected = KvmDetector.isKvmConnected()

                match state.LastKvmState with
                | None ->
                    postToUi (fun () ->
                        updateUiControls isConnected
                        if userTriggered then
                            let statusMsg = if isConnected then "KVM is ACTIVE on this PC." else "KVM is SWITCHED AWAY."
                            notifyIcon.ShowBalloonTip(2000, "AV Access Switcher Status", statusMsg, ToolTipIcon.Info)
                    )
                    return! loop { state with LastKvmState = Some isConnected }

                | Some prevConnected when prevConnected <> isConnected || userTriggered ->
                    postToUi (fun () -> updateUiControls isConnected)

                    if not isConnected then
                        let applied = NativeDisplay.setTopology NativeDisplay.DisplayTopology.ShowOnlyInternal
                        postToUi (fun () ->
                            let msg = 
                                if applied then 
                                    "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen)."
                                else 
                                    "KVM switched to other PC. (Failed to set topology)."
                            notifyIcon.ShowBalloonTip(3000, "AV Access KVM Switched Away", msg, ToolTipIcon.Warning)
                        )
                    else
                        if state.AutoExtendOnReconnect then
                            let applied = NativeDisplay.setTopology NativeDisplay.DisplayTopology.Extend
                            postToUi (fun () ->
                                let msg = 
                                    if applied then 
                                        "KVM reconnected. Display restored to Extend displays."
                                    else 
                                        "KVM reconnected. (Failed to set topology)."
                                notifyIcon.ShowBalloonTip(3000, "AV Access KVM Reconnected", msg, ToolTipIcon.Info)
                            )
                        else
                            postToUi (fun () ->
                                notifyIcon.ShowBalloonTip(3000, "AV Access KVM Reconnected", "KVM is back online on this PC.", ToolTipIcon.Info)
                            )

                    return! loop { state with LastKvmState = Some isConnected }

                | _ ->
                    return! loop state
        }

        let initialState = { LastKvmState = None; AutoExtendOnReconnect = true }
        loop initialState
    )

    do
        statusMenuItem.Enabled <- false
        
        autoRestoreMenuItem.CheckOnClick <- true
        autoRestoreMenuItem.Checked <- true
        autoRestoreMenuItem.CheckedChanged.Add(fun _ -> 
            stateAgent.Post(SetAutoExtend autoRestoreMenuItem.Checked)
        )

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
        notifyIcon.Icon <- SystemIcons.Application
        notifyIcon.Visible <- true

        checkTimer.Interval <- 2000
        checkTimer.Tick.Add(fun _ -> stateAgent.Post(CheckStatus false))
        checkTimer.Start()

        stateAgent.Post(CheckStatus false)

    member private this.ExitApp() =
        checkTimer.Stop()
        stateAgent.Post(StopAgent)
        notifyIcon.Visible <- false
        notifyIcon.Dispose()
        this.ExitThread()
