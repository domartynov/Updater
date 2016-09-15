module Updater.RepoClient

open System
open System.IO.Compression

open Updater
open Updater.Model
open System.Net

let repoClient (repoUrl : string) (versionUrl : string) = 
    let rootUri = Uri(if repoUrl.EndsWith("/") then repoUrl else repoUrl + "/")
    let versionUri = Uri(rootUri, versionUrl)

    match rootUri.Scheme with
    | "file" ->
        let rootPath = rootUri.LocalPath
        let versionPath = versionUri.LocalPath
        { new IRepoClient with
            member __.GetVersion() = 
                (versionPath |> readText).Trim()

            member __.GetManifest(version) = 
                rootPath @@ version @! ".manifest.json" |> readText

            member __.DownloadPackage(name, path, progress) = 
                async {
                    ZipFile.ExtractToDirectory(rootPath @@ name @! ".zip", path)
                    progress(1, 1)
                }
        }
    | "http" | "https" -> 
        let http = new WebClient()
        { new IRepoClient with
            member __.GetVersion() = 
                http.DownloadString(versionUri).Trim()

            member __.GetManifest(version) =
                http.DownloadString(sprintf "%O%s.manifest.json" rootUri version) 

            member __.DownloadPackage(name, path, progress) = 
                async {
                    use stream = http.OpenRead(sprintf "%O%s.zip" rootUri name)
                    use archive = new ZipArchive(stream)
                    archive.ExtractToDirectory(path)
                    progress(1, 1)
                }
        }
    | _ -> 
        failwithf "Not supported repo: %s" repoUrl
