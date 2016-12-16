namespace Updater

open System
open System.IO
open System.Diagnostics
open System.Text

open Updater.Helper
open Updater.Json
open Updater.Model
open Updater.WindowsShell
open Updater.Fs
open Updater.Logging

type CleanItem =
    | CleanFile of string
    | CleanDir of string

type UpdaterStep =
    | Entry of launchVersion : string option
    | ForwardUpdater of path : string
    | UpdateOrLaunch of launchVersion : string option
    | LaunchVersion of version : string
    | LaunchManifest of manifest : Manifest
    | Update of currentVersion : string option * version : string
    | CleanUp of currentVersion : string option * version : string

type Updater(config : Config, client : IRepoClient, ui : IUI) as self =    
    let runningUpdaterVersion () =
        let ver = self.GetType().Assembly.GetName().Version
        ver.Major, ver.Minor, ver.Build

    let parseUpdaterVersion (n : string) = 
        match n.IndexOf('-') with 
        | i when i >=0 && i + 1 < n.Length -> 
            n.Substring(i).Split('.') |> Array.map System.Int32.TryParse |> function
                | [| true, t1; true, t2; true, t3 |] -> t1, t2, t3
                | _ -> 0, 0, 0
        | _ -> 0, 0, 0

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

    let launchApp { launch = launch } =
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
            if proc.WaitForExit(2000) then // TODO review
                match launch.expectExitCodes with
                | Some codes when not (codes |> List.contains proc.ExitCode) ->
                    raise (sprintf "Process failed: \"%s\" %s in (%s)\r\n%O" psi.FileName psi.Arguments psi.WorkingDirectory sb |> exn)
                | _ -> ()

    let copy = Fs.copy ignore
    
    let downloadPackages currentManifest { pkgs = pkgs; layout = layout } = 
        let download baseName name =
            let dest = pkgDir name
            if not (Directory.Exists dest) then
                let tmp = nextTmpDir (dest + "~")
                client.DownloadPackage(baseName, tmp, ignore) |> Async.RunSynchronously
                move tmp dest 

        let (curPkgs, curLayout) = 
            match currentManifest with 
            | Some { pkgs = pkgs; layout = layout } -> pkgs, layout
            | None -> Map.empty, { main = ""; deps = [] }

        let pathFirstSegment (path : string) = 
            match path.IndexOfAny([| Path.DirectorySeparatorChar; Path.AltDirectorySeparatorChar |]) with
            | i when i >= 0 -> path.Substring(0, i)
            | _ -> path

        let depEntries { pkg = pkg; ``to`` = dir } = 
            dip (walk 1 (config.appDir @@ curPkgs.[pkg])) (dir |? "")

        let groupDeps layout = layout.deps |> List.groupBy (fun d -> d.parent |? layout.main) |> Map.ofSeq
        let curDepsMap = curLayout |> groupDeps
        let depsMap = layout |> groupDeps

        let downloadOrReuse (pkg, name) = 
            let existsPkgDir = pkgDir >> Directory.Exists
            match curPkgs.TryFind pkg, DuplicateName.next name with
            | Some curName, _ when curName = name && existsPkgDir name -> None
            | Some curName, (baseName, _) when let (curBase, _) = DuplicateName.next curName in curBase = baseName  && existsPkgDir curName ->
                match curDepsMap.TryFind pkg, depsMap.TryFind pkg with
                | Some curDeps, Some deps when curDeps = deps ->
                    copy (pkgDir curName) (pkgDir name) (deps |> Seq.map depEntries |> Seq.fold merge Map.empty)
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
                copy (pkgPath dep.pkg dep.from) (pkgPath parent dep.``to``) Map.empty

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

    let launchUpdaterExe exePath  =
        let startProcess path args =
            let arg = String.Join(" ", args |> Seq.filter (not << String.IsNullOrWhiteSpace))
            ProcessStartInfo(path |> infoAs (sprintf "LaunchUpdater: \"%s\"" arg), arg, UseShellExecute=false) 
            |> Process.Start 

        let args = "--skip-fwd-updater" :: (splitArgs self.Args)
        let dbg = if System.Diagnostics.Debugger.IsAttached then ["--attach-debugger"] else []
        let ppid = ["--ppid"; Process.GetCurrentProcess().Id |> string]
        startProcess exePath (args @ dbg @ ppid) |> ignore


    let cleanUp excludeVersions =
        let deleteArtifact artifact = 
            try 
                match artifact with
                | CleanDir path -> Directory.Delete(path, true)
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

    let updaterExePath m =
        config.appDir @@ m.pkgs.["updater"] @@ self.UpdaterExeName // TODO review

    let updateUpdater  (cm : Manifest option) m cv v = 
        match cm, m with
        | None, m -> false, v, m
        | Some cm, m ->
            match (updaterPackages cm), (updaterPackages m) with
            | _, [] -> false, v, m
            | curUpdaterPkgs, updaterPkgs when curUpdaterPkgs = updaterPkgs  -> false, v, m
            | curUpdaterPkgs, updaterPkgs -> 
                let pkgs = Map.toSeq cm.pkgs 
                            |> Seq.except curUpdaterPkgs 
                            |> Seq.append updaterPkgs |> Map
                let deps =
                    List.filter (not << isUpdaterDep) cm.layout.deps @
                    List.filter isUpdaterDep m.layout.deps
                let updaterSuffix = updaterPkgs |> List.tryFind (fun (pkg, _) -> pkg = "updater") |> function
                                    | Some (pkg, name) -> trimStart pkg name
                                    | _ -> 
                                        updaterPkgs |> List.tryHead  |> function
                                        | Some (pkg, name) -> trimStart pkg name
                                        | _ -> ""
                let partialVersion = sprintf "%s-p%s"  (cv |? v) updaterSuffix
                let partialManifest = { cm with pkgs = pkgs 
                                                layout = { cm.layout with deps = deps } 
                                                shortcuts = []
                                                launch =     { target = ""; args = None; workDir = None; expectExitCodes = None } }
                true, partialVersion, partialManifest 

    let canSkipConfirmUpdate cm m =
        let confirmBaseNames m =
            m.pkgs 
            |> Map.toSeq
            |> Seq.except (updaterPackages m) 
            |> Seq.map (snd >> DuplicateName.baseName) 
            |> Set

        let confirmDeps m =
            m.layout.deps |> Seq.filter (not << isUpdaterDep) |> Set

        (confirmBaseNames cm) = (confirmBaseNames m ) && (confirmDeps cm) = (confirmDeps m)
    
    let validNewerUpdater updExePath =
        let updDir = Path.GetDirectoryName updExePath
        File.Exists updExePath &&
        File.Exists (updDir @@ "config.json") &&
        (updDir |> Path.GetFileName |> parseUpdaterVersion) > self.UpdaterVersion

    let executeStep = function
        | Entry lv -> 
            if not self.SkipForwardUpdater && File.Exists updaterTxtPath then 
                match readText updaterTxtPath with
                | updExePath when updExePath  @<>@ runningExePath () && validNewerUpdater updExePath -> [ForwardUpdater updExePath]
                | _ -> [UpdateOrLaunch lv]
            else [UpdateOrLaunch lv]
        | ForwardUpdater p -> 
            launchUpdaterExe p
            []
        | UpdateOrLaunch lv ->
            let cv = readVersion()
            let v = client.GetVersion()
            match lv, cv with
            | Some lv, None -> [LaunchVersion lv]
            | Some lv, Some cv when lv <> cv -> [LaunchVersion lv]
            | _, Some cv when cv = v -> [LaunchVersion cv]
            | _, cv -> [Update (cv, v)]
        | Update (cv, v) -> 
            match ExcusiveLock.lockOrWait (sprintf "Global\updater_%s" (config.appUid |? config.appName)) with
            | Choice1Of2 lock ->
                use x = lock
                let json = client.GetManifest(v)
                let m = deserialize<Manifest> (v |> manifestPath |> pathVars) json
                let cm = cv |> Option.map (manifestPath >> read<Manifest>)
        
                let updaterOnly, v, m = updateUpdater cm m cv v

                let upd () = 
                    m
                    |> downloadPackages cm
                    |> layout m
                    |> if not updaterOnly then createShortcuts else ignore 

                    if updaterOnly then 
                        save (manifestPath v |> infoAs "SavePartialManifest") (serialize m)
                    else
                        save (manifestPath v |> infoAs "SaveManifest") json
                    save versionPath v 

                    if updaterOnly then 
                        [m |>  updaterExePath |> ForwardUpdater]
                    else  
                        m |> updaterExePath |> infoAs "SaveUpdaterExePath" |> save updaterTxtPath 
                        [ LaunchManifest m
                          CleanUp (cv, v) ]

                match cm with
                | None -> upd () 
                | Some cm when updaterOnly || canSkipConfirmUpdate cm m || ui.ConfirmUpdate() -> upd ()
                | Some cm -> [LaunchManifest cm]
            | Choice2Of2 waitForAnotherUpdater ->
                ui.ReportWaitForAnotherUpdater()
                waitForAnotherUpdater()
                [Entry None] // TODO review
        | LaunchVersion v -> 
            [v |> manifestPath |> read<Manifest> |> LaunchManifest]
        | LaunchManifest m -> 
            if not self.SkipLaunch then launchApp m
            []
        | CleanUp (cv, v) ->
            if not self.SkipCleanUp then
                Process.GetCurrentProcess().PriorityClass <- ProcessPriorityClass.Idle
                v :: (Option.toList cv)
                |> cleanUp
            []

    let rec execute input =
        input 
        |> infoAs "Step" 
        |> executeStep 
        |> List.collect execute

    member val SkipLaunch = false with get, set
    member val UpdaterExeName = "updater.exe" with get, set
    member val Args: String = "" with get, set
    member val SkipForwardUpdater = false with get, set
    member val SkipCleanUp = false with get, set
    member val UpdaterVersion = runningUpdaterVersion () |> infoAs "UpdaterVersion" with get, set

    member self.Execute (launchVersion: string option) =
        Entry launchVersion |> execute |> ignore
