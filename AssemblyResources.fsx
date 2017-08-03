#nowarn "211"
#I @"../../../../packages/build"
#I @"packages"
#r @"FAKE/tools/FakeLib.dll"
#r @"Mono.Cecil/lib/net40/Mono.Cecil.dll"
#r @"Mono.Cecil/lib/net40/Mono.Cecil.Pdb.dll"
#r @"Chessie/lib/net40/Chessie.dll"
#r @"System.IO.Compression.dll"
#r @"System.IO.Compression.FileSystem.dll"

namespace Aardvark.Fake

open System
open System.Reflection
open System.IO
open System.IO.Compression
open Fake

module AssemblyResources =
    open System
    open Mono.Cecil
    open System.IO
    open System.IO.Compression
    open System.Collections.Generic

    let rec addFolderToArchive (path : string) (folder : string) (archive : ZipArchive) =
        let files = Directory.GetFiles(folder)
        for f in files do
            printfn "adding file: %A" f
            let e = archive.CreateEntryFromFile(f, Path.Combine(path, Path.GetFileName f))
            ()

        let sd = Directory.GetDirectories(folder)
        for d in sd do
            let p = Path.Combine(path, Path.GetFileName d)
            addFolderToArchive p d archive

    let useDir d f =
        let old = System.Environment.CurrentDirectory
        System.Environment.CurrentDirectory <- d
        let r = f ()
        System.Environment.CurrentDirectory <- old
        r

    let addFolder (folder : string) (assemblyPath : string) =
        
        useDir (Path.Combine("bin","Release")) (fun () -> 
            let symbols = 
                // only process symbols if they exist and we are on not on unix like systems (they use mono symbols). 
                // this means: at the moment only windows packages support pdb debugging.
                File.Exists (Path.ChangeExtension(assemblyPath, "pdb")) && System.Environment.OSVersion.Platform <> PlatformID.Unix

        
            let a = AssemblyDefinition.ReadAssembly(assemblyPath,ReaderParameters(ReadSymbols=symbols))
            // remove the old resource (if any)
            let res = a.MainModule.Resources |> Seq.tryFind (fun r -> r.Name = "native.zip")
            match res with
                | Some res -> a.MainModule.Resources.Remove res |> ignore
                | None -> ()

            let temp = System.IO.Path.GetTempFileName()
            let data =
                try
                    let mem = File.Open(temp,FileMode.Open)
                    let archive = new ZipArchive(mem, ZipArchiveMode.Create, true)
                    addFolderToArchive "" folder archive

                    // create and add the new resource
                    archive.Dispose()
                    mem.Close()
                    tracefn "archive size: %d bytes" (FileInfo(temp).Length)
                    let b = File.ReadAllBytes(temp) //mem.ToArray()
                    tracefn "archived native dependencies with size: %d bytes" b.Length
                    b
                finally
                    File.Delete(temp)


            let r = EmbeddedResource("native.zip", ManifestResourceAttributes.Public, data)
    

            a.MainModule.Resources.Add(r)
            tracefn "added native resources to %A" (Path.GetFileName assemblyPath)
            a.Write( assemblyPath, WriterParameters(WriteSymbols=symbols))

        )

    let getFilesAndFolders (folder : string) =
        if Directory.Exists folder then Directory.GetFileSystemEntries folder
        else [||]

    let copyDependencies (folder : string) (targets : seq<string>) =
        let arch = 
            "AMD64" // developer machines are assumed to be 64 bit machines
            
        let targets = targets |> Seq.toArray

        let platform =
            match Environment.OSVersion.Platform with
                | PlatformID.MacOSX -> "mac"
                | PlatformID.Unix -> "linux"
                | _ -> "windows"

        for t in targets do
            getFilesAndFolders(Path.Combine(folder, platform, arch)) 
                |> CopyFiles t

            getFilesAndFolders(Path.Combine(folder, platform)) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64"
                    )
                |> CopyFiles t

            getFilesAndFolders(Path.Combine(folder, arch)) 
                |> CopyFiles t

            getFilesAndFolders(folder) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64" && n <> "windows" && n <> "linux" && n <> "mac"
                    )
                |> CopyFiles t

