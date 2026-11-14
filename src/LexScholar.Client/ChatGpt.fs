/// The researcher's ChatGPT sign-in: pure state, no HTTP and no rendering (see App.fs).
module LexScholar.Client.ChatGpt

type Account = { Name: string; Confirmed: bool }

type Status =
    | Unknown
    | SignedOut
    /// The authorization address was requested; the tab navigates to it next.
    | Starting
    | SignedIn of Account

/// What the destinations answer says about the sign-in.
type Destinations = { Account: Account option; WelcomePending: bool }

/// A message shown once above the account line, then dismissed.
type Notice =
    | Cancelled
    | NoPlanAccess
    | Failed
    | StartFailed
    | NotRevoked
    | SignOutFailed

type StartProblem =
    | AlreadySignedIn
    | RequestFailed

type Model =
    { Status: Status
      WelcomePending: bool
      Notice: Notice option
      /// A sign-out is in flight; sign-in and sign-out are ignored meanwhile.
      SigningOut: bool
      /// Only the response to the latest destinations request is used.
      Request: int }

type Message =
    | Load
    | Loaded of request: int * Result<Destinations, unit>
    /// The `chatgpt` query value of the page address after returning from ChatGPT.
    | Returned of string option
    | SignIn
    | AuthorizationReady of Result<string, StartProblem>
    | SignOut
    | SignedOutResult of Result<bool, unit>
    | DismissWelcome
    | DismissNotice

type Effect =
    | FetchDestinations of request: int
    | StartSignIn
    /// Navigate the tab to the authorization address.
    | Navigate of url: string
    | SendSignOut
    | SendDismissWelcome

let initial = { Status = Unknown; WelcomePending = false; Notice = None; SigningOut = false; Request = 0 }

let update (message: Message) (model: Model) : Model * Effect option =
    model, None
