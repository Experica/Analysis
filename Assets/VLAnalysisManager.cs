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
            var name = VLConvert.Convert<string>(uicontroller.appmanager.config["defaultanalysissystem"]);
            var cleardataperanalysis = VLConvert.Convert<int>(uicontroller.appmanager.config["cleardataperanalysis"]);
            als = AnalysisFactory. GetAnalysisSystem(name, cleardataperanalysis);
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
            als.Signal.StopCollectData(false);
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
        public void RpcNotifyExperiment(byte[] exbs)
        {
            var ex = MsgPackSerializer.ExSerializer.Unpack(new MemoryStream(exbs));
            if(ex.cond!=null&&ex.cond.Count>0)
            {
                foreach(var c in ex.cond.Keys)
                {
                    var vs = new List<double>();
                    for(var i=0;i<ex.cond[c].Count;i++)
                    {
                        ex.cond[c][i]= ((MessagePackObject)ex.cond[c][i]).ToObject();
                    }
                }
            }
            als.DataSet.Ex = ex;
        }

        [ClientRpc]
        public void RpcNotifyCondTestData(string name, byte[] value)
        {
            object v;
            switch(name)
            {
                case "CondIndex":
                    v = MsgPackSerializer.ListIntSerializer.Unpack(new MemoryStream(value));
                    break;
                case "CONDSTATE":
                    v = MsgPackSerializer.CONDSTATESerializer.Unpack(new MemoryStream(value));
                    break;
                default:
                    v = MsgPackSerializer.ListObjectSerializer.Unpack(new MemoryStream(value));
                    break;
            }
            als.CondTestEnqueue(name, v);
        }


        [ClientRpc]
        public void RpcNotifyAnalysis(double time)
        {
            als.AnalysisEnqueue(time);
        }

        public void OnClientDisconnect()
        {
            als.Signal.StopCollectData(false);
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