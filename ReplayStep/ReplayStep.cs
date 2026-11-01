using System.Drawing;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Chart;
using TradingPlatform.BusinessLayer.Native;

namespace ReplayStep
{
    /// <summary>
    /// Chart button that walks Market Replay forward until the current candle's open time changes.
    /// The vendor releases one quote per request, so the button repeats that until the bar rolls.
    /// </summary>
    public class ReplayStep : Indicator
    {
        private const int TimeoutMs = 2000;

        private readonly object gate = new object();
        private readonly Font buttonFont = new Font("Arial", 9f, FontStyle.Bold);

        private bool mouseHooked;
        private Symbol hookedSymbol;
        private Rectangle buttonRect;
        private bool buttonRectValid;
        private bool disposed;

        private volatile bool stepping;
        private volatile bool barReached;
        private DateTime targetBarTime;
        private long idleSince;

        public ReplayStep()
            : base()
        {
            this.Name = "Replay Step";
            this.Description = "Steps Market Replay to the next candle from a chart button.";
            this.SeparateWindow = false;
        }

        public override string ShortName => "Replay Step";

        protected override void OnInit()
        {
            this.HookMouse();
        }

        protected override void OnUpdate(UpdateArgs args)
        {
        }

        protected override void OnClear()
        {
            this.CancelStep();
            this.UnhookPrints();
            this.UnhookMouse();
            base.OnClear();
        }

        public override void Dispose()
        {
            if (this.disposed)
                return;
            this.disposed = true;

            this.CancelStep();
            this.UnhookPrints();
            this.UnhookMouse();
            this.buttonFont.Dispose();
            base.Dispose();
        }

        public override void OnPaintChart(PaintChartEventArgs args)
        {
            base.OnPaintChart(args);

            var graphics = args?.Graphics;
            var window = this.CurrentChart?.MainWindow;
            if (graphics == null || window == null)
                return;

            var replay = this.ReplayConnection() != null;
            var busy = this.stepping;
            var text = busy ? "Stepping" : "Next bar";
            var textSize = graphics.MeasureString(text, this.buttonFont);
            int width = (int)Math.Ceiling(textSize.Width) + 16;
            int height = (int)Math.Ceiling(textSize.Height) + 8;
            var clip = window.ClientRectangle;
            var rect = new Rectangle(clip.Left + 8, clip.Bottom - height - 8, width, height);
            this.buttonRect = rect;
            this.buttonRectValid = true;

            Color fill;
            Color fore;
            if (!replay)
            {
                fill = Color.FromArgb(160, 70, 70, 70);
                fore = Color.FromArgb(180, 190, 190, 190);
            }
            else if (busy)
            {
                fill = Color.FromArgb(220, 180, 120, 20);
                fore = Color.White;
            }
            else
            {
                fill = Color.FromArgb(220, 30, 120, 60);
                fore = Color.White;
            }

            using var brush = new SolidBrush(fill);
            using var textBrush = new SolidBrush(fore);
            using var pen = new Pen(Color.FromArgb(220, 20, 20, 20));
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.FillRectangle(brush, rect);
            graphics.DrawRectangle(pen, rect);
            graphics.DrawString(text, this.buttonFont, textBrush, rect, format);
        }

        private void HookMouse()
        {
            if (this.mouseHooked || this.CurrentChart == null)
                return;
            this.CurrentChart.MouseDown += this.OnMouseDown;
            this.mouseHooked = true;
        }

        private void UnhookMouse()
        {
            if (!this.mouseHooked || this.CurrentChart == null)
                return;
            this.CurrentChart.MouseDown -= this.OnMouseDown;
            this.mouseHooked = false;
        }

        private void HookPrints()
        {
            this.UnhookPrints();
            var symbol = this.Symbol;
            if (symbol == null)
                return;
            symbol.NewLast += this.OnLast;
            this.hookedSymbol = symbol;
        }

        private void UnhookPrints()
        {
            if (this.hookedSymbol == null)
                return;
            this.hookedSymbol.NewLast -= this.OnLast;
            this.hookedSymbol = null;
        }

        private void OnMouseDown(object sender, ChartMouseNativeEventArgs e)
        {
            if (e.Button != NativeMouseButtons.Left)
                return;
            if (!this.buttonRectValid || !this.buttonRect.Contains(e.Location))
                return;

            e.Handled = true;
            this.StartNextBar();
        }

