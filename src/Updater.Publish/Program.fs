module Updater.Publish.Program

open System
open System.IO
open System.Text.RegularExpressions
open Argu
open Updater
open Updater.Json
open Updater.Model


type CliArgs =
    | Repo of string option
    | [<CliPrefix(CliPrefix.None)>] Publish of ParseResults<PublishArgs>
    | [<CliPrefix(CliPrefix.None)>] Cleanup of ParseResults<CleanupArgs>
with 
    interface IArgParserTemplate with
        member this.Usage =
            match this with
            | Repo _ -> "Repo folder URI, file schema only (i.e. file://<path>)"
            | Publish _ -> "Publish new packages"
            | Cleanup _ -> "Cleanup old packages"
and PublishArgs = 
    | Name of string
    | [<MainCommand>] Files of string list
with 
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Name _ -> "Name of the version file (i.e. name1 points to name1.version.json)"
            | Files _ -> "Packages to copy to the repo, add a new manifest file with the packages, update the version file to the new manifest file"
and CleanupArgs =
    | Versions of int
with 
    interface IArgParserTemplate with
        member this.Usage = 
            match this with
            | Versions _ -> "Clean up all manifests and packages older than specified number of versions"


let locateRepo repoUri =
    Uri(defaultArg repoUri (Environment.GetEnvironmentVariable "UPDATER_REPO")).LocalPath

let locateVersion repo (name: string option)= 
    let locate filename = 
        let path = repo @@ filename
        if not (File.Exists path) then failwithf "Not found version file: %s" path
        path

    match name with
    | Some filename when filename.EndsWith(".version.txt") -> locate filename
    | Some name ->                                            locate (name + ".version.txt")
    | None -> 
        match Directory.EnumerateFiles(repo, "*.version.txt") |> Seq.truncate 2 |> Seq.toList with
        | [filename] -> locate filename
        | [] -> failwith "Not found version file in the repo"
        | _ -> failwith "Ambigous version, multiply version files found. Specify --name explicitly."

let locatePackage path =
    if not (File.Exists path) then failwithf "Not found package: %s" path
    path

let publish repo versionPath packages = 
    let toManifestPath version =
        repo @@ version @! ".manifest.json"
    
    let version = (versionPath |> readText).Trim()
    let manifest = version |> toManifestPath |> readText |> fromJson<Manifest>

    let copyPackage path = 
        let filename = Path.GetFileName path
        let tmp = repo @@ filename @! "~"
        File.Copy(path, tmp)
        File.Move(tmp, repo @@ filename)
        Path.GetFileNameWithoutExtension filename

    let verDelimRegex = Regex(@"-\d+") 
    let parsePackagePath (path:string) =
        path |> Path.GetFileNameWithoutExtension |> verDelimRegex.Split |> Array.head, path
        
    let updated, ignored = 
        packages 
        |> List.map parsePackagePath
        |> List.partition (fun (name, _) -> manifest.pkgs |> Map.containsKey name)
    ignored |> List.iter (fun (name, path) -> printfn "Skipped package %s not found in the %s manifest for: %s" name version path)
    let copied = updated |> Seq.map (fun (name, path) -> name, copyPackage path) 
    let pkgs = [Map.toSeq manifest.pkgs; copied] |> Seq.concat |> Map.ofSeq

    let newVersion = 
        let seed = pkgs |> Map.find manifest.layout.main
        let toVersion = function 0 -> seed | i -> sprintf "%s-%d" seed i
        Seq.initInfinite toVersion
        |> Seq.filter (toManifestPath >> File.Exists >> not)
        |> Seq.head

    { manifest with pkgs = pkgs}
    |> serialize
    |> save (newVersion |> toManifestPath)

    newVersion |> save versionPath
    0

let execute repo = function
    | Publish args ->
        let versionPath = args.TryGetResult <@ Name @> |> locateVersion repo
        let packages = args.PostProcessResult (<@ Files @>, List.map locatePackage)
        publish repo versionPath packages
    | Cleanup args ->
        failwith "Not implemented: Cleanup"
    | x -> failwithf "Not supported: %A" x

[<EntryPoint>]
let main argv = 
    try
        let parser = ArgumentParser.Create<CliArgs>(errorHandler=ProcessExiter())
        let results = parser.Parse argv
        let repo = results.PostProcessResult(<@ Repo @>, locateRepo)
        results.GetSubCommand() |> execute repo
    with
    | ex -> printfn "%s" ex.Message; -1
