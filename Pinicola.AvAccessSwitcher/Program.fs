namespace Pinicola.AvAccessSwitcher

open System
open System.IO
open System.Windows.Forms
open Serilog

module Program =

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

    [<STAThread>]
    [<EntryPoint>]
    let main argv =
        initLogging ()

        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(false)

        use context = new TrayApplicationContext()
        Application.Run(context)
        0
