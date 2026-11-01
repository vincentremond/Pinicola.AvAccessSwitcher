namespace Pinicola.AvAccessSwitcher

open System
open System.Reflection

type VersionInfo = {
    SemVer: string
    ShortSha: string option
    InformationalVersion: string
}

[<RequireQualifiedAccess>]
module AppVersion =

    /// Parses an AssemblyInformationalVersion string (e.g. "0.1.0-17+Branch.main.Sha.1eedc72f8e...") into structured VersionInfo.
    let internal parseInformationalVersion (informationalVersion: string) : VersionInfo =
        if String.IsNullOrWhiteSpace(informationalVersion) then
            {
                SemVer = "0.0.0"
                ShortSha = None
                InformationalVersion = "0.0.0"
            }
        else
            let trimmed = informationalVersion.Trim()
            let parts = trimmed.Split('+', 2)

            let semVer =
                if String.IsNullOrWhiteSpace(parts.[0]) then
                    "0.0.0"
                else
                    parts.[0]

            let shortSha =
                if parts.Length < 2 then
                    None
                else
                    let metadata = parts.[1]
                    let shaPrefix = "Sha."
                    let shaIdx = metadata.IndexOf(shaPrefix, StringComparison.OrdinalIgnoreCase)

                    if shaIdx >= 0 then
                        let afterSha = metadata.Substring(shaIdx + shaPrefix.Length)
                        let dotIdx = afterSha.IndexOf('.')

                        let rawSha =
                            if dotIdx >= 0 then
                                afterSha.Substring(0, dotIdx)
                            else
                                afterSha

                        if String.IsNullOrWhiteSpace(rawSha) then None
                        elif rawSha.Length > 7 then Some(rawSha.Substring(0, 7))
                        else Some rawSha
                    elif metadata.Length >= 7 && metadata |> Seq.take 7 |> Seq.forall Uri.IsHexDigit then
                        Some(metadata.Substring(0, 7))
                    else
                        None

            {
                SemVer = semVer
                ShortSha = shortSha
                InformationalVersion = trimmed
            }

    /// Formats a concise display version for UI controls (e.g. "v0.1.0-17 (1eedc72)").
    let formatDisplayVersion (info: VersionInfo) : string =
        match info.ShortSha with
        | Some sha -> $"v{info.SemVer} ({sha})"
        | None -> $"v{info.SemVer}"

    /// Reads the InformationalVersion from the specified assembly.
    let internal fromAssembly (assembly: Assembly) : VersionInfo =
        let attr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()

        let rawVersion =
            if
                not (isNull (box attr))
                && not (String.IsNullOrWhiteSpace(attr.InformationalVersion))
            then
                attr.InformationalVersion
            else
                match assembly.GetName().Version with
                | null -> "0.0.0"
                | v -> v.ToString()

        parseInformationalVersion rawVersion

    /// Returns the VersionInfo of the currently executing Pinicola.AvAccessSwitcher assembly.
    let current: VersionInfo = fromAssembly (Assembly.GetExecutingAssembly())
