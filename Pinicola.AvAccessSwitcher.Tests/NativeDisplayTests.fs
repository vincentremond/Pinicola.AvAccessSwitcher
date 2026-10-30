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

    [<TestCase(0x80000000u, false)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
    [<TestCase(11u, false)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
    [<TestCase(6u, false)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
    [<TestCase(13u, false)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
    [<TestCase(0xFFFFFFFFu, false)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER
    [<TestCase(10u, true)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL
    [<TestCase(5u, true)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI
    [<TestCase(4u, true)>] // DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI
    let ``isExternalOutputTechnology distinguishes internal and external output technologies``
        (tech: uint32, expected: bool)
        =
        Assert.That(isExternalOutputTechnology tech, Is.EqualTo(expected))

    [<TestCase(10u, true, true)>]
    [<TestCase(10u, false, false)>]
    [<TestCase(0x80000000u, true, false)>]
    [<TestCase(11u, true, false)>]
    let ``isExternalDisplayTarget requires both TargetAvailable and external technology``
        (tech: uint32, available: bool, expected: bool)
        =
        let target = {
            TargetId = 261u
            OutputTechnology = tech
            TargetAvailable = available
        }

        Assert.That(isExternalDisplayTarget target, Is.EqualTo(expected))

    [<Test>]
    let ``isExternalDisplayConnectedWith returns true when an available external target is present`` () =
        let targets () =
            seq {
                {
                    TargetId = 256u
                    OutputTechnology = DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    TargetAvailable = true
                }

                {
                    TargetId = 261u
                    OutputTechnology = 10u // DisplayPort External
                    TargetAvailable = true
                }
            }

        Assert.That(isExternalDisplayConnectedWith targets, Is.True)

    [<Test>]
    let ``isExternalDisplayConnectedWith returns false when only internal target is available`` () =
        let targets () =
            seq {
                {
                    TargetId = 256u
                    OutputTechnology = DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    TargetAvailable = true
                }

                {
                    TargetId = 261u
                    OutputTechnology = 10u // DisplayPort External
                    TargetAvailable = false
                }
            }

        Assert.That(isExternalDisplayConnectedWith targets, Is.False)

    [<Test>]
    let ``isExternalDisplayConnectedWith returns false when query throws`` () =
        let targets () : DisplayTargetInfo seq =
            raise (InvalidOperationException("CCD failure"))

        Assert.That(isExternalDisplayConnectedWith targets, Is.False)
