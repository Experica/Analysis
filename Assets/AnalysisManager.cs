/*
AnalysisManager.cs is part of the Experica.
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

namespace Experica.Analysis
{
    [NetworkSettings(channel = 0, sendInterval = 0)]
    public class AnalysisManager : NetworkBehaviour
    {
        public UIController uicontroller;
        public IAnalysis als;

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            if (als == null) return;
            als.Restart();
            uicontroller.UpdateAnalysisState(als.IsAnalyzing, "Analysis Engine Started");
        }

        [ClientRpc]
        public void RpcNotifyStopExperiment()
        {
            als?.ExperimentEndEnqueue();
        }

        [ClientRpc]
        public void RpcNotifyPauseExperiment()
        {
            if (als == null) return;
            if (als.Signal != null)
            {
                var t = new Timer();
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
            using (var stream = new MemoryStream(value))
            {
                var ex = MsgPack.ExSerializer.Unpack(stream);
                var config = uicontroller.config;
                bool isallowed = true;
                switch (config.RegisteredEx)
                {
                    case ExRegistry.WhiteList:
                        isallowed = config.WhiteList.Contains(ex.ID) ? true : false;
                        break;
                    case ExRegistry.BlackList:
                        isallowed = config.BlackList.Contains(ex.ID) ? false : true;
                        break;
                }
                if (isallowed)
                {
                    als.DataSet.Config = config;
                    als.DataSet.Ex = ex;
                    als.DataSet.ParseEx();
                }
                else
                {
                    als.Signal?.Stop(false);
                    als.Stop();
                    uicontroller.UpdateAnalysisState(als.IsAnalyzing, $"ID={ex.ID} is not allowed by registry in config");
                    return;
                }
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
                        v = MsgPack.ListIntSerializer.Unpack(stream);
                        break;
                    case CONDTESTPARAM.SyncEvent:
                        v = MsgPack.ListListStringSerializer.Unpack(stream);
                        break;
                    case CONDTESTPARAM.Event:
                    case CONDTESTPARAM.TASKSTATE:
                    case CONDTESTPARAM.BLOCKSTATE:
                    case CONDTESTPARAM.TRIALSTATE:
                    case CONDTESTPARAM.CONDSTATE:
                        v = MsgPack.ListListEventSerializer.Unpack(stream);
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
            if (als == null) return;
            als.LayoutVisualization(Input.GetButton("ShowInFront"), Input.GetButton("Align"));
            als.VisualizeResults(VisualizeMode.First);
            if (als.ExperimentAnalysisStage == 1)
            {
                als.VisualizeResults(VisualizeMode.All);
                als.SaveVisualization();
                als.ExperimentAnalysisStage = 2;
                als.Stop();
                uicontroller.UpdateAnalysisState(als.IsAnalyzing, "All Finished");
            }
            uicontroller.UpdateAnalysisEventCount(als.AnalysisEventCount);
            uicontroller.UpdateAnalysisDoneCount(als.AnalysisDoneCount);
            uicontroller.UpdateVisualizationDoneCount(als.VisualizationDoneCount);
            uicontroller.UpdateEventSyncIntegrity(als.DataSet.EventSyncIntegrity);
            uicontroller.UpdateEventMeasureIntegrity(als.DataSet.EventMeasureIntegrity);
        }

    }
}