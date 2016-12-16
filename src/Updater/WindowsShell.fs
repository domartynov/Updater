namespace Updater

module WindowsShell = 
    open System
    open System.Reflection
    open System.Runtime.InteropServices

    [<ComImport; Guid("F935DC23-1CF0-11D0-ADB9-00C04FD58A0B"); TypeLibType(0x1040s)>]
    type private IWshShortcut =
        abstract FullName:string with get
        abstract Arguments:string with get, set
        abstract Description:string with get, set
        abstract Hotkey:string with get, set
        abstract IconLocation:string with get, set
        abstract RelativePath:string with set
        abstract TargetPath:string with get, set
        abstract WindowStyle:int with get, set
        abstract WorkingDirectory:string with get, set
        abstract Load : path:string -> unit
        abstract Save : unit -> unit

    type ShellLink = 
        { TargetPath: string
          Arguments: string
          WorkingDir: string
          Description: string
          IconLocation: string }
   
    let createShortcut (path : string) { TargetPath=targetPath; Arguments=arguments; WorkingDir=workingDir; Description=descripton; IconLocation=iconPath } =
        let shellType = Type.GetTypeFromProgID("WScript.Shell")
        let shellObj = Activator.CreateInstance(shellType)
        let shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shellObj, [| path |]) :?> IWshShortcut
        shortcut.Arguments <- arguments
        shortcut.Description <- descripton
        shortcut.Hotkey <- ""
        shortcut.IconLocation <- iconPath
        shortcut.TargetPath <- targetPath
        shortcut.WorkingDirectory <- workingDir
        shortcut.Save()

    let readShortcut (shortcutPath : string) = 
        let shellType = Type.GetTypeFromProgID("WScript.Shell")
        let shellObj = Activator.CreateInstance(shellType)
        let shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shellObj, [| shortcutPath |]) :?> IWshShortcut
        { TargetPath = shortcut.TargetPath
          Arguments = shortcut.Arguments
          WorkingDir = shortcut.WorkingDirectory
          Description = shortcut.Description
          IconLocation = shortcut.IconLocation }

