open System
open NBomber.FSharp

[<EntryPoint>]
let main argv =

    //HelloWorldScenario.run()
    //HttpScenario.run()
    CustomReportingScenario.run("scn1")
    CustomReportingScenario.run("scn2")
    CustomReportingScenario.run("scn3")

    0
