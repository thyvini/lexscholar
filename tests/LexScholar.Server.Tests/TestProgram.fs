module LexScholar.Server.Tests.TestProgram

// Preserve xUnit's generated runner dispatch while adding one test-only child
// mode. CompileAfter places this after the generated MTP extensions in F# order.
[<EntryPoint>]
let main args =
    if Array.tryHead args = Some "--foundation-host" then
        ProcessHarness.runChild args.[1..]
    elif Array.exists (fun arg -> arg = "--server" || arg = "--internal-msbuild-node") args then
        Xunit.MicrosoftTestingPlatform.TestPlatformTestFramework.RunAsync(
            args,
            System.Action<_, _>(fun builder arguments -> SelfRegisteredExtensions.AddSelfRegisteredExtensions(builder, arguments))
        ).GetAwaiter().GetResult()
    else
        Xunit.Runner.InProc.SystemConsole.ConsoleRunner.Run(args).GetAwaiter().GetResult()
