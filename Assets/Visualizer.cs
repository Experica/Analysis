// -----------------------------------------------------------------------------
// Visualizer.cs is part of the VLAB project.
// Copyright (c) 2016 Li Alex Zhang and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included 
// in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF 
// OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
// -----------------------------------------------------------------------------

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
        void Visualize(object result);
    }

    public class lineVisualizer :Form, IVisualizer
    {
        ZedGraphControl control = new ZedGraphControl();
        double[] x;

        public lineVisualizer(int width=400,int height=300)
        {
            control.IsAntiAlias = true;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            control.Dock = DockStyle.Fill;
            control.GraphPane.XAxis.Title.IsVisible = false;
            control.GraphPane.YAxis.Title.IsVisible = false;
            control.GraphPane.Title.IsVisible = false;

            Controls.Add(control);

            Width = width;
            Height = height;
        }

        ~lineVisualizer()
        {
            Close();
        }

        public void Visualize(object oresult)
        {
            var result = oresult as AnalysisResult;
            if (x == null)
            {
                x = result.mfr.Keys.Select(i => (double)i).ToArray();
            }
            var condresp = result.mfr.Values.Select(i => i.Mean()).ToArray();
            var condste = result.mfr.Values.Select(i => i.SEM()).ToArray();
            var semhi = condresp.Select((r, index) => r + condste[index]).ToArray();
            var semlo = condresp.Select((r, index) => r - condste[index]).ToArray();

            if(!control.GraphPane.XAxis.Title.IsVisible)
            {
                if (result.cond.Count > 1)
                {
                    control.GraphPane.XAxis.Title.Text = "Condition";
                }
                else
                {
                    var cname = result.cond.Keys.First();
                    control.GraphPane.XAxis.Title.Text = cname + " (" +cname.GetUnit()+ ")";
                    var lx = x.Select(i =>result.cond[cname][(int)i].Convert<double>()).ToList();
                    lx.Sort();
                    if(lx.Count>2)
                    {
                        control.GraphPane.XAxis.Scale.MajorStep = lx[2] - lx[0];
                    }
                    x = lx.ToArray(); 
                }
                control.GraphPane.XAxis.Title.IsVisible = true;
            }
            if (!control.GraphPane.YAxis.Title.IsVisible)
            {
                control.GraphPane.YAxis.Title.Text = "Mean Firing Rate (spike/s)";
                control.GraphPane.YAxis.Title.IsVisible = true;
            }
            if(!control.GraphPane.Title.IsVisible)
            {
                control.GraphPane.Title.Text ="E"+ result.Elec+"_"+result.ExID;
                control.GraphPane.Title.IsVisible = true;
            }


            control.GraphPane.CurveList.Clear();
            control.GraphPane.AddCurve("", x, condresp, Color.Red);
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