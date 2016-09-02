﻿namespace System
open System.Reflection

[<assembly: AssemblyTitleAttribute("Updater")>]
[<assembly: AssemblyProductAttribute("Updater")>]
[<assembly: AssemblyCompanyAttribute("Dzmitry Martynau")>]
[<assembly: AssemblyDescriptionAttribute("Deploy, update and publish tools for Windows client applications.")>]
[<assembly: AssemblyVersionAttribute("0.1.4")>]
[<assembly: AssemblyFileVersionAttribute("0.1.4")>]
[<assembly: AssemblyInformationalVersionAttribute("0.1.4")>]
do ()

module internal AssemblyVersionInformation =
    let [<Literal>] Version = "0.1.4"
    let [<Literal>] InformationalVersion = "0.1.4"
