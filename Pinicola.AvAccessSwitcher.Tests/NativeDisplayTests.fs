namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher
open Pinicola.AvAccessSwitcher.NativeDisplay
open Vanara.PInvoke

[<TestFixture>]
module NativeDisplayTests =

    [<TestCase(DisplayTopology.ShowOnlyInternal, 0x00000081u)>]
    [<TestCase(DisplayTopology.Clone, 0x00000082u)>]
    [<TestCase(DisplayTopology.Extend, 0x00000084u)>]
    [<TestCase(DisplayTopology.ShowOnlyExternal, 0x00000088u)>]
    let ``toDisplayConfigFlags combines topology flag with SDC_APPLY``
        (topology: DisplayTopology, expectedFlags: uint32)
        =
        // Act
        let actual = toDisplayConfigFlags topology

        // Assert
        Assert.That(actual, Is.EqualTo(expectedFlags))

    [<TestCase(DisplayTopology.ShowOnlyInternal, "/internal")>]
    [<TestCase(DisplayTopology.Clone, "/clone")>]
    [<TestCase(DisplayTopology.Extend, "/extend")>]
    [<TestCase(DisplayTopology.ShowOnlyExternal, "/external")>]
    let ``toDisplaySwitchArg returns expected CLI argument`` (topology: DisplayTopology, expectedArg: string) =
        // Act
        let actual = toDisplaySwitchArg topology

        // Assert
        Assert.That(actual, Is.EqualTo(expectedArg))

    [<Test>]
    let ``toDisplaySwitchArg defaults to /extend for unknown topology value`` () =
        // Arrange
        let unknownTopology = LanguagePrimitives.EnumOfValue<uint32, DisplayTopology> 999u

        // Act
        let actual = toDisplaySwitchArg unknownTopology

        // Assert
        Assert.That(actual, Is.EqualTo("/extend"))

    [<Test>]
    let ``setTopologyWith returns true and does not run fallback when SetDisplayConfig succeeds`` () =
        // Arrange
        let capturedFlags = ResizeArray<uint32>()
        let fallbackArgs = ResizeArray<string>()

        let invokeSetDisplayConfig flags =
            capturedFlags.Add(flags)
            0

        let startDisplaySwitch arg = fallbackArgs.Add(arg)

        // Act
        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.Extend

        // Assert
        Assert.That(result, Is.True)
        Assert.That(capturedFlags.Count, Is.EqualTo(1))
        Assert.That(capturedFlags.[0], Is.EqualTo(0x00000084u))
        Assert.That(fallbackArgs, Is.Empty)

    [<Test>]
    let ``setTopologyWith runs DisplaySwitch fallback when SetDisplayConfig returns non-zero`` () =
        // Arrange
        let fallbackArgs = ResizeArray<string>()
        let invokeSetDisplayConfig _ = 87 // ERROR_INVALID_PARAMETER
        let startDisplaySwitch arg = fallbackArgs.Add(arg)

        // Act
        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.ShowOnlyInternal

        // Assert
        Assert.That(result, Is.True)
        Assert.That(fallbackArgs.Count, Is.EqualTo(1))
        Assert.That(fallbackArgs.[0], Is.EqualTo("/internal"))

    [<Test>]
    let ``setTopologyWith returns false when an exception is thrown`` () =
        // Arrange
        let invokeSetDisplayConfig _ =
            raise (InvalidOperationException("Native failure"))

        let startDisplaySwitch _ = ()

        // Act
        let result =
            setTopologyWith invokeSetDisplayConfig startDisplaySwitch DisplayTopology.Extend

        // Assert
        Assert.That(result, Is.False)

    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL, true)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI, true)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI, true)>]
    let ``isExternalOutputTechnology distinguishes internal and external output technologies``
        (tech: Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY, expected: bool)
        =
        // Act
        let actual = isExternalOutputTechnology tech

        // Assert
        Assert.That(actual, Is.EqualTo(expected))

    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL,
               true,
               true)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL,
               false,
               false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL, true, false)>]
    [<TestCase(Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED,
               true,
               false)>]
    let ``isExternalDisplayTarget requires both TargetAvailable and external technology``
        (tech: Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY, available: bool, expected: bool)
        =
        // Arrange
        let target = {
            TargetId = 261u
            OutputTechnology = tech
            TargetAvailable = available
        }

        // Act
        let actual = isExternalDisplayTarget target

        // Assert
        Assert.That(actual, Is.EqualTo(expected))

    [<Test>]
    let ``isExternalDisplayConnectedWith returns true when an available external target is present`` () =
        // Arrange
        let targets () =
            seq {
                {
                    TargetId = 256u
                    OutputTechnology =
                        Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    TargetAvailable = true
                }

                {
                    TargetId = 261u
                    OutputTechnology =
                        Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL
                    TargetAvailable = true
                }
            }

        // Act
        let actual = isExternalDisplayConnectedWith targets

        // Assert
        Assert.That(actual, Is.True)

    [<Test>]
    let ``isExternalDisplayConnectedWith returns false when only internal target is available`` () =
        // Arrange
        let targets () =
            seq {
                {
                    TargetId = 256u
                    OutputTechnology =
                        Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
                    TargetAvailable = true
                }

                {
                    TargetId = 261u
                    OutputTechnology =
                        Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL
                    TargetAvailable = false
                }
            }

        // Act
        let actual = isExternalDisplayConnectedWith targets

        // Assert
        Assert.That(actual, Is.False)

    [<Test>]
    let ``isExternalDisplayConnectedWith returns false when query throws`` () =
        // Arrange
        let targets () : DisplayTargetInfo seq =
            raise (InvalidOperationException("CCD failure"))

        // Act
        let actual = isExternalDisplayConnectedWith targets

        // Assert
        Assert.That(actual, Is.False)
