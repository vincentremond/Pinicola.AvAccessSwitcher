namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher.Program

[<TestFixture>]
module ProgramTests =

    [<Test>]
    let ``parseArgs returns false for empty arguments`` () =
        let options = parseArgs [||]
        Assert.That(options.AllowMultipleInstances, Is.False)

    [<TestCase("--allow-multiple")>]
    [<TestCase("--ALLOW-MULTIPLE")>]
    [<TestCase("--Allow-Multiple")>]
    let ``parseArgs recognizes --allow-multiple case-insensitively`` (flag: string) =
        let options =
            parseArgs [|
                "--other"
                flag
            |]

        Assert.That(options.AllowMultipleInstances, Is.True)

    [<Test>]
    let ``parseArgs returns false when only unrelated arguments are present`` () =
        let options =
            parseArgs [|
                "--verbose"
                "-m"
            |]

        Assert.That(options.AllowMultipleInstances, Is.False)

    [<Test; Explicit("Creates a real OS named mutex on the system.")>]
    let ``acquireSingleInstance acquires mutex and detects second instance until disposed`` () =
        let mutexName = sprintf @"Local\Pinicola.AvAccessSwitcher.Test.%O" (Guid.NewGuid())

        match acquireSingleInstance mutexName with
        | Acquired firstHandle ->
            try
                match acquireSingleInstance mutexName with
                | AlreadyRunning -> ()
                | other -> Assert.Fail(sprintf "Expected AlreadyRunning, got %A" other)
            finally
                firstHandle.Dispose()

            match acquireSingleInstance mutexName with
            | Acquired reacquiredHandle -> reacquiredHandle.Dispose()
            | other -> Assert.Fail(sprintf "Expected Acquired after disposal, got %A" other)

        | other -> Assert.Fail(sprintf "Expected first call to return Acquired, got %A" other)

    [<Test>]
    let ``execute bypasses single-instance lock when --allow-multiple is passed`` () =
        let events = ResizeArray<string>()

        let deps = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    AlreadyRunning
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        let exitCode = execute deps [| "--allow-multiple" |]

        Assert.That(exitCode, Is.EqualTo(0))
        Assert.That(events, Is.EqualTo(box [| "run" |]))

    [<Test>]
    let ``execute runs application and disposes lock when single-instance lock is acquired`` () =
        let events = ResizeArray<string>()

        let handle =
            { new IDisposable with
                member _.Dispose() = events.Add("dispose")
            }

        let deps = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    Acquired handle
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        let exitCode = execute deps [||]

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
        let events = ResizeArray<string>()

        let deps = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    AlreadyRunning
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        let exitCode = execute deps [||]

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
        let events = ResizeArray<string>()

        let deps = {
            AcquireSingleInstance =
                fun () ->
                    events.Add("acquire")
                    AcquisitionFailed(InvalidOperationException("Mutex failure"))
            NotifyAlreadyRunning = fun () -> events.Add("notify")
            RunApplication = fun () -> events.Add("run")
        }

        let exitCode = execute deps [||]

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
