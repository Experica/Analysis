// -----------------------------------------------------------------------------
// SignalPanel.cs is part of the VLAB project.
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
using System.Collections;

namespace VLabAnalysis
{
    public class SignalPanel : MonoBehaviour
    {
        public VLAUIController uicontroller;
        public GameObject elecviewprefab,content;
        public Text title;

        public void SearchSignal()
        {
            if (uicontroller. alsmanager != null)
            {
                if (uicontroller. alsmanager.als.SearchSignal())
                {
                    title.text = "Signal (" + uicontroller.alsmanager.als.Signal.System + ")";
                    AddSignalView();
                }
            }
        }

        void AddSignalView()
        {
            for(var i=0;i<content.transform.childCount;i++)
            {
                Destroy(content.transform.GetChild(i).gameObject);
            }
            foreach(var e in uicontroller.alsmanager.als.Signal.ElectrodeChannels)
            {
                AddElecView(e);
            }
            UpdateViewRect();
        }

        void UpdateViewRect()
        {
            var np = content.transform.childCount;
            var grid = content.GetComponent<GridLayoutGroup>();
            var cn = grid.constraintCount;
            var rn = Mathf.Floor(np / cn) + 1;
            var rt = (RectTransform)content.transform;
            rt.sizeDelta = new Vector2((grid.cellSize.x + grid.spacing.x) * cn, (grid.cellSize.y+grid.spacing.y) * rn);
        }

        void AddElecView(int elec)
        {
            var go = Instantiate(elecviewprefab);
            var ep = go.GetComponent<ElecPanel>();
            ep.uicontroller = uicontroller;
            ep.AddView(elec);
            go.name = elec.ToString();
            go.transform.SetParent(content.transform,false);
        }
    }
}
