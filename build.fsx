#!/usr/bin/env dotnet fsi
#r "nuget: Fun.Build, 1.1.17"

open Fun.Build

let sln = "src/AgentNet.sln"
let outputDir = ".build"

pipeline "publish" {
    description "Clean, build, test, pack and push all NuGet packages"

    stage "clean" {
        run $"dotnet clean {sln} -c Release --verbosity quiet"
        run (fun _ ->
            if System.IO.Directory.Exists(outputDir) then
                System.IO.Directory.Delete(outputDir, true)
        )
    }

    stage "build" {
        run $"dotnet build {sln} -c Release"
    }

    // Tests run in Debug because DurableIdTests.getDisplayName relies on
    // reflection metadata that the Release compiler optimizes away.
    // TODO: fix the test to be Release-safe, then switch back to -c Release --no-build.
    stage "test" {
        run "dotnet test src/AgentNet.Tests/AgentNet.Tests.fsproj"
    }

    stage "pack" {
        run (fun _ ->
            if not (System.IO.Directory.Exists(outputDir)) then
                System.IO.Directory.CreateDirectory(outputDir) |> ignore
        )
        run $"dotnet pack src/AgentNet/AgentNet.fsproj -c Release --no-build --output {outputDir}"
        run $"dotnet pack src/AgentNet.Durable/AgentNet.Durable.fsproj -c Release --no-build --output {outputDir}"
        run $"dotnet pack src/AgentNet.InProcess/AgentNet.InProcess.fsproj -c Release --no-build --output {outputDir}"
        run $"dotnet pack src/AgentNet.InProcess.Polly/AgentNet.InProcess.Polly.fsproj -c Release --no-build --output {outputDir}"
    }

    stage "push" {
        whenEnvVar "SQLHYDRA_NUGET_KEY"
        run (fun ctx ->
            let key = ctx.GetEnvVar "SQLHYDRA_NUGET_KEY"
            ctx.RunSensitiveCommand $"""dotnet nuget push {outputDir}/*.nupkg -s https://api.nuget.org/v3/index.json -k {key}"""
        )
    }

    runIfOnlySpecified
}

pipeline "test" {
    description "Build and run tests only"

    stage "build" {
        run $"dotnet build {sln} -c Release"
    }

    stage "test" {
        run "dotnet test src/AgentNet.Tests/AgentNet.Tests.fsproj"
    }

    runIfOnlySpecified
}
