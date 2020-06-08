module sqlstreamstore_draft.Program

open Serilog
open System

module EnvVar =

    let tryGet varName : string option = Environment.GetEnvironmentVariable varName |> Option.ofObj
    let set varName value : unit = Environment.SetEnvironmentVariable(varName, value)

// TODO remove this entire comment after reading https://github.com/jet/dotnet-templates#module-configuration
// - this is where any custom retrieval of settings not arriving via commandline arguments or environment variables should go
// - values should be propagated by setting environment variables and/or returning them from `initialize`
module Configuration =

    let private initEnvVar var key loadF =
        if None = EnvVar.tryGet var then
            printfn "Setting %s from %A" var key
            EnvVar.set var (loadF key)

    let initialize () =
        // e.g. initEnvVar     "EQUINOX_COSMOS_CONTAINER"    "CONSUL KEY" readFromConsul
        () // TODO add any custom logic preprocessing commandline arguments and/or gathering custom defaults from external sources, etc

// TODO remove this entire comment after reading https://github.com/jet/dotnet-templates#module-args
// - this module is responsible solely for parsing/validating the commandline arguments (including falling back to values supplied via environment variables)
// - It's expected that the properties on *Arguments types will summarize the active settings as a side effect of
// TODO DONT invest time reorganizing or reformatting this - half the value is having a legible summary of all program parameters in a consistent value
//      you may want to regenerate it at a different time and/or facilitate comparing it with the `module Args` of other programs
// TODO NEVER hack temporary overrides in here; if you're going to do that, use commandline arguments that fall back to environment variables
//      or (as a last resort) supply them via code in `module Configuration`
module Args =

    exception MissingArg of string
    let private getEnvVarForArgumentOrThrow varName argName =
        match EnvVar.tryGet varName with
        | None -> raise (MissingArg(sprintf "Please provide a %s, either as an argument or via the %s environment variable" argName varName))
        | Some x -> x
    let private defaultWithEnvVar varName argName = function
        | None -> getEnvVarForArgumentOrThrow varName argName
        | Some x -> x
    let private isEnvVarTrue varName =
         EnvVar.tryGet varName |> Option.exists (fun s -> String.Equals(s, bool.TrueString, StringComparison.OrdinalIgnoreCase))
    let private seconds (x : TimeSpan) = x.TotalSeconds

    open Argu
    open Propulsion.SqlStreamStore

    /// TODO: add DB connectors other than MsSql
    type [<NoEquality; NoComparison>] SqlStreamStoreSourceParameters =
        | [<AltCommandLine "-t"; Unique>]    Tail of intervalS: float
        | [<AltCommandLine "-m"; Unique>]    BatchSize of int
        | [<AltCommandLine "-c"; Unique>]    Connection of string
        | [<Unique>]                         Checkpoints of string

        interface IArgParserTemplate with
            member a.Usage = a |> function
                | Tail _ ->          "attempt to read from tail at specified interval in Seconds. Default: 1"
                | BatchSize _ ->     "maximum item count to request from feed. Default: 512"
                | Connection _ ->    "connection string for SqlStreamStore db"
                | Checkpoints _ ->   "connection string for Checkpoints sql db. Will use Connection if not provided"

    and SqlStreamStoreSourceArguments(a : ParseResults<SqlStreamStoreSourceParameters>) =
        member __.TailInterval =            a.GetResult(Tail, 1.) |> TimeSpan.FromSeconds
        member __.MaxBatchSize =            a.GetResult(BatchSize, 512)
        member __.StoreConnectionString =
            a.TryGetResult(Connection)
            |> defaultWithEnvVar "SQLSTREAMSTORE_CONNECTION" "Connection"
        member __.CheckpointsConnectionString =
            a.TryGetResult(Checkpoints)
            |> Option.orElseWith (fun () -> EnvVar.tryGet "SQLSTREAMSTORE_CHECKPOINTS_CONNECTION")
            |> Option.defaultValue __.StoreConnectionString

        member x.Connect() =
            let connector = Equinox.SqlStreamStore.MsSql.Connector(x.StoreConnectionString)
            connector.Connect() |> Async.RunSynchronously

    [<NoEquality; NoComparison>]
    type Parameters =
        | [<AltCommandLine "-V"; Unique>]   Verbose
        | [<AltCommandLine "-g"; Mandatory>] ConsumerGroupName of string
        | [<AltCommandLine "-r"; Unique>]   MaxReadAhead of int
        | [<AltCommandLine "-w"; Unique>]   MaxWriters of int
        | [<CliPrefix(CliPrefix.None); Last>] SqlStreamStore of ParseResults<SqlStreamStoreSourceParameters>
        interface IArgParserTemplate with
            member a.Usage =
                match a with
                | Verbose ->                "Request Verbose Logging. Default: off"
                | ConsumerGroupName _ ->    "Projector consumer group name."
                | MaxReadAhead _ ->         "maximum number of batches to let processing get ahead of completion. Default: 64"
                | MaxWriters _ ->           "maximum number of concurrent streams on which to process at any time. Default: 1024"
                | SqlStreamStore _ ->       "specify SqlStreamStore input parameters."
    and Arguments(a : ParseResults<Parameters>) =
        member __.Verbose =                 a.Contains Verbose
        member __.ConsumerGroupName =       a.GetResult ConsumerGroupName
        member __.MaxReadAhead =            a.GetResult(MaxReadAhead, 64)
        member __.MaxConcurrentProcessors = a.GetResult(MaxWriters, 1024)
        member __.StatsInterval =           TimeSpan.FromMinutes 1.
        member __.StateInterval =           TimeSpan.FromMinutes 2.
        member x.BuildProcessorParams() =
            Log.Information("Reading {maxPending} ahead; using {dop} processors", x.MaxReadAhead, x.MaxConcurrentProcessors)
            (x.MaxReadAhead, x.MaxConcurrentProcessors)
        member val SqlStreamStore =         SqlStreamStoreSourceArguments(a.GetResult SqlStreamStore)
        member __.BuildSqlStreamStoreParams() =
            let spec = { ReaderSpec.consumerGroup = __.ConsumerGroupName; maxBatchSize = __.SqlStreamStore.MaxBatchSize;
                         tailSleepInterval = __.SqlStreamStore.TailInterval }
            let srcSql = __.SqlStreamStore
            srcSql, spec

    /// Parse the commandline; can throw exceptions in response to missing arguments and/or `-h`/`--help` args
    let parse argv =
        let programName = System.Reflection.Assembly.GetEntryAssembly().GetName().Name
        let parser = ArgumentParser.Create<Parameters>(programName=programName)
        parser.ParseCommandLine argv |> Arguments

