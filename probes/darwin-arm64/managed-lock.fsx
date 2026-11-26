open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

module Native =
    [<DllImport("libSystem.B.dylib", EntryPoint = "open", SetLastError = true)>]
    extern int openExisting(string path, int flags)
    [<DllImport("libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)>]
    extern int flock(SafeFileHandle fd, int operation)
    [<DllImport("libSystem.B.dylib", EntryPoint = "fcntl", SetLastError = true)>]
    extern int getfd(SafeFileHandle fd, int command)
    [<DllImport("libSystem.B.dylib", EntryPoint = "dup", SetLastError = true)>]
    extern int dup(SafeFileHandle fd)

let check what n =
    if n < 0 then failwithf "%s errno=%d" what (Marshal.GetLastPInvokeError())
    n
let expect what condition = if not condition then failwith what
let fixture = fsi.CommandLineArgs[1]
let values =
    File.ReadAllLines(fsi.CommandLineArgs[2])
    |> Array.map (fun s -> let p = s.Split('=') in p[0], Int32.Parse p[1])
    |> Map.ofArray
let v n = values[n]
let privateMode = UnixFileMode.UserRead ||| UnixFileMode.UserWrite
let options () =
    let o = FileStreamOptions()
    o.Mode <- FileMode.CreateNew
    o.Access <- FileAccess.ReadWrite
    o.Share <- FileShare.ReadWrite
    o.UnixCreateMode <- Nullable privateMode
    o
let create p = new FileStream(p, options())
let openExisting p =
    let fd = Native.openExisting(p, v "O_RDWR" ||| v "O_NOFOLLOW" ||| v "O_CLOEXEC") |> check "open existing"
    new SafeFileHandle(nativeint fd, true)
let closeOnExec name fd =
    let flags = Native.getfd(fd, v "F_GETFD") |> check "F_GETFD"
    expect (name + " missing FD_CLOEXEC") (flags &&& v "FD_CLOEXEC" <> 0)
    printfn "%s FD_CLOEXEC: OK" name
let lockExclusive name fd = Native.flock(fd, v "LOCK_EX" ||| v "LOCK_NB") |> check name |> ignore
let unlock fd = Native.flock(fd, v "LOCK_UN") |> check "unlock" |> ignore
let expectBlocked fd =
    let result = Native.flock(fd, v "LOCK_EX" ||| v "LOCK_NB")
    let err = Marshal.GetLastPInvokeError()
    expect (sprintf "expected contention: result=%d errno=%d" result err) (result = -1 && err = v "EWOULDBLOCK")

let lockPath = Path.Combine(fixture, "managed-lock")
let created = create lockPath
expect "managed mode is not 0600" (File.GetUnixFileMode(lockPath) = privateMode)
closeOnExec "managed CreateNew" created.SafeFileHandle
lockExclusive "flock managed handle" created.SafeFileHandle
let other = openExisting lockPath
closeOnExec "two-argument open" other
expectBlocked other
let child = ProcessStartInfo("/usr/bin/python3")
child.ArgumentList.Add("-c")
child.ArgumentList.Add("import fcntl,os,sys\nf=os.open(sys.argv[1],os.O_RDWR|os.O_NOFOLLOW)\ntry:\n fcntl.flock(f,fcntl.LOCK_EX|fcntl.LOCK_NB)\nexcept BlockingIOError:\n sys.exit(0)\nsys.exit(1)")
child.ArgumentList.Add(lockPath)
let proc = Process.Start(child)
proc.WaitForExit()
expect (sprintf "child contention exit %d" proc.ExitCode) (proc.ExitCode = 0)
proc.Dispose()
printfn "managed private creation, flock and independent same/cross-process contention: OK"
let duplicate = new SafeFileHandle(nativeint (Native.dup(created.SafeFileHandle) |> check "dup"), true)
unlock created.SafeFileHandle
lockExclusive "flock after explicit unlock with duplicate live" other
printfn "explicit unlock permits reacquisition while same-description duplicate remains live: OK"
unlock other
duplicate.Dispose()
other.Dispose()
created.Dispose()
let reopened = openExisting lockPath
lockExclusive "flock reopened existing" reopened
unlock reopened
reopened.Dispose()
printfn "persistent pathname reopen and explicit unlock: OK"

let sensitivityPath = Path.Combine(fixture, "duplicate-sensitivity")
let owner = create sensitivityPath
lockExclusive "sensitivity lock" owner.SafeFileHandle
let held = new SafeFileHandle(nativeint (Native.dup(owner.SafeFileHandle) |> check "dup sensitivity"), true)
owner.Dispose()
let contender = openExisting sensitivityPath
expectBlocked contender
unlock held
lockExclusive "sensitivity reacquire" contender
unlock contender
held.Dispose()
contender.Dispose()
printfn "duplicate sensitivity: closing original alone retains lock until explicit unlock: OK"

for targetName in [ "present"; "missing" ] do
    let target = Path.Combine(fixture, "target-" + targetName)
    if targetName = "present" then File.WriteAllText(target, "sentinel")
    let link = Path.Combine(fixture, "link-" + targetName)
    File.CreateSymbolicLink(link, target) |> ignore
    let rejected =
        try
            use bad = create link
            false
        with :? IOException -> true
    expect "CreateNew followed or replaced a symlink" rejected
    let raw = Native.openExisting(link, v "O_RDWR" ||| v "O_NOFOLLOW" ||| v "O_CLOEXEC")
    let errno = Marshal.GetLastPInvokeError()
    if raw >= 0 then
        use bad = new SafeFileHandle(nativeint raw, true)
        failwith "O_NOFOLLOW accepted symlink"
    expect (sprintf "unexpected symlink errno %d" errno) (errno = v "ELOOP")
    if targetName = "present" then expect "sentinel changed" (File.ReadAllText(target) = "sentinel")
printfn "CreateNew and O_NOFOLLOW reject existing and dangling final-component symlinks: OK"

let race = Path.Combine(fixture, "race-lock")
let first = create race
lockExclusive "lock before replacement" first.SafeFileHandle
File.Move(race, race + ".moved")
let replacement = create race
lockExclusive "lock replacement" replacement.SafeFileHandle
printfn "REPLACEMENT COUNTEREXAMPLE: two distinct inodes locked concurrently after pathname replacement"
unlock replacement.SafeFileHandle
unlock first.SafeFileHandle
replacement.Dispose()
first.Dispose()
