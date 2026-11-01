namespace Pinicola.AvAccessSwitcher

open System
open System.Drawing
open System.Threading
open System.Windows.Forms
open Pinicola.AvAccessSwitcher.Measures
open Serilog
open Vanara.PInvoke

module CursorActivity =

    let IdleThreshold = 2.0<Minute> |> Minute.toTimeSpan

    let ActivityDuration = 400.0<Millisecond> |> Millisecond.toTimeSpan

    let FramesPerMillisecond = 60.0<Frame / Second> / millisecondPerSecond

    let FrameInterval = 1.0<Frame> / FramesPerMillisecond |> Millisecond.toTimeSpan

    let DirectionChangeInterval = ActivityDuration / 4.0

    [<Literal>]
    let DefaultSpeedPxPerSecond = 250.0

    [<Literal>]
    let ScreenEdgeMarginPx = 40

    type ScreenBounds = {
        Left: int
        Top: int
        Right: int
        Bottom: int
    }

    type CursorMovementOptions = {
        Duration: TimeSpan
        FrameInterval: TimeSpan
        DirectionChangeInterval: TimeSpan
        SpeedPxPerSecond: float
    }

    let defaultOptions = {
        Duration = ActivityDuration
        FrameInterval = FrameInterval
        DirectionChangeInterval = DirectionChangeInterval
        SpeedPxPerSecond = DefaultSpeedPxPerSecond
    }

    type CursorDeps = {
        GetCursorPosition: unit -> int * int
        GetScreenBounds: int * int -> ScreenBounds
        SendRelativeMove: int * int -> unit
        RestoreExactPosition: int * int -> unit
        Sleep: TimeSpan -> unit
        NextDirectionAngle: unit -> float
    }

    type IdleTrackerState = {
        LastCursorPos: (int * int) option
        LastMoveTimestamp: TimeSpan
    }

    let initialTrackerState = {
        LastCursorPos = None
        LastMoveTimestamp = TimeSpan.Zero
    }

    /// Evaluates whether the cursor has moved since the last check, or if it has been idle for at least idleThreshold.
    let evaluateCursorIdle
        (idleThreshold: TimeSpan)
        (now: TimeSpan)
        (currentPos: int * int)
        (state: IdleTrackerState)
        : IdleTrackerState * bool =
        match state.LastCursorPos with
        | None ->
            {
                LastCursorPos = Some currentPos
                LastMoveTimestamp = now
            },
            false

        | Some prevPos when prevPos <> currentPos ->
            {
                LastCursorPos = Some currentPos
                LastMoveTimestamp = now
            },
            false

        | Some _ ->
            if now - state.LastMoveTimestamp >= idleThreshold then
                {
                    LastCursorPos = Some currentPos
                    LastMoveTimestamp = now
                },
                true
            else
                state, false

    /// Reflects a direction angle if the projected step would move the cursor outside the safe bounds of its screen.
    let constrainDirectionToBounds
        (bounds: ScreenBounds)
        (curX: int, curY: int)
        (stepDistancePx: float)
        (angle: float)
        : float =
        let minX =
            if bounds.Right - bounds.Left > ScreenEdgeMarginPx * 2 then
                bounds.Left + ScreenEdgeMarginPx
            else
                bounds.Left

        let maxX =
            if bounds.Right - bounds.Left > ScreenEdgeMarginPx * 2 then
                bounds.Right - 1 - ScreenEdgeMarginPx
            else
                bounds.Right - 1

        let minY =
            if bounds.Bottom - bounds.Top > ScreenEdgeMarginPx * 2 then
                bounds.Top + ScreenEdgeMarginPx
            else
                bounds.Top

        let maxY =
            if bounds.Bottom - bounds.Top > ScreenEdgeMarginPx * 2 then
                bounds.Bottom - 1 - ScreenEdgeMarginPx
            else
                bounds.Bottom - 1

        let vx = Math.Cos(angle) * stepDistancePx
        let vy = Math.Sin(angle) * stepDistancePx
        let nextX = float curX + vx
        let nextY = float curY + vy

        let reflectedVx =
            if (nextX < float minX && vx < 0.0) || (nextX > float maxX && vx > 0.0) then
                -vx
            else
                vx

        let reflectedVy =
            if (nextY < float minY && vy < 0.0) || (nextY > float maxY && vy > 0.0) then
                -vy
            else
                vy

        Math.Atan2(reflectedVy, reflectedVx)

    /// Smoothly moves the cursor in random directions within its current screen at the configured speed using SendInput, then smoothly glides back to its original position unless aborted by user mouse movement.
    let performActivityWith (deps: CursorDeps) (options: CursorMovementOptions) : int * int =
        let originPos = deps.GetCursorPosition()
        let originX, originY = originPos
        let screenBounds = deps.GetScreenBounds(originX, originY)

        let minX = screenBounds.Left
        let maxX = max screenBounds.Left (screenBounds.Right - 1)
        let minY = screenBounds.Top
        let maxY = max screenBounds.Top (screenBounds.Bottom - 1)

        let frameInterval =
            if options.FrameInterval > TimeSpan.FromMilliseconds(1.0) then
                options.FrameInterval
            else
                TimeSpan.FromMilliseconds(1.0)

        let totalFrames =
            max 0 (int (Math.Round(options.Duration.TotalMilliseconds / frameInterval.TotalMilliseconds)))

        let framesPerDirection =
            max
                1
                (int (
                    Math.Round(
                        options.DirectionChangeInterval.TotalMilliseconds
                        / frameInterval.TotalMilliseconds
                    )
                ))

        let stepDistancePx = max 0.5 (options.SpeedPxPerSecond * frameInterval.TotalSeconds)

        // Phase 1: Smooth movement along random direction vectors for options.Duration, bouncing off current screen edges.
        // Returns Some userPos if the user moves the mouse during a frame sleep, or None if Phase 1 completes normally.
        let rec moveRandomLoop
            (frameIdx: int)
            (expectedPos: int * int)
            (angle: float)
            (remX: float)
            (remY: float)
            : (int * int) option =
            let curPos = deps.GetCursorPosition()

            if curPos <> expectedPos then
                Log.Information(
                    "User mouse movement detected during cursor activity (expected {ExpectedPos}, actual {ActualPos}). Aborting automatic movement.",
                    expectedPos,
                    curPos
                )

                Some curPos
            elif frameIdx < totalFrames then
                let curX, curY = curPos

                let rawAngle =
                    if frameIdx % framesPerDirection = 0 then
                        deps.NextDirectionAngle()
                    else
                        angle

                let safeAngle =
                    constrainDirectionToBounds screenBounds curPos stepDistancePx rawAngle

                let targetX = Math.Cos(safeAngle) * stepDistancePx + remX
                let targetY = Math.Sin(safeAngle) * stepDistancePx + remY
                let unclampedMoveX = int (Math.Round(targetX))
                let unclampedMoveY = int (Math.Round(targetY))

                let clampedDestX = Math.Clamp(curX + unclampedMoveX, minX, maxX)
                let clampedDestY = Math.Clamp(curY + unclampedMoveY, minY, maxY)
                let moveX = clampedDestX - curX
                let moveY = clampedDestY - curY

                if moveX <> 0 || moveY <> 0 then
                    deps.SendRelativeMove(moveX, moveY)

                let posAfterSend = deps.GetCursorPosition()
                deps.Sleep frameInterval

                moveRandomLoop
                    (frameIdx + 1)
                    posAfterSend
                    safeAngle
                    (targetX - float unclampedMoveX)
                    (targetY - float unclampedMoveY)
            else
                None

        let abortedDuringPhase1 =
            if totalFrames > 0 then
                moveRandomLoop 0 originPos 0.0 0.0 0.0
            else
                None

        match abortedDuringPhase1 with
        | Some userPos -> userPos
        | None ->
            // Phase 2: Closed-loop smooth return glide to the exact origin at the configured speed
            let initialReturnPos = deps.GetCursorPosition()
            let initialReturnX, initialReturnY = initialReturnPos
            let initialDeltaX = originX - initialReturnX
            let initialDeltaY = originY - initialReturnY

            let initialDistance =
                Math.Sqrt(float (initialDeltaX * initialDeltaX + initialDeltaY * initialDeltaY))

            let abortedDuringPhase2 =
                if initialDistance > 0.0 then
                    let maxReturnFrames =
                        max 20 (int (Math.Ceiling(initialDistance / stepDistancePx)) * 4 + 30)

                    let rec returnLoop
                        (stepIdx: int)
                        (expectedPos: int * int)
                        (remX: float)
                        (remY: float)
                        (closeFrames: int)
                        : (int * int) option =
                        let curPos = deps.GetCursorPosition()

                        if curPos <> expectedPos then
                            Log.Information(
                                "User mouse movement detected during cursor return glide (expected {ExpectedPos}, actual {ActualPos}). Aborting return to origin.",
                                expectedPos,
                                curPos
                            )

                            Some curPos
                        else
                            let curX, curY = curPos
                            let dx = originX - curX
                            let dy = originY - curY

                            if (dx <> 0 || dy <> 0) && stepIdx <= maxReturnFrames && closeFrames < 8 then
                                let dist = Math.Sqrt(float (dx * dx + dy * dy))

                                let stepMoveX, stepMoveY, nextRemX, nextRemY, nextCloseFrames =
                                    if dist <= stepDistancePx then
                                        let nudgeX = if Math.Abs(dx) <= 2 then Math.Sign(dx) else dx
                                        let nudgeY = if Math.Abs(dy) <= 2 then Math.Sign(dy) else dy
                                        nudgeX, nudgeY, 0.0, 0.0, (closeFrames + 1)
                                    else
                                        let targetX = (float dx / dist) * stepDistancePx + remX
                                        let targetY = (float dy / dist) * stepDistancePx + remY
                                        let moveX = int (Math.Round(targetX))
                                        let moveY = int (Math.Round(targetY))
                                        moveX, moveY, (targetX - float moveX), (targetY - float moveY), 0

                                if stepMoveX <> 0 || stepMoveY <> 0 then
                                    deps.SendRelativeMove(stepMoveX, stepMoveY)

                                let afterPos = deps.GetCursorPosition()

                                if afterPos <> originPos then
                                    deps.Sleep frameInterval
                                    returnLoop (stepIdx + 1) afterPos nextRemX nextRemY nextCloseFrames
                                else
                                    None
                            else
                                None

                    returnLoop 1 initialReturnPos 0.0 0.0 0
                else
                    None

            match abortedDuringPhase2 with
            | Some userPos -> userPos
            | None ->
                // Final verification step if OS mouse ballistics quantization left a 1-2 px offset
                let finalPos = deps.GetCursorPosition()

                if finalPos <> originPos then
                    deps.RestoreExactPosition(originX, originY)

                originPos

    /// Creates a Win32 INPUT structure for a relative mouse move (mouseData = 0, dx, dy).
    let createRelativeMoveInput (dx: int, dy: int) : User32.INPUT =
        User32.INPUT(User32.MOUSEEVENTF.MOUSEEVENTF_MOVE, 0, dx, dy)

    let private defaultGetCursorPosition () : int * int =
        let ok, pt = User32.GetCursorPos()
        if ok then (pt.X, pt.Y) else (0, 0)

    let private defaultGetScreenBounds (x: int, y: int) : ScreenBounds =
        let rect = Screen.FromPoint(Point(x, y)).Bounds

        {
            Left = rect.Left
            Top = rect.Top
            Right = rect.Right
            Bottom = rect.Bottom
        }

    let private defaultSendRelativeMove (dx: int, dy: int) : unit =
        let inputs = [| createRelativeMoveInput (dx, dy) |]
        let inputSize = System.Runtime.InteropServices.Marshal.SizeOf<User32.INPUT>()
        User32.SendInput(uint32 inputs.Length, inputs, inputSize) |> ignore

    let private defaultRestoreExactPosition (x: int, y: int) : unit =
        let vScreen = SystemInformation.VirtualScreen

        if vScreen.Width > 0 && vScreen.Height > 0 then
            let normX =
                int (((int64 (x - vScreen.Left) * 65536L) + 32768L) / int64 vScreen.Width)

            let normY =
                int (((int64 (y - vScreen.Top) * 65536L) + 32768L) / int64 vScreen.Height)

            let flags =
                User32.MOUSEEVENTF.MOUSEEVENTF_MOVE
                ||| User32.MOUSEEVENTF.MOUSEEVENTF_ABSOLUTE
                ||| User32.MOUSEEVENTF.MOUSEEVENTF_VIRTUALDESK

            let inputs = [| User32.INPUT(flags, 0, normX, normY) |]
            let inputSize = System.Runtime.InteropServices.Marshal.SizeOf<User32.INPUT>()
            User32.SendInput(uint32 inputs.Length, inputs, inputSize) |> ignore

        if defaultGetCursorPosition () <> (x, y) then
            User32.SetCursorPos(x, y) |> ignore

    let private defaultNextDirectionAngle () : float =
        Random.Shared.NextDouble() * 2.0 * Math.PI

    let defaultDeps = {
        GetCursorPosition = defaultGetCursorPosition
        GetScreenBounds = defaultGetScreenBounds
        SendRelativeMove = defaultSendRelativeMove
        RestoreExactPosition = defaultRestoreExactPosition
        Sleep = fun (delay: TimeSpan) -> Thread.Sleep(delay)
        NextDirectionAngle = defaultNextDirectionAngle
    }

    /// Performs brief smooth cursor activity within the current screen using SendInput and glides back to its original coordinates unless aborted by user mouse movement.
    let keepScreenAwake () : int * int =
        try
            Log.Information(
                "No cursor movement detected for {IdleThreshold}. Performing cursor activity at {Speed} px/s...",
                IdleThreshold,
                defaultOptions.SpeedPxPerSecond
            )

            let finalPos = performActivityWith defaultDeps defaultOptions
            Log.Debug("Cursor activity finished at {Position}.", finalPos)
            finalPos
        with ex ->
            Log.Error(ex, "Error while performing cursor activity.")
            defaultGetCursorPosition ()
