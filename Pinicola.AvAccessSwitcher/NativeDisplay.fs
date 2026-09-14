namespace Pinicola.AvAccessSwitcher

open System
open System.Runtime.InteropServices
open System.Diagnostics

module NativeDisplay =

    // Windows CCD (Connecting and Configuring Displays) flags for SetDisplayConfig
    type DisplayTopology =
        | ShowOnlyInternal = 0x00000001u // SDC_TOPOLOGY_INTERNAL (Show only on 1)
        | Clone = 0x00000002u // SDC_TOPOLOGY_CLONE
        | Extend = 0x00000004u // SDC_TOPOLOGY_EXTEND (Extend displays)
        | ShowOnlyExternal = 0x00000008u // SDC_TOPOLOGY_EXTERNAL (Show only on 2)

    [<Literal>]
    let private SDC_APPLY = 0x00000080u

    [<DllImport("user32.dll", SetLastError = true)>]
    extern int private SetDisplayConfig(
        uint32 numPathArrayElements,
        nativeint pathArray,
        uint32 numModeInfoArrayElements,
        nativeint modeInfoArray,
        uint32 flags
    )

    /// Apply display topology directly via Windows SetDisplayConfig API, with DisplaySwitch.exe fallback
    let setTopology (topology: DisplayTopology) : bool =
        try
            let flags = (uint32 topology) ||| SDC_APPLY
            let result = SetDisplayConfig(0u, IntPtr.Zero, 0u, IntPtr.Zero, flags)

            if result = 0 then
                true
            else
                // Fallback to DisplaySwitch.exe if SetDisplayConfig returned non-zero error
                let arg =
                    match topology with
                    | DisplayTopology.ShowOnlyInternal -> "/internal"
                    | DisplayTopology.Extend -> "/extend"
                    | DisplayTopology.Clone -> "/clone"
                    | DisplayTopology.ShowOnlyExternal -> "/external"
                    | _ -> "/extend"

                let psi =
                    ProcessStartInfo("DisplaySwitch.exe", arg, UseShellExecute = true, CreateNoWindow = true)

                use p = Process.Start(psi)
                true
        with ex ->
            eprintfn "Error setting display topology: %s" ex.Message
            false
