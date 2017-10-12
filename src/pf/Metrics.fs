module RabbitMQ.PerfTest.Metrics
open System
open System.Threading

type Stats (id: string, interval : int) =

    let time () =
        DateTime.Now.Ticks

    let startTime =  time()
    let mutable lastStatTime = 0L
    let mutable sendCount = 0
    let mutable sendCountInterval = 0

    let resetInterval n =
        lastStatTime <- n
        Interlocked.Exchange(&sendCountInterval, 0) |> ignore
        ()

    do resetInterval startTime

    let reportInterval () =
        let now = time ()
        (* printfn "report %A" now *)
        let elapsed = TimeSpan (now - lastStatTime)
        if (int elapsed.TotalMilliseconds) >= interval then
            //report
            let msgPerS = sendCountInterval / (interval / 1000)
            let totaltime = TimeSpan (now - startTime)
            printfn "id: %s, time: %.3fs, sent: %i msg/s" id totaltime.TotalSeconds msgPerS
            resetInterval now


    member s.HandleSend() =
        Interlocked.Increment (&sendCount) |> ignore
        Interlocked.Increment (&sendCountInterval) |> ignore
        reportInterval()
