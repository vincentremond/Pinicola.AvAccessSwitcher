namespace Pinicola.AvAccessSwitcher.Tests

open System
open System.IO
open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module AppSettingsTests =

    [<Test>]
    let ``serialize and deserialize round-trip UserSettings in camelCase YAML`` () =
        let settings = {
            AutoExtendOnReconnect = false
            KeepScreenAwake = true
        }

        let yaml = AppSettings.serialize settings

        Assert.That(yaml, Does.Contain("autoExtendOnReconnect: false"))
        Assert.That(yaml, Does.Contain("keepScreenAwake: true"))

        let roundTripped = AppSettings.deserialize yaml
        Assert.That(roundTripped, Is.EqualTo(settings))

    [<Test>]
    let ``deserialize returns defaultSettings when YAML is empty or invalid`` () =
        Assert.That(AppSettings.deserialize "", Is.EqualTo(AppSettings.defaultSettings))
        Assert.That(AppSettings.deserialize "   \n  ", Is.EqualTo(AppSettings.defaultSettings))
        Assert.That(AppSettings.deserialize "::invalid::yaml::[", Is.EqualTo(AppSettings.defaultSettings))

    [<Test>]
    let ``deserialize falls back to default values for missing keys`` () =
        let onlyKeepAwake = "keepScreenAwake: true\n"
        let parsed1 = AppSettings.deserialize onlyKeepAwake

        Assert.That(
            parsed1,
            Is.EqualTo(
                {
                    AutoExtendOnReconnect = true
                    KeepScreenAwake = true
                }
            )
        )

        let onlyAutoExtend = "autoExtendOnReconnect: false\n"
        let parsed2 = AppSettings.deserialize onlyAutoExtend

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
        let tempDir =
            Path.Combine(Path.GetTempPath(), $"Pinicola.AvAccessSwitcher.Tests.{Guid.NewGuid():N}")

        let filePath = Path.Combine(tempDir, "settings.yaml")

        try
            Assert.That(AppSettings.loadFrom filePath, Is.EqualTo(AppSettings.defaultSettings))

            let customSettings = {
                AutoExtendOnReconnect = false
                KeepScreenAwake = true
            }

            AppSettings.saveTo filePath customSettings

            Assert.That(File.Exists(filePath), Is.True)
            Assert.That(AppSettings.loadFrom filePath, Is.EqualTo(customSettings))
        finally
            if Directory.Exists(tempDir) then
                Directory.Delete(tempDir, true)

