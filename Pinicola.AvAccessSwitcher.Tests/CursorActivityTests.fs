namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher
open Pinicola.AvAccessSwitcher.CursorActivity

[<TestFixture>]
module CursorActivityTests =

    let private defaultScreenBounds = {
        Left = 0
        Top = 0
        Right = 1920
        Bottom = 1080
    }

    [<Test>]
    let ``evaluateCursorIdle initializes tracker on first check without triggering activity`` () =
        let now = TimeSpan.FromSeconds(1.0)

        let nextState, shouldTrigger =
            evaluateCursorIdle IdleThreshold now (100, 200) initialTrackerState

        let expected = {
            LastCursorPos = Some(100, 200)
            LastMoveTimestamp = now
        }

        Assert.That(nextState, Is.EqualTo(expected))
        Assert.That(shouldTrigger, Is.False)

    [<Test>]
    let ``evaluateCursorIdle resets timer and does not trigger when cursor position changes`` () =
        let priorState = {
            LastCursorPos = Some(100, 200)
            LastMoveTimestamp = TimeSpan.FromSeconds(1.0)
        }

        let now = TimeSpan.FromSeconds(150.0)

        let nextState, shouldTrigger =
            evaluateCursorIdle IdleThreshold now (105, 210) priorState

        let expected = {
            LastCursorPos = Some(105, 210)
            LastMoveTimestamp = now
        }

        Assert.That(nextState, Is.EqualTo(expected))
        Assert.That(shouldTrigger, Is.False)

    [<Test>]
    let ``evaluateCursorIdle does not trigger when cursor is stationary for less than threshold`` () =
        let priorState = {
            LastCursorPos = Some(100, 200)
            LastMoveTimestamp = TimeSpan.FromSeconds(1.0)
        }

        let now = TimeSpan.FromMilliseconds(120_999.0)

        let nextState, shouldTrigger =
            evaluateCursorIdle IdleThreshold now (100, 200) priorState

        Assert.That(nextState, Is.EqualTo(priorState))
        Assert.That(shouldTrigger, Is.False)

    [<Test>]
    let ``evaluateCursorIdle triggers activity and updates timestamp when stationary for at least 2 minutes`` () =
        let priorState = {
            LastCursorPos = Some(100, 200)
            LastMoveTimestamp = TimeSpan.FromSeconds(1.0)
        }

        let now = TimeSpan.FromSeconds(121.0)

        let nextState, shouldTrigger =
            evaluateCursorIdle IdleThreshold now (100, 200) priorState

        let expected = {
            LastCursorPos = Some(100, 200)
            LastMoveTimestamp = now
        }

        Assert.That(nextState, Is.EqualTo(expected))
        Assert.That(shouldTrigger, Is.True)

    [<Test>]
    let ``performActivityWith smoothly moves cursor at configured speed and glides back to origin`` () =
        let currentPos = ref (500, 400)
        let relativeMoves = ResizeArray<int * int>()
        let sleepDelays = ResizeArray<TimeSpan>()
        let angleIdx = ref 0

        let angles = [|
            0.0 // Right (+X)
            Math.PI / 2.0 // Down (+Y)
            Math.PI // Left (-X)
            1.5 * Math.PI // Up (-Y)
        |]

        let deps = {
            GetCursorPosition = fun () -> currentPos.Value
            GetScreenBounds = fun _ -> defaultScreenBounds
            SendRelativeMove =
                fun (dx, dy) ->
                    let x, y = currentPos.Value
                    currentPos.Value <- (x + dx, y + dy)
                    relativeMoves.Add((dx, dy))
            RestoreExactPosition = fun pos -> currentPos.Value <- pos
            Sleep = fun delay -> sleepDelays.Add(delay)
            NextDirectionAngle =
                fun () ->
                    let a = angles.[angleIdx.Value % angles.Length]
                    angleIdx.Value <- angleIdx.Value + 1
                    a
        }

        let options = {
            Duration = TimeSpan.FromSeconds(2.0)
            FrameInterval = TimeSpan.FromMilliseconds(20.0)
            DirectionChangeInterval = TimeSpan.FromMilliseconds(600.0)
            SpeedPxPerSecond = 200.0
        }

        let finalPos = performActivityWith deps options

        Assert.That(finalPos, Is.EqualTo((500, 400)))
        Assert.That(currentPos.Value, Is.EqualTo((500, 400)))
        Assert.That(relativeMoves.Count, Is.GreaterThan(100))

        // Verify higher speed produces larger per-frame deltas
        let fastMoves = ResizeArray<int * int>()

        let fastDeps = {
            deps with
                SendRelativeMove =
                    fun (dx, dy) ->
                        let x, y = currentPos.Value
                        currentPos.Value <- (x + dx, y + dy)
                        fastMoves.Add((dx, dy))
        }

        let _ = performActivityWith fastDeps { options with SpeedPxPerSecond = 600.0 }

        let avgSlowStep =
            relativeMoves
            |> Seq.averageBy (fun (dx, dy) -> Math.Sqrt(float (dx * dx + dy * dy)))

        let avgFastStep =
            fastMoves
            |> Seq.averageBy (fun (dx, dy) -> Math.Sqrt(float (dx * dx + dy * dy)))

        Assert.That(avgFastStep, Is.GreaterThan(avgSlowStep * 2.0))

    [<Test>]
    let ``performActivityWith keeps cursor strictly inside its current screen bounds and bounces off edges`` () =
        // Start near the top-left corner of a secondary monitor at (1920, 0) .. (3840, 1080)
        let secondaryBounds = {
            Left = 1920
            Top = 0
            Right = 3840
            Bottom = 1080
        }

        let currentPos = ref (1925, 5)
        let visitedPositions = ResizeArray<int * int>()

        let deps = {
            GetCursorPosition = fun () -> currentPos.Value
            GetScreenBounds = fun _ -> secondaryBounds
            SendRelativeMove =
                fun (dx, dy) ->
                    let x, y = currentPos.Value
                    let next = (x + dx, y + dy)
                    currentPos.Value <- next
                    visitedPositions.Add(next)
            RestoreExactPosition = fun pos -> currentPos.Value <- pos
            Sleep = fun _ -> ()
            // Always try to move diagonally up-left (-X, -Y) towards the primary screen / top edge
            NextDirectionAngle = fun () -> -0.75 * Math.PI
        }

        let options = {
            Duration = TimeSpan.FromSeconds(2.0)
            FrameInterval = TimeSpan.FromMilliseconds(20.0)
            DirectionChangeInterval = TimeSpan.FromMilliseconds(400.0)
            SpeedPxPerSecond = 300.0
        }

        let finalPos = performActivityWith deps options

        Assert.That(finalPos, Is.EqualTo((1925, 5)))
        Assert.That(currentPos.Value, Is.EqualTo((1925, 5)))
        Assert.That(visitedPositions, Is.Not.Empty)

        for (x, y) in visitedPositions do
            Assert.That(x, Is.GreaterThanOrEqualTo(secondaryBounds.Left))
            Assert.That(x, Is.LessThan(secondaryBounds.Right))
            Assert.That(y, Is.GreaterThanOrEqualTo(secondaryBounds.Top))
            Assert.That(y, Is.LessThan(secondaryBounds.Bottom))

    [<Test>]
    let ``performActivityWith restores exact origin when OS pointer ballistics scales relative moves`` () =
        let currentPos = ref (600, 450)

        let deps = {
            GetCursorPosition = fun () -> currentPos.Value
            GetScreenBounds = fun _ -> defaultScreenBounds
            SendRelativeMove =
                fun (dx, dy) ->
                    let x, y = currentPos.Value
                    // Simulate OS mouse acceleration/quantization scaling relative mickeys by 1.6x
                    let actualDx = int (Math.Round(float dx * 1.6))
                    let actualDy = int (Math.Round(float dy * 1.6))
                    currentPos.Value <- (x + actualDx, y + actualDy)
            RestoreExactPosition = fun pos -> currentPos.Value <- pos
            Sleep = fun _ -> ()
            NextDirectionAngle = fun () -> 0.4
        }

        let options = {
            Duration = TimeSpan.FromMilliseconds(500.0)
            FrameInterval = TimeSpan.FromMilliseconds(20.0)
            DirectionChangeInterval = TimeSpan.FromMilliseconds(250.0)
            SpeedPxPerSecond = 250.0
        }

        let finalPos = performActivityWith deps options

        Assert.That(finalPos, Is.EqualTo((600, 450)))
        Assert.That(currentPos.Value, Is.EqualTo((600, 450)))

    [<Test>]
    let ``createRelativeMoveInput populates MOUSEINPUT dx and dy fields with zero mouseData`` () =
        let input = createRelativeMoveInput (15, -25)

        Assert.That(input.``type``, Is.EqualTo(Vanara.PInvoke.User32.INPUTTYPE.INPUT_MOUSE))
        Assert.That(input.mi.dwFlags, Is.EqualTo(Vanara.PInvoke.User32.MOUSEEVENTF.MOUSEEVENTF_MOVE))
        Assert.That(input.mi.mouseData, Is.EqualTo(0))
        Assert.That(input.mi.dx, Is.EqualTo(15))
        Assert.That(input.mi.dy, Is.EqualTo(-25))

    [<Test; Explicit("Physically moves the mouse cursor on the system for 2 seconds.")>]
    let ``keepScreenAwake physically moves the cursor for 2 seconds and restores original position`` () =
        let before = defaultDeps.GetCursorPosition()
        let returned = keepScreenAwake ()
        let after = defaultDeps.GetCursorPosition()
        Assert.That(returned, Is.EqualTo(before))
        Assert.That(after, Is.EqualTo(before))
