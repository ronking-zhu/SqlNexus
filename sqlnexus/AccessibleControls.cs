using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using System.Windows.Forms.Automation;
using Microsoft.Reporting.WinForms;

namespace sqlnexus
{
    /// <summary>
    /// Accessible control subclasses for WCAG compliance in WinForms.
    /// These controls address limitations in standard WinForms controls
    /// that prevent screen readers and accessibility tools from fully
    /// interacting with the UI.
    /// </summary>

    /// <summary>
    /// A TextBox subclass that supports the UIA Text pattern for accessibility compliance.
    /// Standard single-line WinForms TextBox controls expose ControlType.Edit but only support
    /// the Value pattern. The UIA spec (Section 508 502.3.10, WCAG 4.1.2) requires Edit controls
    /// to also support the Text pattern. Setting Multiline=true enables the Text pattern in the
    /// underlying Win32 edit control while the visual appearance and behavior remain single-line.
    /// </summary>
    public class AccessibleTextBox : TextBox
    {
        public AccessibleTextBox()
        {
            Multiline = true;
            WordWrap = false;
            AcceptsReturn = false;
            ScrollBars = ScrollBars.None;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Prevent Enter key from inserting a newline
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            // Strip any newlines that may have been pasted
            if (Text.Contains("\n") || Text.Contains("\r"))
            {
                int selStart = SelectionStart;
                Text = Text.Replace("\r\n", " ").Replace("\r", " ").Replace("\n", " ");
                SelectionStart = Math.Min(selStart, Text.Length);
            }
        }
    }

    /// <summary>
    /// A RadioButton subclass that announces positional information (e.g., "1 of 3")
    /// to screen readers. Standard WinForms RadioButton does not expose UIA
    /// PositionInSet/SizeOfSet properties, so Narrator omits positional announcements.
    /// This subclass updates AccessibleDescription with position info when the control
    /// receives focus or is selected via arrow keys.
    /// </summary>
    public class AccessibleRadioButton : RadioButton
    {
        protected override void OnGotFocus(EventArgs e)
        {
            UpdateAccessibleDescription();
            base.OnGotFocus(e);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            if (Checked)
            {
                UpdateAccessibleDescription();
                // Narrator doesn't re-read properties on arrow-key radio button
                // changes since no UIA focus event fires. Explicitly raise a
                // notification so the screen reader announces the newly selected item.
                string announcement = string.Format("{0}, {1}", Text, AccessibleDescription);
                AccessibilityObject.RaiseAutomationNotification(
                    AutomationNotificationKind.ActionCompleted,
                    AutomationNotificationProcessing.ImportantMostRecent,
                    announcement);
            }
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            UpdateAccessibleDescription();
        }

        private void UpdateAccessibleDescription()
        {
            if (Parent == null)
                return;

            // Use TabIndex to determine position since Controls collection
            // order may not match the visual left-to-right layout
            int total = 0;
            int position = 0;

            foreach (Control c in Parent.Controls)
            {
                if (c is RadioButton rb)
                {
                    total++;
                    if (rb == this)
                        position = rb.TabIndex + 1;
                }
            }

            if (position > 0 && total > 0)
            {
                AccessibleDescription = string.Format("{0} of {1}", position, total);
            }
        }
    }

    internal sealed class ChartSamplePoint
    {
        public string SeriesName { get; set; }
        public DateTime Timestamp { get; set; }
        public string RawTimestampText { get; set; }
        public double Value { get; set; }
        public string Unit { get; set; }
        public Point VertexPixel { get; set; }
        public Point[] PolygonPoints { get; set; }
        public Rectangle BarPageBounds { get; set; }
        public bool IsCategorical { get; set; }
        public string Caption { get; set; }
        public Rectangle ScreenBounds { get; set; }
    }

    internal sealed class ChartAccessibilityModel
    {
        public string ChartId { get; set; }
        public string BaseToolTip { get; set; }
        public string OverviewDescription { get; set; }
        public Rectangle PageBounds { get; set; }
        public object RepresentativeAction { get; set; }
        public List<ChartSamplePoint> SamplePoints { get; } = new List<ChartSamplePoint>();
        public Dictionary<string, List<ChartSamplePoint>> SeriesByName { get; } =
            new Dictionary<string, List<ChartSamplePoint>>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class ChartDataPointAccessibleObject : AccessibleObject
    {
        private readonly AccessibleObject _parent;
        private readonly string _name;
        private readonly string _value;
        private readonly Func<Rectangle> _boundsProvider;

        public ChartDataPointAccessibleObject(
            AccessibleObject parent,
            string name,
            string value,
            Func<Rectangle> boundsProvider)
        {
            _parent = parent;
            _name = name ?? string.Empty;
            _value = value ?? string.Empty;
            _boundsProvider = boundsProvider;
        }

        public override AccessibleObject Parent => _parent;

        public override AccessibleRole Role => AccessibleRole.StaticText;

        public override string Name => _name;

        public override string Value => _value;

        public override AccessibleStates State => AccessibleStates.Focusable | AccessibleStates.ReadOnly;

        public override Rectangle Bounds => _boundsProvider != null ? _boundsProvider() : Rectangle.Empty;

        public override int GetChildCount() => 0;
    }

    internal sealed class CachedReportPanelAccessibleObject : Control.ControlAccessibleObject
    {
        private readonly AccessibleObject _inner;
        private List<AccessibleObject> _cachedChildren;

        public CachedReportPanelAccessibleObject(Control ownerControl, AccessibleObject inner)
            : base(ownerControl)
        {
            _inner = inner;
        }

        public AccessibleObject Inner => _inner;

        public void RebuildCachedChildren()
        {
            var list = new List<AccessibleObject>();
            if (_inner != null)
            {
                int count = 0;
                try { count = _inner.GetChildCount(); } catch { count = 0; }
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        AccessibleObject child = _inner.GetChild(i);
                        if (child != null)
                            list.Add(child);
                    }
                    catch
                    {
                    }
                }
            }
            _cachedChildren = list;
        }

        public override AccessibleRole Role => _inner != null ? _inner.Role : base.Role;

