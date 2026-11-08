namespace LexScholar.Application

open System
open System.IO
open System.Threading
open System.Threading.Tasks

// For every stream-opening capability, successful task completion transfers
// ownership even if cancellation raced with it. On a faulted or cancelled task,
// no stream is transferred and the provider must release acquired resources.

/// Journal operations for recovery; the existing PendingImport is the only
/// journal representation. Persistence and recovery behavior belong to T028.
type ImportJournalOperations =
    { Create: PendingImport -> CancellationToken -> Task<unit>
      Update: PendingImport -> CancellationToken -> Task<unit>
      Remove: Guid -> CancellationToken -> Task<unit>
      Recover: CancellationToken -> Task<unit> }

/// Opens a document by UUID after verifying its original bytes. Verification
/// and delivery use the same read-only protected handle, with bytes protected
/// from change until disposal, without assuming a viewer. On success the caller
/// owns the returned stream and must dispose it; on cancellation or failure
/// before success the provider disposes its resources. The token covers
/// opening/verification; callers pass a token to later reads.
type OpenVerifiedContent = Guid -> CancellationToken -> Task<VerifiedContent>

// Staging capabilities consume an internal journal entry, not a user-supplied
// path or filename. Its keys must be opaque, server-generated and validated by
// the implementation. Persist the journal entry before opening the writer.
// Key allocation and the detailed recovery protocol are deferred to T026/T028.

/// Open only this operation's uploading artifact for writing, never a published
/// original. A successful return transfers the stream to the caller, who must
/// dispose it before validation or promotion; if opening fails or is cancelled,
/// the provider must dispose any resources it acquired. The token covers opening;
/// callers pass a token to subsequent stream writes.
type OpenStagedWrite = PendingImport -> CancellationToken -> Task<Stream>

/// Open the operation-owned staged bytes read-only for streamed validation/comparison.
/// The caller owns and disposes a successfully returned stream, including when
/// later reads fail or are cancelled. The provider cleans up on failed opening.
/// The token covers opening; callers pass a token to subsequent stream reads.
type OpenStagedRead = PendingImport -> CancellationToken -> Task<Stream>

/// Promote validated staged bytes without overwriting or mutating an existing
/// original. The storage implementation owns atomic same-filesystem promotion.
/// Cancellation may race with promotion; recovery must reconcile durable effects.
type PromoteStagedContent = PendingImport -> CancellationToken -> Task<unit>

/// Discard only this operation's staging artifact, never a committed original
/// or another operation's artifacts. Recovery policy remains with T028.
type DiscardStagedContent = PendingImport -> CancellationToken -> Task<unit>
