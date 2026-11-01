# Replay Step

A [Quantower](https://www.quantower.com) chart indicator that steps **Market Replay** forward to the next candle from a button drawn on the chart.

The Market Replay panel speed slider only changes playback speed. **Next bar** switches the replay to quote-by-quote mode and keeps releasing quotes until the forming candle's open time moves. The button label is **Stepping** while that walk is in progress, then it stops on the new candle. The panel **Play** button resumes continuous playback from the speed slider.

The button is green on a Market Replay chart. On any other connection it is gray and does nothing. There is no step backward.

## Requirements

- Quantower **v1.146+** (tested on v1.146.17)
- A running **Market Replay** chart
- .NET 10 SDK only if you build from source

## Install

Quantower loads custom indicators from the user settings tree, not from `Program Files`. The usual folder on Windows is:

`C:\Quantower\Settings\Scripts\Indicators\ReplayStep\`

Create the `ReplayStep` subfolder if it does not exist. After copying, restart Quantower (or reload scripts), then add the indicator from the chart: **Indicators → Custom → Replay Step**.

### Pre-built files from `dist/` (no compilation)

This repository ships a **Release** build under [`dist/`](dist/). Copy **everything** from `dist/` **except** `README.txt` into the Quantower indicators folder above.

PowerShell (from the cloned repo root):

```powershell
$dest = "C:\Quantower\Settings\Scripts\Indicators\ReplayStep"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Copy-Item -Path ".\dist\*.dll",".\dist\*.deps.json" -Destination $dest -Force -ErrorAction SilentlyContinue
```

If your Quantower profile lives elsewhere, replace `$dest` with your actual `...\Settings\Scripts\Indicators\ReplayStep\` path.

### Build from source

Requires `TradingPlatform.BusinessLayer.dll` from your Quantower install (compile-time reference only; it is not copied into `dist/`).

```powershell
git clone https://github.com/3csoft/ReplayStep.git
cd ReplayStep
dotnet build ReplayStep/ReplayStep.csproj -c Release
```

The build writes the indicator into the Quantower indicators folder and refreshes the repo-root `dist/` folder on **Release** builds.

Optional environment variables (if unset, the `.csproj` defaults apply):

| Variable | Default | Purpose |
| --- | --- | --- |
| `QuantowerBin` | `C:\Quantower\TradingPlatform\v1.146.17\bin` | Folder with `TradingPlatform.BusinessLayer.dll` |
| `QuantowerIndicators` | `C:\Quantower\Settings\Scripts\Indicators\ReplayStep` | Where the built `.dll` is copied |

## Usage

1. Start **Market Replay** and open its chart.
2. Add **Replay Step**. The **Next bar** button sits at the lower left of the main window.
3. Click it. Replay pauses the speed slider and walks quote by quote until the next candle opens.
4. Click again for another candle, or press **Play** on the Market Replay panel to resume the slider.
