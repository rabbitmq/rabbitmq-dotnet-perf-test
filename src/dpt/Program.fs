module RabbitMQ.PerfTest.Main

open System
open Argu
open RabbitMQ.Client
open RabbitMQ.PerfTest.Metrics

type Args =
    | [<AltCommandLine("-x")>] Producers of arg : int
    | [<AltCommandLine("-y")>] Consumers of arg : int
    | [<AltCommandLine("-h")>] Uri of arg : string
    | [<AltCommandLine("-s")>] Size of arg : int
    | [<AltCommandLine("-a")>] Autoack
    | [<AltCommandLine("-e")>] Exchange of arg : string
    | [<AltCommandLine("-u")>] Queue of arg : string

with
    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Producers _ -> "producer count"
            | Consumers _ -> "consumer count"
            | Uri _ -> "connection URI"
            | Size _ -> "message size in bytes"
            | Autoack _ -> "auto ack"
            | Exchange _ -> "exchange name"
            | Queue _ -> "queue name"

let errorHandler = ProcessExiter(colorizer = function ErrorCode.HelpText -> None | _ -> Some ConsoleColor.Red)
let parser = ArgumentParser.Create<Args>(programName = "dpt",
                                         errorHandler = errorHandler)

let makeBodyGen size =
    let rem = size - 12
    if  rem <= 0 then
        fun (s : int) ->
            let s = s |> BitConverter.GetBytes
            let ts = DateTime.UtcNow.Ticks |> System.BitConverter.GetBytes
            Array.append s ts
    else
        let fill = Array.zeroCreate size
        fun s ->
            let s = s |> BitConverter.GetBytes
            let ts = DateTime.UtcNow.Ticks |> System.BitConverter.GetBytes
            Array.Copy (s, 0, fill, 0, s.Length)
            Array.Copy (ts, 0, fill, 4, ts.Length)
            fill

type Scenario =
    { Uri : Uri
      Consumers : int
      Producers : int
      MsgBodyGen : int -> byte array
      AutoAck : bool
      Stats : Stats
      Exchange : string
      Queue : string option
    }
    with
    static member def () =
            let id = sprintf "test-%s" (DateTime.Now.ToString("yyMMdd-hh:mm:ss"))
            { Uri = System.Uri "amqp://localhost:5672"
              Consumers = 1
              Producers = 1
              MsgBodyGen = makeBodyGen 0
              AutoAck = false
              Stats = Stats (id, 1000)
              Exchange = "direct"
              Queue = None
    }
    static member parse (args : Args list) =
        args
        |> List.fold (fun s a ->
            match a with
            | Producers c ->
                {s with Producers = c}
            | Consumers c ->
                {s with Consumers = c}
            | Uri uri ->
                {s with Uri = System.Uri uri}
            | Size size ->
                {s with MsgBodyGen = makeBodyGen size}
            | Autoack ->
                {s with AutoAck = true}
            | Queue q ->
                {s with Queue = Some q}
            | Exchange e ->
                {s with Exchange = e}
        ) (Scenario.def())

let consume (s: Scenario) (m: IModel) queue f =
        let consumer =
            { new DefaultBasicConsumer(m) with
                member x.HandleBasicDeliver(consumerTag,
                                            deliveryTag,
                                            redelivered,
                                            exchange,
                                            routingKey,
                                            props,
                                            body) =
                                            f deliveryTag props body }
        let consumerTag = m.BasicConsume (queue, s.AutoAck, consumer)
        { new System.IDisposable with
            member __.Dispose () =
                m.BasicCancel(consumerTag) }

let declExchange (m: IModel) { Exchange = exchange }  =
    let args = Collections.Generic.Dictionary ()
    m.ExchangeDeclare (exchange, "direct", false, false, args)

let prepareQueue (m: IModel) { Exchange = exchange; Queue = queue } key =
    let ok =
        match queue with
        | Some q ->
            m.QueueDeclare (queue = q, exclusive = false)
        | None ->
            m.QueueDeclare (exclusive = false)
    m.QueueBind (ok.QueueName, exchange, key)
    ok.QueueName

let startConsumer (s: Scenario) (cf: ConnectionFactory) key =
    let c = cf.CreateConnection()
    let m = c.CreateModel()
    let q = prepareQueue m s key
    consume s m q (fun tag _ body ->
        let recvTicks = DateTime.UtcNow.Ticks
        let sentTicks = BitConverter.ToInt64(body, 4)
        s.Stats.HandleReceive(recvTicks - sentTicks)
        if not s.AutoAck then m.BasicAck (tag, false))

let startProducer (s : Scenario) (cf: ConnectionFactory) key bodyGen =
    let c = cf.CreateConnection()
    let m = c.CreateModel()
    let bp = m.CreateBasicProperties()
    let mutable seqNum = 0
    let stats = s.Stats
    async {
        while true do
            m.BasicPublish (s.Exchange, key, bp, bodyGen seqNum)
            stats.HandleSend ()
            seqNum <- seqNum + 1 }
    |> Async.Start


let run (s : Scenario) =
    let cf = ConnectionFactory()
    cf.Uri <- s.Uri
    use c = cf.CreateConnection()
    use m = c.CreateModel()
    declExchange m s
    let key = Guid.NewGuid().ToString()
    // first start consumers
    let consumers =
        [ for _ in [1 .. s.Consumers] do
            yield startConsumer s cf key ]
    // if there are no consumers still create a queue
    if s.Consumers = 0 then
        prepareQueue m s key |> ignore
    // then the producers
    let producers =
        [ for _ in [1 .. s.Producers] do
            yield startProducer s cf key s.MsgBodyGen]
    ()


[<EntryPoint>]
let main argv =
    let result = parser.ParseCommandLine argv
    let all = result.GetAllResults()
    let scenario = Scenario.parse all
    run scenario |> ignore
    Console.ReadLine() |> ignore
    0