        private void StartNextBar()
        {
            var connection = this.ReplayConnection();
            if (connection == null || !this.TryGetLastBarTime(out var time))
                return;

            lock (this.gate)
            {
                if (this.stepping)
                    return;
                this.stepping = true;
                this.barReached = false;
                this.targetBarTime = time;
                this.idleSince = Environment.TickCount64;
            }

            this.HookPrints();

            // QuoteByQuote stops the slider. Its Set() releases the first quote; the chain keeps the event signaled.
            if (!ReplayRequests.TrySetQuoteByQuote(connection))
            {
                this.CancelStep();
                return;
            }

            this.CurrentChart?.RedrawBuffer();
            Task.Factory.StartNew(this.RunChain, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        private void OnLast(Symbol symbol, Last last)
        {
            if (!this.stepping)
                return;

            Volatile.Write(ref this.idleSince, Environment.TickCount64);
            if (this.BarRolled())
                this.barReached = true;
        }

        /// <summary>
        /// The vendor waits on a manual-reset event, so extra Set() calls collapse into one quote.
        /// Keep the event signaled until the forming bar's open time moves, then stop.
        /// </summary>
        private void RunChain()
        {
            try
            {
                while (this.stepping && !this.barReached)
                {
                    if (this.BarRolled())
                        break;

                    if (Environment.TickCount64 - Volatile.Read(ref this.idleSince) >= TimeoutMs)
                        break;

                    var connection = this.ReplayConnection();
                    if (connection == null || !ReplayRequests.TryStep(connection.Id))
                        break;

                    Thread.Sleep(0);
                }
            }
            finally
            {
                this.CancelStep();
            }
        }

        private void CancelStep()
        {
            var redraw = false;
            lock (this.gate)
            {
                if (!this.stepping)
                    return;
                this.FinishLocked();
                redraw = true;
            }

            if (redraw)
                this.CurrentChart?.RedrawBuffer();
        }

        private void FinishLocked()
        {
            this.stepping = false;
            this.barReached = true;
        }

        private Connection ReplayConnection()
        {
            var connection = this.Symbol?.Connection;
            if (connection == null)
                return null;
            if (connection.Type != ConnectionType.HistoryPlayer)
                return null;
            if (connection.State != ConnectionState.Connected)
                return null;
            return connection;
        }

        private bool BarRolled()
        {
            try
            {
                return this.TryGetLastBarTime(out var time) && time > this.targetBarTime;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private bool TryGetLastBarTime(out DateTime time)
        {
            time = default;
            var history = this.HistoricalData;
            if (history == null || history.Count < 1)
                return false;
            time = history[0].TimeLeft;
            return true;
        }

        private static class ReplayRequests
        {
            private const string HelperName = "EmulatorBasedHelper";
            private static readonly object ResolveGate = new object();
            private static Type stepType;
            private static Type speedType;
            private static RequestParameters stepRequest;
            private static object quoteByQuote;
            private static bool resolved;

            public static bool TrySetQuoteByQuote(Connection connection)
            {
                if (connection == null || !Ensure())
                    return false;

                try
                {
                    var request = (RequestParameters)Activator.CreateInstance(speedType);
                    speedType.GetProperty("HistoryPlayerSpeed").SetValue(request, quoteByQuote);
                    connection.SendCustomRequest(request);
                    return true;
                }
                catch (Exception ex)
                {
                    Core.Instance.Loggers.Log("Replay Step: " + ex.Message, LoggingLevel.Error);
                    return false;
                }
            }

            public static bool TryStep(string connectionId)
            {
                if (!Ensure())
                    return false;

                try
                {
                    stepRequest ??= (RequestParameters)Activator.CreateInstance(stepType);
                    Core.Instance.SendCustomRequest(connectionId, stepRequest);
                    return true;
                }
                catch (Exception ex)
                {
                    Core.Instance.Loggers.Log("Replay Step: " + ex.Message, LoggingLevel.Error);
                    return false;
                }
            }

            private static bool Ensure()
            {
                if (resolved)
                    return stepType != null;

                lock (ResolveGate)
                {
                    if (resolved)
                        return stepType != null;

                    var assembly = FindHelperAssembly();
                    if (assembly == null)
                    {
                        Core.Instance.Loggers.Log("Replay Step: EmulatorBasedHelper is not loaded.", LoggingLevel.Error);
                        return false;
                    }

                    try
                    {
                        stepType = assembly.GetType("EmulatorBasedHelper.CustomRequests.HistoryPlayerStepRequest", true);
                        speedType = assembly.GetType("EmulatorBasedHelper.CustomRequests.SetHistoryPlayerSpeedRequest", true);
                        var speedEnum = assembly.GetType("EmulatorBasedHelper.HistoryPlayerSpeed", true);
                        quoteByQuote = Enum.Parse(speedEnum, "QuoteByQuote");
                        resolved = true;
                        return true;
                    }
                    catch (Exception ex)
                    {
                        stepType = null;
                        resolved = true;
                        Core.Instance.Loggers.Log("Replay Step: " + ex.Message, LoggingLevel.Error);
                        return false;
                    }
                }
            }

            private static Assembly FindHelperAssembly()
            {
                Assembly fallback = null;
                foreach (var context in AssemblyLoadContext.All)
                {
                    Assembly vendor = null;
                    Assembly panel = null;
                    Assembly helper = null;
                    foreach (var assembly in context.Assemblies)
                    {
                        var name = assembly.GetName().Name;
                        if (name == "MarketReplayVendor")
                            vendor = assembly;
                        else if (name == "MarketReplayPanel")
                            panel = assembly;
                        else if (name == HelperName)
                            helper = assembly;
                    }

                    if (helper == null)
                        continue;
                    fallback ??= helper;
                    if (vendor != null || panel != null)
                        return helper;
                }

                return fallback;
            }
        }
    }
}
