using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using sqlnexus;

namespace SqlNexus.UnitTests.sqlnexus
{
    [TestClass]
    public class ReportChartAccessibilityEnhancerTests
    {
        [DataTestMethod]
        [DataRow(@"sqlnexus\Reports\Perfmon_Memory_C.rdlC")]
        [DataRow(@"NexusReports\Perfmon_Memory_C.rdl")]
        public void PerfmonMemoryReport_AllChartsHaveOverviewAndDataPointToolTips(string relativePath)
        {
            string reportPath = Path.Combine(FindRepositoryRoot(), relativePath);
            XDocument doc = XDocument.Load(reportPath);
            XNamespace ns = doc.Root != null ? doc.Root.GetDefaultNamespace() : "http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition";

            List<XElement> charts = doc.Descendants(ns + "Chart").ToList();
            Assert.AreEqual(6, charts.Count, "Perfmon_Memory_C must contain all 6 memory charts.");

            foreach (XElement chart in charts)
            {
                string chartName = (string)chart.Attribute("Name");
                string chartToolTip = (string)chart.Element(ns + "ToolTip");

                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(chartToolTip),
                    $"Chart '{chartName}' in {relativePath} must define a chart-level <ToolTip> for screen reader overview.");
                StringAssert.Contains(chartToolTip, "CountRows() = 0");
                StringAssert.Contains(chartToolTip, ": No data available.");
                StringAssert.Contains(chartToolTip, "Line chart of");
                StringAssert.Contains(chartToolTip, "sampling interval");
                StringAssert.Contains(chartToolTip, "CountDistinct");
                StringAssert.Contains(chartToolTip, ". X-axis: Time. Y-axis: ");

                List<XElement> dataPointToolTips = chart
                    .Descendants(ns + "ChartDataPoint")
                    .Select(dp => dp.Element(ns + "ToolTip"))
                    .Where(tt => tt != null)
                    .ToList();
                Assert.IsTrue(dataPointToolTips.Count > 0, $"Chart '{chartName}' must define <ChartDataPoint><ToolTip>.");
                foreach (XElement dpTt in dataPointToolTips)
                {
                    StringAssert.Contains(dpTt.Value, ": at time ");
                    StringAssert.Contains(dpTt.Value, ", value is ");
                }
            }
        }

