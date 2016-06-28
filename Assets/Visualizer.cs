using System.Drawing;
using System.Collections;
using System.Windows.Forms;
using System.Linq;
using System;
using ZedGraph;

namespace VLabAnalysis
{
    public interface IVisualizer
    {
        void Visualize(object result);
    }

    public class lineVisualizer :Form, IVisualizer
    {
        ZedGraphControl control = new ZedGraphControl();

        public lineVisualizer(int width=400,int height=300)
        {
            control.IsAntiAlias = true;
            control.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            control.Dock = DockStyle.Fill;

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
            var condidx = result.mfr.Keys.Select(i => (double)i).ToArray();
            var condrep = result.mfr.Values.Select(i => (double)i.Count).ToArray();
            control.GraphPane.CurveList.Clear();
                control.GraphPane.AddCurve("Condition Test Count", condidx, condrep, Color.Red);

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