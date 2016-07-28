namespace Updater.UpdaterTests

open System
open System.IO
open System.IO.Compression

open Xunit
open FsUnit.Xunit

open Updater.Json
open Updater.Model
open Updater
open Updater.RepoClient
open Updater.ShellLink

type EntryFile =
    | Path of string
    | Text of string

type EntryName = string
type PackageEntry = EntryFile * EntryName

type UpdaterTests() =
    let binDir = binDir ()
    let testDir = binDir @@ "testTemp\\UpdaterTests" @@ DateTime.Now.ToString("yyMMdd_HHmmss")
    let repoDir = testDir @@ "repo"
    let appName = "app1"
    let appDir = testDir @@ appName
    let userDir = testDir @@ "user"

    let addPkg (name:string, items:PackageEntry list) =
        let zipPath = (makeDir repoDir) @@ name @! ".zip"
        use archive = ZipFile.Open(zipPath, ZipArchiveMode.Create)
        for file, name in items do
            match file with
            | Path path -> 
                archive.CreateEntryFromFile(Environment.ExpandEnvironmentVariables path, name) |> ignore
            | Text value -> 
                use stream = archive.CreateEntry(name).Open()
                use writer = new StreamWriter(stream)
                writer.Write(value)

    let fileEntries (dir:string) (names:string list) =
        [for name in names -> Path(dir @@ name), name]

    let config =
        { appDir = appDir 
          repoUrl = Uri(repoDir).AbsoluteUri
          versionUrl = appName + ".version.json"
          keepVersions = 2 }

    let updaterEntries (): PackageEntry list =
        (Text (serialize config), "config.json") :: 
        fileEntries binDir [ "Updater.exe"; "Updater.pdb"; "FSharp.Core.dll"; "Newtonsoft.Json.dll" ]

    let (++) name version = name + "-" + version

    let appManifest updaterVersion version =
        { app = { name = appName
                  title = appName
                  version = version
                  channel = ""
                  desc = None }
          pkgs = Map.ofList [ "updater", "updater" ++ updaterVersion
                              "tool", "tool"
                              appName, appName ++ version ]
          layout = { main = appName
                     deps = [ { pkg = "tool"; from = None; ``to`` = None } ] }
          shortcuts = [ { name = "${app.title}-${app.version}"
                          target = "${pkgs.updater}\\updater.exe"
                          args = Some "${fileName}"
                          workDir = None
                          parentDir = (userDir @@ "desktop") |> makeDir |> Some 
                          icon = None } ]
          launch = { target = sprintf "${pkgs.%s}\\xcopy.exe" appName
                     args = Some "version.txt result.txt* /Y"
                     workDir = None } }

    let updateVersionFile manifestVersion = 
        File.WriteAllText((makeDir repoDir) @@ (appName + ".version.json"), appName ++ manifestVersion)

    let publishUpdaterPkg version =
        addPkg ("updater" ++ version, updaterEntries() @ [ Text("u" + version), "version.txt" ])
        version

    let publishToolPkg () =
        addPkg("tool", (fileEntries "%windir%\\system32" [ "xcopy.exe" ]) @ [ Text("test"), "docs\\test.txt" ])

    let publishAppPkg version =
        addPkg(appName ++ version, [ Text("v" + version), "version.txt" ])
        version

    let publishManifest updaterVersion appVersion manifestVersionOrNone =
        let manifestVersion = defaultArg manifestVersionOrNone appVersion
        let json = appManifest updaterVersion appVersion |> serialize
        File.WriteAllText((makeDir repoDir) @@ (appName ++ manifestVersion)  @! ".manifest.json", json)
        updateVersionFile manifestVersion

    let publishV1 () = 
        publishToolPkg ()
        publishManifest (publishUpdaterPkg "1") (publishAppPkg "1") None

    let testUI =
        { new IUI with
            member __.ConfirmUpdate () = true 
            member __.ReportError (ex) = raise (ex)
        }

    let client = repoClient config.repoUrl config.versionUrl

    [<Fact>]
    let ``first time install`` () =
        publishV1 ()
        let updater = Updater(config, (repoClient config.repoUrl config.versionUrl), testUI)
        updater.Execute Install

        appDir @@ (appName ++ "1") @@ "result.txt" |> File.ReadAllText 
        |> should equal "v1"

    [<Fact>]
    let ``launch installed application`` () =
        publishV1 ()
        let updater = Updater(config, client, testUI)
        updater.Execute (Install, skipLaunch=true)
        updater.Execute (Update "app1-1.manifest.json")

        appDir @@ (appName ++ "1") @@ "result.txt" |> File.ReadAllText 
        |> should equal "v1"

    [<Fact>]
    let ``update updater`` () =
        publishV1 ()
        let updater = Updater(config, client, testUI)
        updater.Execute (Install, skipLaunch=true)
        publishManifest (publishUpdaterPkg "2") "1" (Some "1.1")
        updater.Execute (Update "app1-1.manifest.json", skipLaunch=true)

        match appDir @@ "app1-1.1.manifest.json" |> read<Manifest> with
        | { shortcuts = [ { parentDir = Some parentDir; name = name } ] } -> 
            let (_, targetPath, _, _, _, _) = parentDir @@ name @! ".lnk" |> readShortcut
            targetPath @@ "..\\version.txt" |> File.ReadAllText
            |> should equal "u2"
        | _ -> failwith "Unexpected manifest file"