        [TestMethod]
        public void AllSqlNexusReports_EveryChartDefinesOverviewAndDataPointToolTips()
        {
            string repoRoot = FindRepositoryRoot();
            string csprojPath = Path.Combine(repoRoot, "sqlnexus", "sqlnexus.csproj");
            XDocument csprojDoc = XDocument.Load(csprojPath);
            XNamespace msbuildNs = csprojDoc.Root.GetDefaultNamespace();

            List<string> shippedReports = csprojDoc
                .Descendants(msbuildNs + "Content")
                .Select(el => (string)el.Attribute("Include"))
                .Where(inc => !string.IsNullOrEmpty(inc) &&
                              inc.StartsWith(@"Reports\", StringComparison.OrdinalIgnoreCase) &&
                              inc.IndexOf(".rdl", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            List<string> reportPathsToVerify = new List<string>();
            foreach (string rel in shippedReports)
            {
                reportPathsToVerify.Add(Path.Combine(repoRoot, "sqlnexus", rel));
                string designName = Path.GetFileNameWithoutExtension(rel) + ".rdl";
                string designPath = Path.Combine(repoRoot, "NexusReports", designName);
                if (File.Exists(designPath))
                    reportPathsToVerify.Add(designPath);
            }

            int totalChartsChecked = 0;

            foreach (string filePath in reportPathsToVerify)
            {
                XDocument doc = XDocument.Load(filePath);
                if (doc.Root == null)
                    continue;

                XNamespace ns = doc.Root.GetDefaultNamespace();
                List<XElement> charts = doc.Descendants(ns + "Chart").ToList();
                if (charts.Count == 0)
                    continue;

                foreach (XElement chart in charts)
                {
                    totalChartsChecked++;
                    string chartName = (string)chart.Attribute("Name");
                    string chartToolTip = (string)chart.Element(ns + "ToolTip");

                    Assert.IsFalse(
                        string.IsNullOrWhiteSpace(chartToolTip),
                        $"Chart '{chartName}' in '{Path.GetFileName(filePath)}' must define a chart-level <ToolTip>.");
                    StringAssert.Contains(chartToolTip, "CountRows() = 0");
                    StringAssert.Contains(chartToolTip, ": No data available.");
                    StringAssert.Contains(chartToolTip, ". X-axis: ");
                    StringAssert.Contains(chartToolTip, ". Y-axis: ");
                }
            }

            Assert.AreEqual(144, totalChartsChecked, "Expected 72 shipped charts + 72 matching NexusReports design charts.");
        }

        [TestMethod]
        public void TryParseCategoricalCaption_ValidAndInvalidInputs_BehavesAsExpected()
        {
            bool okWithUnit = ReportChartAccessibilityEnhancer.TryParseCategoricalCaption(
                "Query 1: value is 45200.5 us",
                out string cat1,
                out double val1,
                out string unit1);

            Assert.IsTrue(okWithUnit);
            Assert.AreEqual("Query 1", cat1);
            Assert.AreEqual(45200.5, val1, 0.001);
            Assert.AreEqual("us", unit1);

            bool okTimeSeriesRejected = ReportChartAccessibilityEnhancer.TryParseCategoricalCaption(
                "Available MBytes: at time 2026-11-15 14:44:35.000, value is 880 MB",
                out _,
                out _,
                out _);

            Assert.IsFalse(okTimeSeriesRejected);
        }

        [TestMethod]
        public void EvaluateHoverAnnouncement_CategoricalBarChart_AnnouncesOverviewAndBarValues()
        {
            var model = new ChartAccessibilityModel
            {
                ChartId = "12iT0",
                BaseToolTip = "Column chart of Top Wait Categories. X-axis: Wait Category. Y-axis: AVg Wait Time (ms) per sec.",
                OverviewDescription = "Column chart of Top Wait Categories. X-axis: Wait Category. Y-axis: AVg Wait Time (ms) per sec.",
                PageBounds = new Rectangle(50, 250, 600, 300)
            };
            model.SamplePoints.Add(new ChartSamplePoint
            {
                SeriesName = "CPU",
                Value = 125.4,
                IsCategorical = true,
                BarPageBounds = new Rectangle(100, 300, 40, 150),
                Caption = "CPU: value is 125.4."
            });

            string firstHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(10, 10),
                isFirstHoverOnChart: true);
            Assert.AreEqual(model.OverviewDescription, firstHover);

            string barHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(70, 100),
                isFirstHoverOnChart: false);
            Assert.AreEqual("CPU: value is 125.4.", barHover);
        }

        [TestMethod]
        public void TryParseSampleCaption_ValidCaptionWithAndWithoutUnit_ParsesAllFields()
        {
            bool okWithUnit = ReportChartAccessibilityEnhancer.TryParseSampleCaption(
                "Available MBytes: at time 2026-11-15 14:44:35.000, value is 880.5 MB",
                out string series1,
                out DateTime time1,
                out string rawTime1,
                out double val1,
                out string unit1);

            Assert.IsTrue(okWithUnit);
            Assert.AreEqual("Available MBytes", series1);
            Assert.AreEqual(new DateTime(2026, 11, 15, 14, 44, 35), time1);
            Assert.AreEqual("2026-11-15 14:44:35.000", rawTime1);
            Assert.AreEqual(880.5, val1, 0.001);
            Assert.AreEqual("MB", unit1);

            bool okWithoutUnit = ReportChartAccessibilityEnhancer.TryParseSampleCaption(
                "Lazy Writes/sec: at time 2026-11-15 14:44:55.000, value is 42",
                out string series2,
                out DateTime time2,
                out _,
                out double val2,
                out string unit2);

            Assert.IsTrue(okWithoutUnit);
            Assert.AreEqual("Lazy Writes/sec", series2);
            Assert.AreEqual(new DateTime(2026, 11, 15, 14, 44, 55), time2);
            Assert.AreEqual(42.0, val2, 0.001);
            Assert.AreEqual(string.Empty, unit2);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("Chart")]
        [DataRow("Available MBytes: invalid format")]
        [DataRow("Available MBytes: at time not-a-date, value is 880 MB")]
        [DataRow("Available MBytes: at time 2026-11-15 14:44:35, value is not-a-number")]
        public void TryParseSampleCaption_MalformedInputs_ReturnsFalse(string malformedCaption)
        {
            bool parsed = ReportChartAccessibilityEnhancer.TryParseSampleCaption(
                malformedCaption,
                out _,
                out _,
                out _,
                out _,
                out _);

            Assert.IsFalse(parsed);
        }

        [TestMethod]
        public void ExtractSeriesVertices_RealDundasChartPolygons_ExtractsExactPixelVertices()
        {
            var points = new List<ChartSamplePoint>
            {
                new ChartSamplePoint
                {
                    Timestamp = new DateTime(2026, 11, 15, 14, 43, 55),
                    PolygonPoints = new[] { new Point(299, 251), new Point(381, 275), new Point(381, 281), new Point(299, 256) }
                },
                new ChartSamplePoint
                {
                    Timestamp = new DateTime(2026, 11, 15, 14, 44, 15),
                    PolygonPoints = new[]
                    {
                        new Point(381, 275), new Point(463, 299), new Point(463, 305), new Point(381, 281),
                        new Point(464, 299), new Point(546, 355), new Point(543, 360), new Point(461, 304)
                    }
                },
                new ChartSamplePoint
                {
                    Timestamp = new DateTime(2026, 11, 15, 14, 44, 35),
                    PolygonPoints = new[]
                    {
                        new Point(546, 355), new Point(628, 412), new Point(625, 416), new Point(543, 360),
                        new Point(625, 411), new Point(707, 376), new Point(709, 381), new Point(627, 416)
                    }
                },
                new ChartSamplePoint
                {
                    Timestamp = new DateTime(2026, 11, 15, 14, 44, 55),
                    PolygonPoints = new[] { new Point(707, 376), new Point(789, 340), new Point(791, 345), new Point(709, 381) }
                }
            };

            ReportChartAccessibilityEnhancer.ExtractSeriesVertices(points);

            Assert.AreEqual(new Point(299, 254), points[0].VertexPixel);
            Assert.AreEqual(new Point(463, 302), points[1].VertexPixel);
            Assert.AreEqual(new Point(626, 414), points[2].VertexPixel);
            Assert.AreEqual(new Point(790, 342), points[3].VertexPixel);
        }

        [TestMethod]
        public void BuildChartOverviewDescription_ComputesSamplingIntervalAndInjectsIntoOverview()
        {
            var samples = new List<ChartSamplePoint>
            {
                new ChartSamplePoint { Timestamp = new DateTime(2026, 11, 15, 14, 43, 55) },
                new ChartSamplePoint { Timestamp = new DateTime(2026, 11, 15, 14, 44, 15) },
                new ChartSamplePoint { Timestamp = new DateTime(2026, 11, 15, 14, 44, 35) }
            };

            string overview = ReportChartAccessibilityEnhancer.BuildChartOverviewDescription(
                "Line chart of Available MBytes. X-axis: Time. Y-axis: Available MBytes.",
                samples);

            Assert.AreEqual(
                "Line chart of Available MBytes, sampling interval 20 seconds. X-axis: Time. Y-axis: Available MBytes.",
                overview);
        }

        [TestMethod]
        public void BuildChartOverviewDescription_WhenNoDataAvailable_AnnouncesNoDataAvailable()
        {
            string fromRdlNoData = ReportChartAccessibilityEnhancer.BuildChartOverviewDescription(
                "Top 5 Memory Clerks over time: No data available.",
                new List<ChartSamplePoint>());
            Assert.AreEqual("Top 5 Memory Clerks over time: No data available.", fromRdlNoData);

            string fromZeroIntervalFallback = ReportChartAccessibilityEnhancer.BuildChartOverviewDescription(
                "Line chart of CPU Utilization by Commands (Normalized), sampling interval 0 seconds. X-axis: Runtime. Y-axis: CPU % (Normalized by Core Count).",
                new List<ChartSamplePoint>());
            Assert.AreEqual("CPU Utilization by Commands (Normalized): No data available.", fromZeroIntervalFallback);

            string explicitHasNoData = ReportChartAccessibilityEnhancer.BuildChartOverviewDescription(
                "Bar chart of Top 5 Memory Consuming Queries. X-axis: Query ID (see table below). Y-axis: Max Memory Grant (MB).",
                new List<ChartSamplePoint>(),
                hasData: false);
            Assert.AreEqual("Top 5 Memory Consuming Queries: No data available.", explicitHasNoData);

            var emptyChartModel = new ChartAccessibilityModel
            {
                ChartId = "15iT0",
                BaseToolTip = "Top 5 Memory Clerks over time: No data available.",
                OverviewDescription = fromRdlNoData,
                PageBounds = new Rectangle(28, 158, 1166, 406)
            };
            Assert.AreEqual(
                "Top 5 Memory Clerks over time: No data available.",
                ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(emptyChartModel, new Point(100, 100), isFirstHoverOnChart: true));
            Assert.IsNull(
                ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(emptyChartModel, new Point(400, 250), isFirstHoverOnChart: false));
        }

        [TestMethod]
        public void EvaluateHoverAnnouncement_CoversFirstHoverSampledPointAndNonSampledInterpolation()
        {
            ChartAccessibilityModel model = CreateSampleAvailableMBytesChartModel();

            // 1. First hover on the chart -> announces Chart Overview
            string firstHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(100, 100),
                isFirstHoverOnChart: true);

            Assert.AreEqual(
                "Line chart of Available MBytes, sampling interval 20 seconds. X-axis: Time. Y-axis: Available MBytes.",
                firstHover);

            // 2. Hover on a sampled vertex -> announces exact sampled timestamp and value
            string sampledHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(626, 414),
                isFirstHoverOnChart: false);

