using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System;
using System.IO;
using System.Linq;
using VLab;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using MsgPack;
using MsgPack.Serialization;
using System.Threading;
using System.Collections;

namespace VLabAnalysis
{
    [NetworkSettings(channel = 0, sendInterval = 0)]
    public class VLAnalysisManager : NetworkBehaviour
    {
        public VLAUIController uicontroller;
        public IAnalysis als;

        void Start()
        {
            als = new AnalysisDotNet(VLConvert.Convert<int>(uicontroller.appmanager.config["cleardataperanalysis"]));
        }

        [ClientRpc]
        public void RpcNotifyStartExperiment()
        {
            if (als != null)
            {
                als.Reset();
                als.Signal.StartCollectSignal();
            }
        }

        [ClientRpc]
        public void RpcNotifyExperiment(byte[] exbs)
        {
            if (als != null)
            {
                var serializer = SerializationContext.Default.GetSerializer<Experiment>();
                var stream = new MemoryStream(exbs);
                als.DataSet.ex = serializer.Unpack(stream);
            }
        }

        [ClientRpc]
        public void RpcNotifyCondTestData(string name, byte[] value)
        {
            if (als != null)
            {
                var serializer = SerializationContext.Default.GetSerializer<List<object>>();
                var stream = new MemoryStream(value);
                var v = serializer.Unpack(stream);
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
        }

        [ClientRpc]
        public void RpcAnalysis()
        {
            if (als != null)
            {
                als.AddAnalysisQueue();
            }
        }

    }
}