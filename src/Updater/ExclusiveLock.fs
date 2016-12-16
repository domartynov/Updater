namespace Updater

module ExcusiveLock = 
    open System
    open System.Threading

    let lockOrWait sharedName =
        let mutex = new Mutex(false, sharedName)

        let tryRelease = ignoreExn mutex.ReleaseMutex
        let tryDispose = ignoreExn mutex.Dispose

        let waitForFunc () =
            try
                if mutex.WaitOne() then tryRelease()
            with :? AbandonedMutexException -> tryRelease()
            tryDispose()
        
        try
            mutex.WaitOne(0, false)
        with :? AbandonedMutexException -> true
        |> function
            | true -> Choice1Of2 { new IDisposable with member __.Dispose() = () |> tryRelease |> tryDispose }
            | false -> Choice2Of2 waitForFunc
