namespace Pinicola.AvAccessSwitcher

open System
open System.Diagnostics
open System.Runtime.InteropServices
open Serilog

module NativeDisplay =

    // Windows CCD (Connecting and Configuring Displays) flags for SetDisplayConfig
    type DisplayTopology =
        | ShowOnlyInternal = 0x00000001u // SDC_TOPOLOGY_INTERNAL (Show only on 1)
        | Clone = 0x00000002u // SDC_TOPOLOGY_CLONE
        | Extend = 0x00000004u // SDC_TOPOLOGY_EXTEND (Extend displays)
        | ShowOnlyExternal = 0x00000008u // SDC_TOPOLOGY_EXTERNAL (Show only on 2)

    [<Literal>]
    let SDC_APPLY = 0x00000080u

    [<Literal>]
    let QDC_ALL_PATHS = 0x00000001u

    [<Literal>]
    let DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = 0xFFFFFFFFu

    [<Literal>]
    let DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6u

    [<Literal>]
    let DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11u

    [<Literal>]
    let DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13u

    [<Literal>]
    let DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = 0x80000000u

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type LUID = {
        LowPart: uint32
        HighPart: int32
    }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type DISPLAYCONFIG_PATH_SOURCE_INFO = {
        adapterId: LUID
        id: uint32
        modeInfoIdx: uint32
        statusFlags: uint32
    }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type DISPLAYCONFIG_RATIONAL = {
        Numerator: uint32
        Denominator: uint32
    }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type DISPLAYCONFIG_PATH_TARGET_INFO = {
        adapterId: LUID
        id: uint32
        modeInfoIdx: uint32
        outputTechnology: uint32
        rotation: uint32
        scaling: uint32
        refreshRate: DISPLAYCONFIG_RATIONAL
        scanLineOrdering: uint32
        targetAvailable: int32
        statusFlags: uint32
    }

    [<Struct; StructLayout(LayoutKind.Sequential)>]
    type DISPLAYCONFIG_PATH_INFO = {
        sourceInfo: DISPLAYCONFIG_PATH_SOURCE_INFO
        targetInfo: DISPLAYCONFIG_PATH_TARGET_INFO
        flags: uint32
    }

    [<Struct; StructLayout(LayoutKind.Sequential, Size = 64)>]
    type DISPLAYCONFIG_MODE_INFO = {
        infoType: uint32
        id: uint32
        adapterId: LUID
    }

    type DisplayTargetInfo = {
        TargetId: uint32
        OutputTechnology: uint32
        TargetAvailable: bool
    }

    [<DllImport("user32.dll", SetLastError = true)>]
    extern int private SetDisplayConfig(
        uint32 numPathArrayElements,
        nativeint pathArray,
        uint32 numModeInfoArrayElements,
        nativeint modeInfoArray,
        uint32 flags
    )

    [<DllImport("user32.dll")>]
    extern int private GetDisplayConfigBufferSizes(
        uint32 flags,
        [<Out>] uint32[] numPathArrayElements,
        [<Out>] uint32[] numModeInfoArrayElements
    )

    [<DllImport("user32.dll")>]
    extern int private QueryDisplayConfig(
        uint32 flags,
        [<In; Out>] uint32[] numPathArrayElements,
        [<Out>] DISPLAYCONFIG_PATH_INFO[] pathArray,
        [<In; Out>] uint32[] numModeInfoArrayElements,
        [<Out>] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
        nativeint currentTopologyId
    )

    /// Determines whether a display output technology represents an external monitor connection.
    let isExternalOutputTechnology (outputTechnology: uint32) : bool =
        outputTechnology <> DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL
        && outputTechnology <> DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED
        && outputTechnology <> DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS
        && outputTechnology <> DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED
        && outputTechnology <> DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER

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
        let numPaths = [| 0u |]
        let numModes = [| 0u |]
        let sizeResult = GetDisplayConfigBufferSizes(QDC_ALL_PATHS, numPaths, numModes)

        if sizeResult <> 0 || numPaths.[0] = 0u then
            Log.Warning("GetDisplayConfigBufferSizes returned {Result} (paths={NumPaths}).", sizeResult, numPaths.[0])
            Seq.empty
        else
            let paths = Array.zeroCreate<DISPLAYCONFIG_PATH_INFO> (int numPaths.[0])
            let modes = Array.zeroCreate<DISPLAYCONFIG_MODE_INFO> (int numModes.[0])

            let queryResult =
                QueryDisplayConfig(QDC_ALL_PATHS, numPaths, paths, numModes, modes, IntPtr.Zero)

            if queryResult <> 0 then
                Log.Warning("QueryDisplayConfig returned {Result}.", queryResult)
                Seq.empty
            else
                let count = min (int numPaths.[0]) paths.Length

                seq {
                    for i in 0 .. count - 1 do
                        let target = paths.[i].targetInfo

                        yield {
                            TargetId = target.id
                            OutputTechnology = target.outputTechnology
                            TargetAvailable = target.targetAvailable <> 0
                        }
                }

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
        SetDisplayConfig(0u, IntPtr.Zero, 0u, IntPtr.Zero, flags)

    let private defaultStartDisplaySwitch (arg: string) : unit =
        let psi =
            ProcessStartInfo("DisplaySwitch.exe", arg, UseShellExecute = true, CreateNoWindow = true)

        use _ = Process.Start(psi)
        ()

    /// Apply display topology directly via Windows SetDisplayConfig API, with DisplaySwitch.exe fallback
    let setTopology (topology: DisplayTopology) : bool =
        setTopologyWith defaultInvokeSetDisplayConfig defaultStartDisplaySwitch topology
