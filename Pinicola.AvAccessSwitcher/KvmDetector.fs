namespace Pinicola.AvAccessSwitcher

open System
open System.Management
open Serilog

[<RequireQualifiedAccess>]
module KvmDetector =

    type internal PnpDeviceInfo = {
        DeviceId: string
        Name: string
    }

    [<Literal>]
    let private KvmHardwareIdToken = "VID_1D6B&PID_B022"

    [<Literal>]
    let private KvmNameToken = "AV Access"

    /// Determines whether a PnP device matches the AV Access KVM USB hub/device.
    let internal isMatchingDevice (device: PnpDeviceInfo) : bool =
        (not (isNull device.DeviceId)
         && device.DeviceId.Contains(KvmHardwareIdToken, StringComparison.OrdinalIgnoreCase))
        || (not (isNull device.Name)
            && device.Name.Contains(KvmNameToken, StringComparison.OrdinalIgnoreCase))

    /// Checks if any device returned by the query matches the AV Access KVM device.
    let internal isKvmConnectedWith (queryDevices: unit -> PnpDeviceInfo seq) : bool =
        try
            queryDevices () |> Seq.exists isMatchingDevice
        with ex ->
            Log.Error(ex, "Error querying KVM status via WMI.")
            false

    [<Literal>]
    let private KvmWqlQuery =
        @"SELECT DeviceID, Name FROM Win32_PnPEntity WHERE Status = 'OK' AND (DeviceID LIKE '%VID_1D6B&PID_B022%' OR Name LIKE '%AV Access%')"

    let private queryWmiDevices () : PnpDeviceInfo seq =
        seq {
            let options =
                EnumerationOptions(ReturnImmediately = true, Rewindable = false, Timeout = TimeSpan.FromSeconds(5.0))

            use searcher =
                new ManagementObjectSearcher(ManagementScope(), SelectQuery(KvmWqlQuery), options)

            use collection = searcher.Get()

            for rawItem in collection |> Seq.cast<ManagementObject> do
                use item = rawItem

                let deviceId =
                    match item.["DeviceID"] with
                    | :? string as s -> s
                    | _ -> ""

                let name =
                    match item.["Name"] with
                    | :? string as s -> s
                    | _ -> ""

                yield {
                    DeviceId = deviceId
                    Name = name
                }
        }

    /// Checks if the AV Access KVM USB hub / device is currently connected and active on this PC.
    let isKvmConnected () : bool = isKvmConnectedWith queryWmiDevices
