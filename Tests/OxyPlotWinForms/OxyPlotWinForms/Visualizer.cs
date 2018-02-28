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
    class Visualizer : PlotView
    {
        PlotModel pm = new PlotModel();
        ContextMenuStrip cms = new ContextMenuStrip();
        List<string> factors = new List<string> { "Ori", "Position", "Ori_Final", "Position_Final" };
        string x1f, x2f;

        public Visualizer()
        {
            Model = pm;
            pm.PlotType = PlotType.XY;
            Dock = DockStyle.Fill;

            NewContextMenuStrip();
            ContextMenuStrip = cms;
        }

        public void NewContextMenuStrip()
        {
            var ff = new ToolStripMenuItem("FirstFactor");
            var sf = new ToolStripMenuItem("SecondFactor");
            var save = new ToolStripMenuItem("Save");

            foreach (var f in factors)
            {
                ff.DropDownItems.Add(f);
                sf.DropDownItems.Add(f);
            }
            ff.DropDownItemClicked += firstfactor_DropDownItemClicked;
            sf.DropDownItemClicked += secondfactor_DropDownItemClicked;

            cms.Items.Add(ff);
            cms.Items.Add(sf);
            cms.Items.Add(save);
        }

        private void secondfactor_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var s = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            if (item.Text != x2f)
            {
                for (var i = 0; i < s.DropDownItems.Count; i++)
                {
                    ((ToolStripMenuItem)s.DropDownItems[i]).Checked = false;
                }
                item.Checked = true;
                x2f = item.Text;
            }
        }

        void firstfactor_DropDownItemClicked(object sender, ToolStripItemClickedEventArgs e)
        {
            var s = sender as ToolStripMenuItem; var item = e.ClickedItem as ToolStripMenuItem;
            if (item.Text != x1f)
            {
                for (var i = 0; i < s.DropDownItems.Count; i++)
                {
                    ((ToolStripMenuItem)s.DropDownItems[i]).Checked = false;
                }
                item.Checked = true;
                x1f = item.Text;
            }
        }

        public void Visualize()
        {
            pm.Series.Add(new FunctionSeries(Math.Cos, 0, 10, 0.1, "cos(x)"));
        }
    }
}
