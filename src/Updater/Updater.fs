namespace Updater

open System
open System.IO
open System.Diagnostics
open System.Text

open Updater.Helper
open Updater.Json
open Updater.Model
open Updater.ShellLink
open Updater.HardLink

type Mode = 
    | Install
    | Update of manifestName : string

type Updater(config : Config, client : IRepoClient, ui : IUI) =    
    let launch (launch : Launch) =
        let psi = ProcessStartInfo(config.appDir @@ launch.target, defaultArg launch.args "")
        psi.WorkingDirectory <- defaultArg launch.workDir (Path.GetDirectoryName psi.FileName)
        psi.UseShellExecute <- false
        psi.RedirectStandardError <- true
        psi.RedirectStandardOutput <- true
        use proc = new Process()
        let sb = StringBuilder()
        proc.ErrorDataReceived.Add(fun a -> if not (String.IsNullOrWhiteSpace a.Data) then sb.Append("ERROR: ").AppendLine(a.Data) |> ignore)
        proc.OutputDataReceived.Add(fun a -> sb.AppendLine(a.Data) |> ignore)
        proc.StartInfo <- psi
        if proc.Start() then
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            if proc.WaitForExit(5000) then
                match launch.expectExitCodes with
                | Some codes when not (codes |> List.contains proc.ExitCode) ->
                    raise (sprintf "Process failed: \"%s\" %s in (%s)\r\n%O" psi.FileName psi.Arguments psi.WorkingDirectory sb |> exn)
                | _ -> ()

    let downloadPackages (pkgs:Packages) = 
        let download (pkg, name) = 
            let dest = config.appDir @@ name
            if Directory.Exists dest then (pkg, false) 
            else
                let tmp = dest + "~"
                client.DownloadPackage(name, tmp, ignore) |> Async.RunSynchronously
                Directory.Move (tmp, dest) 
                (pkg, true)

        pkgs 
        |> Map.toSeq
        |> Seq.map download
        |> Seq.filter snd
        |> Seq.map fst
        |> Seq.toList
        
    let layoutMain (pkgs:Packages) (layout:Layout) (newPkgs: string list) =
        if newPkgs |> List.contains layout.main then
            let pkgPath pkg relativePath = 
                match relativePath with
                | Some path -> config.appDir @@ pkgs.[pkg] @@ path
                | None -> config.appDir @@ pkgs.[pkg]

            let skipOverwrite = 
                ignore  // TODO review

            let rec copy src dest = 
                if File.Exists src then
                    if File.Exists dest || Directory.Exists dest then skipOverwrite dest
                    else createHardLink dest src
                elif Directory.Exists src then
                    if File.Exists dest then skipOverwrite dest
                    else
                        if not (Directory.Exists dest) then Directory.CreateDirectory dest |> ignore
                        for info in DirectoryInfo(src).EnumerateFileSystemInfos() do
                            copy (src @@ info.Name) (dest @@ info.Name)

            for dep in layout.deps do 
                copy (pkgPath dep.pkg dep.from) (pkgPath layout.main dep.``to``)

    let createShortcut (shortcut:Shortcut) = 
        let target = config.appDir @@ shortcut.target
        let name = shortcut.name
        let workDir = defaultArg shortcut.workDir (Path.GetDirectoryName target)
        let path = (defaultArg shortcut.parentDir "%USERPROFILE%\\Desktop" |> Environment.ExpandEnvironmentVariables)  @@ name @! ".lnk"
        let icon = config.appDir @@ (defaultArg shortcut.icon shortcut.target)
        createShortcut (path, target, defaultArg shortcut.args "", workDir, name, icon)

    member __.Execute(mode,?skipLaunch) =
        let skipLaunch = defaultArg skipLaunch false 
        match mode with 
        | Update manifestName when not skipLaunch ->
            let manifest = config.appDir @@ manifestName |> read<Manifest> 
            manifest.launch |> launch
        | _ -> ()
        
        let version = client.GetVersion()
        let manifestName = version @! ".manifest.json"
        let manifestPath = config.appDir @@ manifestName
        if not (File.Exists manifestPath) && (mode = Install || ui.ConfirmUpdate()) then
            let manifestJson = client.GetManifest(version)
            let manifest = deserialize<Manifest> (pathVars manifestPath) manifestJson
        
            downloadPackages manifest.pkgs
            |> layoutMain manifest.pkgs manifest.layout
            manifest.shortcuts |> Seq.iter createShortcut

            save manifestPath manifestJson

            if mode = Install && not skipLaunch then
                { target = config.appDir @@ manifest.pkgs.["updater"] @@ "updater.exe" 
                  args = Some (sprintf "\"%s\"" manifestName) 
                  workDir = None
                  expectExitCodes = Some [ 0 ] } 
                |> launch
