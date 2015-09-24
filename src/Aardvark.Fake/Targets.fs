namespace Aardvark.Fake

open Fake
open System
open System.IO
open System.Diagnostics
open System.Text.RegularExpressions


module DefaultTargets =
    let packageNameRx = Regex @"(?<name>[a-zA-Z_0-9\.]+?)\.(?<version>([0-9]+\.)*[0-9]+)\.nupkg"

    let private debugBuild =
        match environVarOrNone "Configuration" with
            | Some c when c.Trim().ToLower() = "debug" -> true
            | _ -> false

    let install(solutionNames : seq<string>) = 
        let core = solutionNames

        Target "Install" (fun () ->
            AdditionalSources.paketDependencies.Install(false, false, false, true)
            AdditionalSources.installSources ()
        )

        Target "Restore" (fun () ->
            if File.Exists "paket.lock" then
                AdditionalSources.paketDependencies.Restore()
            else
                AdditionalSources.paketDependencies.Install(false, false, false, true)
        
            AdditionalSources.installSources ()
        )

        Target "Update" (fun () ->
            AdditionalSources.paketDependencies.Update(false, false)
            AdditionalSources.installSources ()
        )

        Target "AddSource" (fun () ->
            let args = Environment.GetCommandLineArgs()
            let folders =
                if args.Length > 3 then
                    Array.skip 3 args
                else
                    failwith "no source folder given"

            AdditionalSources.addSources (Array.toList folders)
        )

        Target "RemoveSource" (fun () ->
            let args = Environment.GetCommandLineArgs()
            let folders =
                if args.Length > 3 then
                    Array.skip 3 args
                else
                    failwith "no source folder given"

            AdditionalSources.removeSources (Array.toList folders)
        )

        Target "Clean" (fun () ->
            DeleteDir (Path.Combine("bin", "Release"))
            DeleteDir (Path.Combine("bin", "Debug"))
        )

        Target "Compile" (fun () ->
            if debugBuild then
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
            AdditionalSources.paketDependencies.Pack("bin", version = tag, releaseNotes = releaseNotes)
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
    