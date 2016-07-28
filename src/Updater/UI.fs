namespace Updater

open System.Windows.Forms
open Updater.Model

type UI() =
    interface IUI with
        member __.ConfirmUpdate () =
            DialogResult.Yes = MessageBox.Show("Do you want to install a new version", "Updater", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)

        member __.ReportError (ex) =
            MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore
