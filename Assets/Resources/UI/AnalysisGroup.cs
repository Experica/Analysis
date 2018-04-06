/*
AnalysisGroup.cs is part of the VLAB project.
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
using UnityEngine.UI;
using System.Linq;
using System.Collections;

namespace VLabAnalysis
{
    public class AnalysisGroup : MonoBehaviour
    {
        public Dropdown analyzer, visualizer, controller;
        public ElectrodePanel electrodepanel;

        public void UpdateUI( string a,string v, string c)
        {
            if(analyzer.options.Count==0)
            {
                var ia = VLAExtention.FindAll(AnalysisInterface.IAnalyzer);
                analyzer.AddOptions(ia.Select(i => i.Name).ToList());
            }
            if (visualizer.options.Count == 0)
            {
                var iv = VLAExtention.FindAll( AnalysisInterface.IVisualizer);
                visualizer.AddOptions(iv.Select(i => i.Name).ToList());
            }
            if (controller.options.Count == 0)
            {
                var ic = VLAExtention.FindAll( AnalysisInterface.IController);
                controller.AddOptions(ic.Select(i => i.Name).ToList());
            }
            analyzer.value = analyzer.options.Select(i=>i.text).ToList().IndexOf(a);
            visualizer.value = visualizer.options.Select(i => i.text).ToList().IndexOf(v);
            controller.value = controller.options.Select(i => i.text).ToList().IndexOf(c);
        }

    }
}