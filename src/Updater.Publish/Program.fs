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
    Uri(defaultArg repoUri (Environment.GetEnvironmentVariable "UPDATER_REPO")).LocalPath |> Path.GetFullPath

let locateVersion repo (name: string option)= 
    let locate filename = 
        let path = repo @@ filename
        if not (File.Exists path) then failwithf "Not found version file: %s" path
        path

    match name with
    | Some filename when filename.EndsWith(".version.txt") -> 
        locate filename
    | Some name -> 
        locate (name + ".version.txt")
    | None -> 
        match Directory.EnumerateFiles(repo, "*.version.txt") |> Seq.truncate 2 |> Seq.toList with
        | [filename] -> locate filename
        | [] -> failwith "Not found version file in the repo"
        | _ -> failwith "Ambigous version, found multiple version files. Specify --name explicitly."

let locatePackage path =
    if not (File.Exists path) then failwithf "Not found package: %s" path
    path

let publish repo versionPath packages = 
    let manifestPath version =
        repo @@ version @! ".manifest.json"
    
    let version = (versionPath |> readText).Trim()
    let manifest = version |> manifestPath |> readText |> fromJson<Manifest>
    
    let copyPackage path = 
        let filename = Path.GetFileName path
        let dest = repo @@ filename
        if dest @<>@ path then 
            let tmp = dest @! "~"
            File.Copy(path, tmp)
            if File.Exists dest then File.Delete dest
            File.Move(tmp, dest)
        Path.GetFileNameWithoutExtension filename

    let verDelimRegex = Regex(@"-(?=\d)") 
    let update path =
        let pkg = Path.GetFileNameWithoutExtension path
        let name = pkg |> verDelimRegex.Split |> Array.head
        match Map.tryFind name manifest.pkgs with
        | None -> Choice2Of2 (sprintf "Skipped package %s not found in the %s manifest for: %s" name version path)
        | Some p when p = pkg -> Choice2Of2 (sprintf "Skipped package %s that already exists in the %s manifest for: %s" name version path)
        | _ -> Choice1Of2 (name, path)

    let copied = 
        packages
        |> Seq.map update
        |> Seq.choose (function
            | Choice1Of2 (name, path) -> Some (name, copyPackage path)
            | Choice2Of2 err -> printf "%s" err; None)
        |> Seq.toList

    let duplicatedParentPackages = 
        let copiedPkgs = copied |> List.map fst 
        manifest.layout.deps 
        |> Seq.filter (fun d -> List.contains d.pkg copiedPkgs)
        |> Seq.groupBy (fun d -> d.parent |? manifest.layout.main)
        |> Seq.filter (fun (parent, _) -> not <| List.contains parent copiedPkgs)
        |> Seq.map (fun (parent, _) -> parent, manifest.pkgs.[parent] |> DuplicateName.next |> DuplicateName.format)

    let pkgs = 
        [ manifest.pkgs |> Map.toSeq
          copied |> List.toSeq 
          duplicatedParentPackages ] 
        |> Seq.concat 
        |> Map.ofSeq

    let newVersion = 
        let seed = pkgs.[manifest.layout.main]
        Seq.initInfinite (function 0 -> seed | i -> sprintf "%s-%d" seed i) // TODO review if still needed
        |> Seq.filter (not << File.Exists << manifestPath)
        |> Seq.head

    let mainVersion manifest =
        let getBaseName = DuplicateName.next >> fst
        manifest.pkgs.[manifest.layout.main] 
        |> getBaseName 
        |> verDelimRegex.Split 
        |> Seq.skip 1 
        |> Seq.tryLast
        |? manifest.app.version

    let updateAppVersion = 
        if manifest.app.version = (mainVersion manifest) then 
            fun manifest -> { manifest with app = { manifest.app with version = mainVersion manifest } }
        else id
    
    { manifest with pkgs = pkgs }
    |> updateAppVersion                    
    |> serialize
    |> save (manifestPath newVersion)

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
