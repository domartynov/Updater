namespace Updater

[<AutoOpenAttribute>]
module Helper = 
    open System.IO
    open System

    let inline (>>=) ma f = Option.bind f ma
    let (|?) = defaultArg

    let iff cond f g = if cond then f else g

    let inline (@@) (path1:string) (path2:string) = Path.Combine(path1, path2) 
    let inline (@!) (path:string) (extension:string) = path + extension

    let (@=@) (path1:string) (path2:string) = 
        String.Equals(Path.GetFullPath path1, Path.GetFullPath path2, StringComparison.InvariantCultureIgnoreCase)
    let (@<>@) path1 path2 = not (path1 @=@ path2)

    let inline readText path = File.ReadAllText path

    let binDir () =
        Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath |> Path.GetDirectoryName

    let makeDir dir =
        if not (Directory.Exists dir) then 
            Directory.CreateDirectory dir |> ignore
        dir

    let save path text = 
        File.WriteAllText(path + "~", text)
        if File.Exists path then File.Delete path
        File.Move(path + "~", path)

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


module HardLink =
    open System.Runtime.InteropServices
    open System

    [<DllImport("Kernel32.dll", SetLastError = true, CharSet=CharSet.Auto)>]
    extern bool private CreateHardLink(string lpFileName, string lpExistingFileName, nativeint lpSecurityAttributes)

    let createHardLink linkPath targetPath  = 
        if not <| CreateHardLink(linkPath, targetPath, IntPtr.Zero) then
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error())

module WindowsShell = 
    open System
    open System.Reflection
    open System.Runtime.InteropServices

    [<ComImport; Guid("F935DC23-1CF0-11D0-ADB9-00C04FD58A0B"); TypeLibType(0x1040s)>]
    type private IWshShortcut =
        abstract FullName:string with get
        abstract Arguments:string with get, set
        abstract Description:string with get, set
        abstract Hotkey:string with get, set
        abstract IconLocation:string with get, set
        abstract RelativePath:string with set
        abstract TargetPath:string with get, set
        abstract WindowStyle:int with get, set
        abstract WorkingDirectory:string with get, set
        abstract Load : path:string -> unit
        abstract Save : unit -> unit

    type ShellLink = 
        { TargetPath: string
          Arguments: string
          WorkingDir: string
          Description: string
          IconLocation: string }
   
    let createShortcut (path : string) { TargetPath=targetPath; Arguments=arguments; WorkingDir=workingDir; Description=descripton; IconLocation=iconPath } =
        let shellType = Type.GetTypeFromProgID("WScript.Shell")
        let shellObj = Activator.CreateInstance(shellType)
        let shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shellObj, [| path |]) :?> IWshShortcut
        shortcut.Arguments <- arguments
        shortcut.Description <- descripton
        shortcut.Hotkey <- ""
        shortcut.IconLocation <- iconPath
        shortcut.TargetPath <- targetPath
        shortcut.WorkingDirectory <- workingDir
        shortcut.Save()

    let readShortcut (shortcutPath : string) = 
        let shellType = Type.GetTypeFromProgID("WScript.Shell")
        let shellObj = Activator.CreateInstance(shellType)
        let shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shellObj, [| shortcutPath |]) :?> IWshShortcut
        { TargetPath = shortcut.TargetPath
          Arguments = shortcut.Arguments
          WorkingDir = shortcut.WorkingDirectory
          Description = shortcut.Description
          IconLocation = shortcut.IconLocation }


