namespace LoadAll
#I @"../../../../packages/build/aardvark-platform/aardvark.fake"
#I @"packages"

#r "netstandard.dll"
#r "Mono.Cecil/lib/netstandard1.3/Mono.Cecil.dll" 
#r "Fake.Core.SemVer/lib/netstandard2.0/Fake.Core.SemVer.dll" 
#r "Microsoft.Build.Framework/lib/netstandard2.0/Microsoft.Build.Framework.dll" 
#r "System.IO.Compression.ZipFile/lib/netstandard1.3/System.IO.Compression.ZipFile.dll" 
#r "System.Linq.Parallel/lib/netstandard1.3/System.Linq.Parallel.dll" 
#r "System.Collections.Concurrent/lib/netstandard1.3/System.Collections.Concurrent.dll" 
#r "System.Linq/lib/netstandard1.6/System.Linq.dll" 
#r "System.Resources.Writer/lib/netstandard1.3/System.Resources.Writer.dll" 
#r "System.Runtime.Numerics/lib/netstandard1.3/System.Runtime.Numerics.dll" 
#r "System.Runtime.Serialization.Primitives/lib/netstandard1.3/System.Runtime.Serialization.Primitives.dll" 
#r "System.Security.Cryptography.Primitives/lib/netstandard1.3/System.Security.Cryptography.Primitives.dll" 
#r "Argu/lib/netstandard2.0/Argu.dll" 
#r "FSharp.Control.Reactive/lib/netstandard2.0/FSharp.Control.Reactive.dll" 
#r "System.Reactive/lib/netstandard2.0/System.Reactive.dll" 
#r "System.Threading/lib/netstandard1.3/System.Threading.dll" 
#r "System.Threading.ThreadPool/lib/netstandard1.3/System.Threading.ThreadPool.dll" 
#r "FParsec/lib/netstandard1.6/FParsecCS.dll" 
#r "System.IO.FileSystem.Primitives/lib/netstandard1.3/System.IO.FileSystem.Primitives.dll" 
#r "System.Runtime.InteropServices.WindowsRuntime/lib/netstandard1.3/System.Runtime.InteropServices.WindowsRuntime.dll" 
#r "System.Security.Cryptography.ProtectedData/lib/netstandard2.0/System.Security.Cryptography.ProtectedData.dll" 
#r "System.Threading.Thread/lib/netstandard1.3/System.Threading.Thread.dll" 
#r "BlackFox.VsWhere/lib/netstandard2.0/BlackFox.VsWhere.dll" 
#r "Fake.Core.Context/lib/netstandard2.0/Fake.Core.Context.dll" 
#r "Fake.Core.Environment/lib/netstandard2.0/Fake.Core.Environment.dll" 
#r "Fake.Core.String/lib/netstandard2.0/Fake.Core.String.dll" 
#r "System.Security.AccessControl/lib/netstandard2.0/System.Security.AccessControl.dll" 
#r "System.Buffers/lib/netstandard2.0/System.Buffers.dll" 
#r "System.CodeDom/lib/netstandard2.0/System.CodeDom.dll" 
#r "System.Collections.Immutable/lib/netstandard2.0/System.Collections.Immutable.dll" 
#r "System.Diagnostics.DiagnosticSource/lib/netstandard1.3/System.Diagnostics.DiagnosticSource.dll" 
#r "System.Numerics.Vectors/lib/netstandard2.0/System.Numerics.Vectors.dll" 
#r "System.Reflection.TypeExtensions/lib/netstandard2.0/System.Reflection.TypeExtensions.dll" 
#r "System.Runtime.CompilerServices.Unsafe/lib/netstandard2.0/System.Runtime.CompilerServices.Unsafe.dll" 
#r "System.Security.Cryptography.Cng/lib/netstandard2.0/System.Security.Cryptography.Cng.dll" 
#r "System.Security.Cryptography.OpenSsl/lib/netstandard2.0/System.Security.Cryptography.OpenSsl.dll" 
#r "System.Security.Principal.Windows/lib/netstandard2.0/System.Security.Principal.Windows.dll" 
#r "System.Threading.Tasks.Dataflow/lib/netstandard2.0/System.Threading.Tasks.Dataflow.dll" 
#r "Mono.Cecil/lib/netstandard1.3/Mono.Cecil.Rocks.dll" 
#r "Mono.Cecil/lib/netstandard1.3/Mono.Cecil.Pdb.dll" 
#r "Mono.Cecil/lib/netstandard1.3/Mono.Cecil.Mdb.dll" 
#r "Fake.IO.FileSystem/lib/netstandard2.0/Fake.IO.FileSystem.dll" 
#r "Microsoft.Build.Utilities.Core/lib/netstandard2.0/Microsoft.Build.Utilities.Core.dll" 
#r "System.Reactive.Core/lib/netstandard2.0/System.Reactive.Core.dll" 
#r "System.Reactive.Interfaces/lib/netstandard2.0/System.Reactive.Interfaces.dll" 
#r "System.Reactive.Linq/lib/netstandard2.0/System.Reactive.Linq.dll" 
#r "System.Reactive.PlatformServices/lib/netstandard2.0/System.Reactive.PlatformServices.dll" 
#r "System.Reactive.Providers/lib/netstandard2.0/System.Reactive.Providers.dll" 
#r "Fake.Core.FakeVar/lib/netstandard2.0/Fake.Core.FakeVar.dll" 
#r "FParsec/lib/netstandard1.6/FParsec.dll" 
#r "Microsoft.Win32.Registry/lib/netstandard2.0/Microsoft.Win32.Registry.dll" 
#r "System.Security.Permissions/lib/netstandard2.0/System.Security.Permissions.dll" 
#r "System.Memory/lib/netstandard2.0/System.Memory.dll" 
#r "System.Reflection.Metadata/lib/netstandard2.0/System.Reflection.Metadata.dll" 
#r "System.Text.Encoding.CodePages/lib/netstandard2.0/System.Text.Encoding.CodePages.dll" 
#r "System.Threading.Tasks.Extensions/lib/netstandard2.0/System.Threading.Tasks.Extensions.dll" 
#r "MSBuild.StructuredLogger/lib/netstandard2.0/StructuredLogger.dll" 
#r "Microsoft.Build.Tasks.Core/lib/netstandard2.0/Microsoft.Build.Tasks.Core.dll" 
#r "Fake.Core.CommandLineParsing/lib/netstandard2.0/Fake.Core.CommandLineParsing.dll" 
#r "Fake.Core.Trace/lib/netstandard2.0/Fake.Core.Trace.dll" 
#r "System.Configuration.ConfigurationManager/lib/netstandard2.0/System.Configuration.ConfigurationManager.dll" 
#r "Fake.Core.Process/lib/netstandard2.0/Fake.Core.Process.dll" 
#r "Fake.DotNet.MSBuild/lib/netstandard2.0/Fake.DotNet.MSBuild.dll" 
#r "Fake.Core.Target/lib/netstandard2.0/Fake.Core.Target.dll" 
#r "Fake.Tools.Git/lib/netstandard2.0/Fake.Tools.Git.dll" 
#r "System" 
#r "System.Core" 
#r "System.Numerics" 
#r "System.Configuration" 
#r "System.IO.Compression" 
#r "System.IO.Compression.FileSystem" 
#r "System.ComponentModel.Composition" 
#r "System.Data" 
#r "System.Data.OracleClient" 
#r "System.Drawing" 
#r "System.Net" 
#r "System.ServiceProcess" 
#r "System.Transactions" 
#r "System.Security" 
#r "System.Windows" 
#r "System.Windows.Forms" 
#r "WindowsBase" 
#r "System.Xml" 
#r "System.Runtime.Serialization" 
#r "System.Xaml" 
#r "System.Net.Http" 
#r "System.Reflection" 
#r "System.Xml.Linq" 