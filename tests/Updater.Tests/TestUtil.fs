namespace Updater.Tests

open System
open System.Threading

open Xunit
open FsUnit.Xunit


open Updater

type TestDirFixture () =
    let mutable i = 0
    let binDir = binDir ()

    member __.MakeDir name =
        let dirName = sprintf "%s-%s-%d" name (DateTime.Now.ToString "yyMMdd_HHmmss") (Interlocked.Increment &i)
        binDir @@ "testTemp" @@  dirName |> makeDir

    member self.MakeDir<'T> () = typeof<'T>.Name |> self.MakeDir  

    member self.MakeDirFor (o : obj) = o.GetType().Name |> self.MakeDir

    member __.BinDir = binDir


type SplitArgsTests () =
    [<Fact>]
    let ``one arg`` () =
       "test" |> splitArgs |> should equal [ "test" ]
    
    [<Fact>]
    let ``multiple args`` () =
       "a1 1 b" |> splitArgs |> should equal [ "a1";  "1";  "b" ]
    
    [<Fact>]
    let ``args double space delim`` () =
        "a  b" |> splitArgs |> should equal [ "a"; "b" ]

    [<Fact>]
    let ``quoted args`` () =
        "\"test\" \"a b\" 1" |> splitArgs |> should equal [ "test"; "a b"; "1" ]

    