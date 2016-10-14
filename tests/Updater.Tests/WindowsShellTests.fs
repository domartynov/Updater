namespace Updater.Tests

open System
open System.IO

open Xunit
open FsUnit.Xunit

open Updater
open Updater.WindowsShell


type WindowsShellTests (testDirFixture : TestDirFixture) =
    let testDir = testDirFixture.MakeDir<WindowsShellTests>()
    let path = testDir @@ "test.lnk"
    let target = testDir @@ "test.txt"
    let target2 = testDir @@ "test2.txt"
    let defaultShellLink =         
        { TargetPath = target
          Arguments = ""
          WorkingDir = testDir
          Description = "test desc"
          IconLocation = "notepad.exe,0" }

    do 
        "Hello!" |> save target
        "Hello2!" |> save target2

    [<Fact>]
    let ``create a shortcut`` () =
        defaultShellLink |> createShortcut path
        readShortcut path |> should equal defaultShellLink

    [<Fact>]
    let ``update a shortcut`` () =
        defaultShellLink |> createShortcut path
        let s2 = { defaultShellLink with TargetPath = target2 }
        s2 |> createShortcut path
        readShortcut path |> should equal s2

    interface IClassFixture<TestDirFixture>

