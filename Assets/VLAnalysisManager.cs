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

        void Start()
        {
            var name = VLConvert.Convert<string>(uicontroller.appmanager.config["defaultanalysissystem"]);
            var cleardataperanalysis = VLConvert.Convert<int>(uicontroller.appmanager.config["cleardataperanalysis"]);
            als = AnalysisFactory. GetAnalysisSystem(name, cleardataperanalysis);

            als.Signal.StartCollectSignal(true);
        }

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            als.Reset();
            als.Signal.StartCollectSignal(true);
        }

        [ClientRpc]
        public void RpcNotifyStopExperiment()
        {
            als.Signal.StopCollectSignal();
        }

        [ClientRpc]
        public void RpcNotifyPauseExperiment()
        {
            als.Signal.StopCollectSignal();
        }

        [ClientRpc]
        public void RpcNotifyResumeExperiment()
        {
            als.Signal.StartCollectSignal(false);
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] exbs)
        {
            als.DataSet.Ex =  MsgPackSerializer.ExSerializer.Unpack(new MemoryStream(exbs));
        }

        [ClientRpc]
        public void RpcNotifyCondTestData(string name, byte[] value)
        {
            object v;
            if (name == "CondIndex")
            {
                v = MsgPackSerializer.ListIntSerializer.Unpack(new MemoryStream(value));
            }
            else
            {
                v = MsgPackSerializer.ListObjectSerializer.Unpack(new MemoryStream(value));
            }
            if (als.CondTest.ContainsKey(name))
            {
                als.CondTest[name].Enqueue(v);
            }
            else
            {
                var q = new ConcurrentQueue<object>();
                
                q.Enqueue(v);
                als.CondTest[name] = q;
            }
        }


        [ClientRpc]
        public void RpcNotifyAnalysis()
        {
            als.AddAnalysisQueue();
        }

        public void OnClientDisconnect()
        {
            als.Signal.StopCollectSignal();
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