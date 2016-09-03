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
using MathNet.Numerics.LinearAlgebra;

namespace VLabAnalysis
{
    public interface IVisualizer
    {
        void Visualize(IResult result);
        void Reset();
        void Save(string path, int width, int height, float dpi);
    }

    public class D2Visualizer : Form, IVisualizer
    {
        PlotView control = new PlotView();
        PlotModel pm = new PlotModel();
        bool isawake,isstart;
        Dictionary<string,List<bool>> condvaluedim = new Dictionary<string, List<bool>>();
        Dictionary<string, string> condunit = new Dictionary<string, string>();

        public D2Visualizer(int width = 400, int height = 300)
        {
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            control.Dock = DockStyle.Fill;
            Reset();

            var ca = new LinearColorAxis();
            ca.Palette = OxyPalettes.Jet(256);
            pm.Axes.Add(ca);
            control.Model = pm;
            Controls.Add(control);

            Width = width;
            Height = height;
        }

        ~D2Visualizer()
        {
            Close();
        }

        public void Reset()
        {
            control.Visible = false;
            isawake = false;
            isstart = false;
            condvaluedim.Clear();
            condunit.Clear();
        }

        void Awake(IResult result)
        {
            foreach(var f in result.Cond.Keys)
            {
                List<bool> valuedim;
                condunit[f] = f.GetFactorUnit(out valuedim, result.ExperimentID);
                condvaluedim[f] = valuedim;
            }
            
            Text = "Electrode_" + result.ElectrodID;
            pm.Title = "E" + result.ElectrodID + "_" + result.ExperimentID;
            control.Visible = true;
            isawake = true;
        }

        public void Visualize(IResult result)
        {
            if (!isawake) Awake(result);

            var fn = result.Cond.Count;
            if (fn==1)
            {
                var fl = result.Cond.ElementAt(0);
                var valuedim = condvaluedim[fl.Key];
                var valuedimidx= Enumerable.Range(0, valuedim.Count).Where(i => valuedim[i] == true).ToList();
                var vdn = valuedimidx.Count;
                if(vdn==1)
                {
                    var x= result.CondResponse.Keys.Select(i => fl.Value[i]).GetFactorLevel<double>(valuedim)[valuedimidx[0]];
                    var y = result.CondResponse.Values.Select(i => i.Mean()).ToArray();
                    var yse = result.CondResponse.Values.Select(i => i.SEM()).ToArray();

                    D1Visualize(x,y,yse,condunit[fl.Key],result.Type.GetResponseUnit());
                }
                else if(vdn==2)
                {
                    var x = result.CondResponse.Keys.Select(i => fl.Value[i]).GetFactorLevel<double>(valuedim)[valuedimidx[0]];
                    var y = result.CondResponse.Keys.Select(i => fl.Value[i]).GetFactorLevel<double>(valuedim)[valuedimidx[1]];
                    var z = result.CondResponse.Values.Select(i => i.Mean()).ToArray();
                    var zse = result.CondResponse.Values.Select(i => i.SEM()).ToArray();

                    var ux = x.Distinct().ToList(); var uy = y.Distinct().ToList(); ux.Sort(); uy.Sort();
                    var xn = ux.Count(); var yn = uy.Count();
                    var heatdata = new double[xn, yn];
                    for (var i = 0; i < x.Length; i++)
                    {
                        heatdata[ux.IndexOf(x[i]), uy.IndexOf(y[i])] = z[i];
                    }

                    D2Visualize(heatdata,ux,uy,condunit[fl.Key],condunit[fl.Key]);
                }
                else
                {

                }
            }
            else if(fn==2)
            {

            }
            else
            {

            }

            if (!Visible)
            {
                Show();
            }
            else
            {
                control.Refresh();
            }
        }

        void D1Start(string xtitle,string ytitle)
        {
            if (pm.DefaultXAxis != null)
            {
                pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                pm.DefaultXAxis.Title = xtitle;
                pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                pm.DefaultYAxis.Title = ytitle;

                control.Visible = true;
                isstart = true;
            }
        }

        void D1Visualize(double[] x,double[] y, double[] yse, string xtitle,string ytitle)
        {
            var n = x.Length;
            var line = new LineSeries();
            line.StrokeThickness = 1.5;
            line.Color = OxyColor.FromArgb(255, 20, 100, 200);
            var error = new ScatterErrorSeries();
            error.ErrorBarStopWidth = 0;
            error.ErrorBarStrokeThickness = 2;
            error.ErrorBarColor = OxyColor.FromArgb(255, 20, 100, 150);

            var si = Enumerable.Range(0, n).ToArray();
            Array.Sort(x, si);
            double[] ylo = new double[n], yhi = new double[n];
            for (var i = 0; i < n; i++)
            {
                error.Points.Add(new ScatterErrorPoint(x[i], y[si[i]], 0, yse[si[i]]));
                line.Points.Add(new OxyPlot.DataPoint(x[i], y[si[i]]));
                ylo[i] = y[si[i]] - yse[si[i]];
                yhi[i] = y[si[i]] + yse[si[i]];
            }
            
            pm.Series.Clear();
            pm.Series.Add(line);
            pm.Series.Add(error);
            pm.InvalidatePlot(true);

            if (pm.DefaultYAxis != null)
            {
                var x0 = x.First();var x1 = x.Last();
                pm.DefaultXAxis.Maximum = x1 + 0.1 * (x1 - x0);
                pm.DefaultXAxis.Minimum = x0 - 0.1 * (x1 - x0);
                pm.DefaultYAxis.Maximum = yhi.Max();
                pm.DefaultYAxis.Minimum = ylo.Min();
            }

            if (!isstart) D1Start(xtitle,ytitle);
        }

        void D2Start(string xtitle, string ytitle)
        {
            if (pm.DefaultXAxis != null)
            {
                pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                pm.DefaultXAxis.Title = xtitle;
                pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Inside;
                pm.DefaultYAxis.Title = ytitle;

                control.Visible = true;
                isstart = true;
            }
        }

        void D2Visualize(double[,] heatdata,List<double> x, List<double> y,string xtitle,string ytitle)
        {
            var heat = new HeatMapSeries();
            heat.Data = heatdata;
            heat.X0 = x.Min();
            heat.X1 = x.Max();
            heat.Y0 = y.Min();
            heat.Y1 = y.Max();

            pm.Series.Clear();
            pm.Series.Add(heat);
            pm.InvalidatePlot(true);

            if (pm.DefaultYAxis != null)
            {
                pm.DefaultXAxis.Maximum = heat.X1;
                pm.DefaultXAxis.Minimum = heat.X0;
                pm.DefaultYAxis.Maximum = heat.Y1;
                pm.DefaultYAxis.Minimum = heat.Y0;
            }

            if (!isstart) D2Start(xtitle, ytitle);
        }

        public void Save(string path, int width, int height, float dpi)
        {
            if (isawake)
            {
                using (var stream = File.Create(path + ".png"))
                {

                    var pngexporter = new PngExporter();
                    pngexporter.Width = width;
                    pngexporter.Height = height;
                    pngexporter.Resolution = (int)dpi;
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