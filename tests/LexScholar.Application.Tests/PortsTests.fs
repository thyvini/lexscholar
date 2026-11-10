module LexScholar.Application.Tests.PortsTests

open System
open System.IO
open System.Threading
open System.Threading.Tasks
open LexScholar.Application
open Xunit

type private TrackingStream() =
    inherit MemoryStream()
    let mutable disposed = false
    member _.Disposed = disposed
    override this.Dispose(disposing) =
        if disposing then disposed <- true
        base.Dispose(disposing)

type private TrackingReadStream(bytes: byte array) =
    inherit MemoryStream(bytes, false)
    let mutable disposed = false
    member _.Disposed = disposed
    override this.Dispose(disposing) =
        if disposing then disposed <- true
        base.Dispose(disposing)

let private stagingKey () = StagingKey (Guid.NewGuid())

let private notImplemented<'a> () = Task.FromException<'a>(NotImplementedException())

[<Fact; Trait("Task", "T011")>]
let ``application contracts do not depend on host or infrastructure assemblies`` () =
    let references = typeof<ProjectOperations>.Assembly.GetReferencedAssemblies() |> Array.map (fun name -> name.Name)
    let forbidden = [ "LexScholar.Infrastructure"; "LexScholar.Server"; "Microsoft.EntityFrameworkCore"; "Microsoft.Data.Sqlite"; "Microsoft.AspNetCore" ]
    for prefix in forbidden do
        Assert.False(references |> Array.exists (fun name -> name.StartsWith(prefix, StringComparison.Ordinal)), $"Application references {prefix}.")

[<Fact; Trait("Task", "T011")>]
let ``existing project and library capabilities remain individually consumable`` () =
    use cancellation = new CancellationTokenSource()
    let mutable projectToken = CancellationToken.None
    let mutable libraryToken = CancellationToken.None
    let projects : ProjectOperations =
        { List = fun token -> projectToken <- token; Task.FromResult({ ActiveProjectId = None; Projects = [] })
          Create = fun _ _ -> notImplemented<Project>()
          Select = fun _ _ -> notImplemented<unit>() }
    let library : LibraryOperations =
        { List = fun token -> libraryToken <- token; Task.FromResult([])
          Import = fun _ _ _ -> notImplemented<ImportResult>()
          ListForProject = fun _ _ -> notImplemented<ProjectDocumentView list>()
          Associate = fun _ _ _ _ -> notImplemented<ProjectDocumentView>()
          ChangeRole = fun _ _ _ _ -> notImplemented<ProjectDocumentView>()
          RemoveAssociation = fun _ _ _ -> notImplemented<unit>() }
    let readProjects (list: CancellationToken -> Task<ProjectList>) = list cancellation.Token
    let readLibrary (list: CancellationToken -> Task<Document list>) = list cancellation.Token
    let projectList = readProjects projects.List |> fun result -> result.GetAwaiter().GetResult()
    let documentList = readLibrary library.List |> fun result -> result.GetAwaiter().GetResult()
    Assert.Empty(projectList.Projects)
    Assert.Empty(documentList)
    Assert.Equal(cancellation.Token, projectToken)
    Assert.Equal(cancellation.Token, libraryToken)

[<Fact; Trait("Task", "T011")>]
let ``caller streams verified content and owns the returned handle`` () =
    let id = Guid.NewGuid()
    use cancellation = new CancellationTokenSource()
    let bytes = [| 1uy; 2uy; 3uy; 4uy |]
    let stream = new TrackingReadStream(bytes)
    let mutable observed = false
    let openContent : OpenVerifiedContent = fun documentId token ->
        Assert.Equal(id, documentId)
        Assert.Equal(cancellation.Token, token)
        observed <- true
        Task.FromResult({ Stream = stream :> Stream; ByteLength = int64 bytes.Length; OriginalFileName = "example.pdf" })
    let consume (openRead: OpenVerifiedContent) =
        task {
            let! content = openRead id cancellation.Token
            Assert.False(stream.Disposed)
            use owned = content.Stream
            Assert.False(owned.CanWrite)
            let buffer = Array.zeroCreate<byte> 2
            let! count = owned.ReadAsync(buffer.AsMemory(), cancellation.Token)
            return count, buffer, content.ByteLength
        }
    let count, buffer, length = consume openContent |> fun operation -> operation.GetAwaiter().GetResult()
    Assert.True(observed)
    Assert.Equal(2, count)
    Assert.Equal<byte array>([| 1uy; 2uy |], buffer)
    Assert.Equal(4L, length)
    Assert.True(stream.Disposed)

[<Fact; Trait("Task", "T011")>]
let ``staging writer is caller-owned while the source remains borrowed`` () =
    let key = stagingKey ()
    use cancellation = new CancellationTokenSource()
    use source = new MemoryStream([| 4uy; 5uy; 6uy |])
    let writer = new TrackingStream()
    let mutable observed = false
    let openWrite : OpenStagedWrite = fun actual token ->
        Assert.Equal(key, actual)
        Assert.Equal(cancellation.Token, token)
        observed <- true
        Task.FromResult(writer :> Stream)
    let stage (openWriter: OpenStagedWrite) (input: Stream) =
        task {
            let! output = openWriter key cancellation.Token
            use owned = output
            do! input.CopyToAsync(owned, 2, cancellation.Token)
        }
    stage openWrite source |> fun operation -> operation.GetAwaiter().GetResult()
    Assert.True(observed)
    Assert.True(writer.Disposed)
    Assert.True(source.CanRead)
    Assert.Equal<byte array>([| 4uy; 5uy; 6uy |], writer.ToArray())

