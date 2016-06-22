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
