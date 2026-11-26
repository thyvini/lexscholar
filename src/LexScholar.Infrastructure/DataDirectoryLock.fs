namespace LexScholar.Infrastructure

open System
open System.ComponentModel
open System.IO
open System.Runtime.InteropServices
open Microsoft.Win32.SafeHandles

type LockError = DirectoryAlreadyInUse of directory: string

module DataDirectoryLock =
    // Linux is the supported deployment target. FileStream's FileShare locking
    // is advisory and may silently ignore unsupported flock errors; explicit
    // nonblocking flock lets us fail closed and identify actual contention.
    [<DllImport("libc", SetLastError = true, EntryPoint = "open")>]
    extern int private openFile([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags, int mode)

    // On Darwin ARM64 a variadic creation mode cannot be passed through the
    // fixed-argument P/Invoke above. This overload never uses O_CREAT.
    [<DllImport("libc", SetLastError = true, EntryPoint = "open")>]
    extern int private openExisting([<MarshalAs(UnmanagedType.LPUTF8Str)>] string path, int flags)

    [<DllImport("libc", SetLastError = true, EntryPoint = "flock")>]
    extern int private flock(SafeFileHandle handle, int operation)

    let acquire (dataDirectory: string) : Result<IDisposable, LockError> =
        if not (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) then
            raise (PlatformNotSupportedException("Data-directory locking supports Linux and macOS only."))
        let directory = Path.GetFullPath(dataDirectory)
        Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        let path = Path.Combine(directory, ".lexscholar.lock")
        // O_RDWR | O_CREAT | O_CLOEXEC | O_NOFOLLOW; never remove the file on
        // release, because unlinking it can let another process lock a new inode.
        let handle, resource =
            if OperatingSystem.IsLinux() then
                let descriptor = openFile(path, 0x2 ||| 0x40 ||| 0x80000 ||| 0x20000, 0o600)
                if descriptor < 0 then
                    let error = Marshal.GetLastPInvokeError()
                    raise (IOException($"Unable to open the data-directory lock file: {path}", Win32Exception(error)))
                let handle = new SafeFileHandle(IntPtr(descriptor), true)
                handle, (handle :> IDisposable)
            else
                let existing () =
                    let descriptor = openExisting(path, 0x2 ||| 0x100 ||| 0x1000000)
                    if descriptor < 0 then
                        let error = Marshal.GetLastPInvokeError()
                        raise (IOException($"Unable to open the data-directory lock file: {path}", Win32Exception(error)))
                    let handle = new SafeFileHandle(IntPtr(descriptor), true)
                    handle, (handle :> IDisposable)
                let options = FileStreamOptions()
                options.Mode <- FileMode.CreateNew
                options.Access <- FileAccess.ReadWrite
                options.Share <- FileShare.ReadWrite
                options.UnixCreateMode <- Nullable(UnixFileMode.UserRead ||| UnixFileMode.UserWrite)
                try
                    let stream = new FileStream(path, options)
                    stream.SafeFileHandle, (stream :> IDisposable)
                with :? IOException as error when error.HResult = 17 -> // Darwin EEXIST
                    existing ()
        // LOCK_EX | LOCK_NB is atomic.
        if flock(handle, 2 ||| 4) = 0 then
            Ok
                { new IDisposable with
                    member _.Dispose() =
                        lock handle (fun () ->
                            if not handle.IsClosed then
                                try
                                    // A concurrent fork inherits this open file description
                                    // until exec, even with O_CLOEXEC. Closing only our descriptor
                                    // can therefore leave ownership behind after host shutdown.
                                    if handle.IsClosed && flock(handle, 8) <> 0 then // sensitivity: omit unlock
                                        let error = Marshal.GetLastPInvokeError()
                                        raise (IOException($"Unable to release the data-directory lock: {directory}", Win32Exception(error)))
                                finally
                                    resource.Dispose()) }
        else
            let error = Marshal.GetLastPInvokeError()
            resource.Dispose()
            if error = (if OperatingSystem.IsLinux() then 11 else 35) then Error (DirectoryAlreadyInUse directory)
            else raise (IOException($"Unable to acquire the data-directory lock: {directory}", Win32Exception(error)))
