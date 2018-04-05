#I @"../../../../packages/build"
#I @"packages"
#r @"FAKE/tools/FakeLib.dll"
#r @"FAKE/tools/Argu.dll"

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

    let getGitTag() =
        let ok,msg,errors = Fake.Git.CommandHelper.runGitCommand "." "describe --abbrev=0"
        if ok && msg.Count >= 1 then
            let tag = msg.[0]
            tag
        else
            let err = sprintf "no tag: %A" errors
            traceError err
            failwith err

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
        let argv = Environment.GetCommandLineArgs() |> Array.skip 5 // yeah really
        try 
            let res = argParser.Parse(argv, ignoreUnrecognized = true) 
            let debug = res.Contains <@ Debug @>
            let verbose = res.Contains <@ Verbose @>
            printfn "parsed options: debug=%b, verbose=%b" debug verbose
            let argv = argv |> Array.filter (fun str -> not (str.StartsWith "-")) |> Array.toList

            let target, args =
                match argv with
                    | [] -> "Default", []
                    | t::rest -> t, rest

            //Paket.Logging.verbose <- verbose

            config <- { debug = debug; verbose = verbose; target = target; args = args }

            //Environment.SetEnvironmentVariable("Target", target)
            Run target
        with e ->
            Run "Help"


module DefaultSetup =
    
    let packageNameRx = Regex @"(?<name>[a-zA-Z_0-9\.]+?)\.(?<version>([0-9]+\.)*[0-9]+)\.nupkg"

    let install(solutionNames : seq<string>) = 
        let core = Seq.head solutionNames

        let vsVersion =
            match MSBuildHelper.MSBuildDefaults.Properties |> List.tryPick (fun (n,v) -> if n = "VisualStudioVersion" then Some v else None) with
                | Some vsVersion -> vsVersion
                | None -> 
                    let versionRx = System.Text.RegularExpressions.Regex @"\\(?<version>[0-9]+\.[0-9]+)\\bin\\msbuild\.exe$"
                    let m = versionRx.Match (MSBuildHelper.msBuildExe.ToLower())
                    if m.Success then
                        m.Groups.["version"].Value
                    else
                        "15.0"
                        //failwith "could not determine Visual Studio Version"

        Target "Install" (fun () ->
            //AdditionalSources.paketDependencies.Install(false)
            AdditionalSources.shellExecutePaket (Some core) "install"
            AdditionalSources.installSources()
        )

        Target "Restore" (fun () ->
            if File.Exists "paket.lock" then
                //AdditionalSources.paketDependencies.Restore()
                AdditionalSources.shellExecutePaket (Some core) "restore"
            else
                //AdditionalSources.paketDependencies.Install(false)
                AdditionalSources.shellExecutePaket (Some core) "install"
        
            AdditionalSources.installSources ()
        )

        Target "Update" (fun () ->
             match config.args with 
              | [] ->  
                //AdditionalSources.paketDependencies.Update(false)
                AdditionalSources.shellExecutePaket (Some core) "update"
              | xs ->
                let filter = xs |> List.map (sprintf "(%s)") |> String.concat "|" |> sprintf "(%s)"
                //AdditionalSources.paketDependencies.UpdateFilteredPackages(Some "Main",filter,None,false,false,false,false,false,Paket.SemVerUpdateMode.NoRestriction,false)
                let command = sprintf "update --group Main --filter %s" filter;
                AdditionalSources.shellExecutePaket (Some core) command

             AdditionalSources.installSources ()
        )

        Target "AddSource" (fun () ->
            AdditionalSources.addSources core config.args 
        )

        Target "RemoveSource" (fun () ->
            AdditionalSources.removeSources core config.args 
        )

        Target "Clean" (fun () ->
            DeleteDir (Path.Combine("bin", "Release"))
            DeleteDir (Path.Combine("bin", "Debug"))
        )

        Target "Compile" (fun () ->
            if config.debug then
                DotNetCli.Build (fun p ->
                    { p with 
                        WorkingDir = "src"
                        Project = core
                        Configuration = "Debug"
                    }
                )
            else
                DotNetCli.Build (fun p ->
                    { p with 
                        WorkingDir = "src"
                        Project = core
                        Configuration = "Release"
                    }
                )
        )

        Target "UpdateBuildScript" (fun () ->
            //AdditionalSources.paketDependencies.UpdateGroup("Build",false,false,false,false,false,Paket.SemVerUpdateMode.NoRestriction,false)
            AdditionalSources.shellExecutePaket None "update --group Build"
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

            let tag = getGitTag()
            //AdditionalSources.paketDependencies.Pack("bin", version = tag, releaseNotes = releaseNotes, buildPlatform = "AnyCPU")
            let command = sprintf "pack bin --pin-project-references --build-platform AnyCPU --version %s --release-notes %s" tag releaseNotes
            AdditionalSources.shellExecutePaket None command
        )

        Target "Push" (fun () ->
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

                let tag = getGitTag()
                match accessKey with
                    | Some accessKey ->
                        try
                            for id in myPackages do
                                let packageName = sprintf "bin/%s.%s.nupkg" id tag
                                tracefn "pushing: %s" packageName
                                //Paket.Dependencies.Push(packageName, apiKey = accessKey, url = target)
                                let command = sprintf "push %s --api-key %s --url %s" packageName accessKey target
                                AdditionalSources.shellExecutePaket None command
                        with e ->
                            traceError (string e)
                    | None ->
                        traceError (sprintf "Could not find nuget access key")
        )


        Target "PushMinor" (fun () ->

            let old = getGitTag()
            match Version.TryParse(old) with
             | (true,v) ->
                  let newVersion = Version(v.Major,v.Minor,v.Build + 1) |> string
                  if Fake.Git.CommandHelper.directRunGitCommand "." (sprintf "tag -a %s -m \"%s\"" newVersion newVersion) then
                    tracefn "created tag %A" newVersion
            
                    try
                        Run "Push"

                        try
                            let tag = getGitTag()
                            Fake.Git.Branches.pushTag "." "origin" newVersion
                        with e ->
                            traceError "failed to push tag %A to origin (please push yourself)" 
                            raise e
                    with e ->
                        Fake.Git.Branches.deleteTag "." newVersion
                        tracefn "deleted tag %A" newVersion
                        raise e
                  else
                    failwithf "could not create tag: %A" newVersion
             | _ -> 
                failwithf "could not parse tag: %A" old
        )

        
        Target "PushMajor" (fun () ->

            let old = getGitTag()
            match Version.TryParse(old) with
             | (true,v) ->
                  let newVersion = Version(v.Major,v.Minor + 1,0) |> string
                  if Fake.Git.CommandHelper.directRunGitCommand "." (sprintf "tag -a %s -m \"%s\"" newVersion newVersion) then
                    tracefn "created tag %A" newVersion
            
                    try
                        Run "Push"

                        try
                            let tag = getGitTag()
                            Fake.Git.Branches.pushTag "." "origin" newVersion
                        with e ->
                            traceError "failed to push tag %A to origin (please push yourself)" 
                            raise e
                    with e ->
                        Fake.Git.Branches.deleteTag "." newVersion
                        tracefn "deleted tag %A" newVersion
                        raise e
                  else
                    failwithf "could not create tag: %A" newVersion
             | _ -> 
                failwithf "could not parse tag: %A" old
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
                            Path.Combine("bin/Release/netstandard2.0", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Release/netcoreapp2.0", n + ".exe") |> Path.GetFullPath
                            Path.Combine("bin/Debug/netstandard2.0", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Debug/netcoreapp2.0", n + ".exe") |> Path.GetFullPath
                            Path.Combine("bin/Release", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Release", n + ".exe") |> Path.GetFullPath
                            Path.Combine("bin/Debug", n + ".dll") |> Path.GetFullPath
                            Path.Combine("bin/Debug", n + ".exe") |> Path.GetFullPath
                        ]

                        AssemblyResources.copyDependencies d ["bin/Release"; "bin/Debug"; "bin/Debug/netcoreapp2.0"; "bin/Release/netcoreapp2.0"]

                        for p in paths do
                            if File.Exists p then
                                AssemblyResources.addFolder d p
                | None ->
                    ()
        )

        Target "Help" (fun () ->
            let defColor = Console.ForegroundColor
            let highlightColor = ConsoleColor.Yellow
            printfn "aardvark build script"
            printfn "  syntax: build [Target] [Options] where target is one of the following"
            printfn "          while Options = --verbose | --debug"
            printfn "          please note, that for reasons, debug builds are also built into bin/Release"
            Console.ForegroundColor<-highlightColor
            printfn "    Default (which is executed when no target is given)"
            Console.ForegroundColor<-defColor
            printfn "      same like compile but also copying native dependencies (from libs/Native/PROJECTNAME)"
            printfn "      to bin/Release and injecting them as resource into the resulting dll/exe"
            Console.ForegroundColor<-highlightColor
            printfn "    Compile"
            Console.ForegroundColor<-defColor
            printfn "      builds the project's solution to bin/Release"
            Console.ForegroundColor<-highlightColor
            printfn "    Clean"
            Console.ForegroundColor<-defColor
            printfn "      deletes all output files in bin/Debug and bin/Release"
            Console.ForegroundColor<-highlightColor
            printfn "    CreatePackage"
            Console.ForegroundColor<-defColor
            printfn "      creates packages for all projects having a paket.template using the current"
            printfn "      git tag as version (note that the tag needs to have a comment)."
            printfn "      the resulting packages are stored in bin/*.nupkg"
            Console.ForegroundColor<-highlightColor
            printfn "    Push"
            Console.ForegroundColor<-defColor
            printfn "      creates the packages and deploys them to all feeds specified in deploy.targets"
            Console.ForegroundColor<-highlightColor
            printfn "    Restore"
            Console.ForegroundColor<-defColor
            printfn "      ensures that all packages given in paket.lock are installed"
            printfn "      with their respective version."
            Console.ForegroundColor<-highlightColor
            printfn "    Install"
            Console.ForegroundColor<-defColor
            printfn "      installs all packages specified in paket.dependencies and"
            printfn "      adjusts project files according to paket.references (next to the project)"
            printfn "      may also perform a new resolution when versions in paket.dependencies have changed."
            Console.ForegroundColor<-highlightColor            
            printfn "    Update [regex]"
            Console.ForegroundColor<-defColor
            printfn "      searches for newer compatible version (according to paket.dependencies)"
            printfn "      and installs them if possible. this target also adjusts the project files"
            Console.ForegroundColor<-highlightColor            
            printfn "    UpdateBuildScript"
            Console.ForegroundColor<-defColor
            printfn "      updates the build script and its dependencies."
            printfn ""
            printfn "  advanced features"
            Console.ForegroundColor<-highlightColor
            printfn "    AddSource <folder>"
            Console.ForegroundColor<-defColor
            printfn "      adds the repository located in <folder> as source dependecy causing all packages"
            printfn "      referenced from that repository to be overriden by a locally built variant."
            printfn "      Note that these overrides are not version-aware and will override all packages"
            printfn "      without any compatibility checks."
            printfn "      Furthermore these source packages \"survive\" Install/Update/Restore and"
            printfn "      are rebuilt (upon restore/install/update) when files are modified in the source folder"
            Console.ForegroundColor<-highlightColor            
            printfn "    RemoveSource <folder>"
            Console.ForegroundColor<-defColor
            printfn "      adds the repository located in <folder> from the source dependencies and restores"
            printfn "      the original version from its respective package source."


        )

        Target "Default" DoNothing


        "Restore" ==> "Compile" |> ignore
        "Compile" ==> "AddNativeResources" |> ignore

        "AddNativeResources" ==> "CreatePackage" |> ignore
        "Compile" ==> "CreatePackage" |> ignore
        "CreatePackage" ==> "Push" |> ignore

        "Compile" ==> "AddNativeResources" ==> "Default" |> ignore
        "CreatePackage" ==> "PushMinor" |> ignore
        "CreatePackage" ==> "PushMajor" |> ignore
