namespace Pinicola.AvAccessSwitcher.Tests

open NUnit.Framework
open Pinicola.AvAccessSwitcher

[<TestFixture>]
module AppVersionTests =

    [<Test>]
    let ``parseInformationalVersion extracts SemVer and 7-char ShortSha from GitVersion metadata`` () =
        // Arrange
        let raw = "0.1.0-17+Branch.main.Sha.1eedc72f8e4f13662bf75867eab128a264487b22"

        // Act
        let actual = AppVersion.parseInformationalVersion raw

        // Assert
        Assert.That(actual.SemVer, Is.EqualTo("0.1.0-17"))
        Assert.That(actual.ShortSha, Is.EqualTo(Some "1eedc72"))
        Assert.That(actual.InformationalVersion, Is.EqualTo(raw))

    [<Test>]
    let ``parseInformationalVersion extracts ShortSha from plain hex build metadata`` () =
        // Arrange
        let raw = "1.2.3+abcdef1234567890"

        // Act
        let actual = AppVersion.parseInformationalVersion raw

        // Assert
        Assert.That(actual.SemVer, Is.EqualTo("1.2.3"))
        Assert.That(actual.ShortSha, Is.EqualTo(Some "abcdef1"))

    [<Test>]
    let ``parseInformationalVersion handles plain SemVer without build metadata`` () =
        // Arrange
        let raw = "1.0.0"

        // Act
        let actual = AppVersion.parseInformationalVersion raw

        // Assert
        Assert.That(actual.SemVer, Is.EqualTo("1.0.0"))
        Assert.That(actual.ShortSha, Is.EqualTo(None))
        Assert.That(actual.InformationalVersion, Is.EqualTo("1.0.0"))

    [<TestCase("")>]
    [<TestCase("   ")>]
    let ``parseInformationalVersion returns fallback 0.0.0 for empty input`` (raw: string) =
        // Act
        let actual = AppVersion.parseInformationalVersion raw

        // Assert
        Assert.That(actual.SemVer, Is.EqualTo("0.0.0"))
        Assert.That(actual.ShortSha, Is.EqualTo(None))
        Assert.That(actual.InformationalVersion, Is.EqualTo("0.0.0"))

    [<Test>]
    let ``formatDisplayVersion includes ShortSha when present`` () =
        // Arrange
        let info = {
            SemVer = "0.1.0-17"
            ShortSha = Some "1eedc72"
            InformationalVersion = "0.1.0-17+Branch.main.Sha.1eedc72"
        }

        // Act
        let display = AppVersion.formatDisplayVersion info

        // Assert
        Assert.That(display, Is.EqualTo("v0.1.0-17 (1eedc72)"))

    [<Test>]
    let ``formatDisplayVersion omits parentheses when ShortSha is None`` () =
        // Arrange
        let info = {
            SemVer = "1.0.0"
            ShortSha = None
            InformationalVersion = "1.0.0"
        }

        // Act
        let display = AppVersion.formatDisplayVersion info

        // Assert
        Assert.That(display, Is.EqualTo("v1.0.0"))

    [<Test>]
    let ``fromAssembly reads GitVersion InformationalVersion from main application assembly`` () =
        // Arrange
        let assembly = typeof<VersionInfo>.Assembly

        // Act
        let info = AppVersion.fromAssembly assembly

        // Assert
        Assert.That(info.SemVer, Is.Not.Empty)
        Assert.That(info.InformationalVersion, Does.Contain("Branch."))
        Assert.That(info.ShortSha.IsSome, Is.True)
