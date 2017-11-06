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
        let value () = 
            int (float maxProgressValue * tracker.Value)

        let form = new Form(Text="Updater", ClientSize=Size(601, 61), AutoScaleDimensions=SizeF(6.0F, 13.0F), AutoScaleMode=AutoScaleMode.Font, ShowIcon=false)
        form.Closed.Add (fun _ -> Async.CancelDefaultToken())
        form.SuspendLayout()

        let label1 = new Label(Text="Progress:", AutoSize=true)
        let btnCancel = new Button(Text="Cancel", UseVisualStyleBackColor=true)
        btnCancel.Click.Add (fun _ -> Async.CancelDefaultToken())
        let progressBar = new ProgressBar(Minimum=0, Maximum=maxProgressValue, Value=value(), MarqueeAnimationSpeed=100)
        let timer = new Timer(Enabled=true, Interval=200)
        updateProgress <- fun _ -> if not progressBar.IsDisposed then progressBar.Value <- value()
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

    interface IUI with
        member __.ReportProgress pt = 
            async { 
                do! switchToUI()
                showProgressForm pt |> ignore 
            } |> Async.Start

            let postUpdate () = 
                if ctx.Task.IsCompleted then 
                    ctx.Task.Result.Post (postUpdateProgress, null)
            postUpdate

        member __.ConfirmUpdate () =
            async {
                do! switchToUI()
                return DialogResult.Yes = MessageBox.Show("Do you want to install a new version?", "Updater", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1)
            } |> Async.RunSynchronously

        member __.ReportError ex = 
            async {
                do! switchToUI()
                reportError ex
            } |> Async.RunSynchronously
            
        member __.ReportWaitForAnotherUpdater () = ()

        member __.Run a = 
            let mutable r = 0
            async {
                try
                    do! a
                    do! switchToUI()
                    Application.Exit()
                with ex ->
                    do! switchToUI()
                    r <- 1
                    reportError ex
                    Application.Exit()
            } |> Async.Start
            
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(false)
            let foo = new Form(Visible=false, WindowState=System.Windows.Forms.FormWindowState.Minimized, ShowIcon=false)

            foo.Load.Add (fun _ -> 
                ctx.SetResult System.Threading.SynchronizationContext.Current
                foo.Close())
            foo.Show()
            Application.Run()
            r
