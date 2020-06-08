module sqlstreamstore_draft.Handler

// Each outcome from `handle` is passed to `HandleOk` or `HandleExn` by the scheduler, DumpStats is called at `statsInterval`
// The incoming calls are all sequential - the logic does not need to consider concurrent incoming calls
type ProjectorStats(log, statsInterval, stateInterval) =
    inherit Propulsion.Streams.Projector.Stats<int>(log, statsInterval, stateInterval)

    let mutable totalCount = 0

    // TODO consider best balance between logging or gathering summary information per handler invocation
    // here we don't log per invocation (such high level stats are already gathered and emitted) but accumulate for periodic emission
    override __.HandleOk count =
        totalCount <- totalCount + count
    // TODO consider whether to log cause of every individual failure in full (Failure counts are emitted periodically)
    override __.HandleExn exn =
        log.Information(exn, "Unhandled")

    override __.DumpStats() =
        log.Information(" Total events processed {total}", totalCount)
        totalCount <- 0

let handle (_stream, span: Propulsion.Streams.StreamSpan<_>) = async {
    let r = System.Random()
    let ms = r.Next(1, span.events.Length)
    do! Async.Sleep ms
    return Propulsion.Streams.SpanResult.AllProcessed, span.events.Length }
