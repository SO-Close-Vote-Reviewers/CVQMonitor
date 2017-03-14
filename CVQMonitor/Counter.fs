namespace CVQMonitor

module Counter =
    let mutable private i = 0
    let Get() =
        i <- i + 1
        ":" + i.ToString() + ": "