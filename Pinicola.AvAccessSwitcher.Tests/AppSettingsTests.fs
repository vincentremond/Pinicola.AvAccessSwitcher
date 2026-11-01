namespace Pinicola.AvAccessSwitcher.Tests

open System
open System.IO
open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module AppSettingsTests =

    [<Test>]
    let ``serialize and deserialize round-trip UserSettings in camelCase YAML`` () =
        // Arrange
        let settings = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = true
        }

        // Act
        let yaml = AppSettings.serialize settings
        let roundTripped = AppSettings.deserialize yaml

        // Assert
        Assert.That(yaml, Does.Contain("autoExtendOnReconnect: false"))
        Assert.That(yaml, Does.Contain("keepScreenAwake: true"))
        Assert.That(roundTripped, Is.EqualTo(settings))

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
        let onlyKeepAwake = "keepScreenAwake: true\n"
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
                    KeepScreenAwake = true
                }
            )
        )

        Assert.That(
            parsed2,
            Is.EqualTo(
                {
                    AutoExtendOnReconnect = false
                    KeepScreenAwake = false
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
            KeepScreenAwake = true
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
