namespace Updater.Model

type Config = 
    { appDir : string
      repoUrl : string
      versionUrl: string
      keepVersions : int }

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
      ``to`` : string option }
and Layout = 
    { main : string
      deps : Dependency list }
and Shortcut = 
    { name : string
      target : string 
      args: string option
      workDir: string option
      parentDir: string option }
and Launch = 
    { target : string 
      args : string option
      workDir: string option}

type IRepoClient = 
    abstract GetVersion : unit -> string
    abstract GetManifest : version:string -> string
    abstract DownloadPackage : name:string * path:string * progress:Progress -> Async<unit>
and Progress = int * int -> unit

type IUI =
    abstract ConfirmUpdate: unit -> bool
    abstract ReportError: exn -> unit

