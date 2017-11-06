namespace Updater

open System.Threading
open Updater.Model

type ProgressTracker() =
    let mutable value = 0.0
    let mutable current = 0
    let mutable total = 1

    [<VolatileField>] 
    let mutable _current = 0
    [<VolatileField>] 
    let mutable _total = 1

    let value () = 
        let c = _current
        let t = _total
        if c > current || t > total then
            let v = 
                if c = t then 
                    1.0
                else
                    (float current + float (c - current) * float (total - current) / float (t - current)) / float total
            value <- max value v // TODO review if needed because of calc error
            current <- c
            total <- t
        value
        |> min 1.0
        |> max 0.0

    member __.Expand  totalInc = Interlocked.Add(&_total, totalInc)
    member __.Advance currentInc = Interlocked.Add(&_current, currentInc)
    member __.Value = value()
    
    interface IProgressTracker with 
        member __.Value = value()



module ProgressTracker = 
    let expand inc (pt : ProgressTracker) = pt.Expand inc |> ignore
    let advance inc (pt : ProgressTracker) = pt.Advance inc |> ignore

    let retDone pt v = pt |> advance 1; v
    
    let expandSeq pt s =
        let buf = s |> Seq.toArray
        pt |> expand buf.Length
        buf |> Seq.ofArray

    let iter pt f items = 
            let buf = items |> Seq.toArray
            pt |> expand buf.Length
            for e in buf do
                f e
                pt |> advance 1

