# Aardvark.Fake

This repository contains some useful extensions for the [Fake][1]/[Paket][2] infrastructure, 
which we use in our projects, such as [Aardvark.Rendering][3] and [Aardvark.Base][4].
Most importantly, AdditionalSources.fsx provides functionality ``cabal add-source`` like functionality for referencing source versions of dependent packages.

in out packages we typically use a paket.dependencies config as such:

```group Build
content: none
source https://vrvis.myget.org/F/aardvark_public/api/v3/index.json
source https://api.nuget.org/v3/index.json

github vrvis/Aardvark.Fake:standalone 
```

while the build script might look like (DefaultSetup is the main entry script):

```
#load @"paket-files/build/vrvis/Aardvark.Fake/DefaultSetup.fsx"

open Fake
open Aardvark.Fake
open Fake.Testing

do Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

DefaultSetup.install ["src/Aardvark.sln"]
```

[1]: http://fsharp.github.io/FAKE/
[2]: https://github.com/fsprojects/Paket
[3]: https://github.com/vrvis/aardvark.rendering
[4]: https://github.com/vrvis/aardvark

