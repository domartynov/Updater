namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Updater.Publish")>]
[<assembly: AssemblyProductAttribute("Updater")>]
[<assembly: AssemblyCompanyAttribute("Dzmitry Martynau")>]
[<assembly: AssemblyDescriptionAttribute("Deploy, update and publish tools for Windows client applications.")>]
[<assembly: AssemblyVersionAttribute("0.2.10")>]
[<assembly: AssemblyFileVersionAttribute("0.2.10")>]
[<assembly: AssemblyInformationalVersionAttribute("0.2.10")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.2.10"
    let [<Literal>] InformationalVersion = "0.2.10"
