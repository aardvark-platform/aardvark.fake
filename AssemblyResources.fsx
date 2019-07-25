#nowarn "211"

#load "loadall.fsx"

namespace Aardvark.Fake

open System
open System.Reflection
open System.IO
open System.IO.Compression
open Fake
open Fake.Core
open Fake.IO

[<AutoOpen>]
module PathHelpersAssembly =
    type Path with
        static member ChangeFilename(path : string, newName : string -> string) =
            let dir = Path.GetDirectoryName(path)
            let name = Path.GetFileNameWithoutExtension path
            let ext = Path.GetExtension(path)
            Path.Combine(dir, (newName name) + ext)


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
            archive.CreateEntryFromFile(f, Path.Combine(path, Path.GetFileName f)) |> ignore
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
            let pdbPath = Path.ChangeExtension(assemblyPath, "pdb")
            let symbols = 
                // only process symbols if they exist and we are on not on unix like systems (they use mono symbols). 
                // this means: at the moment only windows packages support pdb debugging.
                File.Exists (pdbPath) && System.Environment.OSVersion.Platform <> PlatformID.Unix

            let bytes = new MemoryStream(File.ReadAllBytes assemblyPath)

            let pdbStream =
                if symbols then
                    new MemoryStream(File.ReadAllBytes pdbPath)
                else
                    null


            let r = ReaderParameters()
            if symbols then
                r.SymbolReaderProvider <- Mono.Cecil.Pdb.PdbReaderProvider()
                r.SymbolStream <- pdbStream
                r.ReadSymbols <- symbols
            let a = AssemblyDefinition.ReadAssembly(bytes,r)


            //let a = AssemblyDefinition.ReadAssembly(assemblyPath,ReaderParameters(ReadSymbols=symbols))
            // remove the old resource (if any)
            let res = a.MainModule.Resources |> Seq.tryFind (fun r -> r.Name = "native.zip")
            match res with
                | Some res -> a.MainModule.Resources.Remove res |> ignore
                | None -> ()

            let temp = System.IO.Path.GetTempFileName()
            let data =
                try
                    let mem = File.OpenWrite(temp)
                    let archive = new ZipArchive(mem, ZipArchiveMode.Create, true)
                    addFolderToArchive "" folder archive

                    // create and add the new resource
                    archive.Dispose()
                    mem.Close()
                    Trace.logfn "archive size: %d bytes" (FileInfo(temp).Length)
                    let b = File.ReadAllBytes(temp) //mem.ToArray()
                    Trace.logfn "archived native dependencies with size: %d bytes" b.Length
                    b
                finally
                    File.Delete(temp)


            let r = EmbeddedResource("native.zip", ManifestResourceAttributes.Public, data)
    
            a.MainModule.Resources.Add(r)
            a.Write(assemblyPath, WriterParameters(WriteSymbols = symbols))
            //a.Write(WriterParameters(WriteSymbols=symbols))
            a.Dispose()
//
//            let pdbPath = Path.ChangeExtension(assemblyPath, ".pdb")
//            let tempPath = Path.ChangeFilename(assemblyPath, fun a -> a + "Tmp")
//            let tempPdb = Path.ChangeExtension(tempPath, ".pdb")
//
//            a.Write( tempPath, WriterParameters(WriteSymbols=symbols))
//            a.Dispose()
//
//            File.Delete assemblyPath
//            File.Move(tempPath, assemblyPath)
//
//            if File.Exists tempPdb then
//                File.Delete pdbPath
//                File.Move(tempPdb, pdbPath)

            Trace.logfn "added native resources to %A" (Path.GetFileName assemblyPath)

        )

    let getFilesAndFolders (folder : string) =
        if Directory.Exists folder then Directory.GetFileSystemEntries folder
        else [||]

    let copy (dstFolder : string) (source : string) =
        let f = FileInfo source
        if f.Exists then 
            if Directory.Exists dstFolder |> not then Directory.CreateDirectory dstFolder |> ignore
            Shell.copyFile dstFolder source
        else 
            let di = DirectoryInfo source
            if di.Exists then
                let dst = Path.Combine(dstFolder, di.Name)
                if Directory.Exists dst |> not then Directory.CreateDirectory dst |> ignore
                Shell.copyRecursive source dst true |> ignore
                ()

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
                |> Seq.iter (copy t)

            getFilesAndFolders(Path.Combine(folder, platform)) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64"
                    )
                |> Seq.iter (copy t)

            getFilesAndFolders(Path.Combine(folder, arch)) 
                |> Seq.iter (copy t)

            getFilesAndFolders(folder) 
                |> Array.filter (fun f -> 
                    let n = Path.GetFileName(f) 
                    n <> "x86" && n <> "AMD64" && n <> "windows" && n <> "linux" && n <> "mac"
                    )
                |> Seq.iter (copy t)

