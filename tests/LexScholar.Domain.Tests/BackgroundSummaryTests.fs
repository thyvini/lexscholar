/// Status rules of background summaries (F004 T010): the job state's marker and the project roll-up.
module LexScholar.Domain.Tests.BackgroundSummaryTests

open System
open LexScholar.Domain
open Xunit

let private stored : StoredSummary =
    { DocumentId = Guid.NewGuid()
      Points = []
      PagesCovered = [ 1 ]
      UnreadablePages = []
      OmittedPoints = 0
      GeneratedAt = DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero)
      ServiceHost = "h"
      Model = "m" }

[<Fact; Trait("Task", "T010")>]
let ``a job state shows as running, finished or failed`` () =
    Assert.Equal(SummaryStatus.Running, Summaries.statusOf (JobState.Running None))
    Assert.Equal(SummaryStatus.Running, Summaries.statusOf (JobState.Running (Some Progress.Combining)))
    Assert.Equal(SummaryStatus.Finished, Summaries.statusOf (JobState.Finished stored))
    Assert.Equal(SummaryStatus.Failed, Summaries.statusOf (JobState.Failed ("service_failed", "", None)))

[<Fact; Trait("Task", "T010")>]
let ``a project with no summaries has no marker`` () =
    Assert.Equal(None, Summaries.rollUp [])

[<Fact; Trait("Task", "T010")>]
let ``any running summary makes the project running, whatever else there is`` () =
    Assert.Equal(Some SummaryStatus.Running, Summaries.rollUp [ SummaryStatus.Finished; SummaryStatus.Failed; SummaryStatus.Running ])

[<Fact; Trait("Task", "T010")>]
let ``without a running one, any failed makes the project failed`` () =
    Assert.Equal(Some SummaryStatus.Failed, Summaries.rollUp [ SummaryStatus.Finished; SummaryStatus.Failed ])

[<Fact; Trait("Task", "T010")>]
let ``only finished summaries make the project finished`` () =
    Assert.Equal(Some SummaryStatus.Finished, Summaries.rollUp [ SummaryStatus.Finished; SummaryStatus.Finished ])
