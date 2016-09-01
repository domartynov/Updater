#r @"packages/build/FAKE/tools/FakeLib.dll"

open Fake
open Fake.Git
open Fake.AssemblyInfoFile
open Fake.ReleaseNotesHelper
open Fake.UserInputHelper
open System
open System.IO
open Fake.Testing.XUnit2

let project = "Updater"
let summary = "Deploy, update and publish tools for Windows client applications."
let description = "A dependency manager for .NET with support for NuGet packages and git repositories."

let authors = [ "Dzmitry Martynau" ]
let tags = "windows, updater, install, deploy, publish, F#"

let solutionFile  = "Updater.sln"
let testAssemblies = "tests/**/bin/Release/*Tests*.dll"

let gitOwner = "domartynov"
let gitHome = "https://github.com/" + gitOwner
let gitName = "Updater"
let gitRaw = environVarOrDefault "gitRaw" "https://raw.github.com/domartynov"

let buildDir = "bin"
let tempDir = "temp"
let buildMergedDir = buildDir @@ "merged"

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

// Read additional information from the release notes document
let releaseNotesData = 
    File.ReadAllLines "RELEASE_NOTES.md"
    |> parseAllReleaseNotes
let release = List.head releaseNotesData
let stable = 
    match releaseNotesData |> List.tryFind (fun r -> r.NugetVersion.Contains("-") |> not) with
    | Some stable -> stable
    | _ -> release

let genFSAssemblyInfo (projectPath) =
    let projectName = System.IO.Path.GetFileNameWithoutExtension(projectPath)
    let folderName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(projectPath))
    let basePath = "src" @@ folderName
    let fileName = basePath @@ "AssemblyInfo.fs"
    CreateFSharpAssemblyInfo fileName
      [ Attribute.Title (projectName)
        Attribute.Product project
        Attribute.Company (authors |> String.concat ", ")
        Attribute.Description summary
        Attribute.Version release.AssemblyVersion
        Attribute.FileVersion release.AssemblyVersion
        Attribute.InformationalVersion release.NugetVersion ]

// Generate assembly info files with the right version & up-to-date information
Target "AssemblyInfo" (fun _ ->
    let fsProjs =  !! "src/**/*.fsproj"
    fsProjs |> Seq.iter genFSAssemblyInfo
)

// --------------------------------------------------------------------------------------
// Clean build results
Target "Clean" (fun _ ->
    CleanDirs [buildDir; tempDir]
)

// --------------------------------------------------------------------------------------
// Build library & test project
Target "Build" (fun _ ->
    !! solutionFile
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner
Target "RunTests" (fun _ ->
    !! testAssemblies
    |> xUnit2 (fun p ->
        { p with
            ShadowCopy = false
            WorkingDir = None // Some "tests/Updater.Tests"
            ExcludeTraits = ["Category", "Integration"]
            TimeOut = TimeSpan.FromMinutes 20. })
)

Target "RunIntegrationTests" (fun _ ->
    !! testAssemblies
    |> xUnit2 (fun p ->
        { p with
            ShadowCopy = false
            WorkingDir = Some "tests/Updater.Tests"
            IncludeTraits = ["Category", "Integration"]
            TimeOut = TimeSpan.FromMinutes 40. })
)

// --------------------------------------------------------------------------------------
// Build a NuGet package
let mergeUpdaterLibs = ["updater.exe"; "FSharp.Core.dll"; "Newtonsoft.Json.dll"]
let mergePublisherLibs = ["publisher.exe"; "Argu.dll"] @ mergeUpdaterLibs 

let mergeLibs libs =
    let toPack = libs |> List.map (fun l -> buildDir @@ l) |> separated " "
    let result =
        ExecProcess (fun info ->
            info.FileName <- currentDirectory </> "packages" </> "build" </> "ILRepack" </> "tools" </> "ILRepack.exe"
            info.Arguments <- sprintf "/lib:%s /ver:%s /out:%s %s" buildDir release.AssemblyVersion (buildMergedDir @@ List.head libs) toPack
            ) (TimeSpan.FromMinutes 5.)
    if result <> 0 then failwithf "Error during ILRepack execution."

Target "MergeToolsExe" (fun _ ->
    CreateDir buildMergedDir
    mergeUpdaterLibs |> mergeLibs
    mergePublisherLibs |> mergeLibs
)

//Target "NuGet" (fun _ ->    
//    !! "integrationtests/**/paket.template" |> Seq.iter DeleteFile
//    Paket.Pack (fun p -> 
//        { p with 
//            ToolPath = "bin/merged/paket.exe" 
//            Version = release.NugetVersion
//            ReleaseNotes = toLines release.Notes })
//)
//
//
//Target "PublishNuGet" (fun _ ->
//    if hasBuildParam "PublishBootstrapper" |> not then
//        !! (tempDir </> "*bootstrapper*")
//        |> Seq.iter File.Delete
//
//    Paket.Push (fun p -> 
//        { p with 
//            ToolPath = "bin/merged/paket.exe"
//            WorkingDir = tempDir }) 
//)

#load "paket-files/build/fsharp/FAKE/modules/Octokit/Octokit.fsx"
open Octokit

let getInput paramName getter prompt =
    match getBuildParam paramName with
        | s when not (String.IsNullOrWhiteSpace s) -> s
        | _ -> getter prompt

Target "ReleaseGitHub" (fun _ ->
    let user =  getInput "github-user" getUserInput "Username: "
    let pw = getInput "github-pw" getUserPassword "Password: "
    let remote =
        Git.CommandHelper.getGitResult "" "remote -v"
        |> Seq.filter (fun (s: string) -> s.EndsWith("(push)"))
        |> Seq.tryFind (fun (s: string) -> s.Contains(gitOwner + "/" + gitName))
        |> function None -> gitHome + "/" + gitName | Some (s: string) -> s.Split().[0]

    StageAll ""
    Git.Commit.Commit "" (sprintf "Bump version to %s" release.NugetVersion)
    Branches.pushBranch "" remote (Information.getBranchName "")

    Branches.tag "" release.NugetVersion
    Branches.pushTag "" remote release.NugetVersion
    
    // release on github
    createClient user pw
    |> createDraft gitOwner gitName release.NugetVersion (release.SemVer.PreRelease <> None) release.Notes 
    |> uploadFile "./bin/merged/updater.exe"
    |> uploadFile "./bin/merged/publisher.exe"
    |> releaseDraft
    |> Async.RunSynchronously
)

Target "Release" DoNothing
Target "All" DoNothing

"Clean"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "RunTests"
  ==> "All"

"All"
  =?> ("RunIntegrationTests", hasBuildParam "RunIntegrationTests")
  ==> "MergeToolsExe" 
  ==> "ReleaseGitHub"

"ReleaseGitHub"
  ==> "Release"

RunTargetOrDefault "All"
