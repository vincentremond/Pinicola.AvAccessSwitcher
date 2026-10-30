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

    [<DllImport("user32.dll", SetLastError = true)>]
    extern int private SetDisplayConfig(
        uint32 numPathArrayElements,
        nativeint pathArray,
        uint32 numModeInfoArrayElements,
        nativeint modeInfoArray,
        uint32 flags
    )

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
