module Updater.Program

open System
open Updater.Model
open Updater.RepoClient
open Updater.Json
open Updater.Logging

let (|FlagParameter|_|) name (input : String[]) =
    if Array.contains name input then Some() else None

let posParameter (input : String[]) =
    input |> Array.tryHead |> Option.bind (fun p -> if not <| p.StartsWith("--") then Some p else None) 

let attachDebugger () = 
    System.Diagnostics.Debugger.Launch() |> ignore

let logEntryAndAttachDebuggerIfRequired hasDebug argv = 
    argv 
    |> infoAs (sprintf "(%s) EntryPoint" (System.Reflection.Assembly.GetExecutingAssembly().CodeBase))
    |> hasDebug
    |> Option.iter attachDebugger

let testUI () =
    { new IUI with  
        member __.ReportProgress _ = ignore
        member __.ConfirmUpdate () = true 
        member __.ReportError ex = raise ex
        member __.ReportWaitForAnotherUpdater () = ()
        member __.Run a = Async.RunSynchronously a; 0 }

let testSlowRepoClient (client: IRepoClient) = 
    { new IRepoClient with
          member __.DownloadPackage(name, path, progress) = 
            System.Threading.Thread.Sleep(2000)
            client.DownloadPackage(name, path, progress)
          member __.GetManifest(version) = client.GetManifest(version)
          member __.GetVersion() = client.GetVersion() }

[<EntryPoint>]
let main argv = 
    argv |> logEntryAndAttachDebuggerIfRequired ((|FlagParameter|_|) "--attach-debugger")

    let ui = match argv with
             | FlagParameter "--test-mode" -> testUI()
             | _ -> UI() :> IUI
    try
        let config = (binDir ()) @@ "config.json" |> read<Config>
        let client = 
            let client = repoClient config.repoUrl config.versionUrl
            match argv with 
            | FlagParameter "--test-slow-mode" -> testSlowRepoClient client
            | _ -> client

        let updater = Updater(config, client, ui, Args=String.Join(" ", argv))
        
        argv |> function | FlagParameter "--skip-launch" -> updater.SkipLaunch <- true | _ -> ()
        argv |> function | FlagParameter "--skip-cleanup" -> updater.SkipCleanUp <- true | _ -> ()
        argv |> function | FlagParameter "--skip-fwd-updater" -> updater.SkipForwardUpdater <- true | _ -> ()
        argv |> function | FlagParameter "--skip-prompt" -> updater.SkipPrompt <- true | _ -> ()
        // TODO restore failwithf "Unexpected arguments: %A" argv
        async { 
            argv |> posParameter |> Option.map (trimEnd ".manifest.json") |> updater.Execute
        } |> ui.Run
    with
    | ex ->
        logError ex
        ui.ReportError ex
        -1
