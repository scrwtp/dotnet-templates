[<AutoOpen>]
module Fc.Infrastructure

open Serilog
open System

let (|Id|) (x : Guid) = x.ToString "N" |> FSharp.UMX.UMX.tag
let inline mkId () = Guid.NewGuid() |> (|Id|)
let (|Ids|) (xs : Guid[]) = xs |> Array.map (|Id|)
let (|IdsAtLeastOne|) (Id x, Ids xs) = Seq.append xs (Seq.singleton x) |> Seq.toArray

module EnvVar =

    let tryGet k = Environment.GetEnvironmentVariable k |> Option.ofObj

module Cosmos =

    open Equinox.Cosmos
    let connect () =
        match EnvVar.tryGet "EQUINOX_COSMOS_CONNECTION", EnvVar.tryGet "EQUINOX_COSMOS_DATABASE", EnvVar.tryGet "EQUINOX_COSMOS_CONTAINER" with
        | Some s, Some d, Some c ->
            let appName = "Domain.Tests"
            let discovery = Discovery.FromConnectionString s
            let connector = Connector(TimeSpan.FromSeconds 5., 5, TimeSpan.FromSeconds 5., Serilog.Log.Logger)
            let connection = connector.Connect(appName, discovery) |> Async.RunSynchronously
            let context = Context(connection, d, c)
            let cache = Equinox.Cache (appName, 10)
            context, cache
        | s, d, c ->
            failwithf "Connection, Database and Container EQUINOX_COSMOS_* Environment variables are required (%b,%b,%b)"
                (Option.isSome s) (Option.isSome d) (Option.isSome c)

/// Adapts the XUnit ITestOutputHelper to be a Serilog Sink
type TestOutputAdapter(testOutput : Xunit.Abstractions.ITestOutputHelper) =
    let template = "{Timestamp:HH:mm:ss.fff zzz} [{Level:u3}] {Message} {Properties}{NewLine}{Exception}"
    let formatter = Serilog.Formatting.Display.MessageTemplateTextFormatter(template, null);
    let writeSerilogEvent logEvent =
        use writer = new System.IO.StringWriter()
        formatter.Format(logEvent, writer)
        let messageLine = string writer
        testOutput.WriteLine messageLine
        System.Diagnostics.Debug.Write messageLine
    interface Serilog.Core.ILogEventSink with member __.Emit logEvent = writeSerilogEvent logEvent

/// Creates a Serilog Log chain emitting to the cited Sink (only)
let createLogger sink =
    Serilog.LoggerConfiguration()
//        .MinimumLevel.Debug()
        .Destructure.FSharpTypes()
        .WriteTo.Sink(sink)
        .CreateLogger()
