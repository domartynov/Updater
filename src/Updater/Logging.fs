namespace Updater

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

