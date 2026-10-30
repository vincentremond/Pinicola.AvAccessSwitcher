namespace Pinicola.AvAccessSwitcher

open System
open System.IO
open System.Threading
open System.Windows.Forms
open Microsoft.Toolkit.Uwp.Notifications
open Serilog

module Program =

    let [<Literal>] private MutexName = @"Local\Pinicola.AvAccessSwitcher.SingleInstance"

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

    [<STAThread>]
    [<EntryPoint>]
    let main argv =
        initLogging ()

        let allowMultiple =
            argv |> Array.exists (fun arg -> String.Equals(arg, "--allow-multiple", StringComparison.OrdinalIgnoreCase))

        if allowMultiple then
            Log.Information("Allow multiple instances requested via command-line argument.")
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(false)

            use context = new TrayApplicationContext()
            Application.Run(context)
            0
        else
            let mutable createdNew = false
            let mutex =
                try
                    Some(new Mutex(true, MutexName, &createdNew))
                with ex ->
                    Log.Error(ex, "Failed to create or acquire single-instance mutex.")
                    None

            match mutex with
            | Some m when not createdNew ->
                Log.Warning("Another instance of Pinicola.AvAccessSwitcher is already running. Exiting.")
                notifyAlreadyRunning ()
                m.Dispose()
                Log.CloseAndFlush()
                0

            | Some m ->
                try
                    Application.EnableVisualStyles()
                    Application.SetCompatibleTextRenderingDefault(false)

                    use context = new TrayApplicationContext()
                    Application.Run(context)
                    0
                finally
                    try
                        m.ReleaseMutex()
                    with _ -> ()
                    m.Dispose()

            | None ->
                Application.EnableVisualStyles()
                Application.SetCompatibleTextRenderingDefault(false)

                use context = new TrayApplicationContext()
                Application.Run(context)
                0
