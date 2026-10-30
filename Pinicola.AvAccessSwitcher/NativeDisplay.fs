namespace Pinicola.AvAccessSwitcher

open System.Diagnostics
open Serilog
open Vanara.PInvoke

module NativeDisplay =

    // Windows CCD (Connecting and Configuring Displays) flags for SetDisplayConfig
    type DisplayTopology =
        | ShowOnlyInternal = 0x00000001u // SDC_TOPOLOGY_INTERNAL (Show only on 1)
        | Clone = 0x00000002u // SDC_TOPOLOGY_CLONE
        | Extend = 0x00000004u // SDC_TOPOLOGY_EXTEND (Extend displays)
        | ShowOnlyExternal = 0x00000008u // SDC_TOPOLOGY_EXTERNAL (Show only on 2)

    [<Literal>]
    let SDC_APPLY = 0x00000080u

    type DisplayTargetInfo = {
        TargetId: uint32
        OutputTechnology: Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY
        TargetAvailable: bool
    }

    /// Determines whether a display output technology represents an external monitor connection.
    let isExternalOutputTechnology (outputTechnology: Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY) : bool =
        outputTechnology
        <> Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
        && outputTechnology
           <> Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
        && outputTechnology
           <> Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
        && outputTechnology
           <> Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
        && outputTechnology
           <> Gdi32.DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY.DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER

    /// Determines whether a display target represents a physically connected external monitor.
    let isExternalDisplayTarget (target: DisplayTargetInfo) : bool =
        target.TargetAvailable && isExternalOutputTechnology target.OutputTechnology

    /// Checks if any display target returned by the query is a connected external display.
    let isExternalDisplayConnectedWith (queryTargets: unit -> DisplayTargetInfo seq) : bool =
        try
            queryTargets () |> Seq.exists isExternalDisplayTarget
        with ex ->
            Log.Error(ex, "Error querying display targets via CCD API.")
            false

    let private queryDisplayTargets () : DisplayTargetInfo seq =
        let err, paths, _, _ = User32.QueryDisplayConfig(User32.QDC.QDC_ALL_PATHS)

        if err.Failed || isNull paths then
            Log.Warning("QueryDisplayConfig failed with error {Error}.", err)
            Seq.empty
        else
            paths
            |> Seq.map (fun path ->
                let target = path.targetInfo

                {
                    TargetId = target.id
                    OutputTechnology = target.outputTechnology
                    TargetAvailable = target.targetAvailable
                }
            )

    /// Checks if any external display target (e.g., dock video output) is physically connected.
    let isExternalDisplayConnected () : bool =
        isExternalDisplayConnectedWith queryDisplayTargets

    /// Compute the flags passed to SetDisplayConfig for a given topology
    let toDisplayConfigFlags (topology: DisplayTopology) : uint32 = (uint32 topology) ||| SDC_APPLY

    /// Compute the DisplaySwitch.exe command-line argument for a given topology
    let toDisplaySwitchArg (topology: DisplayTopology) : string =
        match topology with
        | DisplayTopology.ShowOnlyInternal -> "/internal"
        | DisplayTopology.Extend -> "/extend"
        | DisplayTopology.Clone -> "/clone"
        | DisplayTopology.ShowOnlyExternal -> "/external"
        | _ -> "/extend"

    /// Apply display topology using injected native API and fallback process starter
    let setTopologyWith
        (invokeSetDisplayConfig: uint32 -> int)
        (startDisplaySwitch: string -> unit)
        (topology: DisplayTopology)
        : bool =
        try
            Log.Information("Requesting display topology change to {Topology}...", topology)
            let flags = toDisplayConfigFlags topology
            let result = invokeSetDisplayConfig flags

            if result = 0 then
                Log.Information("SetDisplayConfig succeeded for topology {Topology}.", topology)
                true
            else
                Log.Warning(
                    "SetDisplayConfig returned error code {ErrorCode}. Attempting DisplaySwitch.exe fallback...",
                    result
                )

                let arg = toDisplaySwitchArg topology
                startDisplaySwitch arg
                Log.Information("DisplaySwitch.exe started with argument '{Arg}'.", arg)
                true
        with ex ->
            Log.Error(ex, "Error setting display topology to {Topology}.", topology)
            false

    let private defaultInvokeSetDisplayConfig (flags: uint32) : int =
        let sdcFlags = enum<User32.SDC> (int flags)
        let err = User32.SetDisplayConfig(null, null, sdcFlags)
        int (uint32 err)

    let private defaultStartDisplaySwitch (arg: string) : unit =
        let psi =
            ProcessStartInfo("DisplaySwitch.exe", arg, UseShellExecute = true, CreateNoWindow = true)

        use _ = Process.Start(psi)
        ()

    /// Apply display topology directly via Windows SetDisplayConfig API, with DisplaySwitch.exe fallback
    let setTopology (topology: DisplayTopology) : bool =
        setTopologyWith defaultInvokeSetDisplayConfig defaultStartDisplaySwitch topology