module Json = 
    open System
    open System.Collections.Generic
    open Microsoft.FSharp.Reflection
    open System.IO
    open Newtonsoft.Json
    open Newtonsoft.Json.Linq
    open System.Text.RegularExpressions

    type ResolvingJTokenReader(root, variables : string -> string option) = 
        inherit JTokenReader(root)

        let resolveEnvVars (str : string) = 
            if str.Contains("%") then Environment.ExpandEnvironmentVariables(str) else str

        let resolver (m:Match) =
            let value = m.Value.Substring(2, m.Value.Length - 3)
            match variables value  with
                | Some v -> v
                | None ->
                    match root.SelectToken(value) with
                        | null -> value
                        | t when t.Type = JTokenType.String -> string t
                        | _ -> value

        let rec resolveVars value = 
            match Regex.Replace(value, "\$\{[^}]+}", resolver) with
            | value' when value' = value -> value'
            | value' -> resolveVars value'
            
        override this.Read() =
            if not <| base.Read() then false
            else
                let token = this.CurrentToken
                if token.Type = JTokenType.String then
                    let value = string token
                    let value' = value |> resolveVars |> resolveEnvVars
                    if value' <> value then
                        base.SetToken(JsonToken.String, value')
                true

    // JsonConverters from http://gorodinski.com/blog/2013/01/05/json-dot-net-type-converters-for-f-option-list-tuple/
    let private serializer = 
        let convertors : JsonConverter list = 
            [ { new JsonConverter() with
                    member __.CanConvert(t) = t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>
                
                    member __.WriteJson(writer, value, serializer) = 
                        let value = 
                            match value with
                            | null -> null
                            | value -> 
                                let _, fields = FSharpValue.GetUnionFields(value, value.GetType())
                                fields.[0]
                        serializer.Serialize(writer, value)
                
                    member __.ReadJson(reader, t, _, serializer) = 
                        let innerType = t.GetGenericArguments().[0]
                    
                        let innerType = 
                            if innerType.IsValueType then (typedefof<Nullable<_>>).MakeGenericType([| innerType |])
                            else innerType
                    
                        let value = serializer.Deserialize(reader, innerType)
                        let cases = FSharpType.GetUnionCases(t)
                        match value with
                        | null -> FSharpValue.MakeUnion(cases.[0], [||])
                        | value -> FSharpValue.MakeUnion(cases.[1], [| value |]) }
              { new JsonConverter() with
                    member __.CanConvert(t : Type) = t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>
                
                    member __.WriteJson(writer, value, serializer) = 
                        let list = value :?> System.Collections.IEnumerable |> Seq.cast
                        serializer.Serialize(writer, list)
                
                    member __.ReadJson(reader, t, _, serializer) = 
                        let itemType = t.GetGenericArguments().[0]
                        let collectionType = typedefof<IEnumerable<_>>.MakeGenericType(itemType)
                        let collection = serializer.Deserialize(reader, collectionType) :?> IEnumerable<_>
                        let listType = typedefof<list<_>>.MakeGenericType(itemType)
                        let cases = FSharpType.GetUnionCases(listType)
                    
                        let rec make = 
                            function 
                            | [] -> FSharpValue.MakeUnion(cases.[0], [||])
                            | head :: tail -> 
                                FSharpValue.MakeUnion(cases.[1], 
                                                      [| head
                                                         (make tail) |])
                        make (collection |> Seq.toList) } ]
    
        let configure (s : JsonSerializerSettings) = 
            convertors |> List.iter s.Converters.Add
            s.NullValueHandling <- NullValueHandling.Ignore
            s.Formatting <- Formatting.Indented
            s
    
        JsonSerializer.Create(JsonSerializerSettings() |> configure)

    let serialize obj = 
        use sw = new StringWriter()
        serializer.Serialize(sw, obj)
        sw.ToString()

    let deserialize<'T> vars str = 
        use jr = new ResolvingJTokenReader(JToken.Parse str, vars)
        serializer.Deserialize(jr, typeof<'T>) :?> 'T

    let pathVars path = function
        | "path" -> path |> Some
        | "parentDir" -> Path.GetDirectoryName path |> Some
        | "fileName" -> Path.GetFileName path |> Some
        | "fullPath" -> Path.GetFullPath path |> Some
        | "DesktopDirectory" -> Environment.GetFolderPath Environment.SpecialFolder.DesktopDirectory |> Some
        | "StartMenu" -> Environment.GetFolderPath Environment.SpecialFolder.StartMenu |> Some
        | "LocalApplicationData" -> Environment.GetFolderPath Environment.SpecialFolder.LocalApplicationData |> Some
        | "ApplicationData" -> Environment.GetFolderPath Environment.SpecialFolder.ApplicationData |> Some
        | _ -> None

    let read<'T> path =
        path
        |> readText
        |> deserialize<'T> (pathVars path)

    let fromJson<'T> str = 
        use sr = new StringReader(str)
        serializer.Deserialize(sr, typeof<'T>) :?> 'T


module Logging =
    open System
    open System.Diagnostics
    open System.IO
    open System.IO.Compression

    type EntryLevel = Info=0 | Warn=1 | Error=2
    type Entry = { Timestamp: System.DateTime; Level: EntryLevel; Message: obj }
    
    let mutable hasErrors = false
    let entries = ResizeArray<Entry>()

    let logEntry level (o : obj) = 
        entries.Add { Timestamp = System.DateTime.Now; Level = level; Message = o }
        if level = EntryLevel.Error && not hasErrors then hasErrors <- true

    let inline logInfo m = logEntry EntryLevel.Info m
    let inline logWarn m = logEntry EntryLevel.Warn m
    let inline logError m = logEntry EntryLevel.Error m

    let logWith level name (value: 'a) : 'a = 
        sprintf "%s: %A" name value |> logEntry level
        value

    let infoAs name (value: 'a) : 'a = logWith EntryLevel.Info name value
    let errorAs name (value: 'a) : 'a = logWith EntryLevel.Error name value

    let tsf (ts : System.DateTime) = ts.ToString("s")

    let dump () =
        let ts = (DateTime.Now |> tsf).Replace(":", "").Replace("-", "")
        let pid = Process.GetCurrentProcess().Id
        let path = Path.GetTempPath() @@ (sprintf "updater-%s-%d.dump.zip" ts pid)
        use arch = ZipFile.Open(path, ZipArchiveMode.Create)
        use stream = arch.CreateEntry("dump.txt").Open()
        use w = new StreamWriter(stream)
        for e in entries do
            w.Write (tsf e.Timestamp)
            w.Write '\t'
            e.Level |> function
            | EntryLevel.Error -> "ERROR"
            | EntryLevel.Warn -> "WARN"
            | EntryLevel.Info -> "INFO"
            | other -> other.ToString()
            |> w.Write
            w.Write '\t'
            w.WriteLine e.Message

    do 
        AppDomain.CurrentDomain.ProcessExit.Add (ignore >> dump)
        AppDomain.CurrentDomain.UnhandledException.Add (fun e -> logError e.ExceptionObject)

module ExcusiveLock = 
    open System
    open System.Threading

    let lockOrWait sharedName =
        let mutex = new Mutex(false, sharedName)

        let tryRelease = ignoreExn mutex.ReleaseMutex
        let tryDispose = ignoreExn mutex.Dispose

        let waitForFunc () =
            try
                if mutex.WaitOne() then tryRelease()
            with :? AbandonedMutexException -> tryRelease()
            tryDispose()
        
        try
            mutex.WaitOne(0, false)
        with :? AbandonedMutexException -> true
        |> function
            | true -> Choice1Of2 { new IDisposable with member __.Dispose() = () |> tryRelease |> tryDispose }
            | false -> Choice2Of2 waitForFunc
