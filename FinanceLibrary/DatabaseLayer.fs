﻿namespace FinanceLibrary

open System
open System.Data
open System.Data.Linq
open Microsoft.FSharp.Data.TypeProviders
open Microsoft.FSharp.Linq

module DatabaseLayer = 
 type dbSchema = SqlDataConnection< "Data Source=.;Initial Catalog=AlgorithmicTrading;Integrated Security=True" >
 type FetchData() = 
     let db = dbSchema.GetDataContext()
     do 
         // Enable SQL logging to console.
         db.DataContext.Log <- System.Console.Out
     
     let fetchData (symbol : string) (daysBack : int) = 
         query { for row in db.InterDay_HistoricalStock do
                 where (row.Symbol = symbol && row.Date >= DateTime.Today.AddDays(float -daysBack))
                 sortByDescending row.Date
                 select row }

         |> Seq.map (fun x -> 
                { Date = x.Date
                  Open = x.Open
                  High = x.High
                  Low = x.Low
                  Close = x.Close
                  Volume = decimal x.Volume
                  AdjClose = 0M })
         |> Seq.toArray

     interface IStockService with
         member this.GetStockPrices symbol daysBack = fetchData symbol daysBack


 let test = new FetchData() :> IStockService
 let res = test.GetStockPrices "MSFT" 1000