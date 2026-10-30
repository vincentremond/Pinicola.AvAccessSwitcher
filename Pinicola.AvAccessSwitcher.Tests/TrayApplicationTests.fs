namespace Pinicola.AvAccessSwitcher.Tests

open NUnit.Framework
open Pinicola.AvAccessSwitcher
open Pinicola.AvAccessSwitcher.NativeDisplay

[<TestFixture>]
module TrayApplicationTests =

    type TestHarness = {
        Dependencies: AppDependencies
        AppliedTopologies: ResizeArray<DisplayTopology>
        UiStates: ResizeArray<bool>
        Toasts: ResizeArray<string * string>
    }

    let private createHarness (isKvmConnected: bool) (topologySuccess: bool) : TestHarness =
        let appliedTopologies = ResizeArray<DisplayTopology>()
        let uiStates = ResizeArray<bool>()
        let toasts = ResizeArray<string * string>()

        let deps = {
            IsKvmConnected = fun () -> isKvmConnected
            SetTopology =
                fun topology ->
                    appliedTopologies.Add(topology)
                    topologySuccess
            UpdateUi = fun connected -> uiStates.Add(connected)
            ShowToast = fun title msg -> toasts.Add((title, msg))
        }

        {
            Dependencies = deps
            AppliedTopologies = appliedTopologies
            UiStates = uiStates
            Toasts = toasts
        }

    [<Test>]
    let ``StopAgent returns None`` () =
        let harness = createHarness true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState StopAgent

        Assert.That(result, Is.EqualTo(None))

    [<Test>]
    let ``SetAutoExtend updates AutoExtendOnReconnect in state`` () =
        let harness = createHarness true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (SetAutoExtend false)

        let expected =
            Some {
                LastKvmState = None
                AutoExtendOnReconnect = false
            }

        Assert.That(result, Is.EqualTo(expected))

    [<Test>]
    let ``ForceLaptop sets ShowOnlyInternal topology and shows toast on success`` () =
        let harness = createHarness true true

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
        let harness = createHarness true false

        let _ =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceLaptop

        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``ForceExtend sets Extend topology and shows toast on success`` () =
        let harness = createHarness true true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState ForceExtend

        Assert.That(result, Is.EqualTo(Some TrayStateLogic.initialState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("Display Switcher", "Manually set display to Extend displays.") |])
        )

    [<TestCase(true, "Started monitoring KVM (Display is ACTIVE on this PC).")>]
    [<TestCase(false, "Started monitoring KVM (Display is SWITCHED AWAY).")>]
    let ``Initial CheckStatus updates UI and shows startup toast without changing topology``
        (isConnected: bool, expectedMsg: string)
        =
        let harness = createHarness isConnected true

        let result =
            TrayStateLogic.step harness.Dependencies TrayStateLogic.initialState (CheckStatus false)

        let expectedState =
            Some {
                LastKvmState = Some isConnected
                AutoExtendOnReconnect = true
            }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| isConnected |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access Switcher Started", expectedMsg) |]))

    [<TestCase(true, "KVM switched to other PC. Display set to Show only on 1 (Laptop Screen).")>]
    [<TestCase(false, "KVM switched to other PC. (Failed to set topology).")>]
    let ``CheckStatus transitions from Connected to Disconnected and applies ShowOnlyInternal``
        (topologySuccess: bool, expectedMsg: string)
        =
        let harness = createHarness false topologySuccess

        let priorState = {
            LastKvmState = Some true
            AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState =
            Some {
                LastKvmState = Some false
                AutoExtendOnReconnect = true
            }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.ShowOnlyInternal |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| false |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Switched Away", expectedMsg) |]))

    [<TestCase(true, "KVM reconnected. Display restored to Extend displays.")>]
    [<TestCase(false, "KVM reconnected. (Failed to set topology).")>]
    let ``CheckStatus transitions from Disconnected to Connected with AutoExtend enabled``
        (topologySuccess: bool, expectedMsg: string)
        =
        let harness = createHarness true topologySuccess

        let priorState = {
            LastKvmState = Some false
            AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState =
            Some {
                LastKvmState = Some true
                AutoExtendOnReconnect = true
            }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| true |]))
        Assert.That(harness.Toasts, Is.EqualTo(box [| ("AV Access KVM Reconnected", expectedMsg) |]))

    [<Test>]
    let ``CheckStatus transitions from Disconnected to Connected with AutoExtend disabled`` () =
        let harness = createHarness true true

        let priorState = {
            LastKvmState = Some false
            AutoExtendOnReconnect = false
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        let expectedState =
            Some {
                LastKvmState = Some true
                AutoExtendOnReconnect = false
            }

        Assert.That(result, Is.EqualTo(expectedState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.EqualTo(box [| true |]))

        Assert.That(
            harness.Toasts,
            Is.EqualTo(box [| ("AV Access KVM Reconnected", "KVM is back online on this PC.") |])
        )

    [<Test>]
    let ``CheckStatus does nothing when state is unchanged and not user-triggered`` () =
        let harness = createHarness true true

        let priorState = {
            LastKvmState = Some true
            AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus false)

        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.Empty)
        Assert.That(harness.UiStates, Is.Empty)
        Assert.That(harness.Toasts, Is.Empty)

    [<Test>]
    let ``CheckStatus refreshes UI and topology when state is unchanged but user-triggered`` () =
        let harness = createHarness true true

        let priorState = {
            LastKvmState = Some true
            AutoExtendOnReconnect = true
        }

        let result = TrayStateLogic.step harness.Dependencies priorState (CheckStatus true)

        Assert.That(result, Is.EqualTo(Some priorState))
        Assert.That(harness.AppliedTopologies, Is.EqualTo(box [| DisplayTopology.Extend |]))
        Assert.That(harness.UiStates, Is.EqualTo(box [| true |]))
        Assert.That(harness.Toasts.Count, Is.EqualTo(1))
