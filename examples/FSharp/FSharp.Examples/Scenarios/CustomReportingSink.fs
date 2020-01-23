module CustomReportingScenario

open System
open System.Threading.Tasks
open System.Net.Http

open FSharp.Control.Tasks.V2.ContextInsensitive

open Serilog
open App.Metrics
open App.Metrics.Gauge
open Microsoft.Extensions.Configuration

open NBomber.Contracts
open NBomber.FSharp

// it's a very basic CustomReportingSink example to give you a playground for writing your own custom sink
// for production purposes use https://github.com/PragmaticFlow/NBomber.Sinks.InfluxDB

type InfluxDBSink(url: string, dbName: string) =

    let metrics = MetricsBuilder().Report.ToInfluxDb(url, dbName).Build()

    let saveGaugeMetrics (testInfo: TestInfo) (s: Statistics) =

        let operation =
            match s.NodeInfo.CurrentOperation with
            | NodeOperationType.Bombing  -> "bombing"
            | NodeOperationType.Complete -> "complete"
            | _                          -> "unknown_operation"

        [("OkCount", float s.OkCount); ("FailCount", float s.FailCount);
         ("RPS", float s.RPS); ("Min", float s.Min); ("Mean", float s.Mean); ("Max", float s.Max)
         ("Percent50", float s.Percent50); ("Percent75", float s.Percent75); ("Percent95", float s.Percent95); ("StdDev", float s.StdDev)
         ("DataMinKb", s.DataMinKb); ("DataMeanKb", s.DataMeanKb); ("DataMaxKb", s.DataMaxKb); ("AllDataMB", s.AllDataMB)]

        |> List.iter(fun (name, value) ->
            let m = GaugeOptions(
                        Name = name,
                        Context = "NBomber",
                        Tags = MetricTags([|"machineName"; "sender"
                                            "session_id"; "test_suite"; "test_name"
                                            "scenario"; "step"; "operation"|],
                                          [|s.NodeInfo.MachineName; s.NodeInfo.Sender.ToString()
                                            testInfo.SessionId; testInfo.TestSuite; testInfo.TestName
                                            s.ScenarioName; s.StepName; operation |]))
            metrics.Measure.Gauge.SetValue(m, value))

    interface IReportingSink with
        member x.Init(logger: ILogger, infraConfig: IConfiguration option) =
            ()

        member x.StartTest(testInfo: TestInfo) =
            Task.CompletedTask

        member x.SaveRealtimeStats (testInfo: TestInfo, stats: Statistics[]) =
            stats |> Array.iter(saveGaugeMetrics testInfo)
            Task.WhenAll(metrics.ReportRunner.RunAllAsync())

        member x.SaveFinalStats(testInfo: TestInfo, stats: Statistics[], reportFiles: ReportFile[]) =
            stats |> Array.iter(saveGaugeMetrics testInfo)
            Task.CompletedTask

        member x.FinishTest(testInfo: TestInfo) =
            Task.CompletedTask

type CustomReportingSink() =

    interface IReportingSink with
        member x.Init(logger, infraConfig) = ()

        member x.StartTest(testInfo: TestInfo) =
            Task.CompletedTask

        member x.SaveRealtimeStats(testInfo: TestInfo, stats: Statistics[]) =
            Task.CompletedTask

        member x.SaveFinalStats(testInfo: TestInfo, stats: Statistics[], reportFiles: ReportFile[]) =
            Task.CompletedTask

        member x.FinishTest(testInfo: TestInfo) =
            Task.CompletedTask

let run (scnName) =

    let httpClient = new HttpClient()

    let step = Step.create("GET html", fun context -> task {
        use! response = httpClient.GetAsync("https://nbomber.com",
                                            context.CancellationToken)

        match response.IsSuccessStatusCode with
        | true  -> let size = int response.Content.Headers.ContentLength.Value
                   return Response.Ok(sizeBytes = size)
        | false -> return Response.Fail()
    })

    let scenario = Scenario.create scnName [step]
                   |> Scenario.withConcurrentCopies 200
                   |> Scenario.withWarmUpDuration(TimeSpan.FromSeconds 10.0)
                   |> Scenario.withDuration(TimeSpan.FromSeconds 30.0)

    //let customReportingSink = CustomReportingSink()
    let influxDbSink = new InfluxDBSink("http://localhost:8086", "default")
    let sendStatsInterval = TimeSpan.FromSeconds 10.0

    NBomberRunner.registerScenarios [scenario]
    |> NBomberRunner.withReportingSinks([influxDbSink], sendStatsInterval)
    |> NBomberRunner.runTest
