namespace Pinicola.AvAccessSwitcher

open System
open System.IO
open System.Threading
open System.Windows.Forms
open Microsoft.Toolkit.Uwp.Notifications
open Serilog

module Program =

    [<Literal>]
    let MutexName = @"Local\Pinicola.AvAccessSwitcher.SingleInstance"

    [<Literal>]
    let AllowMultipleFlag = "--allow-multiple"

    type StartupOptions = { AllowMultipleInstances: bool }

    type SingleInstanceResult =
        | Acquired of IDisposable
        | AlreadyRunning
        | AcquisitionFailed of exn

    type ProgramDependencies = {
        AcquireSingleInstance: unit -> SingleInstanceResult
        NotifyAlreadyRunning: unit -> unit
        RunApplication: unit -> unit
    }

    /// Parses command-line arguments into strongly-typed startup options.
    let parseArgs (argv: string array) : StartupOptions =
        let allowMultiple =
            argv
            |> Array.exists (fun arg -> String.Equals(arg, AllowMultipleFlag, StringComparison.OrdinalIgnoreCase))

        { AllowMultipleInstances = allowMultiple }

    /// Attempts to acquire a named OS mutex for single-instance enforcement.
    let acquireSingleInstance (mutexName: string) : SingleInstanceResult =
        try
            let mutable createdNew = false
            let mutex = new Mutex(true, mutexName, &createdNew)

            if createdNew then
                let handle =
                    { new IDisposable with
                        member _.Dispose() =
                            try
                                mutex.ReleaseMutex()
                            with _ ->
                                ()

                            mutex.Dispose()
                    }

                Acquired handle
            else
                mutex.Dispose()
                AlreadyRunning
        with ex ->
            Log.Error(ex, "Failed to create or acquire single-instance mutex.")
            AcquisitionFailed ex

    /// Executes the startup orchestration flow using injected dependencies.
    let execute (deps: ProgramDependencies) (argv: string array) : int =
        let options = parseArgs argv

        if options.AllowMultipleInstances then
            Log.Information("Allow multiple instances requested via command-line argument.")
            deps.RunApplication()
            0
        else
            match deps.AcquireSingleInstance() with
            | AlreadyRunning ->
                Log.Warning("Another instance of Pinicola.AvAccessSwitcher is already running. Exiting.")
                deps.NotifyAlreadyRunning()
                0

            | Acquired instanceLock ->
                use _ = instanceLock
                deps.RunApplication()
                0

            | AcquisitionFailed _ ->
                deps.RunApplication()
                0

    let private initLogging () =
        let logDir =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pinicola.AvAccessSwitcher",
                "logs"
            )

        Directory.CreateDirectory(logDir) |> ignore
        let logFilePath = Path.Combine(logDir, "switcher-.log")

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFilePath, rollingInterval = RollingInterval.Day, retainedFileCountLimit = Nullable 14)
                .CreateLogger()

        Log.Information("Pinicola.AvAccessSwitcher starting up. Log file path: {LogPath}", logFilePath)

    let private notifyAlreadyRunning () =
        try
            let iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.png")

            let builder =
                ToastContentBuilder()
                    .AddText("AV Access Switcher")
                    .AddText("Another instance is already running in the system tray.")

            if File.Exists(iconPath) then
                let iconUri = Uri(iconPath)
                builder.AddAppLogoOverride(iconUri, ToastGenericAppLogoCrop.None) |> ignore

            builder.Show()
        with ex ->
            Log.Warning(ex, "Could not show already-running toast notification.")

    let private runWinFormsApplication () =
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(false)

        use context = new TrayApplicationContext()
        Application.Run(context)

    let private defaultDependencies = {
        AcquireSingleInstance = fun () -> acquireSingleInstance MutexName
        NotifyAlreadyRunning = notifyAlreadyRunning
        RunApplication = runWinFormsApplication
    }

    [<STAThread>]
    [<EntryPoint>]
    let main argv =
        initLogging ()

        try
            execute defaultDependencies argv
        finally
            Log.CloseAndFlush()
