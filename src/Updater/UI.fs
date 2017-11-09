namespace Updater

open System.Drawing
open System.Windows.Forms
open Updater.Model
open System.Threading.Tasks


[<AutoOpen>]
module WinFormsHelpers = 
    let layout (x, y) (w, h) (control: Control) =
        control.Location <- Point(x, y)
        control.Size <- Size(w, h)
        control


type UI() =
    let maxProgressValue = 1000
    let ctx = TaskCompletionSource<System.Threading.SynchronizationContext>()
    let mutable updateProgress = ignore
    let postUpdateProgress = System.Threading.SendOrPostCallback(fun _ -> System.EventArgs() |> updateProgress)

    let switchToUI () = 
        Async.SwitchToContext ctx.Task.Result

    let showProgressForm (tracker: IProgressTracker) =
        let value () = int (float maxProgressValue * tracker.Value)

        let form = new Form(Text="Updater", ClientSize=Size(601, 61), AutoScaleDimensions=SizeF(6.0F, 13.0F), AutoScaleMode=AutoScaleMode.Font, ShowIcon=false)
        form.Closed.Add (fun _ -> Async.CancelDefaultToken())
        form.SuspendLayout()

        let label1 = new Label(Text="Progress:", AutoSize=true)
        let btnCancel = new Button(Text="Cancel", UseVisualStyleBackColor=true)
        btnCancel.Click.Add (fun _ -> Async.CancelDefaultToken())
        let progressBar = new ProgressBar(Minimum=0, Maximum=maxProgressValue, Value=value(), MarqueeAnimationSpeed=100)
        let timer = new Timer(Enabled=true, Interval=200)
        updateProgress <- fun _ -> 
            if not progressBar.IsDisposed then 
                match value() with
                | v when v = maxProgressValue -> form.Hide()
                | v -> progressBar.Value <- v
        timer.Tick.Add updateProgress
        timer.Start()
        
        [ btnCancel   |> layout (519, 25) (75, 23)
          progressBar |> layout (13, 25) (500, 23)
          label1      |> layout (10, 9) (51, 13) ] 
        |> List.iteri (fun i c -> c.TabIndex <- i; form.Controls.Add c)
        
        form.ResumeLayout(true)
        form.Show()
        form

    let reportError ex =
        MessageBox.Show(ex.ToString(), "Error", MessageBoxButtons.OK, MessageBoxIcon.Error) |> ignore

    let reportProgress pt = 
        async { do! switchToUI()
                showProgressForm pt |> ignore } |> Async.Start
        fun () -> if ctx.Task.IsCompleted then ctx.Task.Result.Post (postUpdateProgress, null)

    let confirmUpdate () = 
        async {
            do! switchToUI()
            return DialogResult.Yes = MessageBox.Show("Do you want to install a new version?", "Updater", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
        } |> Async.RunSynchronously

    let hackToCaptureSyncCtx () =
        let foo = new Form(Visible=false, WindowState=FormWindowState.Minimized, ShowIcon=false)
        foo.Load.Add (fun _ -> 
            ctx.SetResult System.Threading.SynchronizationContext.Current
            foo.Close())
        foo.Show()

    let afterWork (t: Task<unit>) =
        Application.Exit()  // TODO review, should work ok from non-UI thread
        match t.Status with
        | TaskStatus.RanToCompletion -> Choice1Of2 0
        | TaskStatus.Faulted -> Choice2Of2 (t.Exception.GetBaseException())
        | _ -> Choice1Of2 2

    let run a = 
        let t = Async.StartAsTask(a).ContinueWith(afterWork, TaskContinuationOptions.ExecuteSynchronously)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(false)
        hackToCaptureSyncCtx ()
        Application.Run()
        match t.Result with
        | Choice1Of2 r -> r
        | Choice2Of2 e -> reportError e; 1

    interface IUI with
        member __.ReportProgress(pt) = reportProgress pt
        member __.ConfirmUpdate() = confirmUpdate ()
        member __.ReportError ex = reportError ex
        member __.ReportWaitForAnotherUpdater () = ()
        member __.Run a = run a
