/*
ISignal.cs is part of the VLAB project.
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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VLabAnalysis
{
    public enum SignalSource
    {
        Ripple,
        Plexon,
        TDT
    }

    public enum SignalType
    {
        Raw,
        ThirtyKSpS,
        TwoKSpS,
        OneKSpS,
        LFP,
        Event,
        Spike,
        Stim
    }

    public struct Signal
    {
        public readonly int Channel;
        public readonly SignalType Type;

        public Signal(int channel, SignalType type)
        {
            Channel = channel;
            Type = type;
        }
    }

    /// <summary>
    /// Implementation should be thread safe
    /// </summary>
    public interface ISignal : IDisposable
    {
        bool IsOnline { get; }
        SignalSource Source { get; }
        int[] Channels { get; }
        SignalType[] GetSignalTypes(int channel, bool isonlyreturnonsignaltype);
        bool IsSignalOn(int channel, SignalType signaltype);
        double Time { get; }
        bool IsReady { get; }
        bool Start(bool isclean);
        bool Stop(bool iscollectallbeforestop);
        bool Restart(bool iscleanall);
        bool Read(out List<double>[] spike, out List<int>[] uid,
            out List<double[,]> lfp, out List<double> lfpstarttime,
            out List<double>[] dintime, out List<int>[] dinvalue);
        bool IsRunning { get; }
    }
}
