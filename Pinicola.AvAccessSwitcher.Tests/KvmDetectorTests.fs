namespace Pinicola.AvAccessSwitcher.Tests

open System
open NUnit.Framework
open Pinicola.AvAccessSwitcher.KvmDetector

[<TestFixture>]
module KvmDetectorTests =

    [<TestCase(@"USB\VID_1D6B&PID_B022\5&123456&0&1", "Generic USB Hub", true)>]
    [<TestCase(@"usb\vid_1d6b&pid_b022\instance", "Other Device", true)>]
    [<TestCase(@"USB\VID_046D&PID_C52B\1234", "AV Access iDock C20", true)>]
    [<TestCase(@"USB\VID_046D&PID_C52B\1234", "av access usb controller", true)>]
    [<TestCase(@"USB\VID_046D&PID_C52B\1234", "Logitech USB Receiver", false)>]
    [<TestCase("", "", false)>]
    [<TestCase(null, null, false)>]
    let ``isMatchingDevice identifies AV Access KVM devices correctly``
        (deviceId: string, name: string, expected: bool)
        =
        // Arrange
        let device = {
            DeviceId = deviceId
            Name = name
        }

        // Act
        let actual = isMatchingDevice device

        // Assert
        Assert.That(actual, Is.EqualTo(expected))

    [<Test>]
    let ``isKvmConnectedWith returns true when at least one device matches`` () =
        // Arrange
        let devices () =
            seq {
                {
                    DeviceId = @"USB\VID_046D&PID_C52B\1"
                    Name = "Mouse"
                }

                {
                    DeviceId = @"USB\VID_1D6B&PID_B022\2"
                    Name = "USB Root Hub"
                }
            }

        // Act
        let actual = isKvmConnectedWith devices

        // Assert
        Assert.That(actual, Is.True)

    [<Test>]
    let ``isKvmConnectedWith returns false when no devices match`` () =
        // Arrange
        let devices () =
            seq {
                {
                    DeviceId = @"USB\VID_046D&PID_C52B\1"
                    Name = "Mouse"
                }

                {
                    DeviceId = @"PCI\VEN_8086\2"
                    Name = "Intel Host Bridge"
                }
            }

        // Act
        let actual = isKvmConnectedWith devices

        // Assert
        Assert.That(actual, Is.False)

    [<Test>]
    let ``isKvmConnectedWith returns false when device list is empty`` () =
        // Arrange
        let devices () = Seq.empty

        // Act
        let actual = isKvmConnectedWith devices

        // Assert
        Assert.That(actual, Is.False)

    [<Test>]
    let ``isKvmConnectedWith returns false when device query throws`` () =
        // Arrange
        let devices () : PnpDeviceInfo seq =
            raise (InvalidOperationException("WMI unavailable"))

        // Act
        let actual = isKvmConnectedWith devices

        // Assert
        Assert.That(actual, Is.False)
