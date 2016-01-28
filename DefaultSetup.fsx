#I @"..\..\..\..\packages\build"
#r @"FAKE\tools\FakeLib.dll"
#r @"Chessie\lib\net40\Chessie.dll"
#r @"Paket.Core\lib\net45\Paket.Core.dll"
#r @"Mono.Cecil\lib\net45\Mono.Cecil.dll"
#r "System.IO.Compression.dll"
#r "System.IO.Compression.FileSystem.dll"
#r @"Newtonsoft.Json\lib\net45\Newtonsoft.Json.dll"
#r @"FAKE\tools\Argu.dll"

#load @"AdditionalSources.fsx"
#load @"AssemblyResources.fsx"

namespace Aardvark.Fake

open Fake
open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions
open Aardvark.Fake
open Argu


[<AutoOpen>]
module Startup =

    type private Arguments =
        | Debug
        | Verbose

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                    | Debug -> "debug build"
                    | Verbose -> "verbose mode"

    let private argParser = ArgumentParser.Create<Arguments>()

    type Config =
        {
            debug : bool
            verbose : bool
            target : string
            args : list<string>
        }

    let mutable config = { debug = false; verbose = false; target = "Default"; args = [] }

    let entry() =
        Environment.SetEnvironmentVariable("Platform", "Any CPU")
        let argv = Environment.GetCommandLineArgs() |> Array.skip 2
        try 
            let res = argParser.Parse(argv, ignoreUnrecognized = true)
            let debug = res.Contains <@ Debug @>
            let verbose = res.Contains <@ Verbose @>
            let argv = argv |> Array.filter (fun str -> not (str.StartsWith "-")) |> Array.toList

            let target, args =
                match argv with
                    | [] -> "Default", []
                    | t::rest -> t, rest

            Paket.Logging.verbose <- verbose

            config <- { debug = debug; verbose = verbose; target = target; args = args }

            RunTargetOrDefault target
        with e ->
            RunTargetOrDefault "Help"


