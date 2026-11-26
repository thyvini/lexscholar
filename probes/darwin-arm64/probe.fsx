open System
open System.IO
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

module Native =
    [<DllImport("libSystem.B.dylib", EntryPoint = "open", SetLastError = true)>]
    extern int openFile(string path, int flags, uint32 mode)

    [<DllImport("libSystem.B.dylib", EntryPoint = "fcntl", SetLastError = true)>]
    extern int fcntl(int fd, int command, int argument)

    [<DllImport("libSystem.B.dylib", EntryPoint = "fsync", SetLastError = true)>]
    extern int fsync(int fd)

    [<DllImport("libSystem.B.dylib", EntryPoint = "close", SetLastError = true)>]
    extern int close(int fd)

let check operation result =
    if result < 0 then
        failwithf "%s failed: errno %d" operation (Marshal.GetLastPInvokeError())
    result

if RuntimeInformation.OSArchitecture <> Architecture.Arm64
   || RuntimeInformation.ProcessArchitecture <> Architecture.Arm64 then
    failwithf "Expected native ARM64: OS=%A process=%A" RuntimeInformation.OSArchitecture RuntimeInformation.ProcessArchitecture

printfn "OS architecture: %A; process architecture: %A" RuntimeInformation.OSArchitecture RuntimeInformation.ProcessArchitecture
let fixture = fsi.CommandLineArgs[1]
let constants =
    File.ReadAllLines(fsi.CommandLineArgs[2])
    |> Array.map (fun line -> let parts = line.Split('=') in parts[0], Int32.Parse(parts[1]))
    |> Map.ofArray
let value name = constants[name]
let path = Path.Combine(fixture, "fixture")
let fd = Native.openFile(path, value "O_CREAT" ||| value "O_EXCL" ||| value "O_RDWR", 0o600u) |> check "open O_CREAT"
let duplicate =
    try Native.fcntl(fd, value "F_DUPFD_CLOEXEC", 0) |> check "fcntl F_DUPFD_CLOEXEC"
    finally Native.close(fd) |> check "close original" |> ignore
if Native.fcntl(duplicate, value "F_GETFD", 0) |> check "fcntl F_GETFD" &&& value "FD_CLOEXEC" = 0 then
    failwith "Duplicate is missing FD_CLOEXEC"
let handle = new SafeFileHandle(nativeint duplicate, true)
let stream = new FileStream(handle, FileAccess.ReadWrite)
let bytes = [| 65uy; 82uy; 77uy |]
stream.Write(bytes, 0, bytes.Length)
stream.Flush(true)
stream.Position <- 0L
let read = Array.zeroCreate<byte> bytes.Length
if stream.Read(read, 0, read.Length) <> bytes.Length || read <> bytes then
    failwith "Duplicated descriptor did not preserve file access"
let mode = File.GetUnixFileMode(path)
if mode <> (UnixFileMode.UserRead ||| UnixFileMode.UserWrite) then
    failwithf "Unexpected created file mode: %A" mode
stream.Dispose()
printfn "open O_CREAT mode 0600 and duplicated descriptor: OK"
let directory = Native.openFile(fixture, value "O_DIRECTORY", 0u) |> check "open directory"
try
    Native.fsync(directory) |> check "fsync directory" |> ignore
    printfn "directory fsync: OK"
finally
    Native.close(directory) |> check "close directory" |> ignore