// TODO remove this entire comment after reading https://github.com/jet/dotnet-templates#module-logging
// Application logic assumes the global `Serilog.Log` is initialized _immediately_ after a successful ArgumentParser.ParseCommandline
module Logging =

    let initialize verbose =
        Log.Logger <-
            LoggerConfiguration()
                .Destructure.FSharpTypes()
                .Enrich.FromLogContext()
            |> fun c -> if verbose then c.MinimumLevel.Debug() else c
            |> fun c -> let t = "[{Timestamp:HH:mm:ss} {Level:u3}] {partitionKeyRangeId,2} {Message:lj} {NewLine}{Exception}"
                        c.WriteTo.Console(theme=Sinks.SystemConsole.Themes.AnsiConsoleTheme.Code, outputTemplate=t)
            |> fun c -> c.CreateLogger()

let [<Literal>] AppName = "template_test"

let build (args : Args.Arguments) =
    let maxReadAhead, maxConcurrentStreams = args.BuildProcessorParams()
    let (srcSql, spec) = args.BuildSqlStreamStoreParams()

    let store = srcSql.Connect()

    let checkpointer = Propulsion.SqlStreamStore.SqlCheckpointer(srcSql.CheckpointsConnectionString)

    let stats = Handler.ProjectorStats(Log.Logger, args.StatsInterval, args.StateInterval)
    let sink = Propulsion.Streams.StreamsProjector.Start(Log.Logger, maxReadAhead, maxConcurrentStreams, Handler.handle, stats, args.StatsInterval)
    let runSourcePipeline =
        Propulsion.SqlStreamStore.SqlStreamStoreSource.Run(Log.Logger, store, checkpointer, spec, sink, args.StatsInterval)
    sink, runSourcePipeline

let run args =
    let sink, runSourcePipeline = build args
    runSourcePipeline |> Async.Start
    sink.AwaitCompletion() |> Async.RunSynchronously
    sink.RanToCompletion

[<EntryPoint>]
let main argv =
    try let args = Args.parse argv
        try Logging.initialize args.Verbose
            try Configuration.initialize ()
                if run args then 0 else 3
            with e when not (e :? Args.MissingArg) -> Log.Fatal(e, "Exiting"); 2
        finally Log.CloseAndFlush()
    with Args.MissingArg msg -> eprintfn "%s" msg; 1
        | :? Argu.ArguParseException as e -> eprintfn "%s" e.Message; 1
        | e -> eprintf "Exception %s" e.Message; 1
