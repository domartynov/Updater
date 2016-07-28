namespace Updater

[<AutoOpenAttribute>]
module Helper = 
    open System.IO
    open System

    let inline (@@) (path1:string) (path2:string) = Path.Combine(path1, path2) 
    let inline (@!) (path:string) (extension:string) = path + extension

    let binDir () =
        Uri(System.Reflection.Assembly.GetExecutingAssembly().CodeBase).LocalPath |> Path.GetDirectoryName

    let makeDir dir =
        if not (Directory.Exists dir) then 
            Directory.CreateDirectory dir |> ignore
        dir

    let save path text = 
        File.WriteAllText(path + "~", text)
        File.Move(path + "~", path)
        

module HardLink =
    open System.Runtime.InteropServices
    open System

    [<DllImport("Kernel32.dll", SetLastError = true, CharSet=CharSet.Auto)>]
    extern bool private CreateHardLink(string lpFileName, string lpExistingFileName, nativeint lpSecurityAttributes)

    let createHardLink linkPath targetPath  = 
        if not <| CreateHardLink(linkPath, targetPath, IntPtr.Zero) then
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error())

module ShellLink = 
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
   
    let createShortcut (shortcutPath: string, targetPath: string, arguments: string, workingDir: string, descripton: string, iconPath: string) =
        let shellType = Type.GetTypeFromProgID("WScript.Shell")
        let shellObj = Activator.CreateInstance(shellType)
        let shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shellObj, [| shortcutPath |]) :?> IWshShortcut
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
        (shortcut.FullName, shortcut.TargetPath, shortcut.Arguments, shortcut.WorkingDirectory, shortcut.Description, shortcut.IconLocation)

module Json = 
    open System
    open System.Collections.Generic
    open Microsoft.FSharp.Reflection
    open System.IO
    open Newtonsoft.Json
    open Newtonsoft.Json.Linq
    open System.Text.RegularExpressions

    type ResolvingJTokenReader(root, variables:Map<string, string>) = 
        inherit JTokenReader(root)

        let resolveEnvVar (str : string) = 
            if str.Contains("%") then Environment.ExpandEnvironmentVariables(str) else str

        let resolver (m:Match) =
            let value = m.Value.Substring(2, m.Value.Length - 3)
            match Map.tryFind value variables with
                | Some v -> v
                | None ->
                    match root.SelectToken(value) with
                        | null -> value
                        | t when t.Type = JTokenType.String -> string t
                        | _ -> value
            
        override this.Read() =
            if not <| base.Read() then false
            else
                let token = this.CurrentToken
                if token.Type = JTokenType.String then
                    let value = string token
                    let value' = Regex.Replace(value, "\$\{[^}]+}", resolver) |> resolveEnvVar
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
                            if value = null then null
                            else 
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
                        if value = null then FSharpValue.MakeUnion(cases.[0], [||])
                        else FSharpValue.MakeUnion(cases.[1], [| value |]) }
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

    let pathVars path = 
        Map.ofList [ "path", path
                     "parentDir", Path.GetDirectoryName(path)
                     "fileName", Path.GetFileName(path)
                     "fullPath", Path.GetFullPath(path) ]

    let read<'T> path =
        File.ReadAllText path
        |> deserialize<'T> (pathVars path)
