using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OxyPlot;
using OxyPlot.Series;
using OxyPlot.WindowsForms;
using System.Windows.Forms;

namespace OxyPlotWinForms
{
    class Visualizer:PlotView
    {
        PlotModel pm = new PlotModel();
        ContextMenuStrip cms = new ContextMenuStrip();

        public Visualizer()
        {
            Model = pm;
            pm.PlotType = PlotType.XY;
            Dock = DockStyle.Fill;

            cms.Items.Add("cms");

            ContextMenuStrip = cms;
        }

        public void Visualize()
        {
            pm.Series.Add(new FunctionSeries(Math.Cos, 0, 10, 0.1, "cos(x)"));
        }
    }
}
