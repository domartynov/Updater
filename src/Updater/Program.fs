module Updater.Program

open System
open Updater.Model
open Updater.RepoClient
open Updater.Json
open Updater.Logging

let (|FlagParameter|_|) name (input : String[]) =
    if Array.contains name input then Some FlagParameter else None

[<EntryPoint>]
let main argv = 
    argv |> infoAs "EntryPoint" |> function | FlagParameter "--attach-debugger" -> System.Diagnostics.Debugger.Launch() |> ignore | _ -> ()

    let ui = 
        match argv with
        | FlagParameter "--test-mode" -> { new IUI with
                                         member __.ConfirmUpdate () = true 
                                         member __.ReportError ex = raise ex }
        | _ -> UI() :> IUI
    try
        let config = (binDir ()) @@ "config.json" |> read<Config>
        let client = repoClient config.repoUrl config.versionUrl
        let updater = Updater(config, client, ui, Args=String.Join(" ", argv))
        argv |> function | FlagParameter "--skip-launch" -> updater.SkipLaunch <- true | _ -> ()
        argv |> function | FlagParameter "--skip-cleanup" -> updater.SkipCleanUp <- true | _ -> ()
        argv |> function | FlagParameter "--skip-fwd-updater" -> updater.SkipForwardUpdater <- true | _ -> ()
        // TODO restore failwithf "Unexpected arguments: %A" argv
        
        updater.Execute()
        0
    with
    | ex ->
        logError ex
        ui.ReportError ex
        -1
