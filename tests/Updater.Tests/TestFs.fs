namespace Updater.Tests

open Updater
open Fs
open System.IO

open Xunit
open FsUnit.Xunit

type FsTests (testDirFixture : TestDirFixture) as test =
    let testDir = testDirFixture.MakeDirFor test

    let setupFolder (entries: string list) =
        for e in entries do
            if e.EndsWith(@"\") then Directory.CreateDirectory(testDir @@ e.Substring(0, e.Length - 1)) |> ignore
            else 
                let dir = testDir @@ (Path.GetDirectoryName e) |> makeDir 
                let name = Path.GetFileName e
                File.WriteAllText(dir @@ name, name)

    let walkPkg dir = walk 1 (testDir @@ dir)

    let exec (path: string) = 
        let p = System.Diagnostics.Process.Start(path)
        { new System.IDisposable with member ___.Dispose() = try p.Kill() with _ -> () }

    [<Fact>]
    let ``test move`` () =
        let dir1 = testDir @@ "dir1" |> makeDir
        let dir2 = testDir @@ "dir2"
        "test" |> save (dir1 @@ "test.txt")
        move dir1 dir2
        dir1 |> Directory.Exists |> should equal false
        dir2 @@ "test.txt" |> File.Exists |> should equal true

    [<Fact>]
    let ``test move locked temp dir copies instead`` () =
        let dir1 = testDir @@ "dir1~" |> makeDir
        let dir2 = testDir @@ "dir2"
        "test" |> save (dir1 @@ "test.txt")
        use lock = File.OpenRead (dir1 @@ "test.txt")
        move dir1 dir2
        dir1 @@ "test.txt" |> File.Exists |> should equal true
        dir2 @@ "test.txt" |> File.Exists |> should equal true

    [<Fact>]
    let ``layout secondary pkg in main pkg using walk and copy`` () = 
        setupFolder [
            @"p1\p1.txt"
            @"p2\p2.txt"
            @"p3\p\p3.txt"
            @"p3\p\a\p4.txt"
            @"app\1.txt"
            @"app\p1.txt"
            @"app\lib\2.txt"
            @"app\lib\p2.txt"
            @"app\p\3.txt"
            @"app\p\p3.txt"
            @"app\p\a\p4.txt"
        ]

        let entries = 
            [ "p1", ""
              "p2", "lib" 
              "p3", "" ]
            |> Seq.map (fun (pkg, ``to``) -> dip (walk 1 (testDir @@ pkg)) ``to``)
            |> Seq.fold merge Map.empty

        copy ignore (testDir @@ "app") (testDir @@ "app2") entries
        
        walk -1 (testDir @@ "app2")
        |> should equal (Map [ "1.txt", WalkEntry
                               "p", WalkDirEntries  (Map [ "3.txt", WalkEntry ]) 
                               "lib", WalkDirEntries  (Map [ "2.txt", WalkEntry ]) ])

    [<Fact>]
    let ``deleteFile return true`` () =
        setupFolder [ @"tmp\"
                      @"1.txt" ]

        deleteFile (testDir @@ "tmp") (testDir @@ "1.txt") |> should equal true
        testDir @@ "1.txt" |> File.Exists |> should equal false

    let setupSomeApp path =
        let someAppBinDir = binDir() @@ @"..\..\..\SomeApp\bin\"
        let someAppExe = Directory.EnumerateFiles(someAppBinDir, "SomeApp.exe", SearchOption.AllDirectories) |> Seq.head
        File.Copy(someAppExe, path)
        

    [<Fact>]
    let ``deleteFile moves to tmpDir if file is locked`` () = 
        setupFolder [ @"tmp\"
                      @"a1\" 
                      @"a2\"
                      @"a3\" ]
        setupSomeApp (testDir @@ @"a1\app.exe")
        HardLink.createHardLink (testDir @@ @"a2\app.exe") (testDir @@ @"a1\app.exe")
        HardLink.createHardLink (testDir @@ @"a3\app.exe") (testDir @@ @"a1\app.exe")
        use p = exec (testDir @@ @"a3\app.exe") 

        deleteFile (testDir @@ "tmp") (testDir @@ @"a1\app.exe") |> should equal true
        deleteFile (testDir @@ "tmp") (testDir @@ @"a2\app.exe") |> should equal true
        testDir @@ @"tmp\app.exe" |> File.Exists |> should equal true
        testDir @@ @"tmp\app.exe~" |> File.Exists |> should equal true
        testDir @@ @"a1\app.exe" |> File.Exists |> should equal false
        testDir @@ @"a1\app.exe" |> File.Exists |> should equal false

    [<Fact>]
    let ``deleteDir return true`` () =
        setupFolder [
            @"tmp\"
            @"1\"
        ]

        deleteDir (testDir @@ "tmp") (testDir @@ "1") |> should equal true
        testDir @@ "1" |> Directory.Exists |> should equal false
    
    [<Fact>]
    let ``deleteDir falls back to deleteFile and deleteDir for each children entry`` () =
        setupFolder [ @"tmp\"
                      @"a1\"
                      @"a1\1.txt"
                      @"a1\b1\2.txt"
                      @"a2\" ]
        setupSomeApp (testDir @@ @"a1\app.exe")
        HardLink.createHardLink (testDir @@ @"a2\app.exe") (testDir @@ @"a1\app.exe")
        use p = exec (testDir @@ @"a2\app.exe") 
         
        deleteDir (testDir @@ "tmp") (testDir @@ @"a1\") |> should equal true
        testDir @@ @"tmp\app.exe" |> File.Exists |> should equal true
        testDir @@ @"a1\" |> Directory.Exists |> should equal false
        
        
    interface IClassFixture<TestDirFixture>

