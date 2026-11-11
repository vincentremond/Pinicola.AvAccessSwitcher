namespace Pinicola.AvAccessSwitcher.Tests

open System
open System.IO
open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module AppSettingsTests =

    [<Test>]
    let ``serialize and deserialize round-trip all KeepScreenAwakeMode values in camelCase YAML`` () =
        // Arrange
        let settingsOff = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = KeepScreenAwakeMode.Off
        }

        let settingsWhenDocked = {
            AutoExtendOnReconnect = true
            KeepScreenAwake = KeepScreenAwakeMode.WhenDocked
        }

        let settingsAlways = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = KeepScreenAwakeMode.Always
        }

        // Act
        let yamlOff = AppSettings.serialize settingsOff
        let yamlWhenDocked = AppSettings.serialize settingsWhenDocked
        let yamlAlways = AppSettings.serialize settingsAlways

        let roundTrippedOff = AppSettings.deserialize yamlOff
        let roundTrippedWhenDocked = AppSettings.deserialize yamlWhenDocked
        let roundTrippedAlways = AppSettings.deserialize yamlAlways

        // Assert
        Assert.That(yamlOff, Does.Contain("autoExtendOnReconnect: false"))
        Assert.That(yamlWhenDocked, Does.Contain("keepScreenAwake: whenDocked"))
        Assert.That(yamlAlways, Does.Contain("keepScreenAwake: always"))
        Assert.That(roundTrippedOff, Is.EqualTo(settingsOff))
        Assert.That(roundTrippedWhenDocked, Is.EqualTo(settingsWhenDocked))
        Assert.That(roundTrippedAlways, Is.EqualTo(settingsAlways))

    [<TestCase("keepScreenAwake: true\n", true)>]
    [<TestCase("keepScreenAwake: false\n", false)>]
    let ``deserialize migrates legacy boolean keepScreenAwake values`` (yaml: string, expectedAlways: bool) =
        // Arrange
        let expectedMode =
            if expectedAlways then
                KeepScreenAwakeMode.Always
            else
                KeepScreenAwakeMode.Off

        // Act
        let actual = AppSettings.deserialize yaml

        // Assert
        Assert.That(actual.KeepScreenAwake, Is.EqualTo(expectedMode))

    [<TestCase("keepScreenAwake: off\n", "Off")>]
    [<TestCase("keepScreenAwake: 'off'\n", "Off")>]
    [<TestCase("keepScreenAwake: whenDocked\n", "WhenDocked")>]
    [<TestCase("keepScreenAwake: WHENDOCKED\n", "WhenDocked")>]
    [<TestCase("keepScreenAwake: always\n", "Always")>]
    [<TestCase("keepScreenAwake: invalidValue\n", "Off")>]
    let ``deserialize parses KeepScreenAwakeMode string variants and falls back to Off for invalid values``
        (yaml: string, expectedCase: string)
        =
        // Arrange
        let expectedMode =
            match expectedCase with
            | "WhenDocked" -> KeepScreenAwakeMode.WhenDocked
            | "Always" -> KeepScreenAwakeMode.Always
            | _ -> KeepScreenAwakeMode.Off

        // Act
        let actual = AppSettings.deserialize yaml

        // Assert
        Assert.That(actual.KeepScreenAwake, Is.EqualTo(expectedMode))

    [<TestCase("")>]
    [<TestCase("   \n  ")>]
    [<TestCase("::invalid::yaml::[")>]
    let ``deserialize returns defaultSettings when YAML is empty or invalid`` (yaml: string) =
        // Act
        let actual = AppSettings.deserialize yaml

        // Assert
        Assert.That(actual, Is.EqualTo(AppSettings.defaultSettings))

    [<Test>]
    let ``deserialize falls back to default values for missing keys`` () =
        // Arrange
        let onlyKeepAwake = "keepScreenAwake: whenDocked\n"
        let onlyAutoExtend = "autoExtendOnReconnect: false\n"

        // Act
        let parsed1 = AppSettings.deserialize onlyKeepAwake
        let parsed2 = AppSettings.deserialize onlyAutoExtend

        // Assert
        Assert.That(
            parsed1,
            Is.EqualTo(
                {
                    AutoExtendOnReconnect = true
                    KeepScreenAwake = KeepScreenAwakeMode.WhenDocked
                }
            )
        )

        Assert.That(
            parsed2,
            Is.EqualTo(
                {
                    AutoExtendOnReconnect = false
                    KeepScreenAwake = KeepScreenAwakeMode.Off
                }
            )
        )

    [<Test>]
    let ``saveTo and loadFrom persist and reload settings YAML file`` () =
        // Arrange
        let tempDir =
            Path.Combine(Path.GetTempPath(), $"Pinicola.AvAccessSwitcher.Tests.{Guid.NewGuid():N}")

        let filePath = Path.Combine(tempDir, "settings.yaml")

        let customSettings = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = KeepScreenAwakeMode.WhenDocked
        }

        try
            // Act
            let beforeSave = AppSettings.loadFrom filePath
            AppSettings.saveTo filePath customSettings
            let afterSave = AppSettings.loadFrom filePath

            // Assert
            Assert.That(beforeSave, Is.EqualTo(AppSettings.defaultSettings))
            Assert.That(File.Exists(filePath), Is.True)
            Assert.That(afterSave, Is.EqualTo(customSettings))
        finally
            if Directory.Exists(tempDir) then
                Directory.Delete(tempDir, true)