        public override string Name
        {
            get => _inner != null ? _inner.Name : base.Name;
            set
            {
                if (_inner != null)
                    _inner.Name = value;
                else
                    base.Name = value;
            }
        }

        public override Rectangle Bounds => _inner != null ? _inner.Bounds : base.Bounds;

        public override int GetChildCount()
        {
            if (_cachedChildren == null)
                RebuildCachedChildren();
            return _cachedChildren.Count;
        }

        public override AccessibleObject GetChild(int index)
        {
            if (_cachedChildren == null)
                RebuildCachedChildren();
            if (index < 0 || index >= _cachedChildren.Count)
                return null;
            return _cachedChildren[index];
        }
    }

    /// <summary>
    /// Enhances Microsoft.Reporting.WinForms.ReportViewer charts with WCAG 1.1.1 / UI Automation
    /// accessibility support:
    /// 1. Populates each chart's UIA AccessibleName with the chart overview (including sampling interval,
    ///    X-axis meaning, and Y-axis meaning) instead of the generic "Chart" string.
    /// 2. Exposes sampled data points as child AccessibleObjects in the UIA tree for Scan Mode navigation.
    /// 3. Announces chart overview on first mouse hover into a chart, exact timestamp/value on sampled points,
    ///    and linearly interpolated timestamp/estimated value on non-sampled line segments without requiring Scan Mode.
    /// </summary>
    public static class ReportChartAccessibilityEnhancer
    {
        private const BindingFlags AllInstanceFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private sealed class RenderingPanelNativeMouseHook : NativeWindow
        {
            private const int WM_MOUSEMOVE = 0x0200;
            private const int WM_MOUSELEAVE = 0x02A3;

            private readonly ReportViewer _viewer;
            private readonly ViewerHoverState _state;

            public RenderingPanelNativeMouseHook(ReportViewer viewer, ViewerHoverState state, Control target)
            {
                _viewer = viewer;
                _state = state;

                if (target.IsHandleCreated)
                    AssignHandle(target.Handle);

                target.HandleCreated += (s, e) => AssignHandle(target.Handle);
                target.HandleDestroyed += (s, e) => ReleaseHandle();
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);

                if (m.Msg == WM_MOUSEMOVE)
                {
                    long lparam = m.LParam.ToInt64();
                    int x = (short)(lparam & 0xFFFF);
                    int y = (short)((lparam >> 16) & 0xFFFF);
                    OnRenderingPanelMouseMove(_viewer, _state, new Point(x, y));
                }
                else if (m.Msg == WM_MOUSELEAVE)
                {
                    _state.LastHoveredChartId = null;
                    _state.LastAnnouncement = null;
                }
            }
        }

        private sealed class ViewerHoverState
        {
            public Control RenderingPanel;
            public Control ReportPanel;
            public Dictionary<string, ChartAccessibilityModel> ChartsById =
                new Dictionary<string, ChartAccessibilityModel>(StringComparer.Ordinal);
            public string LastHoveredChartId;
            public string LastAnnouncement;
            public DateTime LastAnnouncementUtc = DateTime.MinValue;
            public string PendingAnnouncement;
            public Point PendingClientMousePt;
            public Timer PendingHoverTimer;
            public bool MouseHooked;
            public bool MetadataLoadedForCurrentRender;
            public RenderingPanelNativeMouseHook NativeHook;
        }

        static ReportChartAccessibilityEnhancer()
        {
            try
            {
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures", false);
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures.2", false);
                AppContext.SetSwitch("Switch.UseLegacyAccessibilityFeatures.3", false);
            }
            catch
            {
            }
        }

        private static readonly ConditionalWeakTable<ReportViewer, ViewerHoverState> ViewerStates =
            new ConditionalWeakTable<ReportViewer, ViewerHoverState>();

        public static void Attach(ReportViewer viewer)
        {
            if (viewer == null || viewer.IsDisposed)
                return;

            try
            {
                ViewerHoverState state = ViewerStates.GetOrCreateValue(viewer);
                state.MetadataLoadedForCurrentRender = false;
                RefreshMetadata(viewer, state);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ReportChartAccessibilityEnhancer.Attach error: " + ex.Message);
            }
        }

        internal static IReadOnlyDictionary<string, ChartAccessibilityModel> GetChartModels(ReportViewer viewer)
        {
            if (viewer != null && ViewerStates.TryGetValue(viewer, out ViewerHoverState state))
            {
                if (!state.MetadataLoadedForCurrentRender)
                {
                    RefreshMetadata(viewer, state);
                }
                if (state.ChartsById != null)
                {
                    return state.ChartsById;
                }
            }
            return new Dictionary<string, ChartAccessibilityModel>();
        }

        internal static string GetLastAnnouncement(ReportViewer viewer)
        {
            if (viewer != null && ViewerStates.TryGetValue(viewer, out ViewerHoverState state))
            {
                return state.LastAnnouncement;
            }
            return null;
        }

        internal static string SimulateClientMouseMove(ReportViewer viewer, Point clientMousePt)
        {
            if (viewer != null && ViewerStates.TryGetValue(viewer, out ViewerHoverState state))
            {
                OnRenderingPanelMouseMove(viewer, state, clientMousePt);
                return state.LastAnnouncement;
            }
            return null;
        }

        internal static bool TryParseSampleCaption(
            string caption,
            out string seriesName,
            out DateTime timestamp,
            out string rawTimeText,
            out double value,
            out string unit)
        {
            seriesName = string.Empty;
            timestamp = DateTime.MinValue;
            rawTimeText = string.Empty;
            value = 0;
            unit = string.Empty;

            if (string.IsNullOrWhiteSpace(caption))
                return false;

            const string timeMarker = ": at time ";
            const string valueMarker = ", value is ";

            int timeIdx = caption.IndexOf(timeMarker, StringComparison.OrdinalIgnoreCase);
            if (timeIdx < 0)
                return false;

            int valueIdx = caption.IndexOf(valueMarker, timeIdx + timeMarker.Length, StringComparison.OrdinalIgnoreCase);
            if (valueIdx < 0)
                return false;

            seriesName = timeIdx == 0 ? "Value" : caption.Substring(0, timeIdx).Trim();
            if (string.IsNullOrEmpty(seriesName))
                seriesName = "Value";

            rawTimeText = caption.Substring(timeIdx + timeMarker.Length, valueIdx - (timeIdx + timeMarker.Length)).Trim();
            string valueAndUnit = caption.Substring(valueIdx + valueMarker.Length).Trim().TrimEnd('.');

            if (string.IsNullOrEmpty(rawTimeText) || string.IsNullOrEmpty(valueAndUnit))
                return false;

            if (!DateTime.TryParse(rawTimeText, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out timestamp) &&
                !DateTime.TryParse(rawTimeText, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out timestamp))
            {
                return false;
            }

            string numberPart = valueAndUnit;
            int spaceIdx = valueAndUnit.IndexOf(' ');
            if (spaceIdx > 0)
            {
                numberPart = valueAndUnit.Substring(0, spaceIdx).Trim();
                unit = valueAndUnit.Substring(spaceIdx + 1).Trim();
            }

            if (!double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
                !double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
            {
                return false;
            }

            return true;
        }

        internal static bool TryParseCategoricalCaption(
            string caption,
            out string categoryLabel,
            out double value,
            out string unit)
        {
            categoryLabel = string.Empty;
            value = 0;
            unit = string.Empty;

            if (string.IsNullOrWhiteSpace(caption))
                return false;

            if (caption.IndexOf(": at time ", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            const string valueMarker = ": value is ";
            int valueIdx = caption.IndexOf(valueMarker, StringComparison.OrdinalIgnoreCase);
            if (valueIdx <= 0)
                return false;

            categoryLabel = caption.Substring(0, valueIdx).Trim();
            string valueAndUnit = caption.Substring(valueIdx + valueMarker.Length).Trim().TrimEnd('.');
            if (string.IsNullOrEmpty(categoryLabel) || string.IsNullOrEmpty(valueAndUnit))
                return false;

            string numberPart = valueAndUnit;
            int spaceIdx = valueAndUnit.IndexOf(' ');
            if (spaceIdx > 0)
            {
                numberPart = valueAndUnit.Substring(0, spaceIdx).Trim();
                unit = valueAndUnit.Substring(spaceIdx + 1).Trim();
            }

            if (!double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out value) &&
                !double.TryParse(numberPart, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out value))
            {
                return false;
            }

            return true;
        }

        internal static void ExtractSeriesVertices(IList<ChartSamplePoint> seriesPointsSortedByTime)
        {
            if (seriesPointsSortedByTime == null || seriesPointsSortedByTime.Count == 0)
                return;

            int count = seriesPointsSortedByTime.Count;
            for (int k = 0; k < count; k++)
            {
                ChartSamplePoint sample = seriesPointsSortedByTime[k];
                Point[] pts = sample.PolygonPoints;
                if (pts == null || pts.Length == 0)
                    continue;

                if (count == 1 || pts.Length < 4)
                {
                    int avgX = (int)Math.Round(pts.Average(p => p.X));
                    int avgY = (int)Math.Round(pts.Average(p => p.Y));
                    sample.VertexPixel = new Point(avgX, avgY);
                }
                else if (k == 0)
                {
                    int vx = (int)Math.Round((pts[0].X + pts[3].X) / 2.0);
                    int vy = (int)Math.Round((pts[0].Y + pts[3].Y) / 2.0);
                    sample.VertexPixel = new Point(vx, vy);
                }
                else
                {
                    int vx = (int)Math.Round((pts[1].X + pts[2].X) / 2.0);
                    int vy = (int)Math.Round((pts[1].Y + pts[2].Y) / 2.0);
                    sample.VertexPixel = new Point(vx, vy);
                }
            }
        }

        internal static string FormatNoDataOverview(string baseChartToolTip)
        {
            if (string.IsNullOrWhiteSpace(baseChartToolTip))
                return "No data available.";

            string trimmed = baseChartToolTip.Trim();
            if (trimmed.IndexOf("No data available", StringComparison.OrdinalIgnoreCase) >= 0)
                return trimmed;

            const string chartOfMarker = " chart of ";
            int markerIdx = trimmed.IndexOf(chartOfMarker, StringComparison.OrdinalIgnoreCase);
            if (markerIdx >= 0)
            {
                string afterChartOf = trimmed.Substring(markerIdx + chartOfMarker.Length).Trim();
                int intervalIdx = afterChartOf.IndexOf(", sampling interval", StringComparison.OrdinalIgnoreCase);
                if (intervalIdx > 0)
                {
                    string topic = afterChartOf.Substring(0, intervalIdx).Trim();
                    if (!string.IsNullOrEmpty(topic))
                        return topic + ": No data available.";
                }

                int xAxisIdx = afterChartOf.IndexOf(". X-axis:", StringComparison.OrdinalIgnoreCase);
                if (xAxisIdx > 0)
                {
                    string topic = afterChartOf.Substring(0, xAxisIdx).Trim();
                    if (!string.IsNullOrEmpty(topic))
                        return topic + ": No data available.";
                }
            }

            return "No data available.";
        }

        internal static string BuildChartOverviewDescription(
            string baseChartToolTip,
            IList<ChartSamplePoint> allChartPoints,
            bool? hasData = null)
        {
            if (hasData.HasValue && !hasData.Value)
                return FormatNoDataOverview(baseChartToolTip);

            if (!string.IsNullOrWhiteSpace(baseChartToolTip) &&
                baseChartToolTip.IndexOf("No data available", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return baseChartToolTip.Trim();
            }

            if ((allChartPoints == null || allChartPoints.Count == 0) &&
                !string.IsNullOrWhiteSpace(baseChartToolTip) &&
                baseChartToolTip.IndexOf("sampling interval 0 seconds", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return FormatNoDataOverview(baseChartToolTip);
            }

            string baseText = string.IsNullOrWhiteSpace(baseChartToolTip)
                ? "Line chart. X-axis: Time. Y-axis: Metric value."
                : baseChartToolTip.Trim();

            if (allChartPoints == null || allChartPoints.Count < 2)
                return baseText;

            List<DateTime> distinctTimestamps = allChartPoints
                .Select(p => p.Timestamp)
                .Where(t => t != DateTime.MinValue)
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            if (distinctTimestamps.Count < 2)
                return baseText;

            double totalSeconds = (distinctTimestamps[distinctTimestamps.Count - 1] - distinctTimestamps[0]).TotalSeconds;
            int intervalSeconds = (int)Math.Round(totalSeconds / (distinctTimestamps.Count - 1));
            if (intervalSeconds <= 0 || baseText.IndexOf("sampling interval", StringComparison.OrdinalIgnoreCase) >= 0)
                return baseText;

            string intervalClause = string.Format(CultureInfo.InvariantCulture, ", sampling interval {0} seconds", intervalSeconds);
            int periodIdx = baseText.IndexOf(". X-axis:", StringComparison.OrdinalIgnoreCase);
            if (periodIdx > 0)
            {
                return baseText.Substring(0, periodIdx) + intervalClause + baseText.Substring(periodIdx);
            }

            int firstComma = baseText.IndexOf(',');
            if (firstComma > 0)
            {
                return baseText.Substring(0, firstComma) + intervalClause + baseText.Substring(firstComma);
            }

            return baseText.TrimEnd('.') + intervalClause + ".";
        }

        internal static string EvaluateHoverAnnouncement(
            ChartAccessibilityModel chart,
            Point localMousePt,
            bool isFirstHoverOnChart)
        {
            if (chart == null)
                return null;

            if (isFirstHoverOnChart)
                return chart.OverviewDescription;

            const double vertexXHitTolerance = 8.0;
            const double vertexYHitTolerance = 12.0;
            const double segmentHitTolerance = 10.0;

            // 1. Check categorical data points (bar/column/pie) first
            foreach (ChartSamplePoint sample in chart.SamplePoints)
            {
                if (!sample.IsCategorical)
                    continue;

                if (sample.PolygonPoints != null && sample.PolygonPoints.Length >= 3 &&
                    IsPointInPolygon(localMousePt, sample.PolygonPoints))
                {
                    return sample.Caption;
                }

                if (sample.BarPageBounds != Rectangle.Empty && chart.PageBounds != Rectangle.Empty)
                {
                    Rectangle localBar = new Rectangle(
                        sample.BarPageBounds.Left - chart.PageBounds.Left,
                        sample.BarPageBounds.Top - chart.PageBounds.Top,
                        sample.BarPageBounds.Width,
                        sample.BarPageBounds.Height);
                    if (localBar.Contains(localMousePt))
                    {
                        return sample.Caption;
                    }
                }
            }

            // 2. Check exact sampled time-series vertices
            ChartSamplePoint closestSample = null;
            double bestVertexDist = double.MaxValue;
            foreach (ChartSamplePoint sample in chart.SamplePoints)
            {
                if (sample.IsCategorical || sample.VertexPixel == Point.Empty)
                    continue;

                double dx = Math.Abs(localMousePt.X - sample.VertexPixel.X);
                double dy = Math.Abs(localMousePt.Y - sample.VertexPixel.Y);
                if (dx <= vertexXHitTolerance && dy <= vertexYHitTolerance)
                {
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < bestVertexDist)
                    {
                        bestVertexDist = dist;
                        closestSample = sample;
                    }
                }
            }

            if (closestSample != null)
            {
                if (!string.IsNullOrWhiteSpace(closestSample.Caption))
                    return closestSample.Caption;

                string unitPart = string.IsNullOrEmpty(closestSample.Unit) ? string.Empty : " " + closestSample.Unit;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: at time {1}, value is {2:0.##}{3}.",
                    closestSample.SeriesName,
                    !string.IsNullOrEmpty(closestSample.RawTimestampText)
                        ? closestSample.RawTimestampText
                        : closestSample.Timestamp.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    closestSample.Value,
                    unitPart);
            }

            // 3. Check non-sampled line segments between consecutive sampled points of each series
            ChartSamplePoint bestP1 = null;
            ChartSamplePoint bestP2 = null;
            double bestSegmentDist = double.MaxValue;
            double bestAlpha = 0.0;

            foreach (List<ChartSamplePoint> series in chart.SeriesByName.Values)
            {
                if (series == null || series.Count < 2)
                    continue;

                for (int i = 0; i < series.Count - 1; i++)
                {
                    ChartSamplePoint p1 = series[i];
                    ChartSamplePoint p2 = series[i + 1];
                    if (p1.VertexPixel == Point.Empty || p2.VertexPixel == Point.Empty)
                        continue;

                    int x1 = p1.VertexPixel.X;
                    int x2 = p2.VertexPixel.X;
                    if (x2 <= x1)
                        continue;

                    if (localMousePt.X <= x1 + vertexXHitTolerance || localMousePt.X >= x2 - vertexXHitTolerance)
                        continue;

                    double alpha = (double)(localMousePt.X - x1) / (x2 - x1);
                    double expectedY = p1.VertexPixel.Y + alpha * (p2.VertexPixel.Y - p1.VertexPixel.Y);
                    double verticalDist = Math.Abs(localMousePt.Y - expectedY);
                    double perpDist = DistancePointToSegment(localMousePt, p1.VertexPixel, p2.VertexPixel);

                    bool inPoly = (p1.PolygonPoints != null && p1.PolygonPoints.Length >= 3 && IsPointInPolygon(localMousePt, p1.PolygonPoints)) ||
                                  (p2.PolygonPoints != null && p2.PolygonPoints.Length >= 3 && IsPointInPolygon(localMousePt, p2.PolygonPoints));

                    if ((perpDist <= segmentHitTolerance || verticalDist <= segmentHitTolerance || inPoly) && perpDist < bestSegmentDist)
                    {
                        bestSegmentDist = perpDist;
                        bestP1 = p1;
                        bestP2 = p2;
                        bestAlpha = alpha;
                    }
                }
            }

            if (bestP1 != null && bestP2 != null)
            {
                double totalSec = (bestP2.Timestamp - bestP1.Timestamp).TotalSeconds;
                DateTime interpTime = bestP1.Timestamp.AddSeconds(bestAlpha * totalSec);
                double interpVal = Math.Round(bestP1.Value + bestAlpha * (bestP2.Value - bestP1.Value), 2);
                string unitSuffix = string.IsNullOrEmpty(bestP1.Unit) ? string.Empty : " " + bestP1.Unit;

                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: non-sampling time, the time is {1:yyyy-MM-dd HH:mm:ss}, and estimated value is {2:0.##}{3}.",
                    bestP1.SeriesName,
                    interpTime,
                    interpVal,
                    unitSuffix);
            }

            return null;
        }

        private static double DistancePointToSegment(Point pt, Point a, Point b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            if (dx == 0 && dy == 0)
                return Math.Sqrt((pt.X - a.X) * (pt.X - a.X) + (pt.Y - a.Y) * (pt.Y - a.Y));

            double t = ((pt.X - a.X) * dx + (pt.Y - a.Y) * dy) / (dx * dx + dy * dy);
            t = Math.Max(0.0, Math.Min(1.0, t));
            double projX = a.X + t * dx;
            double projY = a.Y + t * dy;
            return Math.Sqrt((pt.X - projX) * (pt.X - projX) + (pt.Y - projY) * (pt.Y - projY));
        }

        private static bool IsPointInPolygon(Point pt, Point[] polygon)
        {
            if (polygon == null || polygon.Length < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if (((polygon[i].Y > pt.Y) != (polygon[j].Y > pt.Y)) &&
                    (pt.X < (polygon[j].X - polygon[i].X) * (double)(pt.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X))
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        private static void EnsureAccessibleTreeInitialized(AccessibleObject rootAcc, int depth = 0)
        {
            if (rootAcc == null || depth > 6)
                return;

            int count = 0;
            try
            {
                count = rootAcc.GetChildCount();
            }
            catch
            {
                count = 0;
            }

            for (int i = 0; i < count; i++)
            {
                try
                {
                    AccessibleObject child = rootAcc.GetChild(i);
                    if (child != null)
                        EnsureAccessibleTreeInitialized(child, depth + 1);
                }
                catch
                {
                }
            }
        }

        private static void RefreshMetadata(ReportViewer viewer, ViewerHoverState state)
        {
            Control reportPanel = FindChildControlByTypeName(viewer, "ReportPanel");
            if (reportPanel == null)
                return;

            state.ReportPanel = reportPanel;
            FieldInfo renderPanelField = reportPanel.GetType().GetField("m_renderPanel", AllInstanceFlags);
            Control renderingPanel = renderPanelField != null ? renderPanelField.GetValue(reportPanel) as Control : null;
            state.RenderingPanel = renderingPanel ?? reportPanel;

            if (!state.MouseHooked && state.RenderingPanel != null)
            {
                state.MouseHooked = true;
                state.NativeHook = new RenderingPanelNativeMouseHook(viewer, state, state.RenderingPanel);
            }

            AccessibleObject rawPanelAcc = reportPanel.AccessibilityObject;
            EnsureAccessibleTreeInitialized(rawPanelAcc);

            object renderingReport = GetFieldOrPropertyValue<object>(
                rawPanelAcc,
                rawPanelAcc.GetType(),
                "Report");
            if (renderingReport != null)
            {
                state.MetadataLoadedForCurrentRender = true;
            }

            IList rptActionList = null;
            if (renderingReport != null)
            {
                object rptToolTips = GetFieldOrPropertyValue<object>(renderingReport, renderingReport.GetType(), "ToolTips");
                rptActionList = ExtractActionList(rptToolTips);
            }

            PropertyInfo currentPageProp = reportPanel.GetType().GetProperty("CurrentPage", AllInstanceFlags);
            object currentPage = currentPageProp != null ? currentPageProp.GetValue(reportPanel, null) : null;
            IList pageActionList = null;
            if (currentPage != null)
            {
                MethodInfo buildInteractivity = currentPage.GetType().GetMethod("BuildInteractivityInfo", AllInstanceFlags);
                if (buildInteractivity != null && buildInteractivity.GetParameters().Length == 0)
                {
                    try { buildInteractivity.Invoke(currentPage, null); } catch { }
                }

                object pageToolTips = GetFieldOrPropertyValue<object>(currentPage, currentPage.GetType(), "ToolTips");
                pageActionList = ExtractActionList(pageToolTips);
            }

            List<object> combinedActions = new List<object>();
            if (pageActionList != null)
            {
                foreach (object act in pageActionList)
                {
                    if (act != null)
                        combinedActions.Add(act);
                }
            }
            if (rptActionList != null)
            {
                foreach (object act in rptActionList)
                {
                    if (act != null && !combinedActions.Contains(act))
                        combinedActions.Add(act);
                }
            }
            IList actionList = combinedActions;

            Dictionary<string, ChartAccessibilityModel> charts =
                new Dictionary<string, ChartAccessibilityModel>(StringComparer.Ordinal);

            if (actionList != null)
            {
                float dpiX = 96f;
                float dpiY = 96f;
                if (reportPanel.IsHandleCreated)
                {
                    try
                    {
                        using (Graphics g = reportPanel.CreateGraphics())
                        {
                            dpiX = g.DpiX;
                            dpiY = g.DpiY;
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (object action in actionList)
                {
                    if (action == null)
                        continue;

                    Type actionType = action.GetType();
                    Rectangle pos = GetFieldOrPropertyValue<Rectangle>(action, actionType, "Position");
                    Point[] points = GetFieldOrPropertyValue<Point[]>(action, actionType, "Points");
                    if (pos == Rectangle.Empty || points == null)
                    {
                        MethodInfo setDpi = actionType.GetMethod("SetDpi", AllInstanceFlags);
                        if (setDpi != null)
                        {
                            try { setDpi.Invoke(action, new object[] { dpiX, dpiY }); } catch { }
                        }
                        pos = GetFieldOrPropertyValue<Rectangle>(action, actionType, "Position");
                        points = GetFieldOrPropertyValue<Point[]>(action, actionType, "Points");
                    }

                    string id = GetFieldOrPropertyValue<string>(action, actionType, "Id");
                    string caption = GetFieldOrPropertyValue<string>(action, actionType, "Caption");
                    object shapeObj = GetFieldOrPropertyValue<object>(action, actionType, "Shape");
                    string shape = shapeObj != null ? shapeObj.ToString() : string.Empty;

                    if (string.IsNullOrEmpty(id) || string.IsNullOrWhiteSpace(caption))
                        continue;

                    ChartAccessibilityModel chartModel;
                    if (!charts.TryGetValue(id, out chartModel))
                    {
                        chartModel = new ChartAccessibilityModel
                        {
                            ChartId = id,
                            PageBounds = pos,
                            RepresentativeAction = action
                        };
                        charts[id] = chartModel;
                    }
                    else
                    {
                        if (chartModel.RepresentativeAction == null)
                            chartModel.RepresentativeAction = action;
                        if (pos != Rectangle.Empty)
                        {
                            chartModel.PageBounds = chartModel.PageBounds == Rectangle.Empty
                                ? pos
                                : Rectangle.Union(chartModel.PageBounds, pos);
                        }
                    }

                    string seriesName;
                    DateTime timestamp;
                    string rawTime;
                    double val;
                    string unit;
                    string categoryLabel;
                    if (TryParseSampleCaption(caption, out seriesName, out timestamp, out rawTime, out val, out unit))
                    {
                        List<ChartSamplePoint> seriesList;
                        if (!chartModel.SeriesByName.TryGetValue(seriesName, out seriesList))
                        {
                            seriesList = new List<ChartSamplePoint>();
                            chartModel.SeriesByName[seriesName] = seriesList;
                        }

                        if (!seriesList.Any(existing => existing.Timestamp == timestamp))
                        {
                            ChartSamplePoint sample = new ChartSamplePoint
                            {
                                SeriesName = seriesName,
                                Timestamp = timestamp,
                                RawTimestampText = rawTime,
                                Value = val,
                                Unit = unit,
                                PolygonPoints = points,
                                BarPageBounds = (points == null || points.Length == 0) ? pos : Rectangle.Empty,
                                IsCategorical = false,
                                Caption = caption
                            };
                            seriesList.Add(sample);
                            chartModel.SamplePoints.Add(sample);
                        }
                    }
                    else if (TryParseCategoricalCaption(caption, out categoryLabel, out val, out unit))
                    {
                        if (!chartModel.SamplePoints.Any(existing => string.Equals(existing.Caption, caption, StringComparison.Ordinal)))
                        {
                            ChartSamplePoint sample = new ChartSamplePoint
                            {
                                SeriesName = categoryLabel,
                                Timestamp = DateTime.MinValue,
                                RawTimestampText = string.Empty,
                                Value = val,
                                Unit = unit,
                                PolygonPoints = points,
                                BarPageBounds = (points == null || points.Length == 0) ? pos : Rectangle.Empty,
                                IsCategorical = true,
                                Caption = caption
                            };
                            chartModel.SamplePoints.Add(sample);
                        }
                    }
                    else if (string.IsNullOrEmpty(chartModel.BaseToolTip) &&
                             string.Equals(shape, "Rectangle", StringComparison.OrdinalIgnoreCase))
                    {
                        chartModel.BaseToolTip = caption;
                        if (pos != Rectangle.Empty)
                            chartModel.PageBounds = pos;
                    }
                }
            }

            foreach (ChartAccessibilityModel chartModel in charts.Values)
            {
                foreach (KeyValuePair<string, List<ChartSamplePoint>> kvp in chartModel.SeriesByName.ToList())
                {
                    List<ChartSamplePoint> sorted = kvp.Value.OrderBy(p => p.Timestamp).ToList();
                    ExtractSeriesVertices(sorted);
                    chartModel.SeriesByName[kvp.Key] = sorted;
                }

                foreach (ChartSamplePoint sample in chartModel.SamplePoints)
                {
                    if (sample.VertexPixel == Point.Empty && sample.BarPageBounds != Rectangle.Empty && chartModel.PageBounds != Rectangle.Empty)
                    {
                        sample.VertexPixel = new Point(
                            sample.BarPageBounds.Left - chartModel.PageBounds.Left + sample.BarPageBounds.Width / 2,
                            sample.BarPageBounds.Top - chartModel.PageBounds.Top + sample.BarPageBounds.Height / 2);
                    }
                }

                chartModel.OverviewDescription = BuildChartOverviewDescription(chartModel.BaseToolTip, chartModel.SamplePoints);
            }

            UpdateAccessibleTree(rawPanelAcc, charts);

            // Update the chart-level overview rectangle tooltip captions in ToolTips with the enriched OverviewDescription
            if (actionList != null)
            {
                foreach (object action in actionList)
                {
                    if (action == null)
                        continue;

                    Type actionType = action.GetType();
                    string id = GetFieldOrPropertyValue<string>(action, actionType, "Id");
                    string cap = GetFieldOrPropertyValue<string>(action, actionType, "Caption");
                    Point[] points = GetFieldOrPropertyValue<Point[]>(action, actionType, "Points");
                    ChartAccessibilityModel chartModel;
                    if (!string.IsNullOrEmpty(id) &&
                        points == null &&
                        !string.IsNullOrEmpty(cap) &&
                        cap.IndexOf(": at time ", StringComparison.OrdinalIgnoreCase) < 0 &&
                        cap.IndexOf(": value is ", StringComparison.OrdinalIgnoreCase) < 0 &&
                        charts.TryGetValue(id, out chartModel) &&
                        !string.IsNullOrEmpty(chartModel.OverviewDescription))
                    {
                        FieldInfo captionField = FindFieldInHierarchy(actionType, "Caption") ?? FindFieldInHierarchy(actionType, "m_caption");
                        if (captionField != null)
                            captionField.SetValue(action, chartModel.OverviewDescription);
                    }
                }
            }

            state.ChartsById = charts;
            state.LastHoveredChartId = null;
        }

        private static void UpdateAccessibleTree(
            AccessibleObject rootAcc,
            Dictionary<string, ChartAccessibilityModel> charts)
        {
            if (rootAcc == null)
                return;

            Queue<AccessibleObject> queue = new Queue<AccessibleObject>();
            queue.Enqueue(rootAcc);

            while (queue.Count > 0)
            {
                AccessibleObject current = queue.Dequeue();
                if (current == null)
                    continue;

                Type accType = current.GetType();
                FieldInfo renderingItemField = FindFieldInHierarchy(accType, "m_renderingItem");
                if (renderingItemField != null)
                {
                    object renderingItem = renderingItemField.GetValue(current);
                    if (renderingItem != null &&
                        string.Equals(renderingItem.GetType().Name, "RenderingDynamicImage", StringComparison.Ordinal))
                    {
                        string uniqueName = GetUniqueNameFromRenderingItem(renderingItem);
                        string itemToolTip = GetPropertyValue<string>(renderingItem, renderingItem.GetType(), "ToolTip");

                        ChartAccessibilityModel chartModel = null;
                        if (!string.IsNullOrEmpty(uniqueName))
                        {
                            if (!charts.TryGetValue(uniqueName, out chartModel))
                            {
                                chartModel = new ChartAccessibilityModel
                                {
                                    ChartId = uniqueName,
                                    BaseToolTip = itemToolTip
                                };
                                charts[uniqueName] = chartModel;
                            }
                            else if (!string.IsNullOrEmpty(itemToolTip))
                            {
                                chartModel.BaseToolTip = itemToolTip;
                            }
                        }

                        if (chartModel != null)
                        {
                            chartModel.OverviewDescription = BuildChartOverviewDescription(
                                chartModel.BaseToolTip,
                                chartModel.SamplePoints);
                        }

                        string overview = chartModel != null && !string.IsNullOrEmpty(chartModel.OverviewDescription)
                            ? chartModel.OverviewDescription
                            : BuildChartOverviewDescription(itemToolTip, null);

                        FieldInfo accNameField = FindFieldInHierarchy(renderingItem.GetType(), "m_accessibleName");
                        if (accNameField != null && !string.IsNullOrEmpty(overview))
                        {
                            accNameField.SetValue(renderingItem, overview);
                        }

                        if (chartModel != null && chartModel.SamplePoints.Count > 0)
                        {
                            FieldInfo childrenField = FindFieldInHierarchy(accType, "m_children");
                            if (childrenField != null)
                            {
                                Rectangle chartScreenBounds = Rectangle.Empty;
                                try { chartScreenBounds = current.Bounds; } catch { }

                                List<AccessibleObject> pointChildren = new List<AccessibleObject>();
                                foreach (ChartSamplePoint sample in chartModel.SamplePoints)
                                {
                                    ChartSamplePoint capturedSample = sample;
                                    AccessibleObject capturedParent = current;
                                    pointChildren.Add(new ChartDataPointAccessibleObject(
                                        capturedParent,
                                        capturedSample.Caption,
                                        capturedSample.Value.ToString(CultureInfo.InvariantCulture),
                                        () =>
                                        {
                                            try
                                            {
                                                Rectangle b = capturedParent.Bounds;
                                                if (b != Rectangle.Empty && capturedSample.VertexPixel != Point.Empty)
                                                {
                                                    return new Rectangle(
                                                        b.Left + capturedSample.VertexPixel.X - 6,
                                                        b.Top + capturedSample.VertexPixel.Y - 6,
                                                        12,
                                                        12);
                                                }
                                            }
                                            catch
                                            {
                                            }
                                            return Rectangle.Empty;
                                        }));
                                }
                                childrenField.SetValue(current, pointChildren);
                            }
                        }
                        continue;
                    }
                }

                int childCount = 0;
                try
                {
                    childCount = current.GetChildCount();
                }
                catch
                {
                    childCount = 0;
                }

                for (int i = 0; i < childCount; i++)
                {
                    try
                    {
                        AccessibleObject child = current.GetChild(i);
                        if (child != null)
                            queue.Enqueue(child);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void OnRenderingPanelMouseMove(ReportViewer viewer, ViewerHoverState state, Point clientMousePt)
        {
            if (state == null || state.RenderingPanel == null)
                return;

            if (state.ChartsById == null || state.ChartsById.Count == 0)
            {
                RefreshMetadata(viewer, state);
                if (state.ChartsById == null || state.ChartsById.Count == 0)
                    return;
            }

            try
            {
                float zoomRate = 1.0f;
                MethodInfo zoomMethod = state.RenderingPanel.GetType().GetMethod("GetZoomRate", AllInstanceFlags);
                if (zoomMethod != null)
                {
                    object z = zoomMethod.Invoke(state.RenderingPanel, null);
                    if (z is float f && f > 0.01f)
                        zoomRate = f;
                }

                int pageX = (int)Math.Round(clientMousePt.X / zoomRate);
                int pageY = (int)Math.Round(clientMousePt.Y / zoomRate);
                Point pagePt = new Point(pageX, pageY);

                ChartAccessibilityModel hitChart = null;
                Rectangle hitChartEffectiveBounds = Rectangle.Empty;
                foreach (ChartAccessibilityModel chart in state.ChartsById.Values)
                {
                    Rectangle effectiveBounds = GetEffectivePageBounds(chart);
                    if (effectiveBounds.Contains(pagePt))
                    {
                        hitChart = chart;
                        hitChartEffectiveBounds = effectiveBounds;
                        break;
                    }
                }

                if (hitChart == null)
                {
                    state.LastHoveredChartId = null;
                    state.LastAnnouncement = null;
                    return;
                }

                bool isFirstHover = !string.Equals(state.LastHoveredChartId, hitChart.ChartId, StringComparison.Ordinal);
                state.LastHoveredChartId = hitChart.ChartId;

                Point localPt = new Point(pagePt.X - hitChartEffectiveBounds.Left, pagePt.Y - hitChartEffectiveBounds.Top);
                string announcement = EvaluateHoverAnnouncement(hitChart, localPt, isFirstHover);

                FieldInfo toolTipField = state.RenderingPanel.GetType().GetField("m_toolTipControl", AllInstanceFlags);
                ToolTip toolTipControl = toolTipField != null ? toolTipField.GetValue(state.RenderingPanel) as ToolTip : null;

                if (string.IsNullOrEmpty(announcement))
                {
                    state.PendingAnnouncement = null;
                    if (state.PendingHoverTimer != null)
                        state.PendingHoverTimer.Stop();
                    return;
                }

                // Update the visual WinForms ToolTip on RenderingPanel so sighted/low-vision users and
                // screen readers inspecting ToolTip text see the exact same announcement (including non-sampling interpolation).
                if (toolTipControl != null)
                {
                    string currentTip = toolTipControl.GetToolTip(state.RenderingPanel);
                    if (!string.Equals(currentTip, announcement, StringComparison.Ordinal))
                    {
                        toolTipControl.SetToolTip(state.RenderingPanel, announcement);
                    }
                }

                DateTime now = DateTime.UtcNow;
                if (!string.Equals(state.LastAnnouncement, announcement, StringComparison.Ordinal))
                {
                    if (isFirstHover || (now - state.LastAnnouncementUtc).TotalMilliseconds >= 220)
                    {
                        if (state.PendingHoverTimer != null)
                            state.PendingHoverTimer.Stop();
                        state.PendingAnnouncement = null;
                        DeliverAnnouncement(viewer, state, toolTipControl, announcement, clientMousePt);
                    }
                    else
                    {
                        // Mouse moved onto the curve shortly after entering the chart or moving from an adjacent point.
                        // Arm a short timer so when the user stops moving the mouse on the curve, the point is still announced.
                        state.PendingAnnouncement = announcement;
                        state.PendingClientMousePt = clientMousePt;
                        if (state.PendingHoverTimer == null)
                        {
                            state.PendingHoverTimer = new Timer { Interval = 220 };
                            state.PendingHoverTimer.Tick += (s, e) =>
                            {
                                state.PendingHoverTimer.Stop();
                                if (!string.IsNullOrEmpty(state.PendingAnnouncement) &&
                                    !string.Equals(state.LastAnnouncement, state.PendingAnnouncement, StringComparison.Ordinal))
                                {
                                    FieldInfo tf = state.RenderingPanel != null
                                        ? state.RenderingPanel.GetType().GetField("m_toolTipControl", AllInstanceFlags)
                                        : null;
                                    ToolTip tc = tf != null ? tf.GetValue(state.RenderingPanel) as ToolTip : null;
                                    DeliverAnnouncement(viewer, state, tc, state.PendingAnnouncement, state.PendingClientMousePt);
                                }
                            };
                        }
                        state.PendingHoverTimer.Stop();
                        state.PendingHoverTimer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OnRenderingPanelMouseMove error: " + ex.Message);
            }
        }

        private static void DeliverAnnouncement(
            ReportViewer viewer,
            ViewerHoverState state,
            ToolTip toolTipControl,
            string announcement,
            Point clientMousePt)
        {
            state.LastAnnouncement = announcement;
            state.LastAnnouncementUtc = DateTime.UtcNow;

            if (toolTipControl != null && state.RenderingPanel != null &&
                state.RenderingPanel.IsHandleCreated && state.RenderingPanel.Visible)
            {
                try
                {
                    toolTipControl.SetToolTip(state.RenderingPanel, announcement);
                    toolTipControl.Show(announcement, state.RenderingPanel, clientMousePt.X + 16, clientMousePt.Y + 20, 4000);
                }
                catch
                {
                }
            }

            if (viewer != null && !viewer.IsDisposed)
            {
                Control announceTarget = viewer.FindForm() ?? (Control)viewer;
                if (announceTarget.IsHandleCreated)
                {
                    announceTarget.AccessibilityObject.RaiseAutomationNotification(
                        AutomationNotificationKind.ActionCompleted,
                        AutomationNotificationProcessing.ImportantMostRecent,
                        announcement);
                }
            }
        }

        private static Rectangle GetEffectivePageBounds(ChartAccessibilityModel chart)
        {
            if (chart == null || chart.PageBounds == Rectangle.Empty)
                return Rectangle.Empty;

            if (chart.RepresentativeAction != null)
            {
                Type actType = chart.RepresentativeAction.GetType();
                int xOffset = GetFieldOrPropertyValue<int>(chart.RepresentativeAction, actType, "XOffset");
                int yOffset = GetFieldOrPropertyValue<int>(chart.RepresentativeAction, actType, "YOffset");
                return new Rectangle(
                    chart.PageBounds.X + xOffset,
                    chart.PageBounds.Y + yOffset,
                    chart.PageBounds.Width,
                    chart.PageBounds.Height);
            }

            return chart.PageBounds;
        }

        private static IList ExtractActionList(object toolTipsObj)
        {
            if (toolTipsObj == null)
                return null;
            if (toolTipsObj is IList directList)
                return directList;
            FieldInfo actionListField = toolTipsObj.GetType().GetField("m_actionList", AllInstanceFlags);
            return actionListField != null ? actionListField.GetValue(toolTipsObj) as IList : null;
        }

        private static string GetUniqueNameFromRenderingItem(object renderingItem)
        {
            if (renderingItem == null)
                return null;

            object instanceProps = GetFieldOrPropertyValue<object>(renderingItem, renderingItem.GetType(), "InstanceProperties")
                ?? GetFieldOrPropertyValue<object>(renderingItem, renderingItem.GetType(), "m_instanceProperties");
            if (instanceProps == null)
                return null;

            return GetFieldOrPropertyValue<string>(instanceProps, instanceProps.GetType(), "UniqueName");
        }

        private static T GetFieldOrPropertyValue<T>(object target, Type type, string memberName)
        {
            FieldInfo fi = FindFieldInHierarchy(type, memberName);
            if (fi != null)
            {
                object val = fi.GetValue(target);
                if (val is T typed)
                    return typed;
            }
            return GetPropertyValue<T>(target, type, memberName);
        }

        private static Control FindChildControlByTypeName(Control parent, string typeName)
        {
            if (parent == null)
                return null;

            foreach (Control child in parent.Controls)
            {
                if (string.Equals(child.GetType().Name, typeName, StringComparison.Ordinal))
                    return child;

                Control nested = FindChildControlByTypeName(child, typeName);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo fi = type.GetField(fieldName, AllInstanceFlags);
                if (fi != null)
                    return fi;
                type = type.BaseType;
            }
            return null;
        }

        private static T GetPropertyValue<T>(object target, Type type, string propName)
        {
            while (type != null)
            {
                PropertyInfo pi = type.GetProperty(propName, AllInstanceFlags);
                if (pi != null)
                {
                    object val = pi.GetValue(target, null);
                    if (val is T typed)
                        return typed;
                    return default(T);
                }
                type = type.BaseType;
            }
            return default(T);
        }
    }
}