            Assert.AreEqual(
                "Available MBytes: at time 2026-11-15 14:44:35.000, value is 880 MB",
                sampledHover);

            // 3. Hover on non-sampled midpoint along the line between s1 (462, 302) and s2 (626, 414) -> announces interpolated time and estimated value
            string nonSampledHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(544, 358),
                isFirstHoverOnChart: false);

            Assert.AreEqual(
                "Available MBytes: non-sampling time, the time is 2026-11-15 14:44:25, and estimated value is 1040 MB.",
                nonSampledHover);

            // 4. Hover on empty whitespace far from the line -> returns null
            string emptySpaceHover = ReportChartAccessibilityEnhancer.EvaluateHoverAnnouncement(
                model,
                new Point(544, 50),
                isFirstHoverOnChart: false);

            Assert.IsNull(emptySpaceHover);
        }

        [TestMethod]
        public void ReportViewerReflectionContract_RequiredInternalMembersExist()
        {
            string repoRoot = FindRepositoryRoot();
            string rvDllPath = Directory.GetFiles(
                Path.Combine(repoRoot, "packages"),
                "Microsoft.ReportViewer.WinForms.dll",
                SearchOption.AllDirectories).FirstOrDefault();
            Assert.IsFalse(string.IsNullOrEmpty(rvDllPath), "Microsoft.ReportViewer.WinForms.dll must exist under packages.");

            var rvAssembly = System.Reflection.Assembly.LoadFrom(rvDllPath);
            const System.Reflection.BindingFlags allFlags =
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            Type reportPanelType = rvAssembly.GetType("Microsoft.Reporting.WinForms.ReportPanel");
            Assert.IsNotNull(reportPanelType, "ReportViewer assembly must contain internal type ReportPanel.");
            Assert.IsNotNull(reportPanelType.GetField("m_renderPanel", allFlags), "ReportPanel must expose m_renderPanel.");
            Assert.IsNotNull(reportPanelType.GetProperty("CurrentPage", allFlags), "ReportPanel must expose CurrentPage.");

            Type dynamicImageType = rvAssembly.GetType("Microsoft.Reporting.WinForms.RenderingDynamicImage");
            Assert.IsNotNull(dynamicImageType, "ReportViewer assembly must contain RenderingDynamicImage.");

            Type elemBase = dynamicImageType;
            System.Reflection.FieldInfo accNameField = null;
            while (elemBase != null && accNameField == null)
            {
                accNameField = elemBase.GetField("m_accessibleName", allFlags);
                elemBase = elemBase.BaseType;
            }
            Assert.IsNotNull(accNameField, "RenderingDynamicImage hierarchy must expose m_accessibleName.");

            Type actionType = rvAssembly.GetType("Microsoft.Reporting.WinForms.Action");
            Assert.IsNotNull(actionType, "ReportViewer assembly must contain Action.");
            Assert.IsNotNull(actionType.GetProperty("Position", allFlags) ?? (object)actionType.GetField("Position", allFlags), "Action must expose Position.");
            Assert.IsNotNull(actionType.GetProperty("Points", allFlags) ?? (object)actionType.GetField("Points", allFlags), "Action must expose Points.");
            Assert.IsNotNull(actionType.GetProperty("Id", allFlags) ?? (object)actionType.GetField("Id", allFlags), "Action must expose Id.");

            Type reportToolTipType = rvAssembly.GetType("Microsoft.Reporting.WinForms.ReportToolTip");
            Assert.IsNotNull(reportToolTipType, "ReportViewer assembly must contain ReportToolTip.");
            Assert.IsNotNull(reportToolTipType.GetProperty("Caption", allFlags) ?? (object)reportToolTipType.GetField("Caption", allFlags), "ReportToolTip must expose Caption.");
        }

        private static ChartAccessibilityModel CreateSampleAvailableMBytesChartModel()
        {
            var s0 = new ChartSamplePoint
            {
                SeriesName = "Available MBytes",
                Timestamp = new DateTime(2026, 11, 15, 14, 43, 55),
                RawTimestampText = "2026-11-15 14:43:55.000",
                Value = 1320,
                Unit = "MB",
                VertexPixel = new Point(299, 254),
                Caption = "Available MBytes: at time 2026-11-15 14:43:55.000, value is 1320 MB"
            };
            var s1 = new ChartSamplePoint
            {
                SeriesName = "Available MBytes",
                Timestamp = new DateTime(2026, 11, 15, 14, 44, 15),
                RawTimestampText = "2026-11-15 14:44:15.000",
                Value = 1200,
                Unit = "MB",
                VertexPixel = new Point(462, 302),
                Caption = "Available MBytes: at time 2026-11-15 14:44:15.000, value is 1200 MB"
            };
            var s2 = new ChartSamplePoint
            {
                SeriesName = "Available MBytes",
                Timestamp = new DateTime(2026, 11, 15, 14, 44, 35),
                RawTimestampText = "2026-11-15 14:44:35.000",
                Value = 880,
                Unit = "MB",
                VertexPixel = new Point(626, 414),
                Caption = "Available MBytes: at time 2026-11-15 14:44:35.000, value is 880 MB"
            };

            var model = new ChartAccessibilityModel
            {
                ChartId = "25iT0",
                BaseToolTip = "Line chart of Available MBytes. X-axis: Time. Y-axis: Available MBytes.",
                PageBounds = new Rectangle(40, 1285, 2248, 566)
            };
            model.SamplePoints.AddRange(new[] { s0, s1, s2 });
            model.SeriesByName["Available MBytes"] = new List<ChartSamplePoint> { s0, s1, s2 };
            model.OverviewDescription = ReportChartAccessibilityEnhancer.BuildChartOverviewDescription(
                model.BaseToolTip,
                model.SamplePoints);

            return model;
        }

        private static string FindRepositoryRoot()
        {
            foreach (string startDir in new[]
            {
                Path.GetDirectoryName(typeof(ReportChartAccessibilityEnhancerTests).Assembly.Location),
                AppDomain.CurrentDomain.BaseDirectory,
                Directory.GetCurrentDirectory()
            })
            {
                if (string.IsNullOrEmpty(startDir))
                    continue;
                var directory = new DirectoryInfo(startDir);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "sqlnexus.sln")))
                {
                    directory = directory.Parent;
                }
                if (directory != null)
                {
                    return directory.FullName;
                }
            }

            throw new DirectoryNotFoundException("Could not locate the SqlNexus repository root.");
        }
    }
}