module DefaultSetup =
    
    let packageNameRx = Regex @"(?<name>[a-zA-Z_0-9\.]+?)\.(?<version>([0-9]+\.)*[0-9]+)\.nupkg"

    let install(solutionNames : seq<string>) = 
        let core = solutionNames

        Target "Install" (fun () ->
            AdditionalSources.paketDependencies.Install(false, false)
            AdditionalSources.installSources ()
        )

        Target "Restore" (fun () ->
            if File.Exists "paket.lock" then
                AdditionalSources.paketDependencies.Restore()
            else
                AdditionalSources.paketDependencies.Install(false, false)
        
            AdditionalSources.installSources ()
        )

        Target "Update" (fun () ->
            AdditionalSources.paketDependencies.Update(false, false)
            AdditionalSources.installSources ()
        )

        Target "AddSource" (fun () ->
            AdditionalSources.addSources config.args 
        )

        Target "RemoveSource" (fun () ->
            AdditionalSources.removeSources config.args 
        )

        Target "Clean" (fun () ->
            DeleteDir (Path.Combine("bin", "Release"))
            DeleteDir (Path.Combine("bin", "Debug"))
        )

        Target "Compile" (fun () ->
            if config.debug then
                MSBuildDebug "bin/Release" "Build" core |> ignore
            else
                MSBuildRelease "bin/Release" "Build" core |> ignore
        )


        Target "CreatePackage" (fun () ->
            let releaseNotes = try Fake.Git.Information.getCurrentHash() |> Some with _ -> None
            if releaseNotes.IsNone then 
                //traceError "could not grab git status. Possible source: no git, not a git working copy"
                failwith "could not grab git status. Possible source: no git, not a git working copy"
            else 
                trace "git appears to work fine."
    
            let releaseNotes = releaseNotes.Value
            let branch = try Fake.Git.Information.getBranchName "." with e -> "master"

            let tag = Fake.Git.Information.getLastTag()
            AdditionalSources.paketDependencies.Pack("bin", version = tag, releaseNotes = releaseNotes, buildPlatform = "AnyCPU")
        )


        Target "DeployToHobel" (fun () ->
            let packages = !!"bin/*.nupkg"
            let packageNameRx = Regex @"(?<name>[a-zA-Z_0-9\.]+?)\.(?<version>([0-9]+\.)*[0-9]+)\.nupkg"
            let tag = Fake.Git.Information.getLastTag()

            let myPackages = 
                packages 
                    |> Seq.choose (fun p ->
                        let m = packageNameRx.Match (Path.GetFileName p)
                        if m.Success then 
                            Some(m.Groups.["name"].Value)
                        else
                            None
                    )
                    |> Set.ofSeq

            try
                for id in myPackages do
                    let source = sprintf "bin/%s.%s.nupkg" id tag
                    let target = sprintf @"\\hobel.ra1.vrvis.lan\NuGet\%s.%s.nupkg" id tag
                    File.Copy(source, target, true)
            with e ->
                traceError (string e)
        )

        Target "Deploy" (fun () ->
            let rx = Regex @"(?<url>[^ ]+)[ \t]*(?<keyfile>[^ ]+)"
            let targets = "deploy.targets"
            let targets =
                if File.Exists targets then 
                    File.ReadAllLines targets 
                        |> Array.choose (fun str -> 
                            let m = rx.Match str 
                            if m.Success then
                                Some (m.Groups.["url"].Value, m.Groups.["keyfile"].Value)
                            else
                                traceImportant (sprintf "could not parse target: %A" str)
                                None
                        )
                else
                    [||]

            for (target, keyName) in targets do

                let packages = !!"bin/*.nupkg"
                let packageNameRx = Regex @"(?<name>[a-zA-Z_0-9\.]+?)\.(?<version>([0-9]+\.)*[0-9]+)\.nupkg"

                let myPackages = 
                    packages 
                        |> Seq.choose (fun p ->
                            let m = packageNameRx.Match (Path.GetFileName p)
                            if m.Success then 
                                Some(m.Groups.["name"].Value)
                            else
                                None
                        )
                        |> Set.ofSeq

   
                let accessKey =
                    let accessKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", keyName)
                    if File.Exists accessKeyPath then 
                        let r = Some (File.ReadAllText accessKeyPath)
                        tracefn "key: %A" r.Value
                        r
                    else None

                let branch = Fake.Git.Information.getBranchName "."
                let releaseNotes = Fake.Git.Information.getCurrentHash()

                let tag = Fake.Git.Information.getLastTag()
                match accessKey with
                    | Some accessKey ->
                        try
                            for id in myPackages do
                                let packageName = sprintf "bin/%s.%s.nupkg" id tag
                                tracefn "pushing: %s" packageName
                                Paket.Dependencies.Push(packageName, apiKey = accessKey, url = target)
                        with e ->
                            traceError (string e)
                    | None ->
                        traceError (sprintf "Could not find nuget access key")
        )


        Target "AddNativeResources" (fun () ->
            let dir =
               if Directory.Exists "libs/Native" then Some "libs/Native"
               elif Directory.Exists "lib/Native" then Some "lib/Native"
               else None

            match dir with
                | Some dir ->
                    let dirs = Directory.GetDirectories dir
                    for d in dirs do
                        let n = Path.GetFileName d
                        let d = d |> Path.GetFullPath

                        let paths = [
                            Path.Combine("bin/Release", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Release", n + ".exe") |> Path.GetFullPath
                            Path.Combine("bin/Debug", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Debug", n + ".exe") |> Path.GetFullPath
                        ]

                        AssemblyResources.copyDependencies d ["bin/Release"; "bin/Debug"]

                        for p in paths do
                            if File.Exists p then
                                AssemblyResources.addFolder d p
                | None ->
                    ()
        )

        Target "Help" (fun () ->

            printfn "aardvark build script"
            printfn "  syntax: build [Target] where target is one of the following"
            printfn "    Default (which is executed when no target is given)"
            printfn "      same like compile but also copying native dependencies (from libs/Native/PROJECTNAME)"
            printfn "      to bin/Release and injecting them as resource into the resulting dll/exe"
            printfn "    Compile"
            printfn "      builds the project's solution to bin/Release"
            printfn "    Clean"
            printfn "      deletes all output files in bin/Debug and bin/Release"
            printfn "    CreatePackage"
            printfn "      creates packages for all projects having a paket.template using the current"
            printfn "      git tag as version (note that the tag needs to have a comment)."
            printfn "      the resulting packages are stored in bin/*.nupkg"
            printfn "    Push"
            printfn "      creates the packages and deploys them to \\\\hobel\\NuGet\\ and all other"
            printfn "      feeds specified in deploy.targets"
            printfn "    Restore"
            printfn "      ensures that all packages given in paket.lock are installed"
            printfn "      with their respective version."
            printfn "    Install"
            printfn "      installs all packages specified in paket.dependencies and"
            printfn "      adjusts project files according to paket.references (next to the project)"
            printfn "      may also perform a new resolution when versions in paket.dependencies have changed."
            printfn "    Update"
            printfn "      searches for newer compatible version (according to paket.dependencies)"
            printfn "      and installs them if possible. this target also adjusts the project files"
            printfn ""
            printfn "  advanced features"
            printfn "    AddSource <folder>"
            printfn "      adds the repository located in <folder> as source dependecy causing all packages"
            printfn "      referenced from that repository to be overriden by a locally built variant."
            printfn "      Note that these overrides are not version-aware and will override all packages"
            printfn "      without any compatibility checks."
            printfn "      Furthermore these source packages \"survive\" Install/Update/Restore and"
            printfn "      are rebuilt (upon restore/install/update) when files are modified in the source folder"
            printfn "    RemoveSource <folder>"
            printfn "      adds the repository located in <folder> from the source dependencies and restores"
            printfn "      the original version from its respective package source."


        )


        Target "Push" DoNothing
        Target "Default" DoNothing


        "Restore" ==> "Compile" |> ignore
        "Compile" ==> "AddNativeResources" |> ignore

        "AddNativeResources" ==> "CreatePackage" |> ignore
        "Compile" ==> "CreatePackage" |> ignore
        "CreatePackage" ==> "Deploy" |> ignore
        "CreatePackage" ==> "DeployToHobel" |> ignore

        "Deploy" ==> "Push" |> ignore
        "DeployToHobel" ==> "Push" |> ignore

        "Compile" ==> "AddNativeResources" ==> "Default" |> ignore
