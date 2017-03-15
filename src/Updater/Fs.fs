namespace Updater

[<AutoOpenAttribute>]
module Helper = 
    open System
    open System.IO

    let inline (@@) p1 p2 = Path.Combine(p1, p2) 
    let inline (@!) (path : string) (extension : string) = path + extension

    let (@=@) (path1 : string) (path2 : string) = 
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


module HardLink =
    open System.Runtime.InteropServices
    open System

    [<DllImport("Kernel32.dll", SetLastError = true, CharSet=CharSet.Auto)>]
    extern bool private CreateHardLink(string lpFileName, string lpExistingFileName, nativeint lpSecurityAttributes)

    let createHardLink linkPath targetPath  = 
        if not <| CreateHardLink(linkPath, targetPath, IntPtr.Zero) then
            Marshal.ThrowExceptionForHR(Marshal.GetHRForLastWin32Error())


module Fs = 
    open System.IO
    open HardLink

    type WalkItem =
        | WalkEntry
        | WalkDirEntries of Map<string, WalkItem>

    let rec walk depth dir = 
        seq {
            for p in Directory.EnumerateFiles dir do yield Path.GetFileName p, WalkEntry
            match depth with 
            | 0 -> for p in Directory.EnumerateDirectories dir -> Path.GetFileName p, WalkEntry
            | _ -> for p in Directory.EnumerateDirectories dir -> Path.GetFileName p, walk (depth - 1) p  |> WalkDirEntries
        }
        |> Map.ofSeq

    let rec dip (entries: Map<string, WalkItem>) (path: string) = 
        match path.TrimEnd('\\') with
        | "" -> entries
        | path ->
            match path.LastIndexOf "\\" with
            | i when i < 0 -> [ path, WalkDirEntries entries ] |> Map
            | i -> 
                let parent, name = path.Substring(0, i), path.Substring(i + 1)
                dip (Map [name , WalkDirEntries entries ]) parent

    let rec merge entries1 entries2 =
        (Map.toList entries1) @ (Map.toList entries2)
        |> List.groupBy fst
        |> Seq.map (function 
            | name, [ _, entries] -> name, entries
            | name, [ _, WalkDirEntries e1; _, WalkDirEntries e2] -> name, WalkDirEntries (merge e1 e2)
            | name, _ -> failwithf "Package config, two pkgs map in the same file: %s" name)
        |> Map

    let copy skipOverwrite src dest (exclude: Map<string, WalkItem>) = 
        let rec copy src dest exclude = 
            if File.Exists src then
                if File.Exists dest || Directory.Exists dest then skipOverwrite dest
                else createHardLink dest src
            elif Directory.Exists src then
                if File.Exists dest then skipOverwrite dest
                else
                    if not (Directory.Exists dest) then Directory.CreateDirectory dest |> ignore
                    for info in DirectoryInfo(src).EnumerateFileSystemInfos() do
                        match Map.tryFind info.Name exclude with
                        | None -> copy (src @@ info.Name) (dest @@ info.Name) Map.empty 
                        | Some (WalkDirEntries entries) -> copy (src @@ info.Name) (dest @@ info.Name) entries
                        | _ -> () 
        copy src dest exclude

    let copyAll src dest = copy ignore src dest Map.empty

    let rec nextTmpDir dir =
        if not (Directory.Exists dir) then dir
        else nextTmpDir (dir + "~")

    let rec nextTmpFile path =
        if not (File.Exists path) then path
        else nextTmpFile (path + "~")

    let private isLockError (ex: IOException) =
        match ex.HResult &&& 0x0000FFFF with
        | 0x0005 -> true // ERROR_ACCESS_DENIED
        | 0x0010 -> true // ERROR_CURRENT_DIRECTORY
        | 0x0020 -> true // ERROR_SHARING_VIOLATION
        | 0x0021 -> true // ERROR_LOCK_VIOLATION
        | _ -> false

    let move src dest =
        try
            Directory.Move (src, dest) 
        with 
            | :? IOException as ex when isLockError ex ->
                copyAll src dest

    let deleteFile tmpDir path = 
        if File.Exists path then 
            try
                File.Delete path 
                true
            with 
                | :? System.UnauthorizedAccessException as ex ->
                    try
                        let tmpPath = nextTmpFile (tmpDir @@ Path.GetFileName path)
                        File.Move(path, tmpPath)
                        true
                    with _ -> false
                | _ -> false
        else
            true

    let rec deleteDir tmpDir path = 
        try
            if Directory.Exists path then Directory.Delete(path, true)
            true
        with _ ->
            (Directory.EnumerateFiles(path) |> Seq.map (deleteFile tmpDir) |> Seq.fold (&&) true) &&
            (Directory.EnumerateDirectories(path) |> Seq.map (deleteDir tmpDir) |> Seq.fold (&&) true)
            |> function
            | true -> 
                try
                    Directory.Delete path
                    true
                with _ -> false
            | _ -> false