namespace Updater

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
