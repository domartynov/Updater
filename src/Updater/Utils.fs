[<AutoOpenAttribute>]
module Updater.Utils

open System

let inline (>>=) ma f = Option.bind f ma
let (|?) = defaultArg

let iff cond f g = if cond then f else g


let ignoreExn f x =
    try
        f x
    with _ -> ()

let trimEnd suffix (str : string)  =
    if str.EndsWith(suffix) then str.Substring(0, str.Length - suffix.Length) else str

let trimStart prefix (str : string) =
    if str.StartsWith(prefix) then str.Substring(prefix.Length) else str

let splitArgs (args : string) = // TODO add regex to handle args in double quotes
    args.Split(' ')
    |> Seq.filter (not << String.IsNullOrWhiteSpace)
    |> Seq.toList

let runningExePath () = 
    match System.Reflection.Assembly.GetEntryAssembly() with
    | null -> "in_unit_test_we_dont_care"  // TODO review
    | entry -> entry.Location
