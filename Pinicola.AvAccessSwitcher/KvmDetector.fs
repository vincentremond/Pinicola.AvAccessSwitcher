namespace Pinicola.AvAccessSwitcher

open System
open System.Management
open Serilog

module KvmDetector =

    /// Checks if the AV Access KVM USB hub / device is currently connected and active on this PC.
    let isKvmConnected () : bool =
        try
            use searcher =
                new ManagementObjectSearcher(@"SELECT DeviceID, Status, Name FROM Win32_PnPEntity WHERE Status = 'OK'")

            use collection = searcher.Get()

            let connected =
                collection
                |> Seq.cast<ManagementObject>
                |> Seq.exists (fun item ->
                    let deviceId =
                        match item.["DeviceID"] with
                        | :? string as s -> s
                        | _ -> ""

                    let name =
                        match item.["Name"] with
                        | :? string as s -> s
                        | _ -> ""

                    deviceId.Contains("VID_1D6B&PID_B022", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("AV Access", StringComparison.OrdinalIgnoreCase)
                )

            connected
        with ex ->
            Log.Error(ex, "Error querying KVM status via WMI.")
            false
