module Updater.Program

open System.IO

open Updater.Model
open Updater.RepoClient
open Updater.Json
    
let readConfig () =
    (binDir ()) @@ "config.json" |> read<Config>

[<EntryPoint>]
let main argv = 
    let ui = UI() :> IUI
    try
        let config = readConfig ()
        let client = repoClient config.repoUrl config.versionUrl
        let updater = Updater(config, client, ui)
        
        match argv with
        | [||] -> Install
        | [| version |] -> Update version
        | _ -> failwithf "Unexpected arguments: %A" (List.ofArray argv)
        |> updater.Execute
        0
    with
    | ex ->
        ui.ReportError(ex)
        -1
