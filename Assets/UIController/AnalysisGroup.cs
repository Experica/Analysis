using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections;

namespace VLabAnalysis
{
    public class AnalysisGroup : MonoBehaviour
    {
        public Dropdown analyzer, visualizer, controller;
        public ElecPanel elecpanel;

        public void AddAnalysis(string a,string v, string c)
        {
            if(analyzer.options.Count==0)
            {
                var azers = AnalysisFactory.FindAll(ANALYSISINTERFACE.IAnalyzer);
                analyzer.AddOptions(azers.Select(i => i.Name).ToList());
            }
            if (visualizer.options.Count == 0)
            {
                var vzers = AnalysisFactory.FindAll( ANALYSISINTERFACE.IVisualizer);
                visualizer.AddOptions(vzers.Select(i => i.Name).ToList());
            }
            if (controller.options.Count == 0)
            {
                var czers = AnalysisFactory.FindAll( ANALYSISINTERFACE.IController);
                controller.AddOptions(czers.Select(i => i.Name).ToList());
            }
            analyzer.value = analyzer.options.Select(i=>i.text).ToList().IndexOf(a);
            visualizer.value = visualizer.options.Select(i => i.text).ToList().IndexOf(v);
            controller.value = controller.options.Select(i => i.text).ToList().IndexOf(c);
        }

    }
}