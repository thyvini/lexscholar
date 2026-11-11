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

    [<DllImport("libc", SetLastError = true, EntryPoint = "flock")>]
    extern int private flock(SafeFileHandle handle, int operation)

    let acquire (dataDirectory: string) : Result<IDisposable, LockError> =
        if not (OperatingSystem.IsLinux()) then
            raise (PlatformNotSupportedException("Data-directory locking currently supports Linux only."))
        let directory = Path.GetFullPath(dataDirectory)
        Directory.CreateDirectory(directory, UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute) |> ignore
        let path = Path.Combine(directory, ".lexscholar.lock")
        // O_RDWR | O_CREAT | O_CLOEXEC | O_NOFOLLOW; never remove the file on
        // release, because unlinking it can let another process lock a new inode.
        let descriptor = openFile(path, 0x2 ||| 0x40 ||| 0x80000 ||| 0x20000, 0o600)
        if descriptor < 0 then
            let error = Marshal.GetLastPInvokeError()
            raise (IOException($"Unable to open the data-directory lock file: {path}", Win32Exception(error)))
        let handle = new SafeFileHandle(IntPtr(descriptor), true)
        // LOCK_EX | LOCK_NB is atomic; descriptor closure releases the lock.
        if flock(handle, 2 ||| 4) = 0 then Ok (handle :> IDisposable)
        else
            let error = Marshal.GetLastPInvokeError()
            handle.Dispose()
            if error = 11 then Error (DirectoryAlreadyInUse directory) // Linux EWOULDBLOCK/EAGAIN
            else raise (IOException($"Unable to acquire the data-directory lock: {directory}", Win32Exception(error)))
