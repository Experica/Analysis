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