[<Fact; Trait("Task", "T011")>]
let ``validation and cancelled import leave caller-owned inputs open`` () =
    use cancellation = new CancellationTokenSource()
    use input = new TrackingStream()
    let limits = { MaxBytes = 100L; MaxPages = 5 }
    let validate : ValidatePdf = fun source actualLimits token ->
        Assert.Same(input, source)
        Assert.Equal(limits, actualLimits)
        Assert.Equal(cancellation.Token, token)
        Task.FromResult(Ok { Sha256 = "test"; ByteLength = 1L; PageCount = 1 })
    validate input limits cancellation.Token |> fun operation -> operation.GetAwaiter().GetResult() |> ignore
    Assert.False(input.Disposed)
    cancellation.Cancel()
    let library =
        { Library.unimplemented with
            Import = fun source _ token ->
                Assert.Same(input, source)
                Assert.Equal(cancellation.Token, token)
                Task.FromCanceled<ImportResult>(token) }
    Assert.ThrowsAny<OperationCanceledException>(fun () ->
        library.Import input "example.pdf" cancellation.Token |> fun operation -> operation.GetAwaiter().GetResult() |> ignore) |> ignore
    Assert.False(input.Disposed)

[<Fact; Trait("Task", "T011")>]
let ``caller disposes an opened staging stream when a later read is cancelled`` () =
    let key = stagingKey ()
    use cancellation = new CancellationTokenSource()
    let stream = new TrackingReadStream([| 1uy |])
    let openRead : OpenStagedRead = fun actual token ->
        Assert.Equal(key, actual)
        Assert.Equal(cancellation.Token, token)
        Task.FromResult(stream :> Stream)
    let consume (openStaged: OpenStagedRead) =
        task {
            let! opened = openStaged key cancellation.Token
            use owned = opened
            Assert.False(owned.CanWrite)
            cancellation.Cancel()
            let buffer = Array.zeroCreate<byte> 1
            let! _ = owned.ReadAsync(buffer.AsMemory(), cancellation.Token)
            return ()
        }
    Assert.ThrowsAny<OperationCanceledException>(fun () -> consume openRead |> fun operation -> operation.GetAwaiter().GetResult()) |> ignore
    Assert.True(stream.Disposed)

[<Fact; Trait("Task", "T011")>]
let ``cancelled opening leaves acquired resources with the provider`` () =
    let key = stagingKey ()
    use cancellation = new CancellationTokenSource()
    let started = TaskCompletionSource<unit>(TaskCreationOptions.RunContinuationsAsynchronously)
    let stream = new TrackingReadStream([| 1uy |])
    let openRead : OpenStagedRead = fun actual token ->
        Assert.Equal(key, actual)
        Assert.Equal(cancellation.Token, token)
        task {
            try
                started.SetResult(())
                do! Task.Delay(Timeout.Infinite, token)
                return stream :> Stream
            with error ->
                stream.Dispose()
                return raise error
        }
    let opening = openRead key cancellation.Token
    started.Task.GetAwaiter().GetResult()
    cancellation.Cancel()
    Assert.ThrowsAny<OperationCanceledException>(fun () -> opening.GetAwaiter().GetResult() |> ignore) |> ignore
    Assert.True(stream.Disposed)

[<Fact; Trait("Task", "T011")>]
let ``successful open transfers ownership even if cancellation races`` () =
    let key = stagingKey ()
    use cancellation = new CancellationTokenSource()
    let stream = new TrackingReadStream([| 1uy |])
    let openRead : OpenStagedRead = fun _ token ->
        Assert.Equal(cancellation.Token, token)
        cancellation.Cancel()
        Task.FromResult(stream :> Stream)
    use opened = openRead key cancellation.Token |> fun result -> result.GetAwaiter().GetResult()
    Assert.True(cancellation.IsCancellationRequested)
    Assert.False(stream.Disposed)
    opened.Dispose()
    Assert.True(stream.Disposed)

[<Fact; Trait("Task", "T011")>]
let ``promotion and staging cleanup remain separately injectable`` () =
    let key = stagingKey ()
    use cancellation = new CancellationTokenSource()
    let calls = ResizeArray<string * StagingKey * CancellationToken>()
    let promote : PromoteStagedContent = fun item token -> calls.Add("promote", item, token); Task.FromResult("generated-storage-key")
    let discard : DiscardStagedContent = fun item token -> calls.Add("discard", item, token); Task.FromResult(())
    let storageKey = promote key cancellation.Token |> fun operation -> operation.GetAwaiter().GetResult()
    discard key cancellation.Token |> fun operation -> operation.GetAwaiter().GetResult()
    Assert.Equal("generated-storage-key", storageKey)
    Assert.Equal<string list>([ "promote"; "discard" ], calls |> Seq.map (fun (name, _, _) -> name) |> Seq.toList)
    Assert.All(calls, fun (_, item, token) ->
        Assert.Equal(key, item)
        Assert.Equal(cancellation.Token, token))
