namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher
open Pinicola.AvAccessSwitcher.NativeDisplay

[<TestFixture>]
module TrayApplicationTests =

    type TestHarness = {
        Dependencies: AppDependencies
        AppliedTopologies: ResizeArray<DisplayTopology>
        UiStates: ResizeArray<DockState>
        Toasts: ResizeArray<string * string>
        SavedSettings: ResizeArray<UserSettings>
        ActivityCount: int ref
        Timestamp: TimeSpan ref
        CursorPos: (int * int) ref
    }

    let private createHarness
        (isKvmConnected: bool)
        (isExternalDisplayConnected: bool)
        (topologySuccess: bool)
        : TestHarness =
        let appliedTopologies = ResizeArray<DisplayTopology>()
        let uiStates = ResizeArray<DockState>()
        let toasts = ResizeArray<string * string>()
        let savedSettings = ResizeArray<UserSettings>()
        let activityCount = ref 0
        let timestamp = ref (TimeSpan.FromSeconds(1.0))
        let cursorPos = ref (100, 100)

        let deps = {
            IsKvmConnected = fun () -> isKvmConnected
            IsExternalDisplayConnected = fun () -> isExternalDisplayConnected
            SetTopology =
                fun topology ->
                    appliedTopologies.Add(topology)
                    topologySuccess
            UpdateUi = fun dockState -> uiStates.Add(dockState)
            ShowToast = fun title msg -> toasts.Add((title, msg))
            GetTimestamp = fun () -> timestamp.Value
            GetCursorPosition = fun () -> cursorPos.Value
            KeepScreenAwake =
                fun () ->
                    activityCount.Value <- activityCount.Value + 1
                    cursorPos.Value
            SaveSettings = fun settings -> savedSettings.Add(settings)
        }

        {
            Dependencies = deps
            AppliedTopologies = appliedTopologies
            UiStates = uiStates
            Toasts = toasts
            SavedSettings = savedSettings
            ActivityCount = activityCount
            Timestamp = timestamp
            CursorPos = cursorPos
        }

    [<Test>]
    let ``resolveDockState maps KVM and external display signals to expected DockState`` () =
        // Act
        let bothFalse = TrayStateLogic.resolveDockState false false
        let kvmOnly = TrayStateLogic.resolveDockState true false
        let displayOnly = TrayStateLogic.resolveDockState false true
        let bothTrue = TrayStateLogic.resolveDockState true true

        // Assert
        Assert.That(bothFalse, Is.EqualTo(DockState.Unplugged))
        Assert.That(kvmOnly, Is.EqualTo(DockState.Unplugged))
        Assert.That(displayOnly, Is.EqualTo(DockState.SwitchedAway))
        Assert.That(bothTrue, Is.EqualTo(DockState.Active))

    [<Test>]
    let ``createInitialState populates AutoExtendOnReconnect and KeepScreenAwake from UserSettings`` () =
        // Arrange
        let settings = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = true
        }

        // Act
        let state = TrayStateLogic.createInitialState settings

        // Assert
        Assert.That(state.AutoExtendOnReconnect, Is.False)
        Assert.That(state.KeepScreenAwake, Is.True)
        Assert.That(state.LastDockState, Is.EqualTo(None))

    [<Test>]
    let ``StopAgent returns None`` () =
        // Arrange
        let harness = createHarness true true true

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState StopAgent

        // Assert
        Assert.That(result, Is.EqualTo(None))

    [<Test>]
    let ``SetAutoExtend updates AutoExtendOnReconnect in state and saves settings`` () =
        // Arrange
        let harness = createHarness true true true

        let expected =
            Some { TrayStateLogic.initialState with AutoExtendOnReconnect = false }

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (SetAutoExtend false)

        // Assert
        Assert.That(result, Is.EqualTo(expected))

        Assert.That(
            harness.SavedSettings,
            Is.EqualTo(
                box [|
                    {
                        AutoExtendOnReconnect = false
                        KeepScreenAwake = false
                    }
                |]
            )
        )

    [<Test>]
    let ``SetKeepScreenAwake initializes and clears CursorIdleTracker and saves settings`` () =
        // Arrange
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(5.0)
        harness.CursorPos.Value <- (250, 350)

        let expectedEnabled =
            Some {
                TrayStateLogic.initialState with
                    KeepScreenAwake = true
                    CursorIdleTracker = {
                        CursorActivity.LastCursorPos = Some(250, 350)
                        CursorActivity.LastMoveTimestamp = TimeSpan.FromSeconds(5.0)
                    }
            }

        // Act
        let enabledResult =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (SetKeepScreenAwake true)

        let disabledResult =
            TrayStateLogic.step harness.Dependencies enabledResult.Value (SetKeepScreenAwake false)

        // Assert
        Assert.That(enabledResult, Is.EqualTo(expectedEnabled))
        Assert.That(disabledResult, Is.EqualTo(Some TrayStateLogic.initialState))

        Assert.That(
            harness.SavedSettings,
            Is.EqualTo(
                box [|
                    {
                        AutoExtendOnReconnect = true
                        KeepScreenAwake = true
                    }
                    {
                        AutoExtendOnReconnect = true
                        KeepScreenAwake = false
                    }
                |]
            )
        )

    [<Test>]
    let ``ForceLaptop sets ShowOnlyInternal topology and shows toast on success`` () =
        // Arrange
        let harness = createHarness true true true

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceLaptop

        // Assert
        Assert.That(result, Is.EqualTo(Some TrayStateLogic.initialState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("Display Switcher", "Manually set display to Show only on 1.") |])
        )

    [<Test>]
    let ``ForceLaptop does not show toast when SetTopology fails`` () =
        // Arrange
        let harness = createHarness true true false

        // Act
        let _ =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceLaptop

        // Assert
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``ForceExtend sets Extend topology and shows toast on success`` () =
        // Arrange
        let harness = createHarness true true true

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceExtend

        // Assert
        Assert.That(result, Is.EqualTo(Some TrayStateLogic.initialState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("Display Switcher", "Manually set display to Extend displays.") |])
        )

    [<TestCase(true, true, "Started monitoring KVM (Display is ACTIVE on this PC).")>]
    [<TestCase(false, true, "Started monitoring KVM (Display is SWITCHED AWAY).")>]
    [<TestCase(false, false, "Started monitoring KVM (Dock is UNPLUGGED).")>]
    let ``Initial CheckStatus updates UI and shows startup toast without changing topology``
        (isKvmConnected: bool, isExternalDisplayConnected: bool, expectedMsg: string)
        =
        // Arrange
        let harness = createHarness isKvmConnected isExternalDisplayConnected true

        let expectedDockState =
            TrayStateLogic.resolveDockState isKvmConnected isExternalDisplayConnected

        let expectedState =
            Some {
                TrayStateLogic.initialState with
                    LastDockState = Some expectedDockState
                    AutoExtendOnReconnect = true
            }

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| expectedDockState |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access Switcher Started", expectedMsg) |]))

    [<TestCase(true, "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen).")>]
    [<TestCase(false, "KVM switched to other PC. (Failed to set topology).")>]
    let ``CheckStatus transitions from Active to SwitchedAway and applies ShowOnlyInternal``
        (topologySuccess: bool, expectedMsg: string)
        =
        // Arrange
        let harness = createHarness false true topologySuccess

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let expectedState =
            Some { priorState with LastDockState = Some DockState.SwitchedAway }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.SwitchedAway |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Switched Away", expectedMsg) |]))

    [<TestCase(true, "KVM reconnected. Display restored to Extend displays.")>]
    [<TestCase(false, "KVM reconnected. (Failed to set topology).")>]
    let ``CheckStatus transitions from SwitchedAway to Active with AutoExtend enabled``
        (topologySuccess: bool, expectedMsg: string)
        =
        // Arrange
        let harness = createHarness true true topologySuccess

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.SwitchedAway
                AutoExtendOnReconnect = true
        }

        let expectedState = Some { priorState with LastDockState = Some DockState.Active }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Reconnected", expectedMsg) |]))

    [<Test>]
    let ``CheckStatus transitions from SwitchedAway to Active with AutoExtend disabled`` () =
        // Arrange
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.SwitchedAway
                AutoExtendOnReconnect = false
        }

        let expectedState = Some { priorState with LastDockState = Some DockState.Active }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("AV Access KVM Reconnected", "KVM is back online on this PC.") |])
        )

    [<Test>]
    let ``CheckStatus transitions from Active to Unplugged without calling SetTopology`` () =
        // Arrange
        let harness = createHarness false false true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let expectedState =
            Some { priorState with LastDockState = Some DockState.Unplugged }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Unplugged |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("AV Access Dock Unplugged", "USB-C cable unplugged. Display is on internal screen.") |])
        )

    [<Test>]
    let ``CheckStatus waits for external display on replug when KVM USB enumerates first`` () =
        // Arrange (Step 1: USB enumerates first, ExternalDisplay = false)
        let usbOnlyHarness = createHarness true false true

        let unpluggedState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Unplugged
                AutoExtendOnReconnect = true
        }

        // Act (Step 1)
        let afterUsbStep =
            TrayStateLogic.step usbOnlyHarness.Dependencies unpluggedState (CheckStatus false)

        // Assert (Step 1: stays Unplugged)
        Assert.That(afterUsbStep, Is.EqualTo(Some unpluggedState))
        Assert.That(usbOnlyHarness.AppliedTopologies, Is.Empty)
        Assert.That(usbOnlyHarness.UiStates, Is.Empty)
        Assert.That(usbOnlyHarness.Toasts, Is.Empty)

        // Arrange (Step 2: DisplayPort Alt-Mode finishes handshake)
        let fullHarness = createHarness true true true

        let expectedActiveState =
            Some { unpluggedState with LastDockState = Some DockState.Active }

        // Act (Step 2)
        let afterDisplayStep =
            TrayStateLogic.step fullHarness.Dependencies afterUsbStep.Value (CheckStatus false)

        // Assert (Step 2: transitions to Active and applies Extend)
        Assert.That(afterDisplayStep, Is.EqualTo(expectedActiveState))
        Assert.That(fullHarness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(fullHarness.UiStates, Is.EqualTo(box [| DockState.Active |]))

        Assert.That(
            fullHarness.Toasts,
            Is.EqualTo(box [| ("AV Access KVM Reconnected", "KVM reconnected. Display restored to Extend displays.") |])
        )

    [<Test>]
    let ``CheckStatus transitions from Unplugged to SwitchedAway when replugged while KVM is on other PC`` () =
        // Arrange
        let harness = createHarness false true true

        let unpluggedState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Unplugged
                AutoExtendOnReconnect = true
        }

        let expectedState =
            Some { unpluggedState with LastDockState = Some DockState.SwitchedAway }

        // Act
        let result =
            TrayStateLogic.step harness.Dependencies unpluggedState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.SwitchedAway |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(
                box [|
                    ("AV Access KVM Switched Away",
                     "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen).")
                |]
            )
        )

    [<Test>]
    let ``CheckStatus does nothing when state is unchanged and not user-triggered`` () =
        // Arrange
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        // Assert
        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.Empty)
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``CheckStatus refreshes UI and topology when state is unchanged but user-triggered`` () =
        // Arrange
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        // Act
        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus true)

        // Assert
        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))
        Assert.That(harness.Toasts.Count, Is.EqualTo(1))

    [<Test>]
    let ``CheckStatus triggers cursor activity when KeepScreenAwake is enabled and cursor has not moved for 2 minutes``
        ()
        =
        // Arrange
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(10.0)
        harness.CursorPos.Value <- (400, 300)

        let initialActive = { TrayStateLogic.initialState with LastDockState = Some DockState.Active }

        let stateWithKeepAwake =
            (TrayStateLogic.step harness.Dependencies initialActive (SetKeepScreenAwake true)).Value

        // Act (1 minute elapsed, cursor hasn't moved)
        harness.Timestamp.Value <- TimeSpan.FromSeconds(70.0)

        let afterOneMin =
            (TrayStateLogic.step harness.Dependencies stateWithKeepAwake (CheckStatus false)).Value

        // Assert (no activity yet)
        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))

        // Act (2 minutes elapsed)
        harness.Timestamp.Value <- TimeSpan.FromSeconds(130.0)

        let afterTwoMin =
            (TrayStateLogic.step harness.Dependencies afterOneMin (CheckStatus false)).Value

        // Assert (triggers cursor activity)
        Assert.That(harness.ActivityCount.Value, Is.EqualTo(1))
        Assert.That(afterTwoMin.CursorIdleTracker.LastMoveTimestamp, Is.EqualTo(TimeSpan.FromSeconds(130.0)))
        Assert.That(afterTwoMin.CursorIdleTracker.LastCursorPos, Is.EqualTo(Some(400, 300)))

    [<Test>]
    let ``CheckStatus resets idle timer when cursor moves before 2 minutes`` () =
        // Arrange
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(10.0)
        harness.CursorPos.Value <- (400, 300)

        let initialActive = { TrayStateLogic.initialState with LastDockState = Some DockState.Active }

        let stateWithKeepAwake =
            (TrayStateLogic.step harness.Dependencies initialActive (SetKeepScreenAwake true)).Value

        // Act (100s elapsed, user moves mouse)
        harness.Timestamp.Value <- TimeSpan.FromSeconds(110.0)
        harness.CursorPos.Value <- (450, 320)

        let afterMove =
            (TrayStateLogic.step harness.Dependencies stateWithKeepAwake (CheckStatus false)).Value

        // Assert (timer reset, no activity)
        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))
        Assert.That(afterMove.CursorIdleTracker.LastMoveTimestamp, Is.EqualTo(TimeSpan.FromSeconds(110.0)))

        // Act (another 60s elapsed: 160s total since start, 60s since move)
        harness.Timestamp.Value <- TimeSpan.FromSeconds(170.0)

        let _ =
            (TrayStateLogic.step harness.Dependencies afterMove (CheckStatus false)).Value

        // Assert (still no activity)
        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))
