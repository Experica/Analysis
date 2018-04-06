/*
RippleSignal.cs is part of the VLAB project.
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
using System.Collections;
using System.Collections.Generic;
using System;
using System.Linq;
using Ripple;
using MathWorks.MATLAB.NET.Arrays;
using MathWorks.MATLAB.NET.Utility;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;

namespace VLabAnalysis
{
    public class RippleSignal : ISignal
    {
        // Interlocked variables
        int disposecount = 0;
        int gotothreadevent = 0;

        XippmexDotNet xippmexdotnet = new XippmexDotNet();
        readonly int digitalIPI, analogIPI, tickfreq, maxelectrodeid, timeunitpersec,
            sleepresolution, maxdigitalinchannel, maxdigitalbuffersize; // 0.5hr spikes of each channel
        int[] _eids;
        // Linear Data Buffers
        List<double>[] spike;
        List<int>[] uid;
        List<double[,]> lfp;
        List<double> lfpstarttime;
        List<double>[] dintime;
        List<int>[] dinvalue;

        readonly object xippmexlock = new object();
        readonly object apilock = new object();
        readonly object gotoeventlock = new object();
        Thread datathread;
        ManualResetEvent datathreadevent = new ManualResetEvent(false);
        bool running = false;
        public bool IsRunning { get { lock (apilock) { return running; } } }
        public SignalSource Source { get { return SignalSource.Ripple; } }

        public RippleSignal(int tickfreq = 30000, int maxelectrodeid = 5120, int timeunitpersec = 1000,
            int digitalIPI = 800, int analogIPI = 4500, int sleepresolution = 1, int maxdigitalinchannel = 6, int maxdigitalbuffersize = 1800000)
        {
            this.tickfreq = tickfreq;
            this.maxelectrodeid = maxelectrodeid;
            this.timeunitpersec = timeunitpersec;
            this.digitalIPI = digitalIPI;
            this.analogIPI = analogIPI;
            this.sleepresolution = Math.Max(1, sleepresolution);
            this.maxdigitalinchannel = maxdigitalinchannel;
            this.maxdigitalbuffersize = maxdigitalbuffersize;
        }

        ~RippleSignal()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (1 == Interlocked.Exchange(ref disposecount, 1))
            {
                return;
            }
            Stop(false);
            xippmexdotnet.xippmex("close");
            xippmexdotnet.Dispose();
        }

        string SignalTypeToRipple(SignalType signaltype)
        {
            switch (signaltype)
            {
                case SignalType.Raw:
                    return "raw";
                case SignalType.ThirtyKSpS:
                    return "30ksps";
                case SignalType.TwoKSpS:
                    return "hi-res";
                case SignalType.OneKSpS:
                    return "1ksps";
                case SignalType.LFP:
                    return "lfp";
                case SignalType.Stim:
                    return "stim";
                default:
                    return "spk";
            }
        }

        SignalType RippleToSignalType(string ripplesignaltype)
        {
            string ripple;
            switch (ripplesignaltype)
            {
                case "raw":
                    ripple = "Raw";
                    break;
                case "30ksps":
                    ripple = "ThirtyKSpS";
                    break;
                case "hi-res":
                    ripple = "TwoKSpS";
                    break;
                case "1ksps":
                    ripple = "OneKSpS";
                    break;
                case "lfp":
                    ripple = "LFP";
                    break;
                case "stim":
                    ripple = "Stim";
                    break;
                default:
                    ripple = "Spike";
                    break;
            }
            return (SignalType)Enum.Parse(typeof(SignalType), ripple);
        }

        public SignalType[] GetSignalTypes(int channel, bool isonlyreturnsignalontype = true)
        {
            lock (xippmexlock)
            {
                SignalType[] v = null;
                if (IsReady && Channels.Contains(channel))
                {
                    var ts = xippmexdotnet.xippmex(1, "signal", channel)[0] as MWCellArray;
                    var tn = ts.NumberOfElements;
                    if (tn > 0)
                    {
                        v = new SignalType[tn];
                        for (var i = 0; i < tn; i++)
                        {
                            v[i] = RippleToSignalType(((MWCharArray)ts[new[] { 1, i + 1 }]).ToString());
                        }
                    }
                }
                if (v != null && isonlyreturnsignalontype)
                {
                    v = v.Where(i => IsSignalOn(channel, i)).ToArray();
                }
                return v;
            }
        }

        public bool IsSignalOn(int channel, SignalType signaltype)
        {
            lock (xippmexlock)
            {
                if (IsReady && Channels.Contains(channel))
                {
                    var t = ((MWNumericArray)xippmexdotnet.xippmex(1, "signal", channel, SignalTypeToRipple(signaltype))[0]).ToScalarInteger();
                    return t == 1 ? true : false;
                }
                return false;
            }
        }

        public bool IsOnline
        {
            get
            {
                lock (xippmexlock)
                {
                    var isonline = ((MWLogicalArray)xippmexdotnet.xippmex(1)[0]).ToVector()[0];
                    if (isonline)
                    {
                        try
                        {
                            var t = (double[])((MWNumericArray)xippmexdotnet.xippmex(1, "elec", "all")[0]).ToVector(MWArrayComponent.Real);
                            _eids = t.Where(i => i <= maxelectrodeid).Select(i => (int)i).ToArray();
                        }
                        catch (Exception ex)
                        {
                            isonline = false;
                            _eids = null;
                        }
                    }
                    else
                    {
                        _eids = null;
                    }
                    return isonline;
                }
            }
        }

        public int[] Channels
        {
            get
            {
                lock (xippmexlock)
                {
                    return _eids;
                }
            }
        }

        public bool IsReady { get { lock (xippmexlock) { return _eids != null; } } }

        public double Time
        {
            get
            {
                lock (xippmexlock)
                {
                    return ((MWNumericArray)xippmexdotnet.xippmex(1, "time")[0]).ToScalarDouble() / tickfreq * timeunitpersec;
                }
            }
        }

        public bool Start(bool isclean = true)
        {
            lock (apilock)
            {
                if (IsReady)
                {
                    if (datathread == null)
                    {
                        datathread = new Thread(CollectDataThreadFunction);
                        datathread.Name = "RippleSignal";
                        NewDataBuffer(Channels.Length);
                        datathreadevent.Set();
                        running = true;
                        datathread.Start();
                    }
                    else
                    {
                        if (!running)
                        {
                            if (isclean)
                            {
                                NewDataBuffer(Channels.Length);
                            }
                            running = true;
                            datathreadevent.Set();
                        }
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool Stop(bool iscollectallbeforestop = true)
        {
            lock (apilock)
            {
                if (datathread != null && running)
                {
                    lock (gotoeventlock)
                    {
                        datathreadevent.Reset();
                        Interlocked.Exchange(ref gotothreadevent, 1);
                    }
                    while (true)
                    {
                        if (0 == Interlocked.CompareExchange(ref gotothreadevent, 0, 255))
                        {
                            if (iscollectallbeforestop)
                            {
                                // make sure to get all data until the time this function is called.
                                CollectData();
                            }
                            running = false;
                            break;
                        }
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public bool Restart(bool iscleanall = true)
        {
            lock (apilock)
            {
                Stop(false);
                if (iscleanall)
                {
                    // Clear xippmex buffer
                    lock (xippmexlock)
                    {
                        xippmexdotnet.xippmex("close");
                        xippmexdotnet.xippmex(1);
                    }
                }
                return Start(true);
            }
        }

        #region Collect Data
        void CollectDataThreadFunction()
        {
            // here we use local, readonly and interlocked variables.
            string fcs, scs;
            int fi, si, fn;
            if (digitalIPI < analogIPI)
            {
                fn = (int)Math.Floor((double)analogIPI / digitalIPI);
                fcs = "digital";
                scs = "analog";
                fi = digitalIPI;
                si = analogIPI % digitalIPI;
            }
            else if (digitalIPI == analogIPI)
            {
                fn = 1;
                fcs = "all";
                scs = "";
                fi = digitalIPI;
                si = 0;
            }
            else
            {
                fn = (int)Math.Floor((double)digitalIPI / analogIPI);
                fcs = "analog";
                scs = "digital";
                fi = analogIPI;
                si = digitalIPI % analogIPI;
            }
            while (true)
            {
                ThreadEvent:
                lock (gotoeventlock)
                {
                    Interlocked.Exchange(ref gotothreadevent, 0);
                    datathreadevent.WaitOne();
                }
                for (var i = 0; i < fn; i++)
                {
                    if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }
                    for (var j = 0; j < fi / sleepresolution; j++)
                    {
                        Thread.Sleep(sleepresolution);
                        if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                        {
                            goto ThreadEvent;
                        }
                    }
                    switch (fcs)
                    {
                        case "digital":
                            CollectDigitalIn();
                            CollectSpike();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectData();
                            break;
                    }
                }
                if (!string.IsNullOrEmpty(scs))
                {
                    if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                    {
                        goto ThreadEvent;
                    }
                    for (var j = 0; j < si / sleepresolution; j++)
                    {
                        Thread.Sleep(sleepresolution);
                        if (1 == Interlocked.CompareExchange(ref gotothreadevent, 0, 1))
                        {
                            goto ThreadEvent;
                        }
                    }
                    switch (scs)
                    {
                        case "digital":
                            CollectDigitalIn();
                            CollectSpike();
                            break;
                        case "analog":
                            CollectLFP();
                            break;
                        default:
                            CollectData();
                            break;
                    }
                }
            }
        }

        void CollectData()
        {
            CollectDigitalIn();
            CollectSpike();
            CollectLFP();
        }

        void CollectDigitalIn()
        {
            MWArray[] d;
            lock (xippmexlock)
            {
                d = xippmexdotnet.digin(2);
            }
            var d0 = d[0] as MWCellArray;
            var d1 = d[1] as MWCellArray;
            for (var i = 0; i < maxdigitalinchannel; i++)
            {
                var idx = new[] { i + 1, 1 };
                var dt = d0[idx] as MWNumericArray;
                var dv = d1[idx] as MWNumericArray;
                if (!dt.IsEmpty)
                {
                    var t = (double[])dt.ToVector(MWArrayComponent.Real);
                    var v = (double[])dv.ToVector(MWArrayComponent.Real);
                    var n = t.Length;
                    for (var j = 0; j < n; j++)
                    {
                        dintime[i].Add((t[j] / tickfreq) * timeunitpersec);
                        dinvalue[i].Add((int)v[j]);
                    }
                    var l = dintime[i].Count - maxdigitalbuffersize;
                    if (l > 0)
                    {
                        dintime[i].RemoveRange(0, l);
                        dinvalue[i].RemoveRange(0, l);
                    }
                }
            }
        }

        void CollectSpike()
        {
            var sid = Channels;
            MWArray[] s;
            lock (xippmexlock)
            {
                // xippmex works only when electrode ids are in (1,n) row vector of doubles
                s = xippmexdotnet.xippmex(4, "spike", new MWNumericArray(1, sid.Length, sid, null, true, false));
            }
            var s1 = s[1] as MWCellArray;
            var s3 = s[3] as MWCellArray;
            for (var i = 0; i < sid.Length; i++)
            {
                // Indexing is 1-based
                var idx = new int[] { i + 1, 1 };
                var st = s1[idx] as MWNumericArray;
                if (!st.IsEmpty)
                {
                    var ss = (double[])st.ToVector(MWArrayComponent.Real);
                    var us = (double[])(s3[idx] as MWNumericArray).ToVector(MWArrayComponent.Real);
                    var sidx = sid[i] - 1;
                    var n = ss.Length;
                    for (var j = 0; j < n; j++)
                    {
                        spike[sidx].Add((ss[j] / tickfreq) * timeunitpersec);
                        uid[sidx].Add((int)us[j]);
                    }
                    var l = spike[sidx].Count - maxdigitalbuffersize;
                    if (l > 0)
                    {
                        spike[sidx].RemoveRange(0, l);
                        uid[sidx].RemoveRange(0, l);
                    }
                }
            }
        }

        void CollectLFP()
        {
        }
        #endregion

        public bool Read(out List<double>[] ospike, out List<int>[] ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out List<double>[] odintime, out List<int>[] odinvalue)
        {
            lock (apilock)
            {
                if (IsReady)
                {
                    if (running)
                    {
                        Stop(true);
                        GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue, Channels.Length);
                        Start(false);
                    }
                    else
                    {
                        GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue, Channels.Length);
                    }
                    return true;
                }
                else
                {
                    GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
                    return false;
                }
            }
        }

        void GetDataBuffer(out List<double>[] ospike, out List<int>[] ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out List<double>[] odintime, out List<int>[] odinvalue, int en = 1)
        {
            ospike = spike;
            ouid = uid;
            olfp = lfp;
            olfpstarttime = lfpstarttime;
            odintime = dintime;
            odinvalue = dinvalue;
            NewDataBuffer(en);
        }

        void NewDataBuffer(int en)
        {
            spike = new List<double>[en];
            uid = new List<int>[en];
            for (var i = 0; i < en; i++)
            {
                spike[i] = new List<double>();
                uid[i] = new List<int>();
            }
            lfp = new List<double[,]>();
            lfpstarttime = new List<double>();
            dintime = new List<double>[maxdigitalinchannel];
            dinvalue = new List<int>[maxdigitalinchannel];
            for (var i = 0; i < maxdigitalinchannel; i++)
            {
                dintime[i] = new List<double>();
                dinvalue[i] = new List<int>();
            }
        }
    }
}