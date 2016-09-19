/*
SignalPanel.cs is part of the VLAB project.
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
using System.Collections.Generic;

namespace VLabAnalysis
{
    public class SignalPanel : MonoBehaviour
    {
        public VLAUIController uicontroller;
        public GameObject electrodepanelprefab, content;
        public Text title;

        public void UpdateSignal(bool issignal)
        {
            if (issignal)
            {
                ClearSignalView();
                AddSignalView();
                title.text = "Signal (" + uicontroller.alsmanager.als.Signal.Source + ")";
            }
            else
            {
                ClearSignalView();
                title.text = "Signal";
            }
        }

        void ClearSignalView()
        {
            for (var i = 0; i < content.transform.childCount; i++)
            {
                Destroy(content.transform.GetChild(i).gameObject);
            }
        }

        void AddSignalView()
        {
            foreach (var e in uicontroller.alsmanager.als.Signal.Channels)
            {
                AddElectrodePanel(e);
            }
            UpdateContentRect();
        }

        void UpdateContentRect()
        {
            var n = content.transform.childCount;
            var grid = content.GetComponent<GridLayoutGroup>();
            var cn = grid.constraintCount;
            var rn = Mathf.Floor(n / cn) + 1;
            var rt = (RectTransform)content.transform;
            rt.sizeDelta = new Vector2((grid.cellSize.x + grid.spacing.x) * cn, (grid.cellSize.y + grid.spacing.y) * rn);
        }

        void AddElectrodePanel(int electrodeid)
        {
            var go = Instantiate(electrodepanelprefab);
            go.name = electrodeid.ToString();
            var ep = go.GetComponent<ElectrodePanel>();
            ep.uicontroller = uicontroller;
            ep.electrodeid = electrodeid;
            ep.title.text = electrodeid.ToString();
            if ((bool)uicontroller.appmanager.config[VLACFG.AddSpikeAnalysisWhenSignalOnLine])
            {
                ep.AddAnalysis(SignalType.Spike);
            }
            go.transform.SetParent(content.transform, false);
        }
    }
}
