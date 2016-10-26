namespace Updater.Model

type Config = 
    { appUid : string option
      appName : string
      appDir : string
      repoUrl : string
      versionUrl: string
      keepVersions : int
      cleanupUpdaters: bool }

type Manifest = 
    { app : App
      pkgs : Packages
      layout : Layout
      shortcuts : Shortcut list
      launch : Launch }
and App = 
    { name : string
      title: string
      version : string
      channel : string
      desc : string option }
and Packages = Map<string, string>
and Dependency = 
    { pkg : string
      from : string option
      ``to`` : string option
      parent: string option }
and Layout = 
    { main : string
      deps : Dependency list }
and Shortcut = 
    { name : string
      target : string 
      args: string option
      workDir: string option
      parentDir: string option
      icon: string option }
and Launch = 
    { target : string 
      args : string option
      workDir: string option
      expectExitCodes: list<int> option}

type IRepoClient = 
    abstract GetVersion : unit -> string
    abstract GetManifest : version:string -> string
    abstract DownloadPackage : name:string * path:string * progress:Progress -> Async<unit>
and Progress = int * int -> unit

type IUI =
    abstract ConfirmUpdate: unit -> bool
    abstract ReportError: exn -> unit
    abstract ReportWaitForAnotherUpdater: unit -> unit

[<RequireQualifiedAccess>]
module DuplicateName =
    let re = System.Text.RegularExpressions.Regex("-d(?<dup>\d+)\+")
    
    let next name =
        re.Match name |> function
        | m when m.Success -> name.Substring(0, m.Groups.[0].Index), (int m.Groups.["dup"].Value) + 1
        | _ -> name, 0

    let baseName = next >> fst

    let format (name, dup) = sprintf "%s-d%d+" name dup
