namespace Pinicola.AvAccessSwitcher.Tests

open System
open System.Diagnostics
open System.IO
open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module AppLoggingTests =

    [<Test>]
    let ``defaultLogDirectory points to Pinicola.AvAccessSwitcher logs folder under LocalApplicationData`` () =
        // Act
        let logDir = AppLogging.defaultLogDirectory ()

        // Assert
        let expected =
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Pinicola.AvAccessSwitcher",
                "logs"
            )

        Assert.That(logDir, Is.EqualTo(expected))

    [<Test>]
    let ``logFileTemplate combines directory with rolling switcher log filename`` () =
        // Arrange
        let logDir = Path.Combine("C:", "temp", "logs")

        // Act
        let template = AppLogging.logFileTemplate logDir

        // Assert
        Assert.That(template, Is.EqualTo(Path.Combine(logDir, "switcher-.log")))

    [<Test>]
    let ``openLogsDirectoryWith ensures directory and starts shell process with logDir`` () =
        // Arrange
        let logDir = Path.Combine("C:", "temp", "Pinicola.AvAccessSwitcher", "logs")
        let ensuredDirs = ResizeArray<string>()
        let startedProcesses = ResizeArray<ProcessStartInfo>()

        // Act
        let result =
            AppLogging.openLogsDirectoryWith
                (fun dir -> ensuredDirs.Add(dir))
                (fun psi -> startedProcesses.Add(psi))
                logDir

        // Assert
        Assert.That(result, Is.True)
        Assert.That(ensuredDirs, Is.EqualTo(box [| logDir |]))
        Assert.That(startedProcesses.Count, Is.EqualTo(1))
        Assert.That(startedProcesses.[0].FileName, Is.EqualTo(logDir))
        Assert.That(startedProcesses.[0].UseShellExecute, Is.True)

    [<Test>]
    let ``openLogsDirectoryWith returns false when starting process throws`` () =
        // Arrange
        let logDir = Path.Combine("C:", "temp", "logs")

        // Act
        let result =
            AppLogging.openLogsDirectoryWith (fun _ -> ()) (fun _ -> invalidOp "boom") logDir

        // Assert
        Assert.That(result, Is.False)
