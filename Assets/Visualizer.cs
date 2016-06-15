using System.Drawing;
using System.Collections;
using System.Windows.Forms;
using System;
using ZedGraph;

namespace VLabAnalysis
{
    public interface IVisualizer
    {
        void Visualize();
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

        public void Visualize()
        {
            control.GraphPane.AddCurve("line", new double[] { 0, 1, 2, 3, 4, 5 }, new double[] { 5, 10, 20, 15, 5, 2 }, Color.Red);

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