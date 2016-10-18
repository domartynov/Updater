namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Updater.Publish")>]
[<assembly: AssemblyProductAttribute("Updater")>]
[<assembly: AssemblyCompanyAttribute("Dzmitry Martynau")>]
[<assembly: AssemblyDescriptionAttribute("Deploy, update and publish tools for Windows client applications.")>]
[<assembly: AssemblyVersionAttribute("0.2.0")>]
[<assembly: AssemblyFileVersionAttribute("0.2.0")>]
[<assembly: AssemblyInformationalVersionAttribute("0.2.0")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.2.0"
    let [<Literal>] InformationalVersion = "0.2.0"
