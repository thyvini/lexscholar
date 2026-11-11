namespace LexScholar.Server

open System
open System.Globalization
open System.IO
open Microsoft.Extensions.Configuration
open LexScholar.Application

type WorkspaceConfiguration =
    { DataDirectory: string
      ImportLimits: ImportLimits }

type ConfigurationError =
    | InvalidLimit of name: string * value: string
    | NonPositiveLimit of name: string
    | InvalidDataDirectory of reason: string

module Configuration =
    [<Literal>]
    let DataDirectoryKey = "LexScholar:DataDirectory"
    [<Literal>]
    let MaxBytesKey = "LexScholar:ImportLimits:MaxBytes"
    [<Literal>]
    let MaxPagesKey = "LexScholar:ImportLimits:MaxPages"

    let private validatePath path =
        if String.IsNullOrWhiteSpace(path) then
            Error (InvalidDataDirectory "The path must not be empty.")
        elif not (Path.IsPathFullyQualified(path)) || path.Contains('\000') then
            Error (InvalidDataDirectory "Use an absolute local filesystem path without NUL characters.")
        else
            try
                let fullPath = Path.GetFullPath(path)
                if fullPath = Path.GetPathRoot(fullPath) then
                    Error (InvalidDataDirectory "The filesystem root cannot be used as the data directory.")
                elif File.Exists(fullPath) then
                    Error (InvalidDataDirectory "The configured path is an existing file, not a directory.")
                else Ok fullPath
            with
            | :? ArgumentException
            | :? NotSupportedException
            | :? PathTooLongException -> Error (InvalidDataDirectory "The path is not valid on this system.")

    let private limit name defaultValue zero parse (value: string option) =
        match value with
        | None -> Ok defaultValue
        | Some text ->
            match parse text with
            | true, number when number > zero -> Ok number
            | true, _ -> Error (NonPositiveLimit name)
            | false, _ -> Error (InvalidLimit (name, text))

    let private parseInt64 (text: string) = Int64.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)
    let private parseInt (text: string) = Int32.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture)

    /// Environment lookup and IConfiguration access remain at the composition boundary.
    let load (configuration: IConfiguration) : Result<WorkspaceConfiguration, ConfigurationError> =
        let get key = configuration.[key] |> Option.ofObj
        let directory =
            match get DataDirectoryKey with
            | Some path -> path
            | None ->
                let xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                let baseDirectory =
                    if not (String.IsNullOrWhiteSpace(xdg)) && Path.IsPathFullyQualified(xdg) then xdg
                    else Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
                Path.Combine(baseDirectory, "lexscholar")
        validatePath directory
        |> Result.bind (fun path ->
            limit MaxBytesKey 100_000_000L 0L parseInt64 (get MaxBytesKey)
            |> Result.bind (fun bytes ->
                limit MaxPagesKey 2_000 0 parseInt (get MaxPagesKey)
                |> Result.map (fun pages ->
                    { DataDirectory = path
                      ImportLimits = { MaxBytes = bytes; MaxPages = pages } })))
