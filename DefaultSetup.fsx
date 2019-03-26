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
        | Pre

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                    | Debug -> "debug build"
                    | Verbose -> "verbose mode"
                    | Pre -> "prerelease package"

    let private argParser = ArgumentParser.Create<Arguments>()

    type Config =
        {
            debug : bool
            prerelease : bool
            verbose : bool
            target : string
            args : list<string>
        }

    let mutable config = { debug = false; prerelease = false; verbose = false; target = "Default"; args = [] }

    let entry() =
        Environment.SetEnvironmentVariable("Platform", "Any CPU")
        let argv = Environment.GetCommandLineArgs() |> Array.skip 5 // yeah really
        try 
            let res = argParser.Parse(argv, ignoreUnrecognized = true) 
            let debug = res.Contains <@ Debug @>
            let verbose = res.Contains <@ Verbose @>
            let prerelease = res.Contains <@ Pre @>

            printfn "parsed options: debug=%b, verbose=%b prerelease=%b" debug verbose prerelease
            let argv = argv |> Array.filter (fun str -> not (str.StartsWith "-")) |> Array.toList

            let target, args =
                match argv with
                    | [] -> "Default", []
                    | t::rest -> t, rest

            //Paket.Logging.verbose <- verbose

            config <- { debug = debug; prerelease = prerelease; verbose = verbose; target = target; args = args }

            //Environment.SetEnvironmentVariable("Target", target)
            Run target
        with e ->
            Run "Help"

    module NugetInfo = 
        open SemVerHelper

        let defaultValue (fallback : 'a) (o : Option<'a>) =
            match o with    
                | Some o -> o
                | None -> fallback

        let private adjust (v : PreRelease) =
            let o = 
                match v.Number with
                    | Some n -> sprintf "%s%d" v.Name n
                    | None -> v.Name
            { v with
                Origin = o
                Parts = [AlphaNumeric o]
            }

        let nextVersion (major : bool) (prerelease : bool) (v : string) =
            let v : SemVerInfo = SemVerHelper.parse v
            let version = 
                match v.PreRelease with
                    | Some _ when prerelease -> v
                    | Some _ -> { v with PreRelease = None }
                    | _ ->
                        match major with
                            | false -> { v with Patch = v.Patch + 1 }
                            | true -> { v with Minor = v.Minor + 1; Patch = 0 }

            if prerelease then
                let pre = 
                    version.PreRelease |> Option.map (fun p -> 
                        { p with Number = p.Number |> Option.map (fun v -> v + 1) |> defaultValue 2 |> Some }
                    )

                let def =
                    {
                        Origin = "prerelease1"
                        Name = "prerelease"
                        Number = Some 1
                        Parts = [ AlphaNumeric "prerelease1" ]
                    }
                { version with PreRelease = pre |> defaultValue def |> adjust |> Some  }.ToString()
            else
                { version with PreRelease = None}.ToString()


