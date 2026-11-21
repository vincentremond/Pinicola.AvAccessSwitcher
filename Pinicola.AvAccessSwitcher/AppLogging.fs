namespace Pinicola.AvAccessSwitcher

open System
open System.Diagnostics
open System.IO
open Serilog

[<RequireQualifiedAccess>]
module internal AppLogging =

    /// Returns the default directory where rolling log files are stored.
    let defaultLogDirectory () : string =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pinicola.AvAccessSwitcher",
            "logs"
        )

    /// Returns the Serilog rolling log file path template for the given directory.
    let logFileTemplate (logDir: string) : string = Path.Combine(logDir, "switcher-.log")

    /// Initializes the global Serilog file logger and records the startup banner.
    let initLogging () : unit =
        let logDir = defaultLogDirectory ()
        Directory.CreateDirectory(logDir) |> ignore
        let logFilePath = logFileTemplate logDir

        Log.Logger <-
            LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(logFilePath, rollingInterval = RollingInterval.Day, retainedFileCountLimit = Nullable 14)
                .CreateLogger()

        let versionInfo = AppVersion.current

        Log.Information(
            "Pinicola.AvAccessSwitcher {Version} starting up. Log file path: {LogPath}",
            versionInfo.InformationalVersion,
            logFilePath
        )

    /// Opens the logs directory using injected directory-creation and process-start helpers.
    let openLogsDirectoryWith
        (ensureDir: string -> unit)
        (startProcess: ProcessStartInfo -> unit)
        (logDir: string)
        : bool =
        try
            ensureDir logDir
            let psi = ProcessStartInfo(logDir, UseShellExecute = true)
            startProcess psi
            true
        with ex ->
            Log.Error(ex, "Failed to open logs directory {LogDir}.", logDir)
            false

    /// Ensures the default logs directory exists and opens it in the system file explorer.
    let openLogsDirectory () : bool =
        openLogsDirectoryWith
            (fun dir -> Directory.CreateDirectory(dir) |> ignore)
            (fun psi ->
                use _ = Process.Start(psi)
                ()
            )
            (defaultLogDirectory ())
