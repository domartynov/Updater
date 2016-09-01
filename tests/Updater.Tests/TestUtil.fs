namespace Updater.Tests

open System
open System.IO
open System.Threading

open Updater

type TestDirFixture () =
    let mutable i = 0
    let binDir = binDir ()

    member __.MakeDir name =
        let dirName = sprintf "%s-%s-%d" name (DateTime.Now.ToString "yyMMdd_HHmmss") (Interlocked.Increment &i)
        binDir @@ "testTemp" @@  dirName |> makeDir
