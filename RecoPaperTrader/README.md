# RecoPaperTrader

Paper-trades signals from `dbo.RecoLog` **only** if they match the `(Strategy, Timeframe)` pairs and risk rules in your generated `bot.settings.<ConfigHash>.json`.

## Build

```
dotnet build
```

## Prepare DB Objects (once)

```
sqlcmd -S localhost -d Tradexus -E -i SqlScripts/CreatePaperTables.sql
```

## Run

```
dotnet run -- --settings bot.settings.<ConfigHash>.json --conn "Server=localhost;Database=Tradexus;Trusted_Connection=True;Encrypt=False;"
```

Optional flags:
- `--poll 1000` (ms)
- `--lookback 60` (minutes)
- `--account Paper-Default-1`

## Behavior

- Polls `dbo.RecoLog` for recent rows (within `Trader:LookbackMinutes`) higher than a saved watermark.
- Filters to allowed `(Strategy, Timeframe)` from the settings JSON; requires confidence >= `max(Trader:MinConfidenceAbs, 0.8*AvgConfidence)`.
- Sizes notional as `RiskPctPerTrade * Equity`.
- Enters at reco price ± slippage; fees at `FeeBpsPerSide`.
- Exits on the configured horizon bar close, using `dbo.Candles` `Close` for the bar with `StartTime == ExitDueBarStartTime`.

## Settings

- Global trader defaults live in `appsettings.json` under `"Trader"`.
- Strategy allow-list and risk per trade come from your generated `bot.settings.<ConfigHash>.json`.
