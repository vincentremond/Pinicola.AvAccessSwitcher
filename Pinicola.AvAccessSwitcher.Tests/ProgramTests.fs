namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module ProgramTests =

    [<Test>]
    let ``parseArgs returns false for empty arguments`` () =
        // Arrange
        let argv = [||]

        // Act
        let options = Program.parseArgs argv

        // Assert
        Assert.That(options.AllowMultipleInstances, Is.False)

    [<TestCase("--allow-multiple")>]
    [<TestCase("--ALLOW-MULTIPLE")>]
    [<TestCase("--Allow-Multiple")>]
    let ``parseArgs recognizes --allow-multiple case-insensitively`` (flag: string) =
        // Arrange
        let argv = [|
            "--other"
            flag
        |]

        // Act
        let options = Program.parseArgs argv

        // Assert
        Assert.That(options.AllowMultipleInstances, Is.True)

    [<Test>]
    let ``parseArgs returns false when only unrelated arguments are present`` () =
        // Arrange
        let argv = [|
            "--verbose"
            "-m"
        |]

        // Act
        let options = Program.parseArgs argv

        // Assert
        Assert.That(options.AllowMultipleInstances, Is.False)

    [<Test; Explicit("Creates a real OS named mutex on the system.")>]
    let ``acquireSingleInstance acquires mutex and detects second instance until disposed`` () =
        // Arrange
        let mutexName = sprintf @"Local\Pinicola.AvAccessSwitcher.Test.%O" (Guid.NewGuid())

        // Act
        let firstAttempt = Program.acquireSingleInstance mutexName

        // Assert
        match firstAttempt with
        | Program.SingleInstanceResult.Acquired firstHandle ->
            try
                // Act & Assert: second instance while held
                match Program.acquireSingleInstance mutexName with
                | Program.SingleInstanceResult.AlreadyRunning -> ()
                | other -> Assert.Fail(sprintf "Expected AlreadyRunning, got %A" other)
            finally
                firstHandle.Dispose()

            // Act & Assert: reacquire after disposal
            match Program.acquireSingleInstance mutexName with
            | Program.SingleInstanceResult.Acquired reacquiredHandle -> reacquiredHandle.Dispose()
            | other -> Assert.Fail(sprintf "Expected Acquired after disposal, got %A" other)

        | other -> Assert.Fail(sprintf "Expected first call to return Acquired, got %A" other)

    [<Test>]
    let ``execute bypasses single-instance lock when --allow-multiple is passed`` () =
        // Arrange
        let events = ResizeArray<string>()

        let deps: Program.ProgramDependencies = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    Program.SingleInstanceResult.AlreadyRunning
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        // Act
        let exitCode = Program.execute deps [| "--allow-multiple" |]

        // Assert
        Assert.That(exitCode, Is.EqualTo(0))
        Assert.That(events, Is.EqualTo(box [| "run" |]))

    [<Test>]
    let ``execute runs application and disposes lock when single-instance lock is acquired`` () =
        // Arrange
        let events = ResizeArray<string>()

        let handle =
            { new IDisposable with
                member _.Dispose() = events.Add("dispose")
            }

        let deps: Program.ProgramDependencies = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    Program.SingleInstanceResult.Acquired handle
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        // Act
        let exitCode = Program.execute deps [||]

        // Assert
        Assert.That(exitCode, Is.EqualTo(0))

        Assert.That(
            events,
            Is.EqualTo(
                box [|
                    "acquire"
                    "run"
                    "dispose"
                |]
            )
        )

    [<Test>]
    let ``execute notifies and exits without running application when already running`` () =
        // Arrange
        let events = ResizeArray<string>()

        let deps: Program.ProgramDependencies = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    Program.SingleInstanceResult.AlreadyRunning
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        // Act
        let exitCode = Program.execute deps [||]

        // Assert
        Assert.That(exitCode, Is.EqualTo(0))

        Assert.That(
            events,
            Is.EqualTo(
                box [|
                    "acquire"
                    "notify"
                |]
            )
        )

    [<Test>]
    let ``execute falls back to running application when mutex acquisition fails`` () =
        // Arrange
        let events = ResizeArray<string>()

        let deps: Program.ProgramDependencies = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    Program.SingleInstanceResult.AcquisitionFailed(InvalidOperationException("Mutex failure"))
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        // Act
        let exitCode = Program.execute deps [||]

        // Assert
        Assert.That(exitCode, Is.EqualTo(0))

        Assert.That(
            events,
            Is.EqualTo(
                box [|
                    "acquire"
                    "run"
                |]
            )
        )
