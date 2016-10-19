namespace Updater

open System.Windows.Forms
open Updater.Model

type UI() =
    let printProgressDot () = printf "."
    let timer = new System.Threading.Timer(new System.Threading.TimerCallback(ignore >> printProgressDot))

    do 
        printfn "Updating, please wait..."
        timer.Change(2000, 2000) |> ignore 

    interface IUI with
        member __.ConfirmUpdate () =
            DialogResult.Yes = MessageBox.Show("Do you want to install a new version?", "Updater", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)

        member __.ReportError ex =
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
