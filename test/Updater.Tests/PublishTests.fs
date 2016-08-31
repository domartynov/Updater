﻿namespace Updater.Tests

open Updater
open Updater.Model
open Updater.Json
open Updater.Publish.Program

open System
open System.IO
open Xunit
open FsUnit.Xunit

type PublishTests () =
    let binDir = binDir ()
    let testDir = binDir @@ "testTemp\\UpdaterTests" @@ DateTime.Now.ToString("yyMMdd_HHmmss")
    let repoDir = testDir @@ "repo" |> makeDir
    let tempDir = testDir @@ "temp" |> makeDir

    let versionPath = repoDir @@ "app1.version.txt"

    let publishV1 () =
        let manifest = 
            { app = { name = "app1"; title = "App 1"; desc = None; channel = ""; version = "1.0" }
              pkgs = Map.ofList ["app1", "app1-1.0"; "updater", "updater-1"]
              layout = { main = "app1"; deps = [] }
              shortcuts = []
              launch = { target = "${pkgs.app1}\\app.exe"; workDir = None; args = None } 
            }
        manifest |> serialize |> save (repoDir @@ "app1-1.0.manifest.json")
        "app1-1.0" |> save versionPath
    
    let genPkg name =
        let path = tempDir @@ name @! ".zip"
        name |> save path
        path    

    [<Fact>]
    let ``publish main package`` () =
        publishV1 ()
        let pkgs = [ "app1-1.1" |> genPkg ] 

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> File.ReadAllText |> should equal "app1-1.1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.1"

    [<Fact>]
    let ``publish secondary package`` () =
        publishV1 ()
        let pkgs = [ "updater-2" |> genPkg ] 

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> File.ReadAllText |> should equal "app1-1.0-1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.0-1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.0"
        manifest.pkgs |> Map.find "updater" |> should equal "updater-2"

    [<Fact>]
    let ``publish main and secondary package`` () =
        publishV1 ()
        let pkgs = [ "app1-1.1"; "updater-2" ] |> List.map genPkg

        publish repoDir versionPath pkgs |> should equal 0

        versionPath |> File.ReadAllText |> should equal "app1-1.1"
        let manifest = read<Manifest> (repoDir @@ "app1-1.1.manifest.json")
        manifest.pkgs |> Map.find "app1" |> should equal "app1-1.1"
        manifest.pkgs |> Map.find "updater" |> should equal "updater-2"

