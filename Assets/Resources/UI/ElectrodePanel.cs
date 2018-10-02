/*
ElectrodePanel.cs is part of the Experica.
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
using System;
using System.Collections.Generic;

namespace Experica.Analysis
{
    public class ElectrodePanel : MonoBehaviour
    {
        public UIController uicontroller;
        public Text title;
        public int electrodeid;
        public GameObject analysisgroupprefab,analysispanel;
        public Dictionary<Guid, AnalysisGroup> analysisgroup = new Dictionary<Guid, AnalysisGroup>();
        public List<Toggle> analysisgroupselect = new List<Toggle>();


        void AddAnalysisGroup(Guid analyzerid, string analyzer, string visualizer, string controller)
        {
            var ag = Instantiate(analysisgroupprefab);
            var agag = ag.GetComponent<AnalysisGroup>();
            agag.electrodepanel = this;
            analysisgroup[analyzerid]=agag;
            analysisgroupselect.Add(ag.GetComponentInChildren<Toggle>());
            agag.UpdateUI(analyzer, visualizer, controller);
            ag.transform.SetParent(analysispanel.transform, false);
        }

        public void AddAnalysis(SignalType signaltype)
        {
            if( uicontroller.alsmanager.als.Signal.IsSignalOn(electrodeid,signaltype))
            {
                var a = electrodeid.GetAnalyzer(signaltype);
                uicontroller.alsmanager.als.AddAnalyzer(a,0);
                AddAnalysisGroup(a.ID, a.GetType().Name,a.Visualizer.GetType().Name,a.Controller.GetType().Name);
            }
        }

        void RemoveAnalysis()
        {

        }
    }
}