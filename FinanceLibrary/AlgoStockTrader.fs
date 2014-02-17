﻿namespace FinanceLibrary.AlgorithmicTrading

(*
    Simple Momentum Algorithm
    1. Calulate the volume weighted average i.e. 3 days/5 days.
    2. Keep the current price of the security.
    3. Portfolio object that calculates how long or short you are. (positions * price)
    4. Conditions where if the value of the security is .5% less than vwap and max short has not been reached
       then call order to sell 100 shares short.
    5. Conversely if the stock is .1% higher and we havent reached max long then call order and buy 100 shares.
*)

module AlgoStockTrader = 

 open System
 open System.Net

 let url = "http://ichart.finance.yahoo.com/table.csv?s="

 type Tick = 
  { Date:DateTime
    Open:decimal
    High:decimal
    Low:decimal
    Close:decimal
    Volume:decimal
    AdjClose:decimal }
 
 /// Returns prices (as tuple) of a given stock for a 
 /// specified number of days (starting from the most recent)
 let getStockPrices stock count =
  // Download the data and split it into lines
  let wc = new WebClient()
  let data = wc.DownloadString(url + stock)
  let dataLines = 
      data.Split([| '\n' |], StringSplitOptions.RemoveEmptyEntries) 
 
  // Parse lines of the CSV file and take specified
  // number of days using in the oldest to newest order
  seq { for line in dataLines |> Seq.skip 1 do
            let infos = line.Split(',')
            yield { Date = DateTime.Parse infos.[0]
                    Open = decimal infos.[1]
                    High = decimal infos.[2]
                    Low = decimal infos.[3]
                    Close = decimal infos.[4]
                    Volume = decimal infos.[5]
                    AdjClose = decimal infos.[6] } }
   |> Seq.take count |> Array.ofSeq |> Array.rev


 type OrderType = Long | Short | Cover

 [<StructuredFormatDisplay("hello {Symbol}")>]
 type Order = 
  { Symbol:string
    Quantity:int
    OrderType:OrderType
    Value:decimal
    mutable Covered:bool }


 type Portfolio(startingCash:decimal, startDate:DateTime) = class
  let mutable positions = new System.Collections.Generic.List<Order>()
  let transactions = new System.Collections.Generic.List<decimal>()
  let mutable currentPrice = 0M

  /// The starting date of the portfolio.
  member x.StartDate = startDate

  /// Starting capital.
  member x.StartingCash = startingCash 

  member x.CurrentPrice
   with get() = currentPrice
   and set(value) = currentPrice <- value

  /// Current amount of available cash.
  /// Sum of starting capital minus the positions as they were ordered (not the current value of the positions).
  member x.Cash
   with get() = startingCash - (positions |> Seq.filter (fun y -> y.OrderType = Long || y.OrderType = Short) |> Seq.sumBy (fun y -> y.Value))
  
  /// Total profit and loss up until the current time.
  member x.ProfitAndLoss
   with get() = (x.Cash + x.PositionsValue) - startingCash
  
  /// Dictionary of open positions (i.e. Stock GOOG 124).
  member x.Positions
   with get() = positions

  member x.ShortPositions
   with get() = positions |> Seq.filter (fun y -> y.OrderType = Short) |> Seq.toList

  /// Total value of the open positions.
  member x.PositionsValue
   with get() = 
    decimal(positions |> Seq.filter (fun y -> y.OrderType = Long) |> Seq.sumBy (fun y -> y.Quantity)) * currentPrice + // long positions value.
     (positions |> Seq.filter (fun y -> y.OrderType = Short) |> Seq.sumBy (fun y -> abs y.Value)) - // short positions value.
      decimal(positions |> Seq.filter (fun y -> y.OrderType = Cover) |> Seq.sumBy (fun y -> y.Quantity)) * currentPrice // short covering cost.

  /// Total value of all short positions.
  member x.ShortPositionsValue
   with get() = positions |> Seq.filter (fun y -> not y.Covered &&  y.OrderType = Short) |> Seq.sumBy (fun y -> y.Value)

  /// Sum of positionsValue and cash.
  member x.PortfolioValue 
   with get() = x.Cash + x.PositionsValue

  /// Cumulative percentage returns for entire portfolio up until now. Pentage gain or loss of start cash.
  member x.Returns
   with get() = x.ProfitAndLoss / startingCash

  // A new position / order.
  member x.AddPosition(order) = positions.Add(order)
  
  // Profit gained will be the initial short price say $100 * Quantity minus the price they shares were bought back at say $75 * Quantity. 
  member x.CloseShortPositions(short,order) = 
   short.Covered <- true
   positions.Add(order)

 end


 type Trader (portfolio:Portfolio, symbol:string, backTestPeriod) = class

  // Get historical stock prices for the symbol
  let prices = getStockPrices symbol backTestPeriod

  // Limit the exposure on open positions.
  let maxlimit = portfolio.StartingCash + 0.1M
  let minlimit = -portfolio.StartingCash 

  // Calculated using mean high low close.
  let volumeWeightedAvgPrice (prices:Tick[]) (period:float) = 
    let ticksInPeriod =
     prices 
     |> Array.filter (fun x -> x.Date <= DateTime.Today.AddDays(-period) || x.Date >= DateTime.Today.AddDays(+period)) // get data within period relative to now.
    (ticksInPeriod |> Array.sumBy (fun x-> ((x.High + x.Low + x.Close)/3.0M) * x.Volume)) // Sum price times volume per trade
     / (ticksInPeriod |> Array.sumBy (fun x -> x.Volume)) // Sum volume over whole period

  /// Place order / make trade
  member private x.PlaceOrder(order:Order) = portfolio.AddPosition(order)

  /// Close position
  member private x.CloseShortPosition(order) = portfolio.CloseShortPositions(order)
  
  /// Call on incoming data
  member private x.IncomingTick(tick:Tick) = 
   let currentPrice = tick.Low
   let calcVwap = volumeWeightedAvgPrice prices 3.0

   // Shares limit to buy/sell
   let numOfShares = 5M

   // if the stocks price less than the vwap by 0.5% and the limit has not been exceeded.
   if currentPrice < (calcVwap * 0.995M) && (portfolio.PositionsValue > minlimit) then
    let order = { Symbol = symbol; Quantity = - int numOfShares; OrderType = Short; Value = -numOfShares * currentPrice; Covered = false }
    x.PlaceOrder(order)

   // if the stock price has increased by 0.1% to the vwap and we havent reached exposure limit then buy.
   elif currentPrice > (calcVwap * 1.001M) && (portfolio.PositionsValue < maxlimit) then
    let order = { Symbol = symbol; Quantity = + int numOfShares; OrderType = Long; Value = +numOfShares * currentPrice; Covered = false }
    x.PlaceOrder(order)

   // Close any short positions when the market starts to rise again.
   // If there any shorts where the market value has risen close to the the initial shorting value then close the position.
   elif not portfolio.ShortPositions.IsEmpty then
    let shortsToClose = portfolio.ShortPositions |> Seq.filter (fun y -> not y.Covered && abs(y.Value / numOfShares) >= 0.8M * currentPrice) |> Seq.toList
    printfn "%A Short Positions Closed" shortsToClose.Length
    for short in shortsToClose do 
     let order = { Symbol = short.Symbol; Quantity = abs short.Quantity; OrderType = Cover; Value = abs (decimal short.Quantity) * currentPrice; Covered = true }
     portfolio.CloseShortPositions(short,order)

  member x.BackTest () = 
   printfn "Algorithm Started."
   
   for tick in prices do 
    portfolio.CurrentPrice <- prices.[prices.Length - 1].Close // Assign most recent price for position end value calculations.
    x.IncomingTick(tick)

   printfn "Sold %A Shares" (portfolio.Positions |> Seq.filter (fun y -> y.OrderType = Short) |> Seq.sumBy (fun y -> y.Quantity))
   printfn "Bought %A Shares" (portfolio.Positions |> Seq.filter (fun y -> y.OrderType = Long) |> Seq.sumBy (fun y -> y.Quantity))
   printfn "Covered %A Shares" (portfolio.Positions |> Seq.filter (fun y -> y.OrderType = Cover) |> Seq.sumBy (fun y -> y.Quantity))
   printfn "Algorithm Ended."

  /// Default constructor for Google, 4 years back, trading limits are $startingCash + 0.1 to -$startingCash.
  new (portfolio) = Trader(portfolio, "GOOG", 1000) 
 end


 let system = 
  let p = new Portfolio(10000M, DateTime.Today)
  let trader = new Trader(p)
  trader.BackTest()