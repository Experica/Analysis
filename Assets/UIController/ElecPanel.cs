// -----------------------------------------------------------------------------
// ElecPanel.cs is part of the VLAB project.
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

using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using System.Collections.Generic;

namespace VLabAnalysis
{
    public class ElecPanel : MonoBehaviour {

        public VLAUIController uicontroller;
        public Text electitle;
        public Dropdown sigtype;
        public GameObject analysisgroupprefab,analysispanel;
        public List<AnalysisGroup> analysisgroups = new List<AnalysisGroup>();

        public void AddView(int elec)
        {
            electitle.text = elec.ToString();
            var sts = uicontroller.alsmanager.als.Signal.GetSignalType(elec);
            var vsts = CheckSignalType(elec, sts);
            sigtype.AddOptions(vsts.Select(i => i.ToString()).ToList());
            if(sts.Contains(SIGNALTYPE.Spike))
            {
                sigtype.value = vsts.IndexOf(SIGNALTYPE.Spike);
            }
            else if(sts.Contains(SIGNALTYPE.LFP))
            {
                sigtype.value = vsts.IndexOf(SIGNALTYPE.LFP);
            }
            if(sigtype.captionText.text=="Spike")
            {
                AddDefaultAnalysis(elec,SIGNALTYPE.Spike);
            }
        }

        List<SIGNALTYPE> CheckSignalType(int elec, SIGNALTYPE[] sts)
        {
            List<SIGNALTYPE> vsts = new List<SIGNALTYPE>();
            foreach(var st in sts)
            {
                if(uicontroller.alsmanager.als.Signal.IsSignalTypeOn(elec,st))
                {
                    vsts.Add(st);
                }
            }
            return vsts;
        }

        void AddDefaultAnalysis(int elec,SIGNALTYPE signaltype)
        {
            var a = AnalysisFactory.Get(signaltype);
            uicontroller.alsmanager.als.Signal.AddAnalysis(elec, signaltype, a);
            AddAnalysisView(a.GetType().Name, a.Visualizer.GetType().Name,a.Controller.GetType().Name);
        }

        void AddAnalysisView(string analyzer, string visualizer, string controller)
        {
            var ag = Instantiate(analysisgroupprefab);
            var agp = ag.GetComponent<AnalysisGroup>();
            agp.elecpanel = this;
            analysisgroups.Add(agp);
            agp.AddAnalysis(analyzer, visualizer, controller);
            ag.transform.SetParent( analysispanel. transform,false);
        }

        void AddAnalysis()
        {

        }

        void RemoveAnalysis()
        {

        }
    }
}