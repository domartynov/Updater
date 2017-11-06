namespace Updater.Tests

open Xunit
open FsUnit.Xunit
open Updater

type ProgressTests() =
    let pt = ProgressTracker()

    [<Fact>]
    let ``progress simple scenario`` () =
        pt.Value |> should equal 0.0
        pt.Expand 99 |> ignore
        pt.Value |> should equal 0.0
        pt.Advance 10 |> ignore
        pt.Value |> should equal 0.1
        pt.Advance 40 |> ignore
        pt.Value |> should equal 0.5
        pt.Advance 50 |> ignore
        pt.Value |> should equal 1.0
