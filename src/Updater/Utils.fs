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

let ignore2 _ _ = ()

let trimEnd suffix (str : string)  =
    if str.EndsWith(suffix) then str.Substring(0, str.Length - suffix.Length) else str

let trimStart prefix (str : string) =
    if str.StartsWith(prefix) then str.Substring(prefix.Length) else str

let re = lazy System.Text.RegularExpressions.Regex(@"(?<p>[^""\s]+)(\s+|$)|""(?<p>[^""]*)""(\s+|$)")

let splitArgs (args : string) = 
    re.Value.Matches(args) 
    |> Seq.cast<System.Text.RegularExpressions.Match> 
    |> Seq.map (fun m -> m.Groups.["p"].Value) 
    |> Seq.toList

let runningExePath () = 
    match System.Reflection.Assembly.GetEntryAssembly() with
    | null -> "in_unit_test_we_dont_care"  // TODO review
    | entry -> entry.Location
