/*
VLAnalysisManager.cs is part of the VLAB project.
Copyright (c) 2017 Li Alex Zhang and Contributors

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
using MsgPack;

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
            if (als == null) return;
            als.Reset();
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
                t.Countdown(als.DataSet.DataLatency);
                als.Signal.StopCollectData(true);
            }
        }

        [ClientRpc]
        public void RpcNotifyResumeExperiment()
        {
            if (als == null) return;
            if (als.Signal != null)
            {
                als.Signal.StartCollectData(false);
            }
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] value)
        {
            if (als == null) return;
            // Set Experiment Data
            var ex = VLMsgPack.ExSerializer.Unpack(new MemoryStream(value));
            if (ex.Cond != null && ex.Cond.Count > 0)
            {
                foreach (var fvs in ex.Cond.Values)
                {
                    for (var i = 0; i < fvs.Count; i++)
                    {
                        fvs[i] = fvs[i].MsgPackObjectToObject();
                    }
                }
            }
            if (ex.EnvParam != null && ex.EnvParam.Count > 0)
            {
                foreach (var k in ex.EnvParam.Keys)
                {
                    ex.EnvParam[k] = ex.EnvParam[k].MsgPackObjectToObject();
                }
            }
            als.DataSet.Ex = ex;
            //  Set VLabTimeZero
            if (als.Signal != null)
            {
                var t = new VLTimer();
                t.Countdown(als.DataSet.DataLatency);
                List<double>[] ospike;
                List<int>[] ouid;
                List<double[,]> olfp;
                List<double> olfpstarttime;
                List<double>[] odintime;
                List<int>[] odinvalue;
                als.Signal.GetData(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
                als.DataSet.Add(ospike, ouid, olfp, olfpstarttime, odintime, odinvalue, null, null, null);
            }
        }

        [ClientRpc]
        public void RpcNotifyCondTest(CONDTESTPARAM name, byte[] value)
        {
            if (als == null) return;
            object v;
            switch (name)
            {
                case CONDTESTPARAM.CondRepeat:
                case CONDTESTPARAM.CondIndex:
                    v = VLMsgPack.ListIntSerializer.Unpack(new MemoryStream(value));
                    break;
                case CONDTESTPARAM.CONDSTATE:
                    v = VLMsgPack.CONDSTATESerializer.Unpack(new MemoryStream(value));
                    break;
                default:
                    v = VLMsgPack.ListObjectSerializer.Unpack(new MemoryStream(value));
                    break;
            }
            als.CondTestEnqueue(name, v);
        }

        [ClientRpc]
        public void RpcNotifyCondTestEnd(double time)
        {
            if (als == null) return;
            als.CondTestEndEnqueue(time);
        }

        void LateUpdate()
        {
            if (als == null || als.Analyzers == null) return;

            var isfront = Input.GetButton("ShowInFront"); var isalign = Input.GetButton("Align");
            var cn = 4f;
            if (isalign)
            {
                cn = Mathf.Floor(Screen.currentResolution.width / (int)uicontroller.appmanager.config[VLACFG.VisualizerWidth]);
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
                            a.Visualizer.Position = new Vector2(ci * (int)uicontroller.appmanager.config[VLACFG.VisualizerWidth],
                                ri * (int)uicontroller.appmanager.config[VLACFG.VisualizerHeight]);
                        }
                        a.Visualizer.ShowInFront();
                    }
                }
            }

            als.VisualizeResults(VisualizeMode.First);
            if (als.IsExperimentAnalysisDone)
            {
                als.VisualizeResults(VisualizeMode.Last);
                if ((bool)uicontroller.appmanager.config[VLACFG.SaveVisualizationWhenExperimentAnalysisDone])
                {
                    als.SaveVisualization((int)uicontroller.appmanager.config[VLACFG.PlotExportWidth], (int)uicontroller.appmanager.config[VLACFG.PlotExportHeight],
                        (int)uicontroller.appmanager.config[VLACFG.PlotExportDPI]);
                }
                als.IsExperimentAnalysisDone = false;
            }
        }

    }

}