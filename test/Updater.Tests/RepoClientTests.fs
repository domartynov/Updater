module RepoClientTests

open System
open System.IO
open System.IO.Compression
open System.Text
open System.Text.RegularExpressions
open System.Diagnostics
open System.Threading.Tasks
open Xunit
open FsUnit.Xunit
open Updater
open Updater.RepoClient

// TODO: review that IIS Express is the simplest dependency
type HttpRepoFixture () =
    let binDir = binDir ()
    let testDir = binDir @@ "testTemp\\HttpRepoTests" @@ DateTime.Now.ToString("yyMMdd_HHmmss")
    let repoDir = testDir @@ "repo"

    let waitToStart = 5000
    let waitToStop = 5000
    let iisexpress = new Process()
    let iisexpressOutput = StringBuilder()

    do 
        let programfilesDir = (if Environment.Is64BitOperatingSystem then "programfiles(x86)" else "programfiles") |> Environment.GetEnvironmentVariable
        let iisexpressExe = programfilesDir @@ "IIS Express\\iisexpress.exe"
        let psi = ProcessStartInfo(iisexpressExe, sprintf "/path:\"%s\"" (makeDir repoDir))    
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardInput <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- false
        iisexpress.StartInfo <- psi
        iisexpress.Start() |> should equal true

        let tcs = TaskCompletionSource<unit>()
        iisexpress.OutputDataReceived.Add(fun e -> 
            if not (isNull e.Data) then
                iisexpressOutput.AppendLine e.Data |> ignore
                if e.Data.Contains("IIS Express is running") then tcs.SetResult(()))
        iisexpress.BeginOutputReadLine()
        tcs.Task.Wait(waitToStart) |> should equal true

    let stop () =
        if not iisexpress.HasExited then
            async {
                iisexpress.StandardInput.Write("Q")
                iisexpress.StandardInput.Flush()
            } |> Async.Start
            if not (iisexpress.WaitForExit waitToStop) then iisexpress.Kill()

    let repoUrl = lazy Regex.Match(iisexpressOutput.ToString(), "(?<=Successfully registered URL \")[^\"]+(?=\")").Value
    member __.RepoUrl = repoUrl.Force()
    member __.RepoDir = repoDir
    member __.ServerOutput = iisexpressOutput.ToString()

    interface IDisposable with
        member __.Dispose() = stop ()


type HttpRepoClientTests (server : HttpRepoFixture) =
    let client = repoClient server.RepoUrl "app1.version.json"

    [<Fact>]
    let ``test GetVersion`` () =
        File.WriteAllText(server.RepoDir @@ "app1.version.json", "app1-1")
        client.GetVersion() |> should equal "app1-1"

    [<Fact>]
    let ``test GetManifest`` () = 
        File.WriteAllText(server.RepoDir @@ "app1-1.manifest.json", "{} // test")
        client.GetManifest "app1-1" |> should equal "{} // test"
        
    [<Fact>] 
    let ``test DownloadPackage`` () =
        let addTestPkg () = 
            use archive = ZipFile.Open(server.RepoDir @@ "app1-1.zip", ZipArchiveMode.Create)
            let entry = archive.CreateEntry("file.txt")
            use stream = entry.Open()
            use writer = new StreamWriter(stream)
            writer.WriteLine("test")
        addTestPkg ()

        let appDir = server.RepoDir @@ "..\\app1" |> Path.GetFullPath
        client.DownloadPackage("app1-1", appDir @@ "app1-1", ignore) |> Async.RunSynchronously
        appDir @@ "app1-1\\file.txt" |> File.ReadAllText |> should equal "test\r\n"   

    interface IClassFixture<HttpRepoFixture>
        
