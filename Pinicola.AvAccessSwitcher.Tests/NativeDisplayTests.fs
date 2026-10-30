namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher
open Pinicola.AvAccessSwitcher.NativeDisplay

[<TestFixture>]
module NativeDisplayTests =

    [<TestCase(DisplayTopology.ShowOnlyInternal, 0x00000081u)>]
    [<TestCase(DisplayTopology.Clone, 0x00000082u)>]
    [<TestCase(DisplayTopology.Extend, 0x00000084u)>]
    [<TestCase(DisplayTopology.ShowOnlyExternal, 0x00000088u)>]
    let ``toDisplayConfigFlags combines topology flag with SDC_APPLY``
        (topology: DisplayTopology, expectedFlags: uint32)
        =
        let actual = toDisplayConfigFlags topology
        Assert.That(actual, Is.EqualTo(expectedFlags))

    [<TestCase(DisplayTopology.ShowOnlyInternal, "/internal")>]
    [<TestCase(DisplayTopology.Clone, "/clone")>]
    [<TestCase(DisplayTopology.Extend, "/extend")>]
    [<TestCase(DisplayTopology.ShowOnlyExternal, "/external")>]
    let ``toDisplaySwitchArg returns expected CLI argument`` (topology: DisplayTopology, expectedArg: string) =
        let actual = toDisplaySwitchArg topology
        Assert.That(actual, Is.EqualTo(expectedArg))

    [<Test>]
    let ``toDisplaySwitchArg defaults to /extend for unknown topology value`` () =
        let unknownTopology = LanguagePrimitives.EnumOfValue<uint32, DisplayTopology> 999u
        let actual = toDisplaySwitchArg unknownTopology
        Assert.That(actual, Is.EqualTo("/extend"))

    [<Test>]
    let ``setTopologyWith returns true and does not run fallback when SetDisplayConfig succeeds`` () =
        let capturedFlags = ResizeArray<uint32>()
        let fallbackArgs = ResizeArray<string>()

        let invokeSetDisplayConfig flags =
            capturedFlags.Add(flags)
            0

        let startDisplaySwitch arg = fallbackArgs.Add(arg)

        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.Extend

        Assert.That(result, Is.True)
        Assert.That(capturedFlags.Count, Is.EqualTo(1))
        Assert.That(capturedFlags.[0], Is.EqualTo(0x00000084u))
        Assert.That(fallbackArgs, Is.Empty)

    [<Test>]
    let ``setTopologyWith runs DisplaySwitch fallback when SetDisplayConfig returns non-zero`` () =
        let fallbackArgs = ResizeArray<string>()
        let invokeSetDisplayConfig _ = 87 // ERROR_INVALID_PARAMETER
        let startDisplaySwitch arg = fallbackArgs.Add(arg)

        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.ShowOnlyInternal

        Assert.That(result, Is.True)
        Assert.That(fallbackArgs.Count, Is.EqualTo(1))
        Assert.That(fallbackArgs.[0], Is.EqualTo("/internal"))

    [<Test>]
    let ``setTopologyWith returns false when an exception is thrown`` () =
        let invokeSetDisplayConfig _ =
            raise (InvalidOperationException("Native failure"))

        let startDisplaySwitch _ = ()

        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.Extend

        Assert.That(result, Is.False)
