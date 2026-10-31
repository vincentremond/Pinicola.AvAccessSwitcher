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
        Assert.That(TrayStateLogic.resolveDockState false false, Is.EqualTo(DockState.Unplugged))
        Assert.That(TrayStateLogic.resolveDockState true false, Is.EqualTo(DockState.Unplugged))
        Assert.That(TrayStateLogic.resolveDockState false true, Is.EqualTo(DockState.SwitchedAway))
        Assert.That(TrayStateLogic.resolveDockState true true, Is.EqualTo(DockState.Active))

    [<Test>]
    let ``createInitialState populates AutoExtendOnReconnect and KeepScreenAwake from UserSettings`` () =
        let settings = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = true
        }

        let state = TrayStateLogic.createInitialState settings

        Assert.That(state.AutoExtendOnReconnect, Is.False)
        Assert.That(state.KeepScreenAwake, Is.True)
        Assert.That(state.LastDockState, Is.EqualTo(None))

    [<Test>]
    let ``StopAgent returns None`` () =
        let harness = createHarness true true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState StopAgent

        Assert.That(result, Is.EqualTo(None))

    [<Test>]
    let ``SetAutoExtend updates AutoExtendOnReconnect in state and saves settings`` () =
        let harness = createHarness true true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (SetAutoExtend false)

        let expected =
            Some { TrayStateLogic.initialState with AutoExtendOnReconnect = false }

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
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(5.0)
        harness.CursorPos.Value <- (250, 350)

        let enabledResult =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (SetKeepScreenAwake true)

        let expectedEnabled =
            Some {
                TrayStateLogic.initialState with
                    KeepScreenAwake = true
                    CursorIdleTracker = {
                        CursorActivity.LastCursorPos = Some(250, 350)
                        CursorActivity.LastMoveTimestamp = TimeSpan.FromSeconds(5.0)
                    }
            }

        Assert.That(enabledResult, Is.EqualTo(expectedEnabled))

        let disabledResult =
            TrayStateLogic.step harness.Dependencies enabledResult.Value (SetKeepScreenAwake false)

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
        let harness = createHarness true true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceLaptop

        Assert.That(result, Is.EqualTo(Some TrayStateLogic.initialState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("Display Switcher", "Manually set display to Show only on 1.") |])
        )

    [<Test>]
    let ``ForceLaptop does not show toast when SetTopology fails`` () =
        let harness = createHarness true true false

        let _ =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceLaptop

        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``ForceExtend sets Extend topology and shows toast on success`` () =
        let harness = createHarness true true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceExtend

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
        let harness = createHarness isKvmConnected isExternalDisplayConnected true

        let expectedDockState =
            TrayStateLogic.resolveDockState isKvmConnected isExternalDisplayConnected

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (CheckStatus false)

        let expectedState =
            Some {
                TrayStateLogic.initialState with
                    LastDockState = Some expectedDockState
                    AutoExtendOnReconnect = true
            }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| expectedDockState |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access Switcher Started", expectedMsg) |]))

    [<TestCase(true, "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen).")>]
    [<TestCase(false, "KVM switched to other PC. (Failed to set topology).")>]
    let ``CheckStatus transitions from Active to SwitchedAway and applies ShowOnlyInternal``
        (topologySuccess: bool, expectedMsg: string)
        =
        let harness = createHarness false true topologySuccess

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState =
            Some { priorState with LastDockState = Some DockState.SwitchedAway }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.SwitchedAway |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Switched Away", expectedMsg) |]))

    [<TestCase(true, "KVM reconnected. Display restored to Extend displays.")>]
    [<TestCase(false, "KVM reconnected. (Failed to set topology).")>]
    let ``CheckStatus transitions from SwitchedAway to Active with AutoExtend enabled``
        (topologySuccess: bool, expectedMsg: string)
        =
        let harness = createHarness true true topologySuccess

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.SwitchedAway
                AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState = Some { priorState with LastDockState = Some DockState.Active }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Reconnected", expectedMsg) |]))

    [<Test>]
    let ``CheckStatus transitions from SwitchedAway to Active with AutoExtend disabled`` () =
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.SwitchedAway
                AutoExtendOnReconnect = false
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState = Some { priorState with LastDockState = Some DockState.Active }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("AV Access KVM Reconnected", "KVM is back online on this PC.") |])
        )

    [<Test>]
    let ``CheckStatus transitions from Active to Unplugged without calling SetTopology`` () =
        let harness = createHarness false false true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState =
            Some { priorState with LastDockState = Some DockState.Unplugged }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Unplugged |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("AV Access Dock Unplugged", "USB-C cable unplugged. Display is on internal screen.") |])
        )

    [<Test>]
    let ``CheckStatus waits for external display on replug when KVM USB enumerates first`` () =
        // Step 1: USB enumerates first (KVM = true, ExternalDisplay = false) -> stays Unplugged
        let usbOnlyHarness = createHarness true false true

        let unpluggedState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Unplugged
                AutoExtendOnReconnect = true
        }

        let afterUsbStep =
            TrayStateLogic.step usbOnlyHarness.Dependencies unpluggedState (CheckStatus false)

        Assert.That(afterUsbStep, Is.EqualTo(Some unpluggedState))
        Assert.That(usbOnlyHarness.AppliedTopologies, Is.Empty)
        Assert.That(usbOnlyHarness.UiStates, Is.Empty)
        Assert.That(usbOnlyHarness.Toasts, Is.Empty)

        // Step 2: DisplayPort Alt-Mode finishes handshake (KVM = true, ExternalDisplay = true) -> transitions to Active and applies Extend
        let fullHarness = createHarness true true true

        let afterDisplayStep =
            TrayStateLogic.step fullHarness.Dependencies afterUsbStep.Value (CheckStatus false)

        let expectedActiveState =
            Some { unpluggedState with LastDockState = Some DockState.Active }

        Assert.That(afterDisplayStep, Is.EqualTo(expectedActiveState))
        Assert.That(fullHarness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(fullHarness.UiStates, Is.EqualTo(box [| DockState.Active |]))

        Assert.That(
            fullHarness.Toasts,
            Is.EqualTo(box [| ("AV Access KVM Reconnected", "KVM reconnected. Display restored to Extend displays.") |])
        )

    [<Test>]
    let ``CheckStatus transitions from Unplugged to SwitchedAway when replugged while KVM is on other PC`` () =
        let harness = createHarness false true true

        let unpluggedState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Unplugged
                AutoExtendOnReconnect = true
        }

        let result =
            TrayStateLogic.step harness.Dependencies unpluggedState (CheckStatus false)

        let expectedState =
            Some { unpluggedState with LastDockState = Some DockState.SwitchedAway }

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
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.Empty)
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``CheckStatus refreshes UI and topology when state is unchanged but user-triggered`` () =
        let harness = createHarness true true true

        let priorState = {
            TrayStateLogic.initialState with
                LastDockState = Some DockState.Active
                AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus true)

        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| DockState.Active |]))
        Assert.That(harness.Toasts.Count, Is.EqualTo(1))

    [<Test>]
    let ``CheckStatus triggers cursor activity when KeepScreenAwake is enabled and cursor has not moved for 2 minutes``
        ()
        =
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(10.0)
        harness.CursorPos.Value <- (400, 300)

        let initialActive = { TrayStateLogic.initialState with LastDockState = Some DockState.Active }

        let stateWithKeepAwake =
            (TrayStateLogic.step harness.Dependencies initialActive (SetKeepScreenAwake true)).Value

        // 1 minute elapsed, cursor hasn't moved -> no activity
        harness.Timestamp.Value <- TimeSpan.FromSeconds(70.0)

        let afterOneMin =
            (TrayStateLogic.step harness.Dependencies stateWithKeepAwake (CheckStatus false)).Value

        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))

        // 2 minutes elapsed -> triggers cursor activity
        harness.Timestamp.Value <- TimeSpan.FromSeconds(130.0)

        let afterTwoMin =
            (TrayStateLogic.step harness.Dependencies afterOneMin (CheckStatus false)).Value

        Assert.That(harness.ActivityCount.Value, Is.EqualTo(1))
        Assert.That(afterTwoMin.CursorIdleTracker.LastMoveTimestamp, Is.EqualTo(TimeSpan.FromSeconds(130.0)))
        Assert.That(afterTwoMin.CursorIdleTracker.LastCursorPos, Is.EqualTo(Some(400, 300)))

    [<Test>]
    let ``CheckStatus resets idle timer when cursor moves before 2 minutes`` () =
        let harness = createHarness true true true
        harness.Timestamp.Value <- TimeSpan.FromSeconds(10.0)
        harness.CursorPos.Value <- (400, 300)

        let initialActive = { TrayStateLogic.initialState with LastDockState = Some DockState.Active }

        let stateWithKeepAwake =
            (TrayStateLogic.step harness.Dependencies initialActive (SetKeepScreenAwake true)).Value

        // 100s elapsed, user moves mouse
        harness.Timestamp.Value <- TimeSpan.FromSeconds(110.0)
        harness.CursorPos.Value <- (450, 320)

        let afterMove =
            (TrayStateLogic.step harness.Dependencies stateWithKeepAwake (CheckStatus false)).Value

        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))
        Assert.That(afterMove.CursorIdleTracker.LastMoveTimestamp, Is.EqualTo(TimeSpan.FromSeconds(110.0)))

        // Another 60s elapsed (total 160s since start, but only 60s since move) -> no activity
        harness.Timestamp.Value <- TimeSpan.FromSeconds(170.0)

        let _ =
            (TrayStateLogic.step harness.Dependencies afterMove (CheckStatus false)).Value

        Assert.That(harness.ActivityCount.Value, Is.EqualTo(0))
