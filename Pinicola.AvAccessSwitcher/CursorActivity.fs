namespace Pinicola.AvAccessSwitcher

open System
open System.Drawing
open System.Threading
open System.Windows.Forms
open Serilog
open Vanara.PInvoke

module CursorActivity =

    let IdleThreshold = TimeSpan.FromMinutes(2.0)

    let ActivityDuration = TimeSpan.FromSeconds(2.0)

    let FrameInterval = TimeSpan.FromMilliseconds(16.0)

    let DirectionChangeInterval = TimeSpan.FromMilliseconds(400.0)

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

    /// Smoothly moves the cursor in random directions within its current screen at the configured speed using SendInput, then smoothly glides back to its original position.
    let performActivityWith (deps: CursorDeps) (options: CursorMovementOptions) : int * int =
        let originX, originY = deps.GetCursorPosition()
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

        // Phase 1: Smooth movement along random direction vectors for options.Duration, bouncing off current screen edges
        let rec moveRandomLoop (frameIdx: int) (angle: float) (remX: float) (remY: float) =
            if frameIdx < totalFrames then
                let curPos = deps.GetCursorPosition()
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

                deps.Sleep frameInterval

                moveRandomLoop
                    (frameIdx + 1)
                    safeAngle
                    (targetX - float unclampedMoveX)
                    (targetY - float unclampedMoveY)

        if totalFrames > 0 then
            moveRandomLoop 0 0.0 0.0 0.0

        // Phase 2: Closed-loop smooth return glide to the exact origin at the configured speed
        let initialReturnX, initialReturnY = deps.GetCursorPosition()
        let initialDeltaX = originX - initialReturnX
        let initialDeltaY = originY - initialReturnY

        let initialDistance =
            Math.Sqrt(float (initialDeltaX * initialDeltaX + initialDeltaY * initialDeltaY))

        if initialDistance > 0.0 then
            let maxReturnFrames =
                max 20 (int (Math.Ceiling(initialDistance / stepDistancePx)) * 4 + 30)

            let rec returnLoop (stepIdx: int) (remX: float) (remY: float) (closeFrames: int) =
                let curX, curY = deps.GetCursorPosition()
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

                    let afterX, afterY = deps.GetCursorPosition()

                    if (afterX, afterY) <> (originX, originY) then
                        deps.Sleep frameInterval
                        returnLoop (stepIdx + 1) nextRemX nextRemY nextCloseFrames

            returnLoop 1 0.0 0.0 0

        // Final verification step if OS mouse ballistics quantization left a 1-2 px offset
        let finalX, finalY = deps.GetCursorPosition()

        if (finalX, finalY) <> (originX, originY) then
            deps.RestoreExactPosition(originX, originY)

        originX, originY

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

    /// Performs smooth cursor activity for 2 seconds within the current screen using SendInput and glides back to its original coordinates.
    let keepScreenAwake () : int * int =
        try
            Log.Information(
                "No cursor movement detected for {IdleThreshold}. Performing cursor activity at {Speed} px/s...",
                IdleThreshold,
                defaultOptions.SpeedPxPerSecond
            )

            let finalPos = performActivityWith defaultDeps defaultOptions
            Log.Debug("Cursor activity completed; cursor restored to {Position}.", finalPos)
            finalPos
        with ex ->
            Log.Error(ex, "Error while performing cursor activity.")
            defaultGetCursorPosition ()
