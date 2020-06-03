[<AutoOpen>]
module Shipping.Domain.Tests.Infrastructure

open System.Collections.Concurrent

type EventAccumulator<'E>() =
    let messages = ConcurrentDictionary<FsCodec.StreamName, ConcurrentQueue<'E>>()

    member __.Record(stream, events : 'E seq) =
        let initStreamQueue _ = ConcurrentQueue events
        let appendToQueue _ (queue : ConcurrentQueue<'E>) = events |> Seq.iter queue.Enqueue; queue
        messages.AddOrUpdate(stream, initStreamQueue, appendToQueue) |> ignore

    member __.Queue stream =
        match messages.TryGetValue stream with
        | false, _ -> Seq.empty<'E>
        | true, xs -> xs :> _

    member __.All() = seq { for KeyValue (_, xs) in messages do yield! xs }

    member __.Clear() =
        messages.Clear()