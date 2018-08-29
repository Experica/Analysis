/*
VLAnalysisManager.cs is part of the VLAB project.
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
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System;
using System.IO;
using System.Linq;
using VLab;

namespace VLabAnalysis
{
    [NetworkSettings(channel = 0, sendInterval = 0)]
    public class VLAnalysisManager : NetworkBehaviour
    {
        public VLAUIController uicontroller;
        public IAnalysis als;

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            als?.Restart();
        }

        [ClientRpc]
        public void RpcNotifyStopExperiment()
        {
            if (als == null) return;
            als.ExperimentEndEnqueue();
        }

        [ClientRpc]
        public void RpcNotifyPauseExperiment()
        {
            if (als == null) return;
            if (als.Signal != null)
            {
                var t = new VLTimer();
                t.Timeout(als.DataSet.DataLatency);
                als.Signal.Stop(true);
            }
        }

        [ClientRpc]
        public void RpcNotifyResumeExperiment()
        {
            if (als == null) return;
            if (als.Signal != null)
            {
                als.Signal.Start(false);
            }
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] value)
        {
            if (als == null) return;
            // Set Experiment
            using (var stream = new MemoryStream(value))
            {
                als.DataSet.Ex = VLMsgPack.ExSerializer.Unpack(stream);
            }
            // Set VLabTimeZero
            if (als.Signal != null)
            {
                var t = new VLTimer();
                t.Timeout(als.DataSet.DataLatency);
                List<double>[] ospike;
                List<int>[] ouid;
                List<double[,]> olfp;
                List<double> olfpstarttime;
                List<double>[] odintime;
                List<int>[] odinvalue;
                als.Signal.Read(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
                als.DataSet.Add(ospike, ouid, olfp, olfpstarttime, odintime, odinvalue, null, null, null);
            }
        }

        [ClientRpc]
        public void RpcNotifyCondTest(CONDTESTPARAM name, byte[] value)
        {
            if (als == null) return;
            object v = null;
            using (var stream = new MemoryStream(value))
            {
                switch (name)
                {
                    case CONDTESTPARAM.BlockRepeat:
                    case CONDTESTPARAM.BlockIndex:
                    case CONDTESTPARAM.CondRepeat:
                    case CONDTESTPARAM.CondIndex:
                        v = VLMsgPack.ListIntSerializer.Unpack(stream);
                        break;
                    case CONDTESTPARAM.SyncEvent:
                        v = VLMsgPack.ListListStringSerializer.Unpack(stream);
                        break;
                    case CONDTESTPARAM.Event:
                    case CONDTESTPARAM.TASKSTATE:
                    case CONDTESTPARAM.BLOCKSTATE:
                    case CONDTESTPARAM.TRIALSTATE:
                    case CONDTESTPARAM.CONDSTATE:
                        v = VLMsgPack.ListListEventSerializer.Unpack(stream);
                        break;
                }
            }
            if (v != null)
            {
                als.CondTestEnqueue(name, v);
            }
        }

        [ClientRpc]
        public void RpcNotifyCondTestEnd(double time)
        {
            als?.CondTestEndEnqueue(time);
        }

        void LateUpdate()
        {
            if (als == null || als.Analyzers == null) return;

            var isfront = Input.GetButton("ShowInFront"); var isalign = Input.GetButton("Align");
            var cn = 4f;
            if (isalign)
            {
                cn = Mathf.Floor(Screen.currentResolution.width / uicontroller.appmanager.config.VisualizerWidth);
            }
            foreach (var i in als.Analyzers.Keys.ToList())
            {
                IAnalyzer a;
                if (als.Analyzers.TryGetValue(i, out a) && a != null && a.Visualizer != null)
                {
                    if (isfront)
                    {
                        if (isalign)
                        {
                            var ci = (a.Signal.Channel - 1) % cn; var ri = Mathf.Floor((a.Signal.Channel - 1) / cn);
                            a.Visualizer.Position = new Vector2(ci * uicontroller.appmanager.config.VisualizerWidth,
                                ri * uicontroller.appmanager.config.VisualizerHeight);
                        }
                        a.Visualizer.ShowInFront();
                    }
                }
            }

            als.VisualizeResults(VisualizeMode.First);
            if (als.IsExperimentEnd)
            {
                als.VisualizeResults(VisualizeMode.Last);
                if (uicontroller.appmanager.config.SaveVisualizationWhenExperimentAnalysisDone)
                {
                    als.SaveVisualization(uicontroller.appmanager.config.PlotExportWidth, uicontroller.appmanager.config.PlotExportHeight, uicontroller.appmanager.config.PlotExportDPI);
                }
                als.IsExperimentEnd = false;
            }
            uicontroller.UpdateAnalysisEventIndex(als.AnalysisEventCount);
            uicontroller.UpdateAnalysisDone(als.AnalysisDoneCount);
            uicontroller.UpdateVisualizationDone(als.VisualizationDoneCount);
        }

    }
}