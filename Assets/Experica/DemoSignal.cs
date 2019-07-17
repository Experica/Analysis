/*
RippleSignal.cs is part of the Experica.
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
using System.Collections.Immutable;
using System.Collections.Generic;
using System;

namespace Experica
{
    /// <summary>
    /// Ripple signal cached by internal thread through xippmex wrapper
    /// </summary>
    public class DemoSignal : ISignal
    {

        readonly int digitalIPI, analogIPI, tickfreq, maxelectrodeid, timeunitpersec, sleepduration, diginbitchange;
        ImmutableArray<int> _electrodeids;
        public int CacheMaxDuration
        {
            get { return 0; }
            set {}
        }
        // Linear Data Buffers
        Dictionary<int, List<double>> spike;
        Dictionary<int, List<int>> uid;
        Dictionary<int, List<double>> dintime;
        Dictionary<int, List<int>> dinvalue;
        List<double[,]> lfp;
        List<double> lfpstarttime;

        public DemoSignal(int tickfreq = 30000, int maxelectrodeid = 5120, int timeunitpersec = 1000,
            int digitalIPI = 800, int analogIPI = 4500, int sleepduration = 1, int cachemaxduration = 1800000, int diginbitchange = 1)
        {
            this.tickfreq = tickfreq;
            this.maxelectrodeid = maxelectrodeid;
            this.timeunitpersec = timeunitpersec;
            this.digitalIPI = digitalIPI;
            this.analogIPI = analogIPI;
            this.sleepduration = Math.Max(1, sleepduration);
            this.diginbitchange = diginbitchange;
            int[] el = new int[1];
            el[0] = 1;
            _electrodeids = el.ToImmutableArray();
            NewDataBuffer();
        }

        public void Dispose() {}
        public SignalSource Source { get { return SignalSource.Demo; } }
        public bool Connect() { return true; }
        public void Close() {}
        public void RefreshChannels() {}
        public ImmutableArray<int> Channels { get { return _electrodeids; } }
        public bool IsChannel { get { return true; } }
        public double Time { get { return 0 * timeunitpersec; } }
        public ImmutableArray<SignalType> GetSignalTypes(int channel, bool onlyreturnsignalontype = true)
        {
            var vs = new SignalType[1];
            vs[0] =  SignalType.OneKSpS;
            return vs.ToImmutableArray();
        }
        public bool IsSignalOn(int channel, SignalType signaltype) { return true; }
        public bool IsRunning { get { return true; } }
        public bool Start(bool isclean = true) { return true; }
        public bool Stop(bool collectallbeforestop = true) { return true; }
        public bool Restart(bool iscleanall = true) { return true; }
        public void Read(out Dictionary<int, List<double>> ospike, out Dictionary<int, List<int>> ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out Dictionary<int, List<double>> odintime, out Dictionary<int, List<int>> odinvalue)
        {
            GetDataBuffer(out ospike, out ouid, out olfp, out olfpstarttime, out odintime, out odinvalue);
        }

        void GetDataBuffer(out Dictionary<int, List<double>> ospike, out Dictionary<int, List<int>> ouid,
            out List<double[,]> olfp, out List<double> olfpstarttime,
            out Dictionary<int, List<double>> odintime, out Dictionary<int, List<int>> odinvalue)
        {
            ospike = spike;
            ouid = uid;
            olfp = lfp;
            olfpstarttime = lfpstarttime;
            odintime = dintime;
            odinvalue = dinvalue;
            NewDataBuffer();
        }

        void NewDataBuffer()
        {
            spike = new Dictionary<int, List<double>>();
            uid = new Dictionary<int, List<int>>();
            lfp = new List<double[,]>();
            lfpstarttime = new List<double>();
            dintime = new Dictionary<int, List<double>>();
            dinvalue = new Dictionary<int, List<int>>();
        }
    }
}