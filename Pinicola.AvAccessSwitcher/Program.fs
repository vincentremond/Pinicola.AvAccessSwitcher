namespace Pinicola.AvAccessSwitcher

open System
open System.Windows.Forms

module Program =

    [<STAThread>]
    [<EntryPoint>]
    let main argv =
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(false)

        use context = new TrayApplicationContext()
        Application.Run(context)
        0
