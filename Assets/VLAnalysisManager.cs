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
            var asn = (AnalysisSystem)uicontroller.appmanager.config[VLACFG.AnalysisSystem];
            var cdpa = (int)uicontroller.appmanager.config[VLACFG.ClearDataPerAnalysis];
            als = asn.GetAnalysisSystem(cdpa);
        }

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            als.Reset();
            if (als.Signal != null)
            {
                als.Signal.StartCollectData(true);
            }
        }

        [ClientRpc]
        public void RpcNotifyStopExperiment()
        {
            if (als.Signal != null)
            {
                als.Signal.StopCollectData(true);
                als.ExperimentEndEnqueue();
            }
        }

        [ClientRpc]
        public void RpcNotifyPauseExperiment()
        {
            if (als.Signal != null)
            {
                als.Signal.StopCollectData(true);
            }
        }

        [ClientRpc]
        public void RpcNotifyResumeExperiment()
        {
            if (als.Signal != null)
            {
                als.Signal.StartCollectData(false);
            }
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] value)
        {
            var ex = VLMsgPack.ExSerializer.Unpack(new MemoryStream(value));
            if(ex.Cond!=null&&ex.Cond.Count>0)
            {
                foreach(var fl in ex.Cond.Values)
                {
                    for(var i=0;i<fl.Count;i++)
                    {
                        fl[i] = fl[i].MsgPackObjectToObject();
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
            als.CondTestEndEnqueue(time);
        }

        [Command]
        public void CmdNotifyUpdate()
        {

        }

        public void OnClientDisconnect()
        {
            if (als.Signal != null)
            {
                als.Signal.StopCollectData(true);
            }
        }

        void Update()
        {
            if(als.Signal!=null&&als.Signal.Analyzers!=null)
            {
                foreach (var a in als.Signal.Analyzers.Values)
                {
                    if(a.Controller!=null)
                    {
                        ICommand command;
                        if (a.Controller.CommandQueue.TryDequeue(out command))
                        {
                            CmdNotifyUpdate();
                        }
                    }
                }
            }
        }

        void LateUpdate()
        {
            if (als.Signal != null && als.Signal.Analyzers != null)
            {
                if (als.IsAnalysisDone)
                {
                    var datapath = als.DataSet.Ex.DataPath;
                    var datadir = Path.GetDirectoryName(datapath);
                    var dataname = Path.GetFileNameWithoutExtension(datapath);
                    int width = (int)uicontroller.appmanager.config[VLACFG.VisualizationWidth];
                    int height = (int)uicontroller.appmanager.config[VLACFG.VisualizationHeight];
                    float dpi = (float)uicontroller.appmanager.config[VLACFG.VisualizationDPI];
                    foreach (var a in als.Signal.Analyzers.Values)
                    {
                            if (a.Visualizer != null)
                            {
                            var filename = dataname + "_" + a.GetType().Name + "_" + a.Visualizer.GetType().Name
                                + "_E" + a.SignalChannel.SignalID;
                            var filedir = Path.Combine(datadir, "E" + a.SignalChannel.SignalID);
                            if(!Directory.Exists(filedir))
                            {
                                Directory.CreateDirectory(filedir);
                            }
                            a.Visualizer.Save(Path.Combine(filedir,filename) ,width,height,dpi);
                            }
                    }
                    als.IsAnalysisDone = false;
                }
                else
                {
                    for (var i = 0; i < als.Signal.Analyzers.Count; i++)
                    {
                        var a = als.Signal.Analyzers.ElementAt(i).Value;
                        IResult result;
                        if (a.ResultQueue.TryDequeue(out result))
                        {
                            if (a.Visualizer != null)
                            {
                                a.Visualizer.Visualize(result);
                            }
                        }
                    }
                    //foreach (var a in als.Signal.Analyzers.Values)
                    //{

                    //}
                }
            }
        }

    }

}