/*
Visualizer.cs is part of the VLAB project.
Copyright (c) 2016 Li Alex Zhang and Contributors

Permission is hereby granted, free of charge, to any person obtaining a 
copy of this software and associated documentation files (the "Software"),
to deal in the Software without restriction, including without limitation
the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the 
Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included 
in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/
using System.Drawing;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq;
using System;
using System.IO;
using MathNet.Numerics.Statistics;
using VLab;
using MsgPack;
using OxyPlot;
using OxyPlot.WindowsForms;
using OxyPlot.Series;
using OxyPlot.Axes;
using MathNet.Numerics.Interpolation;

namespace VLabAnalysis
{
    public interface IVisualizer
    {
        void Visualize(IVisualizeResult result);
        void Reset();
        void Save(string path, int width, int height, int dpi);
    }

    public class D2Visualizer : Form, IVisualizer
    {
        PlotView plot = new PlotView();
        PlotModel pm = new PlotModel();
        Dictionary<int, OxyColor> unitcolor = VLAExtention.GetUnitColors();

        bool isawake, isstart;

        public D2Visualizer(int width = 400, int height = 350)
        {
            plot.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            plot.Dock = DockStyle.Fill;
            Width = width;
            Height = height;

            //var ca = new LinearColorAxis()
            //{
            //    Palette = OxyPalettes.BlueWhiteRed(256),
            //    //Position = AxisPosition.Right,
            //    PositionTier = 1
            //};
            //pm.Axes.Add(ca);
            pm.LegendPosition = LegendPosition.RightTop;
            pm.LegendPlacement = LegendPlacement.Inside;
            
            plot.Model = pm;
            Controls.Add(plot);
        }

        ~D2Visualizer()
        {
            Close();
        }

        public void Reset()
        {
            plot.Visible = false;
            isawake = false;
            isstart = false;
        }

        public void Visualize(IVisualizeResult result)
        {
            if (!isawake)
            {
                if (result.ExperimentID.Contains("RF"))
                {
                    if (result.ExperimentID.Contains('X') || result.ExperimentID.Contains('Y'))
                    {
                        pm.PlotType = PlotType.XY;
                    }
                    else
                    {
                        pm.PlotType = PlotType.Cartesian;
                    }
                }
                else
                {
                    pm.PlotType = PlotType.XY;
                }
                Text = "Signal_" + result.SignalID;
                pm.Title = "S" + result.SignalID + "_" + result.ExperimentID;
                plot.Visible = true;
                isawake = true;
            }

            var xdimn = result.X.Count;
            if (xdimn == 1)
            {
                D1Visualize(result.X[0], result.Y, result.YSE, result.XUnit[0], result.YUnit);
            }
            else if (xdimn == 2)
            {
                D2Visualize(result.X[0], result.X[1], result.Y, result.YSE, result.XUnit[0], result.XUnit[1], result.YUnit);
            }
            else
            {

            }

            if (!Visible)
            {
                Show();
            }
        }

        void D1Visualize(double[] x, Dictionary<int, double[,]> y, Dictionary<int, double[,]> yse, string xtitle, string ytitle)
        {
            pm.Series.Clear();
            var ylo = new List<double>(); var yhi = new List<double>();
            var us = y.Keys.ToList();us. Sort();
            foreach (var u in us)
            {
                var line = new LineSeries();
                line.StrokeThickness = 2;
                line.Color = OxyColor.FromAColor(255, unitcolor[u]);
                line.Title = "U" + u;
                var error = new ScatterErrorSeries();
                error.ErrorBarStopWidth = 2;
                error.ErrorBarStrokeThickness = 1.5;
                error.ErrorBarColor = OxyColor.FromAColor(180, unitcolor[u]);
                error.MarkerSize = 0;

                for (var i = 0; i < x.Length; i++)
                {
                    error.Points.Add(new ScatterErrorPoint(x[i], y[u][0, i], 0, yse[u][0, i]));
                    line.Points.Add(new DataPoint(x[i], y[u][0, i]));
                    ylo.Add(y[u][0, i] - yse[u][0, i]);
                    yhi.Add(y[u][0, i] + yse[u][0, i]);
                }

                pm.Series.Add(line);
                pm.Series.Add(error);
            }

            if (pm.DefaultYAxis != null)
            {
                var x0 = x.First(); var x1 = x.Last();
                pm.DefaultXAxis.Maximum = x1+0.01*(x1-x0);
                pm.DefaultXAxis.Minimum = x0-0.01*(x1-x0);
                pm.DefaultYAxis.Maximum = yhi.Max();
                pm.DefaultYAxis.Minimum =Math.Min(0, ylo.Min());
                pm.DefaultXAxis.Reset();
                pm.DefaultYAxis.Reset();
            }

            if (!isstart)
            {
                if (pm.DefaultXAxis != null)
                {
                    pm.DefaultXAxis.MaximumPadding = 0.005;
                    pm.DefaultXAxis.MinimumPadding = 0.005;
                    pm.DefaultYAxis.MaximumPadding = 0.005;
                    pm.DefaultYAxis.MinimumPadding = 0.005;
                    pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                    pm.DefaultXAxis.Title = xtitle;
                    pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                    pm.DefaultYAxis.Title = ytitle;
                    isstart = true;
                }
            }

            pm.InvalidatePlot(true);
        }

        void D2Visualize(double[] x1, double[] x2, Dictionary<int, double[,]> y, Dictionary<int, double[,]> yse, string x1title, string x2title, string ytitle)
        {
            pm.Series.Clear();
            var x1min = x1.Min(); var x1max = x1.Max(); var x2min = x2.Min(); var x2max = x2.Max();
            var us = y.Keys.ToList(); us.Sort();
            foreach (var u in us)
            {
                var d = y[u];
                var heat = new HeatMapSeries();
                heat.Data = y[u];
                heat.Interpolate = true;
                heat.X0 = x1min;
                heat.X1 = x1max;
                heat.Y0 = x2min;
                heat.Y1 = x2max;

                var ymin = y[u].Min2D();var ymax = y[u].Max2D();var yrange = Math.Max(1, ymax - ymin);
                var contour = new ContourSeries()
                {
                    Data = y[u],
                    ContourColors = ArrayBuilder.CreateVector(0.4, 0.8, 0.2).Select(i => OxyColor.FromAColor(Convert.ToByte(i * 255), unitcolor[u])).ToArray(),
                    LineStyle = LineStyle.Solid,
                    ContourLevels = ArrayBuilder.CreateVector(ymin + 0.6 * yrange, ymin + 0.8 * yrange, 0.1 * yrange),
                    FontSize = 8,
                    LabelStep = 3,
                    TextColor = unitcolor[u],
                    Title = "U" + u,
                    LabelFormatString = "F0",
                    LabelBackground = OxyColors.Undefined,
                    StrokeThickness = 2,
                    ColumnCoordinates =  x1,
                    RowCoordinates = x2,
                };

                
                //pm.Series.Add(heat);
                pm.Series.Add(contour);
            }

            

            if (!isstart)
            {
                if (pm.DefaultXAxis != null)
                {
                    pm.DefaultXAxis.MaximumPadding = 0.005;
                    pm.DefaultXAxis.MinimumPadding = 0.005;
                    pm.DefaultYAxis.MaximumPadding = 0.005;
                    pm.DefaultYAxis.MinimumPadding = 0.005;
                    pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                    pm.DefaultXAxis.Title = x1title;
                    pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                    pm.DefaultYAxis.Title = x2title;
                    //pm.PlotAreaBackground = OxyColor.FromRgb(230, 230, 230);
                    isstart = true;
                }
            }
            if (pm.DefaultYAxis != null)
            {
                pm.DefaultXAxis.Maximum = x1max;
                pm.DefaultXAxis.Minimum = x1min;
                pm.DefaultYAxis.Maximum = x2max;
                pm.DefaultYAxis.Minimum = x2min;
                pm.DefaultXAxis.Reset();
                pm.DefaultYAxis.Reset();
            }
            pm.InvalidatePlot(true);
        }

        public void Save(string path, int width, int height, int dpi)
        {
            if (isawake)
            {
                using (var stream = File.Create(path + ".png"))
                {
                    var pngexporter = new PngExporter();
                    pngexporter.Width = width;
                    pngexporter.Height = height;
                    pngexporter.Resolution = dpi;
                    pngexporter.Export(pm, stream);
                }
                using (var stream = File.Create(path + ".svg"))
                {
                    var svgexporter = new OxyPlot.SvgExporter();
                    svgexporter.Width = width;
                    svgexporter.Height = height;
                    svgexporter.IsDocument = true;
                    svgexporter.Export(pm, stream);
                }
            }
        }
    }

}