module DefaultSetup =

    let packageNameRx = Regex @"^(?<name>[a-zA-Z_0-9\.-]+?)\.(?<version>([0-9]+\.)*[0-9]+)(.*?)\.nupkg$"

    let getUserConsentForPush (oldVersion : string) (newVersion : string) =
            printfn ""
            printfn "Package version (current): %s" oldVersion
            printfn "Package version (new)    : %s" newVersion
            printf "Create and push new version to deploy targets (Y|_N_)? "
            match Console.ReadLine() with
            | "y"
            | "Y" -> ()
            | _ -> printfn "aborting"
                   Environment.Exit(0) |> ignore

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
            if not (File.Exists "paket.lock") then
                //AdditionalSources.paketDependencies.Install(false)
                AdditionalSources.shellExecutePaket None "install"
        
            MSBuild "" "Restore" ["VisualStudioVersion", vsVersion] [core] |> ignore<list<string>>
            
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
                MSBuild "" "Build" ["Configuration", "Debug"; "VisualStudioVersion", vsVersion; ] [core] |> ignore<list<string>>
            else
                MSBuild "" "Build" ["Configuration", "Release"; "VisualStudioVersion", vsVersion; "SourceLinkCreate", "true"] [core] |> ignore<list<string>>
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
            
            let symbolCommand =
                command + " --build-config Debug --symbols"

            let command = 
                if config.debug then
                    command + " --build-config Debug"
                else
                    command
            
                
            AdditionalSources.shellExecutePaket None command
        )

        Target "Push" (fun () ->
            if IncrediblyUglyHackfulNugetOverride.isHackActive () then
                trace "there are hacked packages in your global nuget folder. If you continue you are really hateful. Press any key to destroy all packages and deal with method not founds all the way!"
                System.Console.ReadLine() |> ignore

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

            let allOk = 
                targets |> Array.forall (fun (_,keyName) -> File.Exists <| Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", keyName))

            let cloneIt dir =
                tracefn "cloning to: %s" dir
                try 
                    Fake.Git.Repository.clone "." "git@github.com:haraldsteinlechner/aardvark-keys.git" dir
                    System.Threading.Thread.Sleep 200
                with e -> 
                    traceError <| sprintf "could not clone aardvark keys. ask the platform team for assistance... (%A)" e.Message

            if not allOk  then                  // ok let us try to find a key in the intaarnet.
                let dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"aardvark-keys")
                if Directory.Exists dir then
                    try 
                        tracefn "pulling: %s" dir
                        Fake.Git.Branches.pull dir "origin" "master"
                    with e -> 
                        tracefn "could not pull keys dir."
                        Directory.Delete(dir,true) |> ignore
                        System.Threading.Thread.Sleep 200
                        cloneIt dir
                else
                    cloneIt dir
       

            for (target, keyName) in targets do

                let packages = !!"bin/*.nupkg"
                let packageNameRx = Regex @"^(?<name>[a-zA-Z_0-9\.-]+?)\.(?<version>([0-9]+\.)*[0-9]+)(.*?)\.nupkg$"


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
                    let accessKeyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".ssh")

                    let readKey dir =
                        let accessKeyPath = Path.Combine(dir, keyName)
                        if File.Exists accessKeyPath then
                            let r = Some (File.ReadAllText accessKeyPath)
                            tracefn "key: %A" r.Value
                            r
                        else printfn "bad:%s" accessKeyPath; None

                    match readKey accessKeyPath with   
                        | Some key -> Some key
                        | _ -> readKey (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"aardvark-keys"))


                let branch = Fake.Git.Information.getBranchName "."
                let releaseNotes = Fake.Git.Information.getCurrentHash()

                let tag = getGitTag()
                match accessKey with
                    | Some accessKey ->
                        try
                            for id in myPackages do
                                let names = [ sprintf "bin/%s.%s.nupkg" id tag ]
                                for packageName in names do
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
            if IncrediblyUglyHackfulNugetOverride.isHackActive () then
                trace "there are hacked packages in your global nuget folder. If you continue you are really hateful. Press any key to destroy all packages and deal with method not founds all the way!"
                System.Console.ReadLine() |> ignore
            
            let old = getGitTag()
            let newVersion = NugetInfo.nextVersion false config.prerelease old

            getUserConsentForPush old newVersion

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
        )

        Target "PushPre" (fun () ->
            if IncrediblyUglyHackfulNugetOverride.isHackActive () then
                trace "there are hacked packages in your global nuget folder. If you continue you are really hateful. Press any key to destroy all packages and deal with method not founds all the way!"
                System.Console.ReadLine() |> ignore

            let old = getGitTag()
            let newVersion = NugetInfo.nextVersion false true old

            getUserConsentForPush old newVersion

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
        )

        
        Target "PushMajor" (fun () ->
            if IncrediblyUglyHackfulNugetOverride.isHackActive () then
                trace "there are hacked packages in your global nuget folder. If you continue you are really hateful. Press any key to destroy all packages and deal with method not founds all the way!"
                System.Console.ReadLine() |> ignore

            let old = getGitTag()
            let newVersion = NugetInfo.nextVersion true config.prerelease old

            getUserConsentForPush old newVersion

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

        Target "OverrideGlobalPackages" (fun () ->
            IncrediblyUglyHackfulNugetOverride.copyToGlobal getGitTag false 
        )

        Target "RevertGlobalPackages" (fun () ->
            IncrediblyUglyHackfulNugetOverride.copyToGlobal getGitTag true 
        )

        Target "RevertAllGlobalPackages" (fun () ->
            IncrediblyUglyHackfulNugetOverride.removeAllHacked()
        )

        Target "Help" (fun () ->
            let defColor = Console.ForegroundColor
            let highlightColor = ConsoleColor.Yellow
            printfn "aardvark build script"
            printfn "  syntax: build [Target] [Options] where target is one of the following"
            printfn "          with Options = --verbose | --debug |--pre"
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
            printfn "      creates packages (see CreatePackage) and deploys to all feeds specified in deploy.targets"

            Console.ForegroundColor<-highlightColor
            printfn "    PushMinor [--pre]"
            Console.ForegroundColor<-defColor
            printfn "      increments current version and creates and deploys package(s) (Push), e.g."
            printfn "        1.2.3 -> 1.2.4"
            printfn "        1.2.3 -> 1.2.4-prelease1 (when using --pre)"

            Console.ForegroundColor<-highlightColor
            printfn "    PushMajor [--pre]"
            Console.ForegroundColor<-defColor
            printfn "      increments current version and creates and deploys package(s) (Push), e.g."
            printfn "        1.2.3 -> 1.3.0"
            printfn "        1.2.3 -> 1.3.0-prelease1 (when using --pre)"

            Console.ForegroundColor<-highlightColor
            printfn "    PushPre"
            Console.ForegroundColor<-defColor
            printfn "      creates and deploys prelease package(s), e.g."
            printfn "        1.2.3           -> 1.2.4-prelease1"
            printfn "        1.2.3-prelease1 -> 1.2.3-prelease2"

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

        "Compile" ==> "AddNativeResources" ==> "Default" |> ignore

        "CreatePackage" ==> "OverrideGlobalPackages" |> ignore

        "CreatePackage" ==> "Push" |> ignore
        "CreatePackage" ==> "PushMinor" |> ignore
        "CreatePackage" ==> "PushMajor" |> ignore
        "CreatePackage" ==> "PushPre" |> ignore
