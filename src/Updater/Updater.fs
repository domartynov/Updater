﻿namespace Updater

open System
open System.IO
open System.Diagnostics
open System.Text

open Updater.Helper
open Updater.Json
open Updater.Model
open Updater.WindowsShell
open Updater.HardLink
open Updater.Logging

type CleanItem =
    | CleanFile of string
    | CleanDir of string

type Updater(config : Config, client : IRepoClient, ui : IUI) =    
    let manifestName version = 
        version + ".manifest.json"

    let manifestPath version =
        config.appDir @@ (manifestName version)

    let versionPath = config.appDir @@ (config.appName + ".version.txt")
    let updaterTxtPath = config.appDir @@ "updater.txt"

    let isUpdaterDep = function 
        | { parent = Some "updater" } -> true
        | _ -> false

    let updaterPackages { pkgs = pkgs; layout = layout } =
        [ for { pkg = pkg } in layout.deps |> Seq.filter isUpdaterDep -> pkg, pkgs.[pkg] ] @
        match pkgs.TryFind "updater" with
        | Some updatePkgName -> [ "updater", updatePkgName ] 
        | _ -> [] 

    let pkgDir name = 
        config.appDir @@ name

    let findLatestManifestVersions () =
        DirectoryInfo(config.appDir).GetFiles(manifestName "*")
        |> Array.sortByDescending (fun fi -> fi.LastWriteTimeUtc)
        |> Seq.map (fun fi -> fi.Name |> trimEnd (manifestName ""))

    let readVersion () =
        if File.Exists versionPath then 
            readText versionPath |> Some
        elif Directory.Exists config.appDir then 
            // if app.version.txt write failed, try to recover by searching for the newest manifest file
            findLatestManifestVersions () |> Seq.tryHead
        else None

    let launch { launch = launch } =
        let args = launch.args |? ""
        let target = config.appDir @@ launch.target |> infoAs (sprintf "Launch: \"%s\"" args)
        let psi = ProcessStartInfo(target, args, UseShellExecute=false, RedirectStandardError=true, RedirectStandardOutput=true)
        psi.WorkingDirectory <- launch.workDir |? Path.GetDirectoryName psi.FileName
        use proc = new Process(StartInfo=psi)
        let sb = StringBuilder()
        proc.ErrorDataReceived.Add(fun a -> if not (String.IsNullOrWhiteSpace a.Data) then sb.Append("ERROR: ").AppendLine(a.Data) |> ignore)
        proc.OutputDataReceived.Add(fun a -> sb.AppendLine(a.Data) |> ignore)
        if proc.Start() then
            proc.BeginOutputReadLine()
            proc.BeginErrorReadLine()
            if proc.WaitForExit(5000) then
                match launch.expectExitCodes with
                | Some codes when not (codes |> List.contains proc.ExitCode) ->
                    raise (sprintf "Process failed: \"%s\" %s in (%s)\r\n%O" psi.FileName psi.Arguments psi.WorkingDirectory sb |> exn)
                | _ -> ()

    let skipOverwrite = ignore  // TODO review

    let rec copy src dest exclude = 
        if File.Exists src then
            if File.Exists dest || Directory.Exists dest then skipOverwrite dest
            else createHardLink dest src
        elif Directory.Exists src then
            if File.Exists dest then skipOverwrite dest
            else
                if not (Directory.Exists dest) then Directory.CreateDirectory dest |> ignore
                for info in DirectoryInfo(src).EnumerateFileSystemInfos() do
                    if not (Set.contains info.Name exclude) then
                        copy (src @@ info.Name) (dest @@ info.Name) Set.empty // TODO: review to add support for deep layout with folders merge

    let downloadPackages currentManifest { pkgs = pkgs; layout = layout } = 
        let download baseName name =
            let dest = pkgDir name
            if not (Directory.Exists dest) then
                let tmp = dest + "~"
                client.DownloadPackage(baseName, tmp, ignore) |> Async.RunSynchronously
                Directory.Move (tmp, dest) 

        let (curPkgs, curLayout) = 
            match currentManifest with 
            | Some { pkgs = pkgs; layout = layout } -> pkgs, layout
            | None -> Map.empty, { main = ""; deps = [] }

        let pathFirstSegment (path : string) = 
            match path.IndexOfAny([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]) with
            | i when i >= 0 -> path.Substring(0, i)
            | _ -> path

        let depItems = function
            | { pkg = pkg; ``to`` = None }  -> // TODO cannot nest layouts (add to manifest validation)
                [ for e in DirectoryInfo(config.appDir @@ curPkgs.[pkg]).GetFileSystemInfos() -> e.Name ]
            | { ``to`` = Some dir } -> 
                [ pathFirstSegment dir ] // TODO go deep recurivesly (if we want to allow merge deps in the same folder

        let groupDeps layout = layout.deps |> List.groupBy (fun d -> d.parent |? layout.main) |> Map.ofSeq
        let curDepsMap = curLayout |> groupDeps
        let depsMap = layout |> groupDeps

        let downloadOrReuse (pkg, name) = 
            match curPkgs.TryFind pkg, DuplicateName.next name with
            | Some curName, _ when curName = name -> None
            | Some curName, (baseName, _) 
                when let (curBase, _) = DuplicateName.next curName in curBase = baseName ->
                match curDepsMap.TryFind pkg, depsMap.TryFind pkg with
                | Some curDeps, Some deps when curDeps = deps ->
                    let excludeItems = deps |> List.collect depItems |> Set
                    copy (pkgDir curName) (pkgDir name) excludeItems
                | _ -> download baseName name
                Some pkg
            | _, (baseName, _) ->
                download baseName name
                Some pkg
        pkgs
        |> Map.toSeq
        |> Seq.choose downloadOrReuse
        |> Seq.toList
    
    let layout manifest newPkgs =
        let { pkgs = pkgs; layout = layout } = manifest
        
        let pkgPath pkg relativePath = 
            match relativePath with
            | Some path -> (pkgs.[pkg] |> pkgDir) @@ path
            | None -> pkgs.[pkg] |> pkgDir 

        let layoutGroup (parent, deps) =
            for dep in deps do 
                copy (pkgPath dep.pkg dep.from) (pkgPath parent dep.``to``) Set.empty

        layout.deps
        |> Seq.groupBy (fun d -> d.parent |? layout.main)
        |> Seq.filter (fun (parent, _) -> List.contains parent newPkgs)
        |> Seq.iter layoutGroup
        manifest

    let desktopLocation = lazy (Environment.GetFolderPath Environment.SpecialFolder.DesktopDirectory)

    let shortcutPath { parentDir = parentDir; name = name } = 
        (parentDir |? desktopLocation.Value)  @@ name @! ".lnk"

    let createShortcuts { shortcuts = shortcuts } =
        let create (shortcut : Shortcut) = 
            let target = config.appDir @@ shortcut.target
            { TargetPath=target
              Arguments=shortcut.args |? ""
              WorkingDir=shortcut.workDir |? Path.GetDirectoryName target
              Description=shortcut.name
              IconLocation=config.appDir @@ (shortcut.icon |? shortcut.target) }
            |> createShortcut (shortcutPath shortcut |> infoAs "CreateShortcut")
        
        shortcuts |> Seq.iter create

    let launchUpdaterExe argStr exePath  =
        let startProcess path args =
            let arg = String.Join(" ", args |> Seq.filter (not << String.IsNullOrWhiteSpace)  |> Set)
            ProcessStartInfo(path |> infoAs (sprintf "LaunchUpdater: \"%s\"" arg), arg, UseShellExecute=false) 
            |> Process.Start 

        "--skip-fwd-updater" :: (splitArgs argStr)
        |> function
            | l when System.Diagnostics.Debugger.IsAttached -> "--attach-debugger" :: l
            | l -> l 
        |> startProcess exePath
        |> ignore


    let cleanUp excludeVersions =
        let deleteArtifact artifact = 
            try 
                match artifact with
                | CleanDir path -> Directory.Move(path, path + "~")
                                   Directory.Delete(path + "~", true)
                | CleanFile path -> File.Delete(path)
                true
            with _ -> false 

        [ Directory.EnumerateDirectories(config.appDir, "*~") |> Seq.map CleanDir
          Directory.EnumerateFiles(config.appDir, "*.*~") |> Seq.map CleanFile ]
        |> Seq.concat
        |> Seq.iter (deleteArtifact >> ignore)

        let findArtifacts skipUpdaters manifest =
            let updPkgs = if skipUpdaters then updaterPackages manifest else []
            [ manifest.pkgs |> Map.toSeq |> Seq.except updPkgs |> Seq.map (snd >> pkgDir >> CleanDir)
              manifest.shortcuts |> Seq.map (shortcutPath >> CleanFile) ]
            |> Seq.concat
            |> Seq.toList

        let keepVersions, removeVersions = 
            let kl, rl = 
                findLatestManifestVersions ()
                |> List.ofSeq
                |> List.partition (fun v -> List.contains v excludeVersions)
            match config.keepVersions - kl.Length with
            | i when 0 < i && i <= rl.Length -> let kl2, rl2 = List.splitAt i rl in kl @ kl2, rl2
            | i when 0 < i -> kl @ rl, []
            | _ -> kl, rl

        let keepArtifacts = 
            keepVersions 
            |> Seq.collect (manifestPath >> read<Manifest> >> findArtifacts false) 
            |> Set

        let deleteArtifactsAndManifest (manifestPath, artifacts) =
            artifacts 
            |> Seq.filter (not << keepArtifacts.Contains)
            |> Seq.map deleteArtifact 
            |> Seq.fold (&&) true |> function 
                | true -> manifestPath |> CleanFile |> deleteArtifact |> ignore
                | _ -> ()

        removeVersions 
        |> Seq.map (fun v -> let path = manifestPath v in 
                             path, path |> read<Manifest> |> findArtifacts (not config.cleanupUpdaters))
        |> Seq.iter deleteArtifactsAndManifest

    member val SkipLaunch = false with get, set
    member val UpdaterExeName = "updater.exe" with get, set
    member val Args: String = "" with get, set
    member val SkipForwardUpdater = false with get, set
    member val SkipCleanUp = false with get, set
    member val SkipRelaunchNewUpdater = false with get, set

    member self.Execute() =
        self.Args |> infoAs "Execute" |> ignore    

        let launchIfConfigured manifest = 
            if not self.SkipLaunch then launch manifest

        let updaterExePath manifest =
            config.appDir @@ manifest.pkgs.["updater"] @@ self.UpdaterExeName // TODO review

        let launchUpdater manifest =
            if not self.SkipRelaunchNewUpdater then 
                manifest 
                |> updaterExePath 
                |> launchUpdaterExe self.Args
            
        let updateUpdater  (currentManifest : Manifest option) manifest currentVersion version = 
            match currentManifest, manifest with
            | None, manifest -> false, version, manifest
            | Some curManifest, manifest ->
                match (updaterPackages curManifest), (updaterPackages manifest) with
                | _, [] -> false, version, manifest
                | curUpdaterPkgs, updaterPkgs when curUpdaterPkgs = updaterPkgs  -> false, version, manifest
                | curUpdaterPkgs, updaterPkgs -> 
                    let pkgs = Map.toSeq curManifest.pkgs 
                               |> Seq.except curUpdaterPkgs 
                               |> Seq.append updaterPkgs |> Map
                    let deps =
                        List.filter (not << isUpdaterDep) curManifest.layout.deps @
                        List.filter isUpdaterDep manifest.layout.deps
                    let updaterSuffix = updaterPkgs |> List.tryFind (fun (pkg, _) -> pkg = "updater") |> function
                                        | Some (pkg, name) -> trimStart pkg name
                                        | _ -> 
                                            updaterPkgs |> List.tryHead  |> function
                                            | Some (pkg, name) -> trimStart pkg name
                                            | _ -> ""
                    let partialVersion = sprintf "%s-p%s"  (currentVersion |? version) updaterSuffix
                    let partialManifest = { curManifest with pkgs = pkgs; 
                                                             layout = { curManifest.layout with deps = deps } }
                    true, partialVersion, partialManifest 

        let update currentVersion version =
            let json = client.GetManifest(version)
            let manifest = deserialize<Manifest> (version |> manifestPath |> pathVars) json
            let currentManifest = currentVersion |> Option.map (manifestPath >> read<Manifest>)
        
            let updaterOnly, version, manifest = updateUpdater currentManifest manifest currentVersion version
            
            manifest
            |> downloadPackages currentManifest
            |> layout manifest
            |> if updaterOnly then ignore else createShortcuts 

            save (manifestPath version |> infoAs "SaveManifest") json
            save versionPath version 

            if updaterOnly then 
                launchUpdater manifest
                self.SkipLaunch <- true // TODO review here we break current update process relaunching updater.exe
                self.SkipCleanUp <- true
            else  
                manifest |> updaterExePath |> infoAs "SaveUpdaterExePath" |> save updaterTxtPath 
            manifest

        if not self.SkipForwardUpdater && File.Exists updaterTxtPath then 
            match readText updaterTxtPath with
            | latestUpdExePath when latestUpdExePath  @<>@ runningExePath() -> Some latestUpdExePath
            | _ -> None
        else None
        |> function
            | Some fwdUpdPath -> launchUpdaterExe self.Args fwdUpdPath
            | _ ->
                let currentVersion = readVersion()
                let version = client.GetVersion()
                match currentVersion with
                | Some cv when cv = version || 
                          not <| ui.ConfirmUpdate() -> cv |> manifestPath |> read<Manifest> 
                | currentVersion -> (currentVersion, version) |> infoAs "Update" ||> update 
                |> launchIfConfigured

                if not self.SkipCleanUp then
                    Process.GetCurrentProcess().PriorityClass <- ProcessPriorityClass.Idle
                    version :: (Option.toList currentVersion)
                    |> cleanUp
