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
            als = GetAnalysisSystem(name, cleardataperanalysis);
        }

        public IAnalysis GetAnalysisSystem(string name = "DotNet", int cleardataperanalysis = 1)
        {
            IAnalysis als;
            switch (name)
            {
                default:
                    als = new AnalysisDotNet(cleardataperanalysis);
                    break;
            }
            return als;
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
            als.DataSet.ex =  MsgPackSerializer.ExSerializer.Unpack(new MemoryStream(exbs));
        }

        [ClientRpc]
        public void RpcNotifyCondTestData(string name, byte[] value)
        {
            var v = MsgPackSerializer.ListObjectSerializer.Unpack(new MemoryStream(value));
            if (als.CondTest.ContainsKey(name))
            {
                als.CondTest[name].Enqueue(v);
            }
            else
            {
                var q = new ConcurrentQueue<List<object>>();
                q.Enqueue(v);
                als.CondTest[name] = q;
            }
        }

        [ClientRpc]
        public void RpcAnalysis()
        {
            als.AddAnalysisQueue();
        }

        public void OnClientDisconnect()
        {
            als.Signal.StopCollectSignal();
        }

    }

}