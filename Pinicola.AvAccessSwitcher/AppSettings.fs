namespace Pinicola.AvAccessSwitcher

open System
open System.Collections.Generic
open System.IO
open Serilog
open YamlDotNet.Serialization
open YamlDotNet.Serialization.NamingConventions

type UserSettings = {
    AutoExtendOnReconnect: bool
    KeepScreenAwake: bool
}

module AppSettings =

    [<Literal>]
    let SettingsFileName = "settings.yaml"

    let defaultSettings: UserSettings = {
        AutoExtendOnReconnect = true
        KeepScreenAwake = false
    }

    let defaultFilePath () : string =
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

    /// Serializes user settings to a YAML string.
    let serialize (settings: UserSettings) : string = serializer.Serialize(settings)

    /// Deserializes user settings from a YAML string, falling back to defaults for missing or invalid fields.
    let deserialize (yaml: string) : UserSettings =
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
                        tryGetBool rawDict "keepScreenAwake"
                        |> Option.defaultValue defaultSettings.KeepScreenAwake

                    {
                        AutoExtendOnReconnect = autoExtend
                        KeepScreenAwake = keepAwake
                    }
            with ex ->
                Log.Warning(ex, "Failed to parse settings YAML. Falling back to default settings.")
                defaultSettings

    /// Loads user settings from the specified YAML file path, or returns defaultSettings if the file does not exist or cannot be read.
    let loadFrom (filePath: string) : UserSettings =
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
    let saveTo (filePath: string) (settings: UserSettings) : unit =
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
