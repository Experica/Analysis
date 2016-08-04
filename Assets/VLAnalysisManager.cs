// -----------------------------------------------------------------------------
// VLAnalysisManager.cs is part of the VLAB project.
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

        void Start()
        {
            var asn = (AnalysisSystem)uicontroller.appmanager.config[VLACFG.DefaultAnalysisSystem];
            var cdpa = (int)uicontroller.appmanager.config[VLACFG.ClearDataPerAnalysis];
            als = asn.GetAnalysisSystem(cdpa);
        }

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            als.Reset();
            als.Signal.StartCollectData(true);
        }

        [ClientRpc]
        public void RpcNotifyStopExperiment()
        {
            als.Signal.StopCollectData(true);
        }

        [ClientRpc]
        public void RpcNotifyPauseExperiment()
        {
            als.Signal.StopCollectData(true);
        }

        [ClientRpc]
        public void RpcNotifyResumeExperiment()
        {
            als.Signal.StartCollectData(false);
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] value)
        {
            var ex = MsgPackSerializer.ExSerializer.Unpack(new MemoryStream(value));
            if(ex.Cond!=null&&ex.Cond.Count>0)
            {
                foreach(var c in ex.Cond.Keys)
                {
                    var vs = new List<double>();
                    for(var i=0;i<ex.Cond[c].Count;i++)
                    {
                        ex.Cond[c][i]= ((MessagePackObject)ex.Cond[c][i]).ToObject();
                    }
                }
            }
            als.DataSet.Ex = ex;
        }

        [ClientRpc]
        public void RpcNotifyCondTest(CONDTESTPARAM name, byte[] value)
        {
            object v;
            switch(name)
            {
                case CONDTESTPARAM.CondIndex:
                    v = MsgPackSerializer.ListIntSerializer.Unpack(new MemoryStream(value));
                    break;
                case CONDTESTPARAM.CONDSTATE:
                    v = MsgPackSerializer.CONDSTATESerializer.Unpack(new MemoryStream(value));
                    break;
                default:
                    v = MsgPackSerializer.ListObjectSerializer.Unpack(new MemoryStream(value));
                    break;
            }
            als.CondTestEnqueue(name, v);
        }

        [ClientRpc]
        public void RpcNotifyCondTestEnd(double time)
        {
            als.CondTestEndEnqueue(time);
        }

        public void OnClientDisconnect()
        {
            als.Signal.StopCollectData(true);
        }

        void Update()
        {
            if (als.Signal != null && als.Signal.Analyzers != null)
            {
                foreach (var a in als.Signal.Analyzers)
                {
                    AnalysisResult result;
                    if (a.Results.TryDequeue(out result))
                    {
                        a.Visualizer.Visualize(result);
                    }
                }
            }
        }
    }

}