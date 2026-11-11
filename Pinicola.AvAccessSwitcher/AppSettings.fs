namespace Pinicola.AvAccessSwitcher

open System
open System.Collections.Generic
open System.IO
open Serilog
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

[<RequireQualifiedAccess>]
type KeepScreenAwakeMode =
    | Off
    | WhenDocked
    | Always

type UserSettings = {
    AutoExtendOnReconnect: bool
    KeepScreenAwake: KeepScreenAwakeMode
}

[<RequireQualifiedAccess>]
module AppSettings =

    [<Literal>]
    let private SettingsFileName = "settings.yaml"

    let defaultSettings: UserSettings = {
        AutoExtendOnReconnect = true
        KeepScreenAwake = KeepScreenAwakeMode.Off
    }

    let private formatKeepScreenAwakeMode (mode: KeepScreenAwakeMode) : string =
        match mode with
        | KeepScreenAwakeMode.Off -> "off"
        | KeepScreenAwakeMode.WhenDocked -> "whenDocked"
        | KeepScreenAwakeMode.Always -> "always"

    let private parseKeepScreenAwakeMode (rawValue: obj) : KeepScreenAwakeMode option =
        match rawValue with
        | :? bool as b ->
            Some(
                if b then
                    KeepScreenAwakeMode.Always
                else
                    KeepScreenAwakeMode.Off
            )
        | :? string as s ->
            let trimmed = s.Trim()

            if
                String.Equals(trimmed, "off", StringComparison.OrdinalIgnoreCase)
                || String.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)
            then
                Some KeepScreenAwakeMode.Off
            elif String.Equals(trimmed, "whenDocked", StringComparison.OrdinalIgnoreCase) then
                Some KeepScreenAwakeMode.WhenDocked
            elif
                String.Equals(trimmed, "always", StringComparison.OrdinalIgnoreCase)
                || String.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)
            then
                Some KeepScreenAwakeMode.Always
            else
                None
        | _ -> None

    let private defaultFilePath () : string =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Pinicola.AvAccessSwitcher",
            SettingsFileName
        )

    let private serializer =
        SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build()

    let private deserializer = DeserializerBuilder().Build()

    let private tryGetBool (dict: IDictionary<string, obj>) (key: string) : bool option =
        dict
        |> Seq.tryFind (fun kvp -> String.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
        |> Option.bind (fun kvp ->
            match kvp.Value with
            | :? bool as b -> Some b
            | :? string as s ->
                match Boolean.TryParse(s) with
                | true, b -> Some b
                | false, _ -> None
            | _ -> None
        )

    let private tryGetKeepScreenAwakeMode (dict: IDictionary<string, obj>) (key: string) : KeepScreenAwakeMode option =
        dict
        |> Seq.tryFind (fun kvp -> String.Equals(kvp.Key, key, StringComparison.OrdinalIgnoreCase))
        |> Option.bind (fun kvp -> parseKeepScreenAwakeMode kvp.Value)

    /// Serializes user settings to a YAML string.
    let internal serialize (settings: UserSettings) : string =
        let payload = Dictionary<string, obj>()
        payload.["autoExtendOnReconnect"] <- box settings.AutoExtendOnReconnect
        payload.["keepScreenAwake"] <- box (formatKeepScreenAwakeMode settings.KeepScreenAwake)
        serializer.Serialize(payload)

    /// Deserializes user settings from a YAML string, falling back to defaults for missing or invalid fields.
    let internal deserialize (yaml: string) : UserSettings =
        if String.IsNullOrWhiteSpace(yaml) then
            defaultSettings
        else
            try
                let rawDict = deserializer.Deserialize<IDictionary<string, obj>>(yaml)

                if isNull (box rawDict) then
                    defaultSettings
                else
                    let autoExtend =
                        tryGetBool rawDict "autoExtendOnReconnect"
                        |> Option.defaultValue defaultSettings.AutoExtendOnReconnect

                    let keepAwake =
                        tryGetKeepScreenAwakeMode rawDict "keepScreenAwake"
                        |> Option.defaultValue defaultSettings.KeepScreenAwake

                    {
                        AutoExtendOnReconnect = autoExtend
                        KeepScreenAwake = keepAwake
                    }
            with ex ->
                Log.Warning(ex, "Failed to parse settings YAML. Falling back to default settings.")
                defaultSettings

    /// Loads user settings from the specified YAML file path, or returns defaultSettings if the file does not exist or cannot be read.
    let internal loadFrom (filePath: string) : UserSettings =
        try
            if File.Exists(filePath) then
                let yaml = File.ReadAllText(filePath)
                let settings = deserialize yaml

                Log.Information(
                    "Loaded settings from {SettingsPath}: AutoExtendOnReconnect = {AutoExtend}, KeepScreenAwake = {KeepAwake}.",
                    filePath,
                    settings.AutoExtendOnReconnect,
                    settings.KeepScreenAwake
                )

                settings
            else
                Log.Information("No settings file found at {SettingsPath}. Using defaults.", filePath)
                defaultSettings
        with ex ->
            Log.Warning(ex, "Failed to load settings from {SettingsPath}. Using defaults.", filePath)
            defaultSettings

    /// Saves user settings to the specified YAML file path, creating the parent directory if needed.
    let internal saveTo (filePath: string) (settings: UserSettings) : unit =
        try
            let dir = Path.GetDirectoryName(filePath)

            if not (String.IsNullOrEmpty(dir)) then
                Directory.CreateDirectory(dir) |> ignore

            let yaml = serialize settings
            File.WriteAllText(filePath, yaml)

            Log.Information(
                "Saved settings to {SettingsPath}: AutoExtendOnReconnect = {AutoExtend}, KeepScreenAwake = {KeepAwake}.",
                filePath,
                settings.AutoExtendOnReconnect,
                settings.KeepScreenAwake
            )
        with ex ->
            Log.Error(ex, "Failed to save settings to {SettingsPath}.", filePath)

    /// Loads user settings from the default %LOCALAPPDATA%\Pinicola.AvAccessSwitcher\settings.yaml path.
    let load () : UserSettings = loadFrom (defaultFilePath ())

    /// Saves user settings to the default %LOCALAPPDATA%\Pinicola.AvAccessSwitcher\settings.yaml path.
    let save (settings: UserSettings) : unit = saveTo (defaultFilePath ()) settings
