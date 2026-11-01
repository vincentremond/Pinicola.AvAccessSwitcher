namespace Pinicola.AvAccessSwitcher

open System

[<AutoOpen>]
module Measures =

    [<Measure>]
    type Second =
        static member toTimeSpan(seconds: float<Second>) = TimeSpan.FromSeconds(float seconds)

    [<Measure>]
    type Minute =
        static member toTimeSpan(minutes: float<Minute>) = TimeSpan.FromMinutes(float minutes)

    [<Measure>]
    type Millisecond =
        static member toTimeSpan(ms: float<Millisecond>) = TimeSpan.FromMilliseconds(float ms)

    let millisecondPerSecond = 1000.0<Millisecond> / 1.0<Second>

    [<Measure>]
    type Frame
