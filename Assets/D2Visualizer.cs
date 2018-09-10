/*
D2Visualizer.cs is part of the VLAB project.
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
using UnityEngine;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq;
using System;
using System.IO;
using VLab;
using MsgPack;
using OxyPlot;
using OxyPlot.WindowsForms;
using OxyPlot.Series;
using OxyPlot.Axes;
using MathNet.Numerics.Statistics;
using MathNet.Numerics.Interpolation;

namespace VLabAnalysis
{
    public class D2Visualizer : Form, IVisualizer
    {
        D2VisualizeControl plotview = new D2VisualizeControl();

        public D2Visualizer(int width = 300, int height = 280)
        {
            Width = width;
            Height = height;

            Controls.Add(plotview);
        }

        public void Reset()
        {
            plotview.Reset();
        }

        public void ShowInFront()
        {
            if (Visible)
            {
                WindowState = FormWindowState.Minimized;
                Show();
                WindowState = FormWindowState.Normal;
            }
        }

        public Vector2 Position
        {
            get { return new Vector2(Location.X, Location.Y); }

            set
            {
                if (StartPosition != FormStartPosition.Manual)
                {
                    StartPosition = FormStartPosition.Manual;
                }
                Location = new System.Drawing.Point(Convert.ToInt32(value.x), Convert.ToInt32(value.y));
            }
        }

        public void Visualize(IResult result)
        {
            if (!Visible)
            {
                Show();
            }
            plotview.Visualize(result);
        }

        public void Save(string path, int width, int height, int dpi)
        {
            plotview.Save(path, width, height, dpi);
        }
    }

    public class D2VisualizeControl : PlotView
    {
        PlotModel pm;
        bool isprepared, isupdated;
        ContextMenuStrip cms = new ContextMenuStrip();
        IResult result;

        Dictionary<string, string> factorunit = new Dictionary<string, string>();
        Dictionary<string, Type> factorvaluetype = new Dictionary<string, Type>();
        Dictionary<string, Type> factorvalueelementtype = new Dictionary<string, Type>();
        Dictionary<string, int> factorvaluendim = new Dictionary<string, int>();
        Dictionary<string, int[]> factorvaluevdimidx = new Dictionary<string, int[]>();
        Dictionary<string, string[]> factorvaluevdimunit = new Dictionary<string, string[]>();

        string x1factor, x2factor, ytitle;
        int x1dimidx, x2dimidx;

        public D2VisualizeControl()
        {
            pm = new PlotModel();
            pm.LegendPosition = LegendPosition.TopRight;
            pm.LegendPlacement = LegendPlacement.Inside;
            Model = pm;
            ContextMenuStrip = cms;
            Dock = DockStyle.Fill;
        }

        public void Reset()
        {
            Visible = false;
            isprepared = false;
            isupdated = false;

            x1factor = null;
            x2factor = null;
            ytitle = null;
            x1dimidx = 0;
            x2dimidx = 0;

            factorvaluetype.Clear();
            factorvalueelementtype.Clear();
            factorvaluendim.Clear();
            factorvaluevdimidx.Clear();
            factorvaluevdimunit.Clear();
            factorunit.Clear();
            cms.Items.Clear();
            result = null;
        }

        void NewContextMenuStrip()
        {
            var ff = new ToolStripMenuItem("FirstFactor");
            var sf = new ToolStripMenuItem("SecondFactor");
            var save = new ToolStripMenuItem("Save");
            sf.DropDownItems.Add("None");
            foreach (var f in factorunit.Keys.ToArray())
            {
                var ffi = new ToolStripMenuItem(f);
                var sfi = new ToolStripMenuItem(f);
                if (factorvalueelementtype[f] != null)
                {
                    for (var i = 0; i < factorvaluevdimidx[f].Length; i++)
                    {
                        var ffid = new ToolStripMenuItem(factorvaluevdimunit[f][i]);
                        ffid.Tag = factorvaluevdimidx[f][i];
                        ffi.DropDownItems.Add(ffid);

                        var sfid = new ToolStripMenuItem(factorvaluevdimunit[f][i]);
                        sfid.Tag = factorvaluevdimidx[f][i];
                        sfi.DropDownItems.Add(sfid);
                    }
                    ffi.DropDownItemClicked += firstfactoritem_DropDownItemClicked;
                    sfi.DropDownItemClicked += secondfactoritem_DropDownItemClicked;
                }
                ff.DropDownItems.Add(ffi);
                sf.DropDownItems.Add(sfi);
            }
            ff.DropDownItemClicked += firstfactor_DropDownItemClicked;
            sf.DropDownItemClicked += secondfactor_DropDownItemClicked;

            cms.Items.Add(ff);
            cms.Items.Add(sf);
            cms.Items.Add(save);
        }

        void firstfactoritem_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var ffi = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            factorchange((ToolStripMenuItem)ffi.OwnerItem, ffi, 1, false, false);
            factoritemchange(ffi, item, 1, true);
        }

        void secondfactoritem_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var sfi = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            factorchange((ToolStripMenuItem)sfi.OwnerItem, sfi, 2, true, false);
            factoritemchange(sfi, item, 2, true);
        }

        void factoritemchange(ToolStripMenuItem factoritem, ToolStripMenuItem factordimitem, int whichfactor = 1, bool isvisualize = false)
        {
            var vdimidx = (int)factordimitem.Tag;
            for (var i = 0; i < factoritem.DropDownItems.Count; i++)
            {
                ((ToolStripMenuItem)factoritem.DropDownItems[i]).Checked = false;
            }
            factordimitem.Checked = true;
            if (whichfactor == 1)
            {
                x1dimidx = vdimidx;
            }
            else
            {
                x2dimidx = vdimidx;
            }
            if (isvisualize) Visualize(result);
        }

        void firstfactor_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var ff = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            factorchange(ff, item, 1, false, true);
        }

        void secondfactor_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var sf = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            factorchange(sf, item, 2, true, true);
        }

        void factorchange(ToolStripMenuItem factor, ToolStripMenuItem factoritem, int whichfactor = 1, bool containsnull = false, bool isvisualize = false)
        {
            var f = containsnull ? factoritem.Text == "None" ? null : factoritem.Text : factoritem.Text;
            for (var i = 0; i < factor.DropDownItems.Count; i++)
            {
                ((ToolStripMenuItem)factor.DropDownItems[i]).Checked = false;
            }
            factoritem.Checked = true;
            if (whichfactor == 1)
            {
                x1factor = f;
                x1dimidx = 0;
            }
            else
            {
                x2factor = f;
                x2dimidx = 0;
            }
            isupdated = false;
            if (isvisualize) Visualize(result);
        }

        public void Visualize(IResult result)
        {
            if (result == null) return;
            this.result = result;
            var ctc = result.DataSet.CondTestCond;
            if (!isprepared)
            {
                foreach (var f in ctc.Keys.ToArray())
                {
                    if (ctc[f].Count > 0)
                    {
                        string funit; int valuendim; int[] valuevdimidx; string[] valuevdimunit; Type valuetype; Type valueelementtype;
                        f.GetFactorInfo(ctc[f].First(), result.ExperimentID, out funit, out valuetype, out valueelementtype, out valuendim, out valuevdimidx, out valuevdimunit);
                        factorunit[f] = funit; factorvaluetype[f] = valuetype; factorvalueelementtype[f] = valueelementtype; factorvaluendim[f] = valuendim; factorvaluevdimidx[f] = valuevdimidx; factorvaluevdimunit[f] = valuevdimunit;
                    }
                }
                NewContextMenuStrip();
                var dfs = factorunit.Keys.Where(i => !i.EndsWith("_Final")).ToArray();
                switch (dfs.Length)
                {
                    case 0:
                        break;
                    case 1:
                        var ff = dfs.First().GetFinalFactor();
                        switch (factorvaluevdimidx[ff].Length)
                        {
                            case 0:
                                break;
                            case 1:
                                x1factor = ff;
                                x1dimidx = factorvaluevdimidx[x1factor][0];
                                break;
                            default:
                                x1factor = ff;
                                x2factor = ff;
                                x1dimidx = factorvaluevdimidx[x1factor][0];
                                x2dimidx = factorvaluevdimidx[x2factor][1];
                                break;
                        }
                        break;
                    default:
                        x1factor = dfs.First().GetFinalFactor();
                        x2factor = dfs.ElementAt(1).GetFinalFactor();
                        x1dimidx = factorvaluevdimidx[x1factor][0];
                        x2dimidx = factorvaluevdimidx[x2factor][0];
                        break;
                }
                ytitle = result.GetResultTitle();
                Parent.Text = "Channel_" + result.SignalChannel;
                pm.Title = "Ch" + result.SignalChannel + "_" + result.ExperimentID;
                isprepared = true;
            }
            if (string.IsNullOrEmpty(x1factor)) return;

            var alluuid = result.UnitCondTestResponse.Keys.ToList();
            alluuid.Sort();
            var nct = ctc.Values.First().Count;
            // XY plot
            if (!string.IsNullOrEmpty(x1factor) && string.IsNullOrEmpty(x2factor))
            {
                Type x1type; string[] dimunits;
                var fvs = ctc[x1factor].GetFactorValues(factorvaluetype[x1factor], factorvalueelementtype[x1factor],
                    factorvaluendim[x1factor], new[] { x1dimidx }, out x1type, out dimunits)[0];
                if (fvs == null || fvs.Count == 0) return;
                var x1 = fvs.Distinct().ToArray(); var x1dimunit = dimunits[0]; var n1 = x1.Length; Array.Sort(x1);
                var y = new Dictionary<int, double[]>(); var yse = new Dictionary<int, double[]>();

                for (var xi1 = 0; xi1 < n1; xi1++)
                {
                    var cis = Enumerable.Range(0, nct).Where(i => fvs[i].Equals(x1[xi1])).ToList();
                    foreach (var u in alluuid)
                    {
                        if (!y.ContainsKey(u))
                        {
                            y[u] = new double[n1];
                            yse[u] = new double[n1];
                        }
                        var flur = new List<double>();
                        foreach (var ci in cis)
                        {
                            flur.Add(result.UnitCondTestResponse[u][ci]);
                        }
                        y[u][xi1] = flur.Mean(); yse[u][xi1] = flur.SEM();
                    }
                }
                if (x1type.IsNumeric() && y.Count > 0)
                {
                    if (!Visible)
                    {
                        Visible = true;
                    }
                    D1Visualize(x1.Select(i => i.Convert<double>()).ToArray(), y, yse, x1factor.JoinFactorTitle(x1dimunit, factorunit[x1factor]), ytitle);
                }
            }
            // XYZ plot
            if (!string.IsNullOrEmpty(x1factor) && !string.IsNullOrEmpty(x2factor))
            {
                Type x1type, x2type; string[] dimunits1, dimunits2;
                var fvs1 = ctc[x1factor].GetFactorValues(factorvaluetype[x1factor], factorvalueelementtype[x1factor],
                    factorvaluendim[x1factor], new[] { x1dimidx }, out x1type, out dimunits1)[0];
                var fvs2 = ctc[x2factor].GetFactorValues(factorvaluetype[x2factor], factorvalueelementtype[x2factor],
                    factorvaluendim[x2factor], new[] { x2dimidx }, out x2type, out dimunits2)[0];
                if (fvs1 == null || fvs2 == null || fvs1.Count == 0 || fvs2.Count == 0) return;
                var x1 = fvs1.Distinct().ToArray(); var x1dimunit = dimunits1[0]; var n1 = x1.Length; Array.Sort(x1);
                var x2 = fvs2.Distinct().ToArray(); var x2dimunit = dimunits2[0]; var n2 = x2.Length; Array.Sort(x2);
                var y = new Dictionary<int, double[,]>(); var yse = new Dictionary<int, double[,]>();

                for (var xi1 = 0; xi1 < n1; xi1++)
                {
                    for (var xi2 = 0; xi2 < n2; xi2++)
                    {
                        var cis = Enumerable.Range(0, nct).Where(i => fvs1[i].Equals(x1[xi1]) && fvs2[i].Equals(x2[xi2])).ToList();
                        foreach (var u in alluuid)
                        {
                            if (!y.ContainsKey(u))
                            {
                                y[u] = new double[n1, n2];
                                yse[u] = new double[n1, n2];
                            }
                            var flur = new List<double>();
                            foreach (var ci in cis)
                            {
                                flur.Add(result.UnitCondTestResponse[u][ci]);
                            }
                            y[u][xi1, xi2] = flur.Mean(); yse[u][xi1, xi2] = flur.SEM();
                        }
                    }
                }
                if (x1type.IsNumeric() && x2type.IsNumeric() && y.Count > 0)
                {
                    if (!Visible)
                    {
                        Visible = true;
                    }
                    D2Visualize(x1.Select(i => i.Convert<double>()).ToArray(), x2.Select(i => i.Convert<double>()).ToArray(), y, yse,
                        x1factor.JoinFactorTitle(x1dimunit, factorunit[x1factor]), x2factor.JoinFactorTitle(x2dimunit, factorunit[x2factor]), ytitle);
                }
            }
        }

        void D1Visualize(double[] x, Dictionary<int, double[]> y, Dictionary<int, double[]> yse, string xtitle, string ytitle)
        {
            pm.Series.Clear();
            if (pm.PlotType != PlotType.XY)
            { pm.PlotType = PlotType.XY; }
            var ylo = 0.0; var yhi = 0.0;
            foreach (var u in y.Keys.ToList())
            {
                var line = new LineSeries()
                {
                    Title = "U" + u,
                    StrokeThickness = 2,
                    Color = VLAExtention.Unit5Colors[u],
                    TrackerFormatString = "{0}\nX: {2:0.0}\nY: {4:0.0}"
                };
                var error = new ScatterErrorSeries()
                {
                    ErrorBarStopWidth = 2,
                    ErrorBarStrokeThickness = 1.5,
                    ErrorBarColor = OxyColor.FromAColor(180, VLAExtention.Unit5Colors[u]),
                    MarkerSize = 0,
                    TrackerFormatString = "{0}\nX: {2:0.0}\nY: {4:0.0}"
                };
                for (var i = 0; i < x.Length; i++)
                {
                    error.Points.Add(new ScatterErrorPoint(x[i], y[u][i], 0, yse[u][i]));
                    line.Points.Add(new DataPoint(x[i], y[u][i]));
                    yhi = Math.Max(yhi, y[u][i] + yse[u][i]);
                    ylo = Math.Min(ylo, y[u][i] - yse[u][i]);
                }
                pm.Series.Add(line);
                pm.Series.Add(error);
            }

            if (pm.DefaultXAxis != null)
            {
                var x0 = x.First(); var x1 = x.Last();
                pm.DefaultXAxis.Maximum = x1 + 0.01 * (x1 - x0);
                pm.DefaultXAxis.Minimum = x0 - 0.01 * (x1 - x0);
                pm.DefaultYAxis.Maximum = yhi;
                pm.DefaultYAxis.Minimum = Math.Min(0, ylo);
                pm.DefaultXAxis.Reset();
                pm.DefaultYAxis.Reset();
            }
            if (!isupdated)
            {
                if (pm.DefaultXAxis != null)
                {
                    pm.DefaultXAxis.MaximumPadding = 0.005;
                    pm.DefaultXAxis.MinimumPadding = 0.005;
                    pm.DefaultYAxis.MaximumPadding = 0.005;
                    pm.DefaultYAxis.MinimumPadding = 0.005;
                    pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Outside;
                    pm.DefaultXAxis.Title = xtitle;
                    pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Outside;
                    pm.DefaultYAxis.Title = ytitle;
                    isupdated = true;
                }
            }
            pm.InvalidatePlot(true);
        }

        void D2Visualize(double[] x1, double[] x2, Dictionary<int, double[,]> y, Dictionary<int, double[,]> yse, string x1title, string x2title, string ytitle)
        {
            pm.Series.Clear();
            if (pm.PlotType != PlotType.Cartesian)
            { pm.PlotType = PlotType.Cartesian; }
            var x1min = x1.Min(); var x1max = x1.Max(); var x2min = x2.Min(); var x2max = x2.Max();
            foreach (var u in y.Keys.ToList())
            {
                var ymin = y[u].Min2D(); var ymax = y[u].Max2D(); var yrange = Math.Max(1, ymax - ymin);
                var contour = new ContourSeries()
                {
                    Title = "U" + u,
                    Data = y[u],
                    ColumnCoordinates = x1,
                    RowCoordinates = x2,
                    LineStyle = LineStyle.Solid,
                    StrokeThickness = 2,
                    ContourLevels = new double[] { ymin + 0.6 * yrange, ymin + 0.7 * yrange, ymin + 0.8 * yrange },
                    ContourColors = new OxyColor[] { OxyColor.FromAColor(102,VLAExtention.Unit5Colors[u]),
                        OxyColor.FromAColor(153, VLAExtention.Unit5Colors[u]), OxyColor.FromAColor(204, VLAExtention.Unit5Colors[u]) },
                    LabelStep = 3,
                    TextColor = VLAExtention.Unit5Colors[u],
                    FontSize = 9,
                    LabelFormatString = "F0",
                    LabelBackground = OxyColors.Undefined,
                    TrackerFormatString = "{0}\nX: {2:0.0}\nY: {4:0.0}\nZ: {6:0.0}"
                };
                pm.Series.Add(contour);
            }

            if (pm.DefaultXAxis != null)
            {
                pm.DefaultXAxis.Maximum = x1max;
                pm.DefaultXAxis.Minimum = x1min;
                pm.DefaultXAxis.MajorStep = Math.Round((x1max - x1min) / 2, 1);
                pm.DefaultXAxis.MinorStep = pm.DefaultXAxis.MajorStep / 2;
                pm.DefaultYAxis.Maximum = x2max;
                pm.DefaultYAxis.Minimum = x2min;
                pm.DefaultYAxis.MajorStep = Math.Round((x2max - x2min) / 2, 1);
                pm.DefaultYAxis.MinorStep = pm.DefaultYAxis.MajorStep / 2;

                pm.DefaultXAxis.Reset();
                pm.DefaultYAxis.Reset();
            }
            if (!isupdated)
            {
                if (pm.DefaultXAxis != null)
                {
                    pm.DefaultXAxis.MaximumPadding = 0.0005;
                    pm.DefaultXAxis.MinimumPadding = 0.0005;
                    pm.DefaultYAxis.MaximumPadding = 0.0005;
                    pm.DefaultYAxis.MinimumPadding = 0.0005;
                    pm.DefaultXAxis.TickStyle = OxyPlot.Axes.TickStyle.Outside;
                    pm.DefaultXAxis.Title = x1title;
                    pm.DefaultYAxis.TickStyle = OxyPlot.Axes.TickStyle.Outside;
                    pm.DefaultYAxis.Title = x2title;
                    isupdated = true;
                }
            }
            pm.InvalidatePlot(true);
        }

        public void Save(string path, int width, int height, int dpi)
        {
            if (isprepared)
            {
                using (var stream = File.Create(path + ".png"))
                {
                    var pngexporter = new OxyPlot.WindowsForms.PngExporter
                    {
                        Width = width,
                        Height = height,
                        Resolution = dpi
                    };
                    pngexporter.Export(pm, stream);
                }
                using (var stream = File.Create(path + ".svg"))
                {
                    var svgexporter = new OxyPlot.WindowsForms.SvgExporter
                    {
                        Width = width,
                        Height = height,
                        IsDocument = true
                    };
                    svgexporter.Export(pm, stream);
                }
            }
        }
    }
}