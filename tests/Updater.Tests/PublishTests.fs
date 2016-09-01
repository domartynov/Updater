namespace Updater.Tests

open Updater
open Updater.Model
open Updater.Json
open Updater.Publish.Program

open System.IO
open Xunit
open FsUnit.Xunit


type PublishTests (testDirFixture : TestDirFixture) =
    let testDir = testDirFixture.MakeDir("PublishTests")
    let repoDir = testDir @@ "repo" |> makeDir
    let tempDir = testDir @@ "temp" |> makeDir

    let versionPath = repoDir @@ "app1.version.txt"

    let publishV1 () =
        let manifest = 
            { app = { name = "app1"; title = "App 1"; desc = None; channel = ""; version = "1.0" }
              pkgs = Map.ofList ["app1", "app1-1.0"; "updater", "updater-1"; "some-tool", "some-tool"]
              layout = { main = "app1"; deps = [] }
              shortcuts = []
              launch = { target = "${pkgs.app1}\\app.exe"; workDir = None; args = None } 
            }
        manifest |> serialize |> save (repoDir @@ "app1-1.0.manifest.json")
        "app1-1.0" |> save versionPath
    
    let loadManifest name = 
        repoDir @@ name |> readText |> fromJson<Manifest> 

    let genPkg name =
        let path = tempDir @@ name @! ".zip"
        name |> save path
        path    

    [<Fact>]
    let ``publish main package`` () =
        publishV1 ()
        let pkgs = [ "app1-1.1" |> genPkg ] 

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> readText |> should equal "app1-1.1"
        let manifest = loadManifest "app1-1.1.manifest.json"
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.1"
        manifest.launch.target |> should haveSubstring "${pkgs.app1}"

    [<Fact>]
    let ``publish secondary package`` () =
        publishV1 ()
        let pkgs = [ "updater-2" |> genPkg ] 

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> readText |> should equal "app1-1.0-1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.0-1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.0"
        manifest.pkgs |> Map.find "updater" |> should equal "updater-2"

    [<Fact>]
    let ``publish secondary package with dash in name`` () =
        publishV1 ()
        let pkgs = [ "some-tool-2" |> genPkg ] 

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> readText |> should equal "app1-1.0-1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.0-1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.0"
        manifest.pkgs |> Map.find "some-tool" |> should equal "some-tool-2"

    [<Fact>]
    let ``publish main and secondary package`` () =
        publishV1 ()
        let pkgs = [ "app1-1.1"; "updater-2" ] |> List.map genPkg

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> readText |> should equal "app1-1.1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.1"
        manifest.pkgs |> Map.find "updater" |> should equal "updater-2"

    interface IClassFixture<TestDirFixture>
