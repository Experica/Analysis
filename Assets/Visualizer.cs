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
using System.Collections;
using System.Windows.Forms;
using System.Linq;
using System;
using ZedGraph;
using MathNet.Numerics.Statistics;
using VLab;
using MsgPack;

namespace VLabAnalysis
{
    public interface IVisualizer
    {
        void Visualize(IResult result);
        void Reset();
    }

    public class LineVisualizer :Form, IVisualizer
    {
        ZedGraphControl control = new ZedGraphControl();
        bool isinit;

        public LineVisualizer(int width=400,int height=300)
        {
            control.IsAntiAlias = true;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            control.Dock = DockStyle.Fill;
            Reset();

            Controls.Add(control);

            Width = width;
            Height = height;
        }

        ~LineVisualizer()
        {
            Close();
        }

        public void Reset()
        {
            control.GraphPane.XAxis.Title.IsVisible = false;
            control.GraphPane.YAxis.Title.IsVisible = false;
            control.GraphPane.Title.IsVisible = false;
            control.GraphPane.CurveList.Clear();
            control.AxisChange();
            isinit = false;
        }

        void Init(IResult result)
        {
            string xtitle;
            if (result.Cond.Count > 1)
            {
                xtitle = "Condition Index";
            }
            else
            {
                var cname = result.Cond.Keys.First();
                xtitle = cname + " (" + cname.GetUnit() + ")";
            }
            control.GraphPane.XAxis.Title.Text = xtitle;
            control.GraphPane.XAxis.Title.IsVisible = true;
            control.GraphPane.YAxis.Title.Text = result.Type.GetResponseAndUnit();
            control.GraphPane.YAxis.Title.IsVisible = true;
            Text = "Electrod_" + result.ElectrodID;
            control.GraphPane.Title.Text = "E" + result.ElectrodID + "_" + result.ExperimentID;
            control.GraphPane.Title.IsVisible = true;

            isinit = true;
        }

        public void Visualize(IResult result)
        {
            if (!isinit) Init(result);

            double[] x;
            if (result.Cond.Count>1)
            {
                x = result.CondResponse.Keys.Select(i => (double)i).ToArray();
            }
            else
            {
                var lx = result.CondResponse.Keys.Select(i => result.Cond.Values.First()[i].Convert<double>()).ToList();
                lx.Sort();
                if (lx.Count > 2)
                {
                    control.GraphPane.XAxis.Scale.MajorStep = lx[2] - lx[0];
                }
                x = lx.ToArray();
            }
            var condmean = result.CondResponse.Values.Select(i => i.Mean()).ToArray();
            var condsem = result.CondResponse.Values.Select(i => i.SEM()).ToArray();
            var semhi = condmean.Select((r, index) => r + condsem[index]).ToArray();
            var semlo = condmean.Select((r, index) => r - condsem[index]).ToArray();

            control.GraphPane.CurveList.Clear();
            control.GraphPane.AddCurve("", x, condmean, Color.DarkBlue, SymbolType.Circle);
            control.GraphPane.AddErrorBar("", x, semhi, semlo, Color.Black);
            control.AxisChange();

            if (!Visible)
            {
                Show();
            }
            else
            {
                control.Refresh();
            }
        }

    }
}