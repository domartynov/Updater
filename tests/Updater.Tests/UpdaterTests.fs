namespace Updater.Tests

open System
open System.IO
open System.IO.Compression
open System.Diagnostics

open Xunit
open FsUnit.Xunit

open Updater.Json
open Updater.Model
open Updater
open Updater.RepoClient
open Updater.WindowsShell
open Updater.Publish.Program


type EntryFile =
    | Path of string
    | Text of string

type EntryName = string
type PackageEntry = EntryFile * EntryName

type UpdaterTests (testDirFixture : TestDirFixture) =
    let binDir = testDirFixture.BinDir
    let testDir = testDirFixture.MakeDir<UpdaterTests>()
    let repoDir = testDir @@ "repo" |> makeDir
//    let tempDir = testDir @@ "temp" |> makeDir
    let appDir = testDir @@ "app1"
    let userDir = testDir @@ "user"

    let repoVersionPath = repoDir @@ "app1.version.txt"

    let publish pkgs = 
        let paths = [ for p in pkgs -> repoDir @@ p @! ".zip" ]
        publish repoDir repoVersionPath paths |> should equal 0

    let addPkgTo dir name items =
        let zipPath = dir @@ name @! ".zip"
        use archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)
        for file, name in items do
            match file with
            | Path path -> 
                archive.CreateEntryFromFile(Environment.ExpandEnvironmentVariables path, name) |> ignore
            | Text value -> 
                use stream = archive.CreateEntry(name).Open()
                use writer = new StreamWriter(stream)
                writer.Write(value)
        name

    let addPkg = addPkgTo repoDir

    let fileEntries dir names =
        [for name in names -> Path(dir @@ name), name]

    let config =
        { appUid = None
          appName = "app1"
          appDir = appDir 
          repoUrl = Uri(repoDir).AbsoluteUri
          versionUrl = "app1.version.txt"
          keepVersions = 2
          cleanupUpdaters = false }

    let (++) name version = name + "-" + version

    let defaultManifest pkgNames = 
        { app = { name = "app1"; title = "App1"; version = ""; channel = ""; desc = None }
          pkgs = Map.ofList [ for name in pkgNames -> (name, name) ]
          layout = { main = "app1"; deps = [] }
          shortcuts = []
          launch = { target = ""; args = None; workDir = None; expectExitCodes = Some [ 0 ] } }

    let withAppVersion version manifest =
        { manifest with app = { manifest.app with version = version } }

    let withDefaultShortcut manifest = 
        let shortcut =
            { name = "${app.title}-${app.version}"
              target = "${pkgs.updater}\\updater.exe"
              args = Some "${fileName}"
              workDir = None
              parentDir = (userDir @@ "desktop") |> makeDir |> Some 
              icon = Some "${launch.target}" }
        { manifest with shortcuts = shortcut :: manifest.shortcuts }

    let withLaunch (exeFileName, args) manifest =
        let launch = { target = sprintf "${pkgs.%s}\\%s" (manifest.layout.main) exeFileName
                       args = args
                       workDir = None
                       expectExitCodes = None }
        { manifest with launch = launch }

    let addDependency dep manifest = 
        { manifest with Manifest.layout = { manifest.layout with deps = dep :: manifest.layout.deps } }

    let defaultDep pkg  = 
        { pkg = pkg; from = None; ``to`` = None; parent = None }

    let publishManifest version manifest =
        let manifestName = "app1" ++ version
        manifest |> serialize |> save (repoDir @@ manifestName @! ".manifest.json")
        manifestName |> save repoVersionPath

    let appManifest =
        defaultManifest [ "updater"; "updater-config"; "tools"; "app1" ]
        |> withDefaultShortcut 
        |> addDependency (defaultDep "tools")
        |> addDependency { defaultDep "updater-config" with parent = Some "updater" }
        |> withLaunch ("xcopy.exe", Some "version.txt result.txt* /Y")

    let genUpdaterPkg version =
        [ Text version, "updater.txt" ] @
        fileEntries binDir [ "Updater.exe"; "Updater.pdb"; "FSharp.Core.dll"; "Newtonsoft.Json.dll" ]
        |> addPkg ("updater" ++ version)

    let genUpdaterConfigPkg version =
        [ Text (serialize config), "config.json"
          Text version, "updater-config.txt" ]
        |> addPkg ("updater-config" ++ version)

    let genToolsPkg version = 
        [ Text version, "tools.txt"
          Text "test", "docs\\test.txt" ] @
        fileEntries "%windir%\\system32" [ "xcopy.exe" ]
        |> addPkg ("tools" ++ version)

    let genAppPkg version =
        [ Text version, "version.txt" ]
        |> addPkg ("app1" ++ version)


    let mutable userPrompts = 0
    let testUI =
        { new IUI with
            member __.ConfirmUpdate () = 
                userPrompts <- userPrompts + 1
                true 
            member __.ReportError ex = raise ex
            member __.ReportWaitForAnotherUpdater () = () // TODO add to test
        }

    let client = repoClient config.repoUrl config.versionUrl
    let updater = Updater(config, client, testUI, Args="--test-mode --skip-cleanup", SkipForwardUpdater=true, SkipCleanUp=true)

    let publishV1 () =
        appManifest |> publishManifest "template"
        [ genUpdaterPkg "0.1.0" 
          genUpdaterConfigPkg "1.0.0" 
          genToolsPkg "1.0"
          genAppPkg "1.0.0" ]
        |> publish

    let manifestPath name =
        appDir @@ name @! ".manifest.json"

    let readCurrentManifest () =
        appDir @@ "app1.version.txt" |> readText |> manifestPath |> read<Manifest>

    let updaterProcsShouldComplete () = 
        let shouldExit (proc : Process) =
            let exited = 
                if Debugger.IsAttached then 
                    proc.WaitForExit() 
                    true
                else 
                    proc.WaitForExit(1000)
            if not exited then proc.Kill()
            exited |> should equal true

        let currentTestUpdater (proc : Process) =
            let appDirUri = Uri(if appDir.EndsWith(@"\") then appDir else appDir + @"\")
            if not proc.HasExited then
                try
                    Some proc.MainModule.FileName
                with :? InvalidOperationException -> None
                |> function 
                | Some filename -> let relPath = appDirUri.MakeRelativeUri(filename |> Uri).ToString()
                                   not (relPath.Contains "..")
                | None -> false
            else
                false

        let rec check iter =
            Process.GetProcessesByName("updater")
            |> Seq.filter currentTestUpdater
            |> Seq.map shouldExit
            |> Seq.length
            |> function 
                | n when n > 0 -> 
                    System.Threading.Thread.Sleep(500)
                    check (iter + 1)
                | _ -> ()
        check 0

    let executeFor (version : string option) (updater : Updater) =
        version |> updater.Execute |> updaterProcsShouldComplete

    let execute = executeFor None

    let updateOnly () =
        let tmp = updater.SkipLaunch
        updater.SkipLaunch <- true
        updater |> execute
        updater.SkipLaunch <- tmp

    [<Fact>]
    let ``install updater, updater-config, tool and app`` () =
        appManifest |> publishManifest "template"
        [ genUpdaterPkg "0.1.0" 
          genUpdaterConfigPkg "1.0.0" 
          genToolsPkg "1.0"
          genAppPkg "1.0.0" ]
        |> publish

        updater |> execute

        appDir @@ "app1-1.0.0" @@ "result.txt" |> readText |> should equal "1.0.0"
        appDir @@ "app1-1.0.0" @@ "tools.txt" |> readText |> should equal "1.0"
        appDir @@ "updater-0.1.0" @@ "updater.txt" |> readText |> should equal "0.1.0"
        appDir @@ "updater-0.1.0" @@ "updater-config.txt" |> readText |> should equal "1.0.0"

        match appDir @@ "app1-1.0.0.manifest.json" |> read<Manifest> with
        | { shortcuts = [ { parentDir = Some parentDir; name = name } ]; launch = { target = target } } -> 
            let { IconLocation=icon } = parentDir @@ name @! ".lnk" |> readShortcut
            icon |> should equal (appDir @@ target + ",0")
        | _ -> failwith "Unexpected manifest file"

    [<Fact>]
    let ``update main app`` () =
        publishV1() |> updateOnly

        [ genAppPkg "1.0.1" ] |> publish
        updater |> execute

        appDir @@ "app1-1.0.1" @@ "result.txt" |> readText |> should equal "1.0.1"

    [<Fact>]
    let ``update main app and updater`` () =
        publishV1() |> updateOnly

        [ genUpdaterPkg "0.2.0"; genAppPkg "1.0.1" ] |> publish
        updater |> execute

        appDir @@ "app1-1.0.1" @@ "result.txt" |> readText |> should equal "1.0.1"
        appDir @@ "updater-0.2.0" @@ "updater.txt" |> readText |> should equal "0.2.0"
        appDir @@ "updater-0.2.0" @@ "updater-config.txt" |> readText |> should equal "1.0.0"

    [<Fact>]
    let ``update main app, but launch prev`` () =
        publishV1() |> updateOnly
        [ genAppPkg "1.0.1" ] |> publish |> updateOnly

        updater |> executeFor (Some "app1-1.0.0")

        appDir @@ "app1-1.0.0" @@ "result.txt" |> readText |> should equal "1.0.0"
        appDir @@ "app1-1.0.1" @@ "result.txt" |> File.Exists |> should equal false

    [<Fact>]
    let ``update main app and tools`` () =
        publishV1() |> updateOnly

        [ genToolsPkg "1.1"; genAppPkg "1.1.2" ] |> publish
        updater |> execute

        appDir @@ "app1-1.1.2" @@ "result.txt" |> readText |> should equal "1.1.2"
        appDir @@ "app1-1.1.2" @@ "tools.txt" |> readText |> should equal "1.1"
        userPrompts |> should equal 1

    [<Fact>]
    let ``update updater`` () =
        publishV1() |> updateOnly

        [ genUpdaterPkg "0.2.0" ] |> publish
        updater |> execute

        appDir @@ "updater-0.2.0" @@ "updater.txt" |> readText |> should equal "0.2.0"
        appDir @@ "updater-0.2.0" @@ "updater-config.txt" |> readText |> should equal "1.0.0"

    [<Fact>]
    let ``update updater and updater-config`` () =
        publishV1() |> updateOnly

        [ genUpdaterPkg "0.3.0"; genUpdaterConfigPkg "1.0.1" ] |> publish
        updater |> execute

        appDir @@ "updater-0.3.0" @@ "updater.txt" |> readText |> should equal "0.3.0"
        appDir @@ "updater-0.3.0" @@ "updater-config.txt" |> readText |> should equal "1.0.1"
        userPrompts |> should equal 0

    [<Fact>]
    let ``update updater-config only`` () =
        publishV1() |> updateOnly

        [ genUpdaterConfigPkg "1.0.1" ] |> publish
        updater |> execute

        appDir @@ "updater-0.1.0-d0+" @@ "updater.txt" |> readText |> should equal "0.1.0"
        appDir @@ "updater-0.1.0-d0+" @@ "updater-config.txt" |> readText |> should equal "1.0.1"
        readCurrentManifest().app.version |> should equal "1.0.0"
        userPrompts |> should equal 0

    [<Fact>]
    let ``updater on entry to forward to the latest updater`` () =
        publishV1() |> updateOnly
        [ genUpdaterPkg "0.3.0" ] |> publish |> updateOnly
        File.Copy(repoDir @@ "app1.version.txt", repoDir @@ "broken.version.txt")
        [ genAppPkg "1.0.1" ] |> publish

        let config = { config with versionUrl = "broken.version.txt" }
        let client = repoClient config.repoUrl config.versionUrl
        let brokenUpdater = Updater(config, client, testUI, Args="--test-mode --skip-cleanup", SkipCleanUp=true)
        brokenUpdater |> execute

        appDir @@ "app1-1.0.1" @@ "result.txt" |> readText |> should equal "1.0.1"
        userPrompts |> should equal 0

    [<Fact>]
    let ``updater on entry skips forward if the latest updater has prior version`` () =
        publishV1() |> updateOnly
        [ genUpdaterPkg "0.3.0" ] |> publish |> updateOnly
        File.Copy(repoDir @@ "app1.version.txt", repoDir @@ "broken.version.txt")
        [ genAppPkg "1.0.1" ] |> publish

        let config = { config with versionUrl = "broken.version.txt" }
        let client = repoClient config.repoUrl config.versionUrl
        let brokenUpdater = Updater(config, client, testUI, Args="--test-mode --skip-cleanup", SkipCleanUp=true)
        brokenUpdater.UpdaterVersion <- 0, 3, 1
        brokenUpdater |> execute

        appDir @@ "app1-1.0.0" @@ "result.txt" |> readText |> should equal "1.0.0"
        userPrompts |> should equal 0

    [<Fact>]
    let ``cleanup old package dirs and shortcuts`` () =
        publishV1() |> updateOnly
        [ genAppPkg "1.0.1"; genToolsPkg "1.1" ] |> publish |> updateOnly
        appDir @@ "test~" |> Directory.CreateDirectory |> ignore
        save (appDir @@ "test.txt~") "test!!!" 

        updater.SkipCleanUp <- false
        [ genAppPkg "1.0.2" ] |> publish |> updateOnly

        appDir @@ "app1-1.0.0" |> Directory.Exists |> should equal false
        appDir @@ "tools-1.0" |> Directory.Exists |> should equal false
        appDir @@ "app1-1.0.0.manifest.json" |> File.Exists |> should equal false
        testDir @@ "user" @@ "desktop" @@ "App1-1.0.0.lnk" |> File.Exists |> should equal false
        appDir @@ "test~" |> Directory.Exists |> should equal false
        appDir @@ "test.txt~" |> File.Exists |> should equal false


    interface IClassFixture<TestDirFixture>

    interface IDisposable with
        member __.Dispose () = updaterProcsShouldComplete ()