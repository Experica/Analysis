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
                    var lx = x.Select(i => VLConvert.Convert<double>(result.cond[cname][(int)i])).ToList();